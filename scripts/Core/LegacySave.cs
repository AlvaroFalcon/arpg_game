#nullable enable
using System.IO;
using Godot;

namespace ArpgGame.Core;

/// <summary>
/// One-time save migration after the project was renamed: Godot keys the
/// user:// folder on the project name, so builds made before the rename kept
/// profiles and settings in a sibling folder. If ours has no profile yet,
/// copy the old files over (the originals are left in place).
/// </summary>
public static class LegacySave
{
    private const string OldFolder = "Path to Exile";
    private static readonly string[] Files = { "profile.json", "settings.cfg" };
    private static bool _checked;

    public static void Migrate()
    {
        if (_checked)
        {
            return;
        }

        _checked = true;
        string current = OS.GetUserDataDir();
        string? parent = Path.GetDirectoryName(current);
        if (parent is null || File.Exists(Path.Combine(current, "profile.json")))
        {
            return;
        }

        string old = Path.Combine(parent, OldFolder);
        if (!Directory.Exists(old))
        {
            return;
        }

        Directory.CreateDirectory(current);
        foreach (string name in Files)
        {
            string from = Path.Combine(old, name);
            string to = Path.Combine(current, name);
            if (File.Exists(from) && !File.Exists(to))
            {
                File.Copy(from, to);
                GD.Print($"[Save] migrated {name} from the pre-rename save folder");
            }
        }
    }
}
