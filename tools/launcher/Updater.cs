using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ArpgGameLauncher
{
    /// <summary>
    /// Keeps a local copy of the game in sync with the latest GitHub release.
    /// The release carries <c>version.txt</c> and <c>ArpgGame-windows.zip</c>;
    /// GitHub's <c>releases/latest/download/&lt;asset&gt;</c> URLs always point at
    /// the newest release, so no API calls (or rate limits) are involved.
    /// Installs swap folders so a failed download never breaks the game you have.
    /// </summary>
    public sealed class Updater
    {
        public const string GameExe = "ArpgGame.exe";
        public const string ZipAsset = "ArpgGame-windows.zip";
        public const string VersionAsset = "version.txt";

        private readonly HttpClient _http;

        public Updater(string baseUrl, string installRoot, HttpClient http)
        {
            BaseUrl = baseUrl.TrimEnd('/') + "/";
            InstallRoot = installRoot;
            _http = http;
        }

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

        public async Task<string> FetchLatestVersionAsync(CancellationToken ct)
        {
            using (HttpResponseMessage response = await _http.GetAsync(BaseUrl + VersionAsset, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
            }
        }

        /// <summary>Downloads and installs the latest build. Progress is 0..1 (or -1 when the size is unknown).</summary>
        public async Task InstallAsync(string version, IProgress<double> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(InstallRoot);
            string zip = Path.Combine(InstallRoot, "download.zip");
            string staging = Path.Combine(InstallRoot, "game.new");
            string backup = Path.Combine(InstallRoot, "game.old");

            using (HttpResponseMessage response = await _http.GetAsync(BaseUrl + ZipAsset, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
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

            File.WriteAllText(Path.Combine(staging, VersionAsset), version);

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
}
