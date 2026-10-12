using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Forms;

namespace ArpgGameLauncher
{
    internal static class Program
    {
        /// <summary>Where builds are published (the CI release workflow).</summary>
        private const string ApiUrl = "https://api.github.com/repos/AlvaroFalcon/arpg_game/releases?per_page=10";
        private const string ReleasesUrl = "https://github.com/AlvaroFalcon/arpg_game/releases/latest/download/";

        [STAThread]
        private static void Main(string[] args)
        {
            // .NET Framework defaults can predate TLS 1.2, which GitHub requires.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArpgGame");
            // A folder URL argument (tests against a local server) skips the API.
            string baseUrl = args.Length > 0 ? args[0] : ReleasesUrl;
            string apiUrl = args.Length > 0 ? null : ApiUrl;
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ArpgGameLauncher/1.1");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherForm(new Updater(apiUrl, baseUrl, root, http)));
        }
    }
}
