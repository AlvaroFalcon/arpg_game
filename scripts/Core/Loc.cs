#nullable enable
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Godot;

namespace ArpgGame.Core;

/// <summary>
/// Localisation — docs/design/localization.md. The game is written in English and English is
/// the key: <c>assets/i18n/&lt;locale&gt;.tsv</c> maps each English text to its translation
/// (tab-separated, <c>\n</c> for line breaks). Godot's Labels, Buttons, tabs and option lists
/// translate their text on their own (auto-translate), so most of the UI needs no code; text
/// drawn by hand goes through <see cref="T"/>.
///
/// Texts built with values ("Level 6", "Danger 3 · Tier 2") are matched against templates
/// ("Level {0}" → "Nivel {0}"), whose values are translated too when they are known texts
/// ("Chest · Spark → Lv 4" → "Cofre · Chispa → Nv 4"). A text with no translation shows in
/// English, never as a blank.
/// </summary>
public static class Loc
{
    /// <summary>Languages the settings offer: locale, the name shown (in its own language).</summary>
    public static readonly (string Locale, string Name)[] Languages = { ("en", "English"), ("es", "Español") };

    /// <summary>The setting's "follow the system" value.</summary>
    public const string Auto = "auto";

    private static PatternTranslation? _loaded;

    /// <summary>The locale in use ("en", "es").</summary>
    public static string Current { get; private set; } = "en";

    /// <summary>Resolves a setting ("auto", "en", "es") to a supported locale.</summary>
    public static string Resolve(string setting)
    {
        if (setting != Auto)
        {
            foreach ((string locale, _) in Languages)
            {
                if (locale == setting)
                {
                    return locale;
                }
            }
        }

        string os = OS.GetLocaleLanguage();
        foreach ((string locale, _) in Languages)
        {
            if (locale == os)
            {
                return locale;
            }
        }

        return "en";
    }

    /// <summary>Switches the game's language (loads its table the first time).</summary>
    public static void Apply(string setting)
    {
        string locale = Resolve(setting);
        Current = locale;
        if (locale != "en" && (_loaded is null || _loaded.Locale != locale))
        {
            if (_loaded is not null)
            {
                TranslationServer.RemoveTranslation(_loaded);
            }

            _loaded = PatternTranslation.Load(locale);
            if (_loaded is not null)
            {
                TranslationServer.AddTranslation(_loaded);
            }
        }

        TranslationServer.SetLocale(locale);
    }

    /// <summary>The text in the current language (hand-drawn text, tooltips built in code).</summary>
    public static string T(string english) => Current == "en" || string.IsNullOrEmpty(english) ? english : TranslationServer.Translate(english).ToString();

    /// <summary>A template in the current language, then formatted: <c>F("Level {0}", 6)</c>.</summary>
    public static string F(string template, params object[] args) => string.Format(T(template), args);

    /// <summary>The table loaded now (tests, the coverage tool).</summary>
    public static PatternTranslation? Table => _loaded;
}

/// <summary>A translation table with exact texts, single lines of multi-line texts and templates.</summary>
public partial class PatternTranslation : Translation
{
    private readonly Dictionary<string, string> _exact = new();
    private readonly Dictionary<string, List<Template>> _byPrefix = new();
    private readonly List<Template> _open = new();
    private readonly Dictionary<string, string> _cache = new();

    private sealed record Template(Regex Pattern, string Target);

    public int ExactCount => _exact.Count;
    public int TemplateCount { get; private set; }

    public static PatternTranslation? Load(string locale)
    {
        string path = $"res://assets/i18n/{locale}.tsv";
        if (!FileAccess.FileExists(path))
        {
            GD.PushWarning($"Loc: no table at {path}; the game stays in English.");
            return null;
        }

        using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var table = new PatternTranslation { Locale = locale };
        table.Parse(f.GetAsText());
        return table;
    }

    public void Parse(string text)
    {
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            int tab = line.IndexOf('\t');
            if (line.Length == 0 || line[0] == '#' || tab <= 0)
            {
                continue;
            }

            string key = Unescape(line[..tab]);
            string value = Unescape(line[(tab + 1)..]);
            if (value.Length == 0)
            {
                continue;
            }

            Add(key, value);
        }
    }

    public void Add(string key, string value)
    {
        if (Regex.IsMatch(key, @"\{\d+(:[^{}]*)?\}"))
        {
            AddTemplate(key, value);
            return;
        }

        _exact[key] = value;
        // Tooltips show multi-line texts one line at a time: every line is a key too.
        string[] k = key.Split('\n');
        string[] v = value.Split('\n');
        if (k.Length > 1 && k.Length == v.Length)
        {
            for (int i = 0; i < k.Length; i++)
            {
                if (k[i].Trim().Length > 0)
                {
                    _exact.TryAdd(k[i], v[i]);
                }
            }
        }
    }

    private void AddTemplate(string key, string value)
    {
        string literal = Regex.Replace(key, @"\{\d+(:[^{}]*)?\}", "");
        if (Regex.Matches(literal, "[A-Za-z]").Count < 2)
        {
            return; // "{0} {1}" would match anything
        }

        var pattern = new StringBuilder("^");
        int last = 0;
        MatchCollection holes = Regex.Matches(key, @"\{(\d+)(:[^{}]*)?\}");
        for (int i = 0; i < holes.Count; i++)
        {
            Match m = holes[i];
            string literalBefore = key[last..m.Index];
            pattern.Append(Regex.Escape(literalBefore));
            // Two values side by side ("{1}{2}"): the first takes everything, the second
            // whatever is left (often nothing) — lazy-lazy would split "Skill Duration" after "S".
            bool followedByHole = i + 1 < holes.Count && holes[i + 1].Index == m.Index + m.Length;
            bool afterHole = i > 0 && literalBefore.Length == 0;
            string body = followedByHole ? ".+" : afterHole ? ".*?" : ".+?";
            pattern.Append($"(?<g{m.Groups[1].Value}>{body})");
            last = m.Index + m.Length;
        }

        pattern.Append(Regex.Escape(key[last..])).Append('$');
        Regex re;
        try
        {
            re = new Regex(pattern.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant);
        }
        catch (System.ArgumentException)
        {
            return; // a duplicate group: the same value twice in one template
        }

        var t = new Template(re, value);
        TemplateCount++;
        int brace = key.IndexOf('{');
        string prefix = brace >= 3 ? key[..3] : "";
        if (prefix.Length == 0)
        {
            _open.Add(t);
        }
        else
        {
            if (!_byPrefix.TryGetValue(prefix, out List<Template>? list))
            {
                _byPrefix[prefix] = list = new List<Template>();
            }

            list.Add(t);
        }
    }

    public override StringName _GetMessage(StringName srcMessage, StringName context) => Lookup(srcMessage.ToString(), 0) ?? "";

    /// <summary>The translation of <paramref name="s"/>, or null when none is known.</summary>
    public string? Lookup(string s, int depth)
    {
        if (s.Length == 0)
        {
            return null;
        }

        if (_exact.TryGetValue(s, out string? hit))
        {
            return hit;
        }

        if (_cache.TryGetValue(s, out string? cached))
        {
            return cached.Length == 0 ? null : cached;
        }

        string? result = depth < 2 ? MatchTemplate(s, depth) : null;
        if (result is null && depth == 0 && s.Contains('\n'))
        {
            // A multi-line text built from known lines.
            string[] lines = s.Split('\n');
            bool any = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (Lookup(lines[i], 1) is { } line)
                {
                    lines[i] = line;
                    any = true;
                }
            }

            result = any ? string.Join("\n", lines) : null;
        }

        if (_cache.Count > 20000)
        {
            _cache.Clear();
        }

        _cache[s] = result ?? "";
        return result;
    }

    private string? MatchTemplate(string s, int depth)
    {
        if (s.Length >= 3 && _byPrefix.TryGetValue(s[..3], out List<Template>? list) && TryAll(list, s, depth) is { } a)
        {
            return a;
        }

        return TryAll(_open, s, depth);
    }

    private string? TryAll(List<Template> list, string s, int depth)
    {
        foreach (Template t in list)
        {
            Match m = t.Pattern.Match(s);
            if (!m.Success || !Plausible(m, depth))
            {
                continue;
            }

            return Regex.Replace(t.Target, @"\{(\d+)(:[^{}]*)?\}", r =>
            {
                Group g = m.Groups["g" + r.Groups[1].Value];
                if (!g.Success)
                {
                    return r.Value;
                }

                // A value that is itself a known text (a skill's name) is translated too.
                return Lookup(g.Value, depth + 1) ?? g.Value;
            });
        }

        return null;
    }

    /// <summary>
    /// A template only fits when each value it captured looks like a value: a number, a short
    /// name, or a text the table knows. Without this, "{0}% more {1}" would turn "Monsters have
    /// 40% more life" into half-translated noise ("Monsters have 40% más life").
    /// </summary>
    private bool Plausible(Match m, int depth)
    {
        foreach (Group g in m.Groups)
        {
            if (!g.Name.StartsWith('g') || !g.Success || g.Value.Length == 0)
            {
                continue;
            }

            int spaces = 0;
            foreach (char c in g.Value)
            {
                spaces += c == ' ' ? 1 : 0;
            }

            if (spaces > 2 && Lookup(g.Value, depth + 1) is null)
            {
                return false;
            }
        }

        return true;
    }

    public static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");
}
