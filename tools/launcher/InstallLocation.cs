using System;
using System.IO;

namespace ArpgGameLauncher
{
    /// <summary>
    /// Where the game is installed — docs/design/distribution.md -> "Install folder".
    /// The choice lives in a small settings file that never moves
    /// (<c>%LOCALAPPDATA%\ArpgGame\launcher.cfg</c>), so the launcher finds the game
    /// wherever the player put it. Installs from before the choice existed sit in
    /// that same folder and are picked up without asking.
    /// </summary>
    public static class InstallLocation
    {
        public const string FolderName = "ArpgGame";
        private const string Key = "install_root=";

        /// <summary>The fixed folder for the launcher's own settings (and the old default install).</summary>
        public static string SettingsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

        private static string SettingsFile => Path.Combine(SettingsDir, "launcher.cfg");

        /// <summary>The suggested place for a new install.</summary>
        public static string DefaultRoot => SettingsDir;

        /// <summary>
        /// The install root to use, or null when the game isn't installed anywhere we
        /// know of (the launcher then asks where to install it). A saved choice wins;
        /// otherwise an install in the old default folder is adopted and remembered.
        /// </summary>
        public static string Resolve()
        {
            string saved = Load();
            if (saved != null && Updater.HasGame(saved))
            {
                return saved;
            }

            if (Updater.HasGame(DefaultRoot))
            {
                Save(DefaultRoot);
                return DefaultRoot;
            }

            return null;
        }

        /// <summary>The saved choice even if the game is no longer there (to suggest it again).</summary>
        public static string Load()
        {
            try
            {
                if (!File.Exists(SettingsFile))
                {
                    return null;
                }

                foreach (string line in File.ReadAllLines(SettingsFile))
                {
                    if (line.StartsWith(Key, StringComparison.Ordinal))
                    {
                        string path = line.Substring(Key.Length).Trim();
                        return path.Length > 0 && Path.IsPathRooted(path) ? path : null;
                    }
                }
            }
            catch (Exception)
            {
                // Unreadable settings: ask again.
            }

            return null;
        }

        public static void Save(string root)
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsFile, Key + root + Environment.NewLine);
        }

        /// <summary>
        /// Turns a folder the player picked into an install root. A folder that already
        /// holds the game (its root, or the <c>game</c> folder inside it) is used as
        /// is; any other folder gets an <c>ArpgGame</c> subfolder unless it is already
        /// named that or empty, so picking "D:\Games" doesn't scatter files into it.
        /// </summary>
        public static string FromPicked(string picked)
        {
            string full = Path.GetFullPath(picked);
            if (Updater.HasGame(full))
            {
                return full;
            }

            if (string.Equals(Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)), "game", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(full, Updater.GameExe)))
            {
                return Path.GetDirectoryName(full.TrimEnd(Path.DirectorySeparatorChar));
            }

            if (Updater.HasGame(Path.Combine(full, FolderName)))
            {
                return Path.Combine(full, FolderName);
            }

            bool named = string.Equals(Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)), FolderName, StringComparison.OrdinalIgnoreCase);
            bool empty = !Directory.Exists(full) || Directory.GetFileSystemEntries(full).Length == 0;
            return named || empty ? full : Path.Combine(full, FolderName);
        }

        /// <summary>Null if the launcher can create files in <paramref name="root"/>, else why not.</summary>
        public static string CheckWritable(string root)
        {
            try
            {
                Directory.CreateDirectory(root);
                string probe = Path.Combine(root, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return "No permission to write there — pick another folder";
            }
            catch (Exception ex)
            {
                return $"Can't use that folder: {ex.Message}";
            }
        }

        /// <summary>Free space on the drive holding <paramref name="root"/>, e.g. "48.2 GB free", or "" if unknown.</summary>
        public static string FreeSpace(string root)
        {
            try
            {
                string drive = Path.GetPathRoot(Path.GetFullPath(root));
                long free = new DriveInfo(drive).AvailableFreeSpace;
                return free >= 1L << 30 ? $"{free / (double)(1L << 30):0.0} GB free" : $"{free / (double)(1L << 20):0} MB free";
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
