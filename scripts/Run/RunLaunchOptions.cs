#nullable enable
using Godot;

namespace ArpgGame.Run;

/// <summary>
/// How the next run starts. Normally all defaults (a fresh run with the skill
/// pick); capture tools, tests and developers can preset them, and the same
/// values can come from the command line after <c>--</c>:
/// <c>--run-time=400 --skill=spark --auto-levels=12</c>.
/// </summary>
public static class RunLaunchOptions
{
    /// <summary>Seconds into the timeline to start at (0 = beginning).</summary>
    public static float StartTime;

    /// <summary>Skip the pick and start with this skill id.</summary>
    public static string? StartSkill;

    /// <summary>Apply this many random level-ups immediately (preview builds).</summary>
    public static int AutoLevels;

    /// <summary>Take the reward the moment the Colossus falls: no offer, no endless phase. The
    /// autoplay bot sets it so balance numbers stay comparable with earlier passes.</summary>
    public static bool AutoCashOut;

    /// <summary>The Rift tier (docs/design/survival-roadmap.md -> M4), 1..10.</summary>
    public static int RiftTier = 1;

    /// <summary>The Fear cards taken with it (ids, up to three).</summary>
    public static System.Collections.Generic.List<string> Fears = new();

    /// <summary>The difficulty these options describe.</summary>
    public static RiftDifficulty Difficulty()
    {
        var d = new RiftDifficulty { Tier = Mathf.Clamp(RiftTier, 1, RiftDifficulty.MaxTier) };
        foreach (string id in Fears)
        {
            if (RiftDifficulty.FindFear(id) is { } fear && !d.Fears.Contains(fear) && d.Fears.Count < RiftDifficulty.MaxFears)
            {
                d.Fears.Add(fear);
            }
        }

        return d;
    }

    public static void ReadCommandLine()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string[] kv = arg.TrimStart('-').Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            switch (kv[0])
            {
                case "run-time":
                    StartTime = kv[1].ToFloat();
                    break;
                case "skill":
                    StartSkill = kv[1];
                    break;
                case "auto-levels":
                    AutoLevels = kv[1].ToInt();
                    break;
                case "auto-cash-out":
                    AutoCashOut = kv[1] != "0";
                    break;
                case "rift-tier":
                    RiftTier = kv[1].ToInt();
                    break;
                case "fear":
                    Fears = new System.Collections.Generic.List<string>(kv[1].Split(',', System.StringSplitOptions.RemoveEmptyEntries));
                    break;
            }
        }
    }

    public static void Reset()
    {
        StartTime = 0f;
        StartSkill = null;
        AutoLevels = 0;
        AutoCashOut = false;
        RiftTier = 1;
        Fears = new System.Collections.Generic.List<string>();
    }
}
