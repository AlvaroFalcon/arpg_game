#nullable enable
using System;
using Godot;

namespace ArpgGame.UI;

/// <summary>
/// A title-screen entry — docs/design/menus.md -> "Title screen": bare text, no box, and
/// under the one selected a soft brush stroke that sweeps in from the left. Hovering with
/// the mouse selects it too (it takes focus), so mouse and arrow keys share one highlight.
/// A <see cref="Button"/> underneath, so focus, Enter/Space and clicks behave as usual.
/// </summary>
public partial class BrushMenuOption : Button
{
    private Stroke _stroke = null!;

    public static BrushMenuOption Create(string text, Action onPressed, int size = 30)
    {
        var option = new BrushMenuOption { Text = text, Name = text };
        option.AddThemeFontSizeOverride("font_size", size);
        option.Pressed += onPressed;
        return option;
    }

    public override void _Ready()
    {
        Flat = true;
        FocusMode = FocusModeEnum.All;
        CustomMinimumSize = new Vector2(280f, 58f);
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        var empty = new StyleBoxEmpty();
        foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled", "hover_pressed" })
        {
            AddThemeStyleboxOverride(state, empty);
        }

        AddThemeColorOverride("font_color", UiTheme.TextDim);
        AddThemeColorOverride("font_hover_color", Colors.White);
        AddThemeColorOverride("font_focus_color", Colors.White);
        AddThemeColorOverride("font_pressed_color", UiTheme.Gold);
        AddThemeColorOverride("font_hover_pressed_color", UiTheme.Gold);
        AddThemeConstantOverride("outline_size", 6);
        AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.7f));

        // Drawn under the text: a child shown behind its parent.
        _stroke = new Stroke { ShowBehindParent = true, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_stroke);
        _stroke.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _stroke.Font = GetThemeFont("font");
        _stroke.FontSize = GetThemeFontSize("font_size");
        _stroke.Label = Text;

        FocusEntered += () => Core.Sfx.Play(this, "ui", -14f);
        MouseEntered += () =>
        {
            if (!Disabled)
            {
                GrabFocus();
            }
        };
    }

    /// <summary>Selected (focused) now — what the stroke shows (tests).</summary>
    public bool Selected => HasFocus();

    public override void _Process(double delta)
    {
        float target = HasFocus() ? 1f : 0f;
        float reveal = Mathf.MoveToward(_stroke.Reveal, target, (float)delta * (target > 0f ? 5f : 7f));
        if (!Mathf.IsEqualApprox(reveal, _stroke.Reveal))
        {
            _stroke.Reveal = reveal;
            _stroke.QueueRedraw();
        }
    }

    /// <summary>The brush stroke: a few stacked translucent bands (a soft, blurred edge),
    /// slightly slanted and tapered at both ends, with dry-brush streaks along it.</summary>
    private sealed partial class Stroke : Control
    {
        public float Reveal;
        public Font Font = null!;
        public int FontSize = 30;
        public string Label = "";

        private static readonly Color Ink = new(0.91f, 0.6f, 0.24f);

        public override void _Draw()
        {
            if (Reveal <= 0.001f)
            {
                return;
            }

            float textW = Font.GetStringSize(Label, HorizontalAlignment.Left, -1, FontSize).X;
            float w = Mathf.Min(Size.X, textW + 70f);
            float x0 = (Size.X - w) / 2f;
            float y = Size.Y * 0.8f;
            float len = w * Ease(Reveal);
            if (len < 6f)
            {
                return;
            }

            const float thick = 14f;
            // Blurred edge: wide and faint first, narrow and stronger last.
            for (int layer = 0; layer < 9; layer++)
            {
                float spread = thick * (2.4f - layer * 0.2f);
                float alpha = 0.05f + layer * 0.035f;
                DrawBand(x0, y, len, spread, Ink with { A = alpha * Reveal });
            }

            // Dry-brush streaks: thin lighter lines that stop short at different points.
            for (int i = 0; i < 4; i++)
            {
                float dy = (i - 1.5f) * 2.6f;
                float end = len * (0.7f + 0.08f * i);
                DrawLine(new Vector2(x0 + 8f + i * 5f, y + dy - 1.5f), new Vector2(x0 + end, y + dy - 1.5f - end * 0.012f), new Color(1f, 0.85f, 0.55f, 0.16f * Reveal), 1.2f);
            }
        }

        /// <summary>A band from <paramref name="x0"/> of length <paramref name="len"/>, tapered at
        /// both ends and rising a little to the right, like a quick stroke.</summary>
        private void DrawBand(float x0, float y, float len, float thick, Color color)
        {
            const int steps = 16;
            var top = new Vector2[steps + 1];
            var bottom = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float x = x0 + len * t;
                // Fuller near the start (where the brush lands), thinning to the tail.
                float taper = Mathf.Sin(Mathf.Pi * Mathf.Clamp(t * 0.92f + 0.04f, 0f, 1f));
                float half = thick * 0.5f * Mathf.Pow(taper, 0.3f) * (1.1f - 0.25f * t);
                float cy = y - len * t * 0.03f;
                top[i] = new Vector2(x, cy - half);
                bottom[i] = new Vector2(x, cy + half);
            }

            var poly = new Vector2[(steps + 1) * 2];
            for (int i = 0; i <= steps; i++)
            {
                poly[i] = top[i];
                poly[poly.Length - 1 - i] = bottom[i];
            }

            DrawColoredPolygon(poly, color);
        }

        private static float Ease(float t) => 1f - Mathf.Pow(1f - t, 3f);
    }
}
