#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Items;

namespace ArpgGame.UI;

/// <summary>
/// The bottom-centre player cluster — docs/design/menus.md -> "HUD". Modelled on
/// the owner's reference (AION 2): a long red life bar and a cyan mana bar that
/// meet at a central hexagonal badge holding the level, with an experience ring
/// around it; under the bars the flasks (Q life, R mana) as bottles whose liquid
/// shows the charges left; and dodge charges under the badge. The skill row
/// below it is a separate control (<see cref="CreateSkillRow"/>), so the HUD
/// editor can move it on its own. Custom-drawn rather than built from ProgressBars so the
/// bars can be slanted, mirrored and drain towards the centre, with a lighter
/// "recent damage" trail.
/// </summary>
public partial class PlayerHudBar : Control
{
    public const float Width = 920f;
    public const float Height = 142f;

    public Player Player = null!;

    /// <summary>Level and experience fraction to show in the badge.</summary>
    public Func<(int Level, float Experience)> Progress = () => (1, 0f);

    /// <summary>The class resource (Rage, Combo...), if any.</summary>
    public Func<ClassResource?> Resource = () => null;

    private const float CenterX = Width / 2f;
    private static readonly Vector2 Badge = new(CenterX, 60f);
    private const float BadgeR = 46f;
    private const float BarTop = 42f;
    private const float BarH = 22f;
    private const float BarInner = 52f; // gap from the centre to the bars' inner end
    private const float BarLen = 350f;
    private const float Slant = 12f;

    private static readonly Color LifeTop = new(0.95f, 0.25f, 0.22f);
    private static readonly Color LifeBottom = new(0.55f, 0.06f, 0.08f);
    private static readonly Color ManaTop = new(0.35f, 0.9f, 1f);
    private static readonly Color ManaBottom = new(0.05f, 0.42f, 0.7f);
    private static readonly Color Track = new(0.05f, 0.04f, 0.07f, 0.88f);
    private static readonly Color Rim = new(0.9f, 0.76f, 0.42f);

    private float _lifeTrail = 1f;
    private float _manaTrail = 1f;
    private float _time;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(Width, Height);
        Size = new Vector2(Width, Height);
    }

    /// <summary>The skill row (LMB, RMB, 1-4), docked under the cluster. Its own
    /// control so the HUD editor can move it separately.</summary>
    public static HBoxContainer CreateSkillRow(Control root)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        root.AddChild(row);
        row.SetAnchorsPreset(LayoutPreset.CenterBottom, true);
        row.OffsetLeft = -230f;
        row.OffsetRight = 230f;
        row.OffsetTop = -74f;
        row.OffsetBottom = -8f;
        return row;
    }

    /// <summary>Places the cluster at the bottom centre of a full-rect parent.</summary>
    public void Dock()
    {
        SetAnchorsPreset(LayoutPreset.CenterBottom, true);
        OffsetLeft = -Width / 2f;
        OffsetRight = Width / 2f;
        OffsetTop = -Height - 78f;
        OffsetBottom = -78f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        float life = Fraction(Player.Stats.Life);
        float mana = Fraction(Player.Stats.Mana);
        // The trail snaps up on heals and drains down a moment after damage.
        _lifeTrail = life > _lifeTrail ? life : Mathf.MoveToward(_lifeTrail, life, dt * 0.6f);
        _manaTrail = mana > _manaTrail ? mana : Mathf.MoveToward(_manaTrail, mana, dt * 0.6f);
        QueueRedraw();
    }

    private static float Fraction(Stats.ResourcePool? pool) =>
        pool is null || pool.Max <= 0f ? 0f : Mathf.Clamp(pool.Current / pool.Max, 0f, 1f);

    private bool ShowMana => !Player.Stats.IgnoresResourceCosts && Player.Stats.Mana is not null;

    public override void _Draw()
    {
        Font font = ThemeDB.FallbackFont;

        // --- the two bars ---
        DrawBar(-1, Fraction(Player.Stats.Life), _lifeTrail, LifeTop, LifeBottom);
        if (Player.Stats.Life is { } life)
        {
            int maxLife = Mathf.RoundToInt(life.Max);
            Text(font, $"{Mathf.Min(Mathf.CeilToInt(life.Current), maxLife):N0} / {maxLife:N0}", new Vector2(CenterX - BarInner - 10f, BarTop + 16f), 15, Colors.White, HorizontalAlignment.Right);
        }

        if (ShowMana)
        {
            DrawBar(1, Fraction(Player.Stats.Mana), _manaTrail, ManaTop, ManaBottom);
            Stats.ResourcePool mana = Player.Stats.Mana!;
            Text(font, $"{Mathf.FloorToInt(mana.Current):N0} / {Mathf.RoundToInt(mana.Max):N0}", new Vector2(CenterX + BarInner + 10f, BarTop + 16f), 15, Colors.White, HorizontalAlignment.Left);
        }

        // Stamina under the life bar, the class resource under the mana bar.
        if (Player.MaxStamina > 0f)
        {
            float s = Mathf.Clamp(Mathf.Max(0f, Player.Stamina) / Player.MaxStamina, 0f, 1f);
            float inner = CenterX - BarInner;
            DrawRect(new Rect2(inner - BarLen * 0.8f, BarTop + BarH + 4f, BarLen * 0.8f, 4f), Track);
            DrawRect(new Rect2(inner - BarLen * 0.8f * s, BarTop + BarH + 4f, BarLen * 0.8f * s, 4f), Player.Exhausted ? new Color(1f, 0.45f, 0.4f) : new Color(0.95f, 0.85f, 0.35f));
        }

        if (Resource() is { } res)
        {
            float x = CenterX + BarInner;
            float pipW = Mathf.Min(14f, (BarLen * 0.8f - 2f * res.Max) / Mathf.Max(1, res.Max));
            for (int i = 0; i < res.Max; i++)
            {
                DrawRect(new Rect2(x + i * (pipW + 2f), BarTop + BarH + 4f, pipW, 5f), i < res.Value ? res.Color : Track);
            }

            // The label sits at the far end of the pips, clear of the mana flask.
            Text(font, $"{res.Label} {res.Value}/{res.Max}", new Vector2(CenterX + BarInner + BarLen, BarTop + BarH + 22f), 12, new Color(0.75f, 0.72f, 0.85f), HorizontalAlignment.Right);
        }

        // --- flasks ---
        if (Flasks.Of(Player) is { } flasks)
        {
            DrawFlask(font, new Rect2(CenterX - BarInner - 70f, 82f, 54f, 54f), flasks.Life, "Q", LifeTop, LifeBottom);
            if (ShowMana && flasks.Mana.Enabled)
            {
                DrawFlask(font, new Rect2(CenterX + BarInner + 16f, 82f, 54f, 54f), flasks.Mana, "R", ManaTop, ManaBottom);
            }
        }

        // --- the badge (drawn last so it sits over the bars' inner ends) ---
        DrawBadge(font);

        // Dodge charges: small diamonds under the badge.
        if (Player.Dodge is { } dodge)
        {
            int max = dodge.MaxCharges;
            float start = CenterX - (max - 1) * 9f;
            for (int i = 0; i < max; i++)
            {
                float fill = i < dodge.Charges ? 1f : i == dodge.Charges ? dodge.RechargeProgress : 0f;
                Diamond(new Vector2(start + i * 18f, 124f), 6.5f, fill);
            }
        }
    }

    /// <summary>A slanted bar on one side (-1 left, 1 right) that empties towards the badge.</summary>
    private void DrawBar(int side, float value, float trail, Color top, Color bottom)
    {
        float inner = CenterX + side * BarInner;
        float outer = CenterX + side * (BarInner + BarLen);
        Vector2[] Shape(float frac)
        {
            float end = inner + (outer - inner) * frac;
            // The outer end is cut on a slant (wider at the bottom).
            float slant = Mathf.Abs(end - outer) < Slant ? side * (Slant - Mathf.Abs(end - outer)) : 0f;
            return new[]
            {
                new Vector2(inner, BarTop),
                new Vector2(end - slant, BarTop),
                new Vector2(end, BarTop + BarH),
                new Vector2(inner, BarTop + BarH),
            };
        }

        Vector2[] track = Shape(1f);
        // Frame: a dark plate a little larger than the bar, with a gold rim.
        var plate = new[] { track[0] + new Vector2(0f, -3f), track[1] + new Vector2(side * 2f, -3f), track[2] + new Vector2(side * 4f, 3f), track[3] + new Vector2(0f, 3f) };
        DrawColoredPolygon(plate, new Color(0.02f, 0.015f, 0.03f, 0.9f));
        DrawColoredPolygon(track, Track);
        if (trail > value + 0.01f)
        {
            DrawColoredPolygon(Shape(trail), new Color(1f, 0.85f, 0.6f, 0.55f));
        }

        if (value * BarLen > 2f)
        {
            Vector2[] fill = Shape(value);
            DrawPolygon(fill, new[] { top, top, bottom, bottom });
            // A glassy highlight along the top and a sheen sweeping along.
            DrawLine(fill[0] + new Vector2(0f, 2f), fill[1] + new Vector2(-side * 2f, 2f), new Color(1f, 1f, 1f, 0.35f), 2f);
            float sweep = (_time * 0.25f) % 1.4f - 0.2f;
            if (sweep > 0f && sweep < value)
            {
                float x = inner + (outer - inner) * sweep;
                DrawLine(new Vector2(x, BarTop + 1f), new Vector2(x - side * 6f, BarTop + BarH - 1f), new Color(1f, 1f, 1f, 0.18f), 4f);
            }
        }

        DrawPolyline(new[] { plate[0], plate[1], plate[2], plate[3] }, Rim with { A = 0.75f }, 1.5f, true);
    }

    private void DrawBadge(Font font)
    {
        (int level, float xp) = Progress();
        // Experience ring around the badge.
        DrawArc(Badge, BadgeR + 7f, 0f, Mathf.Tau, 64, new Color(0.05f, 0.04f, 0.08f, 0.9f), 6f, true);
        if (xp > 0.001f)
        {
            DrawArc(Badge, BadgeR + 7f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * Mathf.Clamp(xp, 0f, 1f), 64, UiTheme.Experience, 4f, true);
        }

        // Hexagon, pointy top: dark centre, gold double rim.
        var outer = new Vector2[6];
        var inner = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float a = -Mathf.Pi / 2f + i * Mathf.Tau / 6f;
            outer[i] = Badge + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * BadgeR;
            inner[i] = Badge + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (BadgeR - 7f);
        }

        DrawColoredPolygon(outer, new Color(0.12f, 0.1f, 0.16f));
        DrawPolygon(inner, new[] { new Color(0.22f, 0.18f, 0.32f), new Color(0.16f, 0.13f, 0.24f), new Color(0.08f, 0.06f, 0.12f), new Color(0.05f, 0.04f, 0.08f), new Color(0.08f, 0.06f, 0.12f), new Color(0.16f, 0.13f, 0.24f) });
        DrawPolyline(new[] { outer[0], outer[1], outer[2], outer[3], outer[4], outer[5], outer[0] }, Rim, 2.5f, true);
        DrawPolyline(new[] { inner[0], inner[1], inner[2], inner[3], inner[4], inner[5], inner[0] }, Rim with { A = 0.5f }, 1f, true);
        // Little wings joining the bars.
        foreach (int side in new[] { -1, 1 })
        {
            DrawColoredPolygon(new[]
            {
                Badge + new Vector2(side * (BadgeR - 6f), -10f),
                Badge + new Vector2(side * (BarInner + 6f), BarTop - 2f - Badge.Y + 2f),
                Badge + new Vector2(side * (BarInner + 6f), BarTop + BarH - Badge.Y + 2f),
                Badge + new Vector2(side * (BadgeR - 6f), 10f),
            }, Rim with { A = 0.9f });
        }

        Text(font, level.ToString(), Badge + new Vector2(0f, 12f), 34, Colors.White, HorizontalAlignment.Center);
        Text(font, $"{Mathf.FloorToInt(Mathf.Clamp(xp, 0f, 1f) * 100f)}%", Badge + new Vector2(0f, 30f), 11, UiTheme.Experience, HorizontalAlignment.Center);
    }

    private void DrawFlask(Font font, Rect2 r, Flask flask, string key, Color top, Color bottom)
    {
        bool empty = flask.Usable < 1;
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.04f, 0.08f, 0.9f),
            BorderColor = flask.Active ? top : Rim with { A = 0.7f },
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
        };
        box.SetBorderWidthAll(flask.Active ? 2 : 1);
        if (flask.Active)
        {
            box.ShadowColor = top with { A = 0.5f };
            box.ShadowSize = 8;
        }

        DrawStyleBox(box, r);

        // The bottle: a round body, a neck and a cork; liquid level = charges.
        Vector2 c = r.Position + new Vector2(r.Size.X * 0.42f, r.Size.Y * 0.6f);
        float br = r.Size.X * 0.25f;
        DrawRect(new Rect2(c.X - 4f, c.Y - br - 9f, 8f, 10f), new Color(0.75f, 0.85f, 0.95f, 0.35f));
        DrawRect(new Rect2(c.X - 5f, c.Y - br - 13f, 10f, 5f), new Color(0.55f, 0.36f, 0.2f));
        DrawCircle(c, br, new Color(0.75f, 0.85f, 0.95f, 0.22f));
        float level = Mathf.Clamp(flask.Charges / flask.MaxCharges, 0f, 1f);
        if (level > 0.01f)
        {
            // A circular segment: the arc below the liquid surface, from the right
            // intersection round the bottom to the left one.
            float surface = c.Y + br - 2f * br * level;
            float a1 = Mathf.Asin(Mathf.Clamp((surface - c.Y) / br, -1f, 1f));
            float a2 = Mathf.Pi - a1;
            var pts = new List<Vector2>();
            const int steps = 24;
            for (int i = 0; i <= steps; i++)
            {
                float a = a1 + (a2 - a1) * i / steps;
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * br);
            }

            Color liquid = empty ? bottom.Darkened(0.4f) : top.Lerp(bottom, 0.3f);
            if (a2 - a1 > 0.05f)
            {
                DrawColoredPolygon(pts.ToArray(), liquid);
            }
            DrawCircle(c + new Vector2(-br * 0.35f, -br * 0.3f), br * 0.18f, new Color(1f, 1f, 1f, 0.45f));
        }

        DrawArc(c, br, 0f, Mathf.Tau, 32, new Color(0.85f, 0.92f, 1f, 0.7f), 1.5f, true);

        // Regeneration towards the next charge, along the bottom edge.
        if (!flask.Full)
        {
            DrawRect(new Rect2(r.Position.X + 4f, r.End.Y - 5f, (r.Size.X - 8f) * flask.Partial, 2f), top with { A = 0.8f });
        }

        Text(font, key, r.Position + new Vector2(5f, 14f), 13, Rim, HorizontalAlignment.Left);
        Text(font, flask.Usable.ToString(), r.Position + new Vector2(r.Size.X - 5f, r.Size.Y - 7f), 17, empty ? new Color(1f, 0.4f, 0.35f) : Colors.White, HorizontalAlignment.Right);
        if (empty)
        {
            DrawRect(r, new Color(0f, 0f, 0f, 0.35f));
        }
    }

    private void Diamond(Vector2 c, float r, float fill)
    {
        var pts = new[] { c + new Vector2(0f, -r), c + new Vector2(r, 0f), c + new Vector2(0f, r), c + new Vector2(-r, 0f) };
        DrawColoredPolygon(pts, new Color(0.05f, 0.04f, 0.08f, 0.9f));
        if (fill >= 1f)
        {
            DrawColoredPolygon(pts, new Color(0.45f, 0.85f, 1f));
        }
        else if (fill > 0f)
        {
            float h = 2f * r * fill;
            DrawRect(new Rect2(c.X - r * 0.4f, c.Y + r - h, r * 0.8f, h), new Color(0.3f, 0.55f, 0.7f));
        }

        DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[0] }, new Color(0.7f, 0.95f, 1f), 1.5f, true);
    }

    /// <summary>Text with a dark outline. <paramref name="at"/> is the baseline anchor: left, right or centre point.</summary>
    private void Text(Font font, string text, Vector2 at, int size, Color color, HorizontalAlignment align)
    {
        float w = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        float x = align switch { HorizontalAlignment.Right => at.X - w, HorizontalAlignment.Center => at.X - w / 2f, _ => at.X };
        var p = new Vector2(x, at.Y);
        DrawStringOutline(font, p, text, HorizontalAlignment.Left, -1, size, 4, new Color(0f, 0f, 0f, 0.85f));
        DrawString(font, p, text, HorizontalAlignment.Left, -1, size, color);
    }
}
