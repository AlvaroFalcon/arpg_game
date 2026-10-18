#nullable enable
using System.Text.RegularExpressions;
using Godot;
using ArpgGame.Quests;

namespace ArpgGame.UI;

/// <summary>
/// How quests are drawn — docs/design/quests.md -> "Markers". The "!" and "?" are plain
/// ASCII in the theme font; the check marks, checkboxes and section arrows are drawn,
/// because the theme's fallback font has no ☐ ☑ ▸ ✓ glyphs (the same reason
/// <see cref="Spinner"/> is drawn). One place so the world marker, the prompt, the
/// directory, the maps, the tracker, the log and the toast agree.
/// </summary>
public static class QuestGlyphs
{
    public static readonly Color Disc = new(0.051f, 0.039f, 0.078f, 0.9f);

    /// <summary>Gold for New and Ready, grey for Active.</summary>
    public static Color ColorOf(QuestMarker marker) => marker == QuestMarker.Active ? UiTheme.TextDim : UiTheme.Gold;

    /// <summary>"!" for something new, "?" for in progress and ready.</summary>
    public static string GlyphOf(QuestMarker marker) => marker == QuestMarker.New ? "!" : "?";

    /// <summary>A dark disc ringed in the glyph's colour with the glyph in it, outlined in black.</summary>
    public static void DrawMarker(CanvasItem canvas, Vector2 center, float radius, QuestMarker marker, float glyphSize)
    {
        if (marker == QuestMarker.None)
        {
            return;
        }

        Color color = ColorOf(marker);
        canvas.DrawCircle(center, radius, Disc);
        canvas.DrawArc(center, radius - 1f, 0f, Mathf.Tau, 32, color, Mathf.Max(1.5f, radius / 8f), true);
        DrawGlyph(canvas, center, GlyphOf(marker), glyphSize, color);
    }

    /// <summary>Text centred on a point with a black outline (the theme font's metrics place the baseline).</summary>
    public static void DrawGlyph(CanvasItem canvas, Vector2 center, string glyph, float size, Color color)
    {
        Font font = ThemeDB.FallbackFont;
        int px = Mathf.RoundToInt(size);
        float width = font.GetStringSize(glyph, HorizontalAlignment.Left, -1, px).X;
        float baseline = center.Y + (font.GetAscent(px) - font.GetDescent(px)) / 2f;
        var at = new Vector2(center.X - width / 2f, baseline);
        canvas.DrawStringOutline(font, at, glyph, HorizontalAlignment.Left, -1, px, Mathf.Max(2, px / 6), Colors.Black);
        canvas.DrawString(font, at, glyph, HorizontalAlignment.Left, -1, px, color);
    }

    /// <summary>A check mark inside <paramref name="box"/>.</summary>
    public static void DrawCheck(CanvasItem canvas, Rect2 box, Color color, float width = 2.5f)
    {
        Vector2 a = box.Position + box.Size * new Vector2(0.18f, 0.55f);
        Vector2 b = box.Position + box.Size * new Vector2(0.42f, 0.78f);
        Vector2 c = box.Position + box.Size * new Vector2(0.84f, 0.24f);
        canvas.DrawPolyline(new[] { a, b, c }, color, width, true);
    }

    /// <summary>An empty or ticked checkbox.</summary>
    public static void DrawCheckbox(CanvasItem canvas, Rect2 box, bool ticked, Color color)
    {
        canvas.DrawRect(box, new Color(0.05f, 0.04f, 0.08f, 0.9f));
        canvas.DrawRect(box, color, false, 1.5f);
        if (ticked)
        {
            DrawCheck(canvas, box.Grow(-2f), UiTheme.Experience);
        }
    }

    /// <summary>A section arrow: pointing right when collapsed, down when open.</summary>
    public static void DrawDisclosure(CanvasItem canvas, Vector2 center, bool open, Color color, float size = 5f)
    {
        Vector2[] points = open
            ? new[] { center + new Vector2(-size, -size * 0.6f), center + new Vector2(size, -size * 0.6f), center + new Vector2(0f, size * 0.7f) }
            : new[] { center + new Vector2(-size * 0.6f, -size), center + new Vector2(-size * 0.6f, size), center + new Vector2(size * 0.8f, 0f) };
        canvas.DrawColoredPolygon(points, color);
    }

    private static readonly Regex Emphasis = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);

    /// <summary>The copy's <c>**emphasis**</c> as gold BBCode (the theme font has no bold face).</summary>
    public static string Rich(string text) => Emphasis.Replace(text, "[color=#e8b84a]$1[/color]");

    /// <summary>The same text without the markers.</summary>
    public static string Plain(string text) => Emphasis.Replace(text, "$1");
}
