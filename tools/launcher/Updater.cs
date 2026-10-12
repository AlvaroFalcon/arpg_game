using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArpgGameLauncher
{
    /// <summary>The newest build: its version and where its zip lives.</summary>
    public sealed class Build
    {
        public Build(string version, string zipUrl)
        {
            Version = version;
            ZipUrl = zipUrl;
        }

        public string Version { get; }
        public string ZipUrl { get; }
    }

    /// <summary>
    /// Keeps a local copy of the game in sync with the latest GitHub release.
    /// It asks the GitHub API for recent releases and takes the newest one that
    /// already has <c>ArpgGame-windows.zip</c> attached (a release is created a
    /// minute before its files finish uploading). If the API is unreachable or
    /// rate-limited it falls back to the <c>releases/latest/download</c> links.
    /// Installs swap folders so a failed download never breaks the game you have.
    /// </summary>
    public sealed class Updater
    {
        public const string GameExe = "ArpgGame.exe";
        public const string ZipAsset = "ArpgGame-windows.zip";
        public const string VersionAsset = "version.txt";

        private readonly HttpClient _http;

        /// <param name="apiUrl">GitHub releases API URL, or null to only use <paramref name="baseUrl"/>.</param>
        /// <param name="baseUrl">A folder URL serving version.txt and the zip (releases/latest/download).</param>
        public Updater(string apiUrl, string baseUrl, string installRoot, HttpClient http)
        {
            ApiUrl = apiUrl;
            BaseUrl = baseUrl.TrimEnd('/') + "/";
            InstallRoot = installRoot;
            _http = http;
        }

        public string ApiUrl { get; }
        public string BaseUrl { get; }
        public string InstallRoot { get; }
        public string GameDir => Path.Combine(InstallRoot, "game");
        public string GameExePath => Path.Combine(GameDir, GameExe);
        public bool IsInstalled => File.Exists(GameExePath);

        /// <summary>Version of the installed game, or null if none.</summary>
        public string InstalledVersion
        {
            get
            {
                string file = Path.Combine(GameDir, VersionAsset);
                return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
            }
        }

        public async Task<Build> FetchLatestAsync(CancellationToken ct)
        {
            if (ApiUrl != null)
            {
                try
                {
                    Build fromApi = await FetchFromApiAsync(ct).ConfigureAwait(false);
                    if (fromApi != null)
                    {
                        return fromApi;
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    // Rate limit or API hiccup: the direct links below still work.
                }
            }

            string version = (await GetStringAsync(BaseUrl + VersionAsset, ct).ConfigureAwait(false)).Trim();
            return new Build(version, BaseUrl + ZipAsset);
        }

        private async Task<Build> FetchFromApiAsync(CancellationToken ct)
        {
            using (HttpResponseMessage response = await _http.GetAsync(ApiUrl, ct).ConfigureAwait(false))
            {
                await EnsureOkAsync(response, ApiUrl).ConfigureAwait(false);
                var serializer = new DataContractJsonSerializer(typeof(ReleaseDto[]));
                using (Stream body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var releases = (ReleaseDto[])serializer.ReadObject(body);
                    return releases
                        .Where(r => !r.Draft && !r.Prerelease)
                        .Select(r => (r.Tag, Zip: r.Assets?.FirstOrDefault(a => a.Name == ZipAsset)))
                        .Where(r => r.Zip != null && !string.IsNullOrEmpty(r.Tag))
                        .Select(r => new Build(r.Tag, r.Zip.Url))
                        .FirstOrDefault();
                }
            }
        }

        private async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            using (HttpResponseMessage response = await _http.GetAsync(url, ct).ConfigureAwait(false))
            {
                await EnsureOkAsync(response, url).ConfigureAwait(false);
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        private static Task EnsureOkAsync(HttpResponseMessage response, string url)
        {
            if (response.IsSuccessStatusCode)
            {
                return Task.CompletedTask;
            }

            string hint = (int)response.StatusCode == 404 ? " — no build is published yet, or it is still uploading" : "";
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}{hint}.");
        }

        /// <summary>Downloads and installs the latest build. Progress is 0..1 (or -1 when the size is unknown).</summary>
        public async Task InstallAsync(Build build, IProgress<double> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(InstallRoot);
            string zip = Path.Combine(InstallRoot, "download.zip");
            string staging = Path.Combine(InstallRoot, "game.new");
            string backup = Path.Combine(InstallRoot, "game.old");

            using (HttpResponseMessage response = await _http.GetAsync(build.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                await EnsureOkAsync(response, build.ZipUrl).ConfigureAwait(false);
                long? total = response.Content.Headers.ContentLength;
                using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (FileStream target = File.Create(zip))
                {
                    var buffer = new byte[81920];
                    long done = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        await target.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                        done += read;
                        progress?.Report(total > 0 ? (double)done / total.Value : -1);
                    }
                }
            }

            DeleteDir(staging);
            ZipFile.ExtractToDirectory(zip, staging);
            File.Delete(zip);
            if (!File.Exists(Path.Combine(staging, GameExe)))
            {
                throw new InvalidDataException($"The download does not contain {GameExe}.");
            }

            File.WriteAllText(Path.Combine(staging, VersionAsset), build.Version);

            // Swap: the old build survives until the new one is in place.
            DeleteDir(backup);
            if (Directory.Exists(GameDir))
            {
                Directory.Move(GameDir, backup);
            }

            Directory.Move(staging, GameDir);
            DeleteDir(backup);
        }

        private static void DeleteDir(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }

    [DataContract]
    internal sealed class ReleaseDto
    {
        [DataMember(Name = "tag_name")] public string Tag { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
        [DataMember(Name = "assets")] public AssetDto[] Assets { get; set; }
    }

    [DataContract]
    internal sealed class AssetDto
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "browser_download_url")] public string Url { get; set; }
    }
}
