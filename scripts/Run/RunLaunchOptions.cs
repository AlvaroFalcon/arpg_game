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
            }
        }
    }

    public static void Reset()
    {
        StartTime = 0f;
        StartSkill = null;
        AutoLevels = 0;
        AutoCashOut = false;
    }
}
