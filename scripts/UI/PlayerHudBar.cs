#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Items;

namespace ArpgGame.UI;

/// <summary>
/// The bottom-centre player cluster — docs/design/menus.md -> "HUD: the mirror". Drawn
/// from the owner's sketch (2026-10-29): an oval mirror rising out of the bottom edge
/// (only its upper half shows), split down the middle, life filling the left half and
/// mana (or what stands in for it) the right, both rising from the screen edge like
/// liquid; its rim is the experience bar, a purple band that fills from the bottom left,
/// over the top, to the bottom right, cut into ten by black notches. Around it: the level on a
/// small gem at the top, dodge charges above that, the flasks (1 life, 2 mana) at the
/// outer ends of the skill bar, stamina over the left skills and the class resource over
/// the right ones. The skills themselves are a separate control (<see cref="CreateSkillRow"/>,
/// <see cref="SkillBar"/>) so the HUD editor can move them on their own. Custom-drawn so
/// the halves can be clipped to the oval and drain with a lighter "recent damage" trail.
/// </summary>
public partial class PlayerHudBar : Control
{
    /// <summary>What fills the right half when there is no mana to show (the Survival
    /// Rift: the danger level and what comes next).</summary>
    public readonly record struct SideGauge(string Label, string Detail, float Fraction, Color Top, Color Bottom);

    public const float Width = 640f;
    public const float Height = 170f;

    /// <summary>The mirror's half-width and half-height.</summary>
    public const float MirrorRx = 70f;
    public const float MirrorRy = 92f;

    /// <summary>Room left either side of the mirror for the skill bar (<see cref="SkillBar.Gap"/>).</summary>
    public const float MirrorClearance = MirrorRx + 22f;

    public Player Player = null!;

    /// <summary>Level and experience fraction to show.</summary>
    public Func<(int Level, float Experience)> Progress = () => (1, 0f);

    /// <summary>The class resource (Rage, Combo...), if any.</summary>
    public Func<ClassResource?> Resource = () => null;

    /// <summary>Right half when mana is hidden; null leaves it empty.</summary>
    public Func<SideGauge?> RightGauge = () => null;

    private const float CenterX = Width / 2f;
    /// <summary>The oval's centre sits on the screen's bottom edge: only its upper half
    /// shows, like the sketch's mirror rising out of the frame.</summary>
    private static readonly Vector2 Mirror = new(CenterX, Height);

    /// <summary>Where the visible mirror ends (the screen's bottom edge): the liquids' floor.</summary>
    private const float BaseY = Height;

    /// <summary>The visible rim runs from the left end (π) over the top to the right end (2π).</summary>
    private const float ArcFrom = Mathf.Pi;
    private const float ArcTo = Mathf.Tau;
    private const float RimW = 9f;

    /// <summary>Top of the skill slots, in this control's coordinates (they sit on the same baseline).</summary>
    private const float SlotTop = Height - 14f - SkillBar.SlotSize;

    private static readonly Color LifeTop = new(0.98f, 0.3f, 0.26f);
    private static readonly Color LifeBottom = new(0.5f, 0.04f, 0.07f);
    private static readonly Color ManaTop = new(0.38f, 0.82f, 1f);
    private static readonly Color ManaBottom = new(0.04f, 0.2f, 0.62f);
    private static readonly Color Glass = new(0.04f, 0.03f, 0.07f, 0.92f);
    private static readonly Color Rim = new(0.9f, 0.76f, 0.42f);
    private static readonly Color XpPurple = new(0.7f, 0.32f, 0.95f);
    private static readonly Color XpTrack = new(0.1f, 0.06f, 0.14f, 0.95f);
    private static readonly Color Track = new(0.05f, 0.04f, 0.07f, 0.88f);

    private float _lifeTrail = 1f;
    private float _manaTrail = 1f;
    private float _time;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(Width, Height);
        Size = new Vector2(Width, Height);
    }

    /// <summary>The skill bar (LMB, RMB | mirror | Q, E), docked on the cluster's baseline.
    /// Its own control so the HUD editor can move it separately.</summary>
    public static SkillBar CreateSkillRow(Control root)
    {
        var bar = new SkillBar { Name = "SkillBar" };
        root.AddChild(bar);
        bar.SetAnchorsPreset(LayoutPreset.CenterBottom, true);
        bar.GrowHorizontal = GrowDirection.Both;
        bar.OffsetLeft = -SkillBar.TotalWidth / 2f;
        bar.OffsetRight = SkillBar.TotalWidth / 2f;
        bar.OffsetTop = -14f - SkillBar.SlotSize - SkillBar.KeyRoom;
        bar.OffsetBottom = -14f;
        return bar;
    }

    /// <summary>Places the cluster at the bottom centre of a full-rect parent.</summary>
    public void Dock()
    {
        SetAnchorsPreset(LayoutPreset.CenterBottom, true);
        OffsetLeft = -Width / 2f;
        OffsetRight = Width / 2f;
        OffsetTop = -Height;
        OffsetBottom = 0f;
    }

    /// <summary>The life and mana values show (hovering the mirror, or the character panel open).</summary>
    public bool NumbersShowing { get; private set; }

    /// <summary>The mouse is over the visible half of the mirror (the control itself ignores the mouse).</summary>
    private bool MirrorHovered()
    {
        Vector2 m = GetLocalMousePosition();
        if (m.Y > BaseY)
        {
            return false;
        }

        Vector2 d = m - Mirror;
        float rx = MirrorRx + RimW + 3f;
        float ry = MirrorRy + RimW + 3f;
        return d.X * d.X / (rx * rx) + d.Y * d.Y / (ry * ry) <= 1f;
    }

    private bool CharacterPanelOpen() =>
        GetTree().CurrentScene?.GetNodeOrNull<CharacterPanels>("CharacterPanels") is { Sheet.IsOpen: true };

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        NumbersShowing = IsVisibleInTree() && (MirrorHovered() || CharacterPanelOpen());
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

    /// <summary>A point on the mirror's oval (t = 0 right, π/2 bottom, π left, 3π/2 top).</summary>
    private static Vector2 OnOval(float t, float grow = 0f) =>
        Mirror + new Vector2(Mathf.Cos(t) * (MirrorRx + grow), Mathf.Sin(t) * (MirrorRy + grow));

    public override void _Draw()
    {
        Font font = ThemeDB.FallbackFont;

        // --- the glass and its two liquids ---
        DrawColoredPolygon(UpperHalf(2f), Glass);
        DrawHalf(-1, Fraction(Player.Stats.Life), _lifeTrail, LifeTop, LifeBottom);
        SideGauge? gauge = null;
        if (ShowMana)
        {
            DrawHalf(1, Fraction(Player.Stats.Mana), _manaTrail, ManaTop, ManaBottom);
        }
        else if (RightGauge() is { } g)
        {
            gauge = g;
            float f = Mathf.Clamp(g.Fraction, 0f, 1f);
            DrawHalf(1, f, f, g.Top, g.Bottom);
        }

        // Energy shield: a pale band hugging the inside of the life half, emptying
        // downwards. A co-op client never sees the host's shield numbers, so it shows none.
        if (Player.Health is { MaxEnergyShield: > 0f } health && !Net.NetMode.ClientSide)
        {
            float frac = Mathf.Clamp(health.EnergyShield / health.MaxEnergyShield, 0f, 1f);
            DrawOvalArc(ArcFrom, ArcFrom + Mathf.Pi / 2f * frac, -7f, 3f, new Color(0.7f, 0.9f, 1f, 0.9f));
        }

        // Glass: the split down the middle and a soft reflection top-left.
        DrawLine(Mirror + new Vector2(0f, -MirrorRy), new Vector2(Mirror.X, BaseY), new Color(0.02f, 0.01f, 0.03f, 0.95f), 3f);
        DrawLine(Mirror + new Vector2(0f, -MirrorRy + 4f), new Vector2(Mirror.X, BaseY), Rim with { A = 0.35f }, 1f);
        DrawOvalArc(Mathf.Pi * 1.08f, Mathf.Pi * 1.42f, -14f, 4f, new Color(1f, 1f, 1f, 0.12f));
        DrawOvalArc(Mathf.Pi * 1.12f, Mathf.Pi * 1.3f, -20f, 2f, new Color(1f, 1f, 1f, 0.1f));

        // --- the rim: experience, ten notches ---
        (int level, float xp) = Progress();
        DrawOvalArc(ArcFrom, ArcTo, RimW / 2f + 1f, RimW + 4f, new Color(0.02f, 0.015f, 0.03f, 0.95f));
        DrawOvalArc(ArcFrom, ArcTo, RimW / 2f + 1f, RimW, XpTrack);
        float x = Mathf.Clamp(xp, 0f, 1f);
        if (x > 0.002f)
        {
            // From the bottom left, up and over the top, down to the bottom right.
            float end = ArcFrom + (ArcTo - ArcFrom) * x;
            DrawOvalArc(ArcFrom, end, RimW / 2f + 1f, RimW - 2f, XpPurple);
            DrawOvalArc(ArcFrom, end, RimW / 2f - 1f, 1.5f, new Color(1f, 0.85f, 1f, 0.45f));
        }

        for (int i = 1; i < 10; i++)
        {
            float t = ArcFrom + (ArcTo - ArcFrom) * i / 10f;
            DrawLine(OnOval(t, -1f), OnOval(t, RimW + 3f), new Color(0f, 0f, 0f, 0.95f), 2.5f);
        }

        DrawOvalArc(ArcFrom, ArcTo, -1f, 1.5f, Rim with { A = 0.85f });
        DrawOvalArc(ArcFrom, ArcTo, RimW + 3f, 2f, Rim);

        // --- numbers: the current value only, and only while the mirror is hovered or the
        // character panel is open (owner, 2026-10-29) ---
        if (NumbersShowing && Player.Stats.Life is { } life)
        {
            int current = Mathf.Min(Mathf.CeilToInt(life.Current), Mathf.RoundToInt(life.Max));
            Text(font, $"{current:N0}", new Vector2(Mirror.X - MirrorRx * 0.45f, BaseY - 18f), 17, Colors.White, HorizontalAlignment.Center);
        }

        if (ShowMana)
        {
            if (NumbersShowing)
            {
                Text(font, $"{Mathf.FloorToInt(Player.Stats.Mana!.Current):N0}", new Vector2(Mirror.X + MirrorRx * 0.45f, BaseY - 18f), 17, Colors.White, HorizontalAlignment.Center);
            }
        }
        else if (gauge is { } gg)
        {
            // The Rift: the right half carries the run's pressure; its words sit over the right skills.
            Text(font, gg.Label, new Vector2(CenterX + MirrorClearance, SlotTop - 26f), 14, Colors.White, HorizontalAlignment.Left);
            Text(font, gg.Detail, new Vector2(CenterX + MirrorClearance, SlotTop - 10f), 12, gg.Top.Lightened(0.35f), HorizontalAlignment.Left);
        }

        // --- the level on a gem at the top of the rim ---
        Vector2 gem = Mirror + new Vector2(0f, -MirrorRy - RimW / 2f - 2f);
        var diamond = new[] { gem + new Vector2(0f, -17f), gem + new Vector2(19f, 0f), gem + new Vector2(0f, 17f), gem + new Vector2(-19f, 0f) };
        DrawColoredPolygon(diamond, new Color(0.13f, 0.08f, 0.2f));
        DrawPolyline(new[] { diamond[0], diamond[1], diamond[2], diamond[3], diamond[0] }, Rim, 2f, true);
        Text(font, level.ToString(), gem + new Vector2(0f, 6f), 16, Colors.White, HorizontalAlignment.Center);

        // Dodge charges: small diamonds over the gem.
        if (Player.Dodge is { } dodge)
        {
            int max = dodge.MaxCharges;
            float start = CenterX - (max - 1) * 9f;
            for (int i = 0; i < max; i++)
            {
                float fill = i < dodge.Charges ? 1f : i == dodge.Charges ? dodge.RechargeProgress : 0f;
                Diamond(new Vector2(start + i * 18f, gem.Y - 30f), 6f, fill);
            }
        }

        // The class resource over the right skills (stamina lives under the bars over the
        // character since 2026-10-29: OverheadBars).
        float sideLen = SkillBar.SideWidth;
        float leftStart = CenterX - MirrorClearance - sideLen;

        if (Resource() is { } res)
        {
            float rx = CenterX + MirrorClearance;
            float pipW = Mathf.Min(14f, (sideLen - 2f * res.Max) / Mathf.Max(1, res.Max));
            for (int i = 0; i < res.Max; i++)
            {
                DrawRect(new Rect2(rx + i * (pipW + 2f), SlotTop - 9f, pipW, 5f), i < res.Value ? res.Color : Track);
            }

            Text(font, $"{res.Label} {res.Value}/{res.Max}", new Vector2(rx, SlotTop - 15f), 11, new Color(0.75f, 0.72f, 0.85f), HorizontalAlignment.Left);
        }

        // --- flasks at the outer ends of the skill bar ---
        if (Flasks.Of(Player) is { } flasks)
        {
            const float fs = 38f;
            float y = Height - 14f - fs;
            DrawFlask(font, new Rect2(leftStart - 10f - fs, y, fs, fs), flasks.Life, "1", LifeTop, LifeBottom);
            if (ShowMana && flasks.Mana.Enabled)
            {
                DrawFlask(font, new Rect2(CenterX + MirrorClearance + sideLen + 10f, y, fs, fs), flasks.Mana, "2", ManaTop, ManaBottom);
            }
        }
    }

    /// <summary>The visible (upper) half of the oval as a polygon, closed along the bottom edge.</summary>
    private static Vector2[] UpperHalf(float grow, int steps = 40)
    {
        var pts = new Vector2[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            pts[i] = OnOval(ArcFrom + (ArcTo - ArcFrom) * i / steps, grow);
        }

        return pts;
    }

    /// <summary>An arc of the oval from <paramref name="from"/> to <paramref name="to"/> (radians, see <see cref="OnOval"/>).</summary>
    private void DrawOvalArc(float from, float to, float grow, float width, Color color)
    {
        int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(to - from) / Mathf.Tau * 96f));
        var pts = new Vector2[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            pts[i] = OnOval(from + (to - from) * i / steps, grow);
        }

        DrawPolyline(pts, color, width, true);
    }

    /// <summary>One half of the mirror (-1 left, 1 right) filled from the bottom up to
    /// <paramref name="value"/>, with the paler trail above it and a gently moving surface.</summary>
    private void DrawHalf(int side, float value, float trail, Color top, Color bottom)
    {
        if (trail > value + 0.01f)
        {
            Vector2[]? t = HalfShape(side, trail, 0f);
            if (t is not null)
            {
                DrawColoredPolygon(t, new Color(1f, 0.85f, 0.6f, 0.35f));
            }
        }

        Vector2[]? fill = HalfShape(side, value, value < 0.995f ? 1.6f : 0f);
        if (fill is null)
        {
            return;
        }

        var colors = new Color[fill.Length];
        float yTop = Mirror.Y - MirrorRy;
        float span = BaseY - yTop;
        for (int i = 0; i < fill.Length; i++)
        {
            float k = Mathf.Clamp((fill[i].Y - yTop) / span, 0f, 1f);
            colors[i] = top.Lerp(bottom, k);
        }

        DrawPolygon(fill, colors);
        // The surface catches the light.
        if (value < 0.995f && value > 0.01f)
        {
            float y = SurfaceY(value);
            float half = HalfWidthAt(y);
            float x0 = side < 0 ? Mirror.X - half + 2f : Mirror.X + 2f;
            DrawLine(new Vector2(x0, y + 1f), new Vector2(x0 + half - 4f, y + 1f), new Color(1f, 1f, 1f, 0.3f), 1.5f);
        }
    }

    /// <summary>The liquid's surface: empty at the screen edge, full at the top of the oval.</summary>
    private static float SurfaceY(float value) => BaseY - (BaseY - (Mirror.Y - MirrorRy)) * Mathf.Clamp(value, 0f, 1f);

    private static float HalfWidthAt(float y)
    {
        float k = (y - Mirror.Y) / MirrorRy;
        return MirrorRx * Mathf.Sqrt(Mathf.Max(0f, 1f - k * k));
    }

    /// <summary>The filled part of one half as a polygon: the centre line, the surface and the
    /// oval's edge down to the bottom. Null when there is too little to draw.</summary>
    private Vector2[]? HalfShape(int side, float value, float wave)
    {
        if (value <= 0.005f)
        {
            return null;
        }

        float surface = SurfaceY(value);
        float bottomY = BaseY;
        if (bottomY - surface < 1.5f)
        {
            return null;
        }

        var pts = new List<Vector2>();
        // The surface, from the centre line outwards, with a small travelling wave.
        const int surfaceSteps = 8;
        float half = HalfWidthAt(surface);
        for (int i = 0; i <= surfaceSteps; i++)
        {
            float u = (float)i / surfaceSteps;
            float y = surface + Mathf.Sin(_time * 2.4f + u * 5f + side) * wave * Mathf.Sin(u * Mathf.Pi);
            pts.Add(new Vector2(Mirror.X + side * half * u, y));
        }

        // Down the oval's edge.
        const int edgeSteps = 20;
        for (int i = 1; i <= edgeSteps; i++)
        {
            float y = surface + (bottomY - surface) * i / edgeSteps;
            pts.Add(new Vector2(Mirror.X + side * HalfWidthAt(y), y));
        }

        pts.Add(new Vector2(Mirror.X, bottomY));
        if (side > 0)
        {
            pts.Reverse();
        }

        return pts.ToArray();
    }

    private void DrawFlask(Font font, Rect2 r, Flask flask, string key, Color top, Color bottom)
    {
        bool empty = flask.Usable < 1;
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.04f, 0.08f, 0.9f),
            BorderColor = flask.Active ? top : Rim with { A = 0.7f },
        };
        box.SetCornerRadiusAll(6);
        box.SetBorderWidthAll(flask.Active ? 2 : 1);
        if (flask.Active)
        {
            box.ShadowColor = top with { A = 0.5f };
            box.ShadowSize = 8;
        }

        DrawStyleBox(box, r);

        // The bottle: a round body, a neck and a cork; liquid level = charges.
        Vector2 c = r.Position + new Vector2(r.Size.X * 0.5f, r.Size.Y * 0.6f);
        float br = r.Size.X * 0.24f;
        DrawRect(new Rect2(c.X - 3.5f, c.Y - br - 8f, 7f, 9f), new Color(0.75f, 0.85f, 0.95f, 0.35f));
        DrawRect(new Rect2(c.X - 4.5f, c.Y - br - 12f, 9f, 4.5f), new Color(0.55f, 0.36f, 0.2f));
        DrawCircle(c, br, new Color(0.75f, 0.85f, 0.95f, 0.22f));
        float level = Mathf.Clamp(flask.Charges / flask.MaxCharges, 0f, 1f);
        if (level > 0.01f)
        {
            // A circular segment: the arc below the liquid surface.
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

            if (a2 - a1 > 0.05f)
            {
                DrawColoredPolygon(pts.ToArray(), empty ? bottom.Darkened(0.4f) : top.Lerp(bottom, 0.3f));
            }

            DrawCircle(c + new Vector2(-br * 0.35f, -br * 0.3f), br * 0.18f, new Color(1f, 1f, 1f, 0.45f));
        }

        DrawArc(c, br, 0f, Mathf.Tau, 32, new Color(0.85f, 0.92f, 1f, 0.7f), 1.5f, true);

        // Regeneration towards the next charge, along the bottom edge.
        if (!flask.Full)
        {
            DrawRect(new Rect2(r.Position.X + 4f, r.End.Y - 5f, (r.Size.X - 8f) * flask.Partial, 2f), top with { A = 0.8f });
        }

        SkillBar.DrawKeyCap(this, font, r.Position + new Vector2(2f, 2f), key, 10);
        Text(font, flask.Usable.ToString(), r.Position + new Vector2(r.Size.X - 3f, r.Size.Y - 4f), 12, empty ? new Color(1f, 0.4f, 0.35f) : Colors.White, HorizontalAlignment.Right);
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
