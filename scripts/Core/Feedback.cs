#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace ArpgGame.Core;

public enum FeedbackKind
{
    Bug,
    Idea,
    FeatureRequest,
}

/// <summary>
/// In-game feedback → GitHub issues — docs/design/menus.md -> "Feedback". The game
/// can't create issues itself without shipping a token in the client (anyone could
/// extract it), so it opens the repository's "new issue" page in the browser with an
/// issue form chosen and every field filled in; the player presses Submit. The issue
/// forms in .github/ISSUE_TEMPLATE apply the labels (bug / idea / enhancement), which
/// works for anyone, unlike labels passed in the URL. A relay (Discord or a small
/// web function) can replace this later without changing the form.
/// </summary>
public static class Feedback
{
    public const string Repository = "https://github.com/AlvaroFalcon/arpg_game";

    /// <summary>GitHub rejects very long URLs; stay well under its ~8 KB limit.</summary>
    public const int MaxUrlLength = 7000;

    public static string Template(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Bug => "bug_report.yml",
        FeedbackKind.Idea => "idea.yml",
        _ => "feature_request.yml",
    };

    public static string Label(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Bug => "bug",
        FeedbackKind.Idea => "idea",
        _ => "enhancement",
    };

    public static string TitlePrefix(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Bug => "[Bug] ",
        FeedbackKind.Idea => "[Idea] ",
        _ => "[Feature] ",
    };

    /// <summary>The "new issue" URL with the form and its fields filled in. Field ids
    /// match the issue forms (description, steps, why, game-info).</summary>
    public static string BuildUrl(FeedbackKind kind, string title, string description, string extra, string gameInfo)
    {
        var fields = new List<(string Key, string Value)>
        {
            ("template", Template(kind)),
            ("title", TitlePrefix(kind) + title.Trim()),
            ("description", description.Trim()),
        };
        if (extra.Trim().Length > 0)
        {
            fields.Add((kind == FeedbackKind.Bug ? "steps" : "why", extra.Trim()));
        }

        if (gameInfo.Length > 0)
        {
            fields.Add(("game-info", gameInfo));
        }

        string url = Compose(fields);
        // Too long: shorten the longest free-text field until it fits.
        while (url.Length > MaxUrlLength)
        {
            int longest = Enumerable.Range(0, fields.Count).Where(i => fields[i].Key is "description" or "steps" or "why" or "game-info")
                .OrderByDescending(i => fields[i].Value.Length).First();
            string v = fields[longest].Value;
            int keep = Math.Max(40, v.Length - (url.Length - MaxUrlLength) - 40);
            fields[longest] = (fields[longest].Key, v[..Math.Min(keep, v.Length)] + " […]");
            url = Compose(fields);
        }

        return url;
    }

    private static string Compose(IEnumerable<(string Key, string Value)> fields) =>
        $"{Repository}/issues/new?" + string.Join("&", fields.Select(f => $"{f.Key}={Uri.EscapeDataString(f.Value)}"));

    /// <summary>The same report as Markdown, for "Copy text".</summary>
    public static string AsMarkdown(FeedbackKind kind, string title, string description, string extra, string gameInfo)
    {
        var sb = new StringBuilder();
        sb.Append("## ").Append(TitlePrefix(kind)).AppendLine(title.Trim()).AppendLine().AppendLine(description.Trim());
        if (extra.Trim().Length > 0)
        {
            sb.AppendLine().AppendLine(kind == FeedbackKind.Bug ? "### Steps to reproduce" : "### Why it would help").AppendLine(extra.Trim());
        }

        if (gameInfo.Length > 0)
        {
            sb.AppendLine().AppendLine("### Game info").AppendLine("```").AppendLine(gameInfo).AppendLine("```");
        }

        return sb.ToString();
    }

    /// <summary>What the report carries about the game: version, system, where the
    /// player is and with whom. Nothing personal.</summary>
    public static string GameInfo(Node context)
    {
        var lines = new List<string>
        {
            $"Version: {GameVersion()}",
            $"System: {OS.GetName()} {OS.GetVersion()} · {RenderingServer.GetVideoAdapterName()}",
            $"Window: {DisplayServer.WindowGetSize()} · voxel view {(GameSettings.Get(context)?.VoxelGraphics ?? true ? "on" : "off")}",
        };
        if (context.IsInsideTree() && context.GetTree().CurrentScene is { } scene)
        {
            string where = scene switch
            {
                World.RealityController r => $"reality {r.Layout.Name}, tier {r.Tier}",
                _ => scene.Name.ToString(),
            };
            lines.Add($"Where: {where}");
        }

        if (Profile.Get(context) is { HasCharacters: true } profile)
        {
            Characters.CharacterData c = profile.Active;
            lines.Add($"Character: level {c.Level} {c.AscendancyDef?.Name ?? c.Class.Name}, best tier {c.HighestTierCleared}");
        }

        lines.Add("Sent from the game");
        return string.Join("\n", lines);
    }

    /// <summary>The release version: version.txt next to the executable (written by
    /// the release workflow), else "dev build".</summary>
    public static string GameVersion()
    {
        string path = OS.GetExecutablePath().GetBaseDir().PathJoin("version.txt");
        if (FileAccess.FileExists(path) && FileAccess.Open(path, FileAccess.ModeFlags.Read) is { } f)
        {
            using (f)
            {
                string v = f.GetAsText().Trim();
                if (v.Length > 0)
                {
                    return v;
                }
            }
        }

        return "dev build";
    }
}
