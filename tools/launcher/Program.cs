using System;
using System.Drawing;
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
            // Where the game lives: the player's saved choice, an older install in the
            // default folder, or null — then the launcher asks before installing.
            string root = InstallLocation.Resolve();
            // A folder URL argument (tests against a local server) skips the API.
            string baseUrl = args.Length > 0 ? args[0] : ReleasesUrl;
            string apiUrl = args.Length > 0 ? null : ApiUrl;
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ArpgGameLauncher/2.0");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            System.Reflection.Assembly self = typeof(Program).Assembly;
            using (Stream art = self.GetManifestResourceStream("background.png"))
            using (Stream iconStream = self.GetManifestResourceStream("icon.ico"))
            using (var image = Image.FromStream(art))
            using (var icon = new Icon(iconStream))
            {
                Application.Run(new LauncherForm(new Updater(apiUrl, baseUrl, root, http), image, icon));
            }
        }
    }
}
