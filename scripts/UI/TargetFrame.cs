#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Enemies;

namespace ArpgGame.UI;

/// <summary>
/// The target frame — docs/design/menus.md -> "Target frame". Modelled on the
/// owner's AION 2 reference: level and name over an ornate bar with pointed end
/// caps, phase marks, a recent-damage trail, a central emblem showing the
/// boss's element and a rank star at the end; under it the monster's modifiers
/// as tags, a cast bar that fills until a telegraphed attack lands, and the
/// ailments on it.
///
/// Shows the boss during its fight; otherwise whatever monster the mouse is
/// over (kept for a few seconds after), or else the nearest rare or unique
/// monster that is awake — so rares get their name, life and modifiers on
/// screen too.
/// </summary>
public partial class TargetFrame : Control
{
    public const float Width = 640f;
    public const float Height = 140f;

    public Player Player = null!;

    /// <summary>The boss to show while its fight is on (null otherwise).</summary>
    public Func<BossEnemy?> Boss = () => null;

    private const float BarLeft = 44f;
    private const float BarRight = Width - 44f;
    private const float BarTop = 34f;
    private const float BarH = 22f;
    private const float RareRange = 420f;
    private const float StickySeconds = 3f;

    private static readonly Color Bronze = new(0.86f, 0.68f, 0.4f);
    private static readonly Color Track = new(0.05f, 0.03f, 0.04f, 0.9f);

    private Enemy? _target;
    private Enemy? _hovered;
    private float _hoverTime;
    private float _trail = 1f;
    private float _alpha;
    private bool _preview;
    private float _time;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(Width, Height);
        Size = new Vector2(Width, Height);
    }

    /// <summary>Who the frame is showing right now (null when hidden).</summary>
    public Enemy? Target => _target;

    /// <summary>Top centre of a full-rect parent.</summary>
    public void Dock(float top)
    {
        SetAnchorsPreset(LayoutPreset.CenterTop, true);
        OffsetLeft = -Width / 2f;
        OffsetRight = Width / 2f;
        OffsetTop = top;
        OffsetBottom = top + Height;
    }

    /// <summary>HUD editor placeholder: a sample target so the frame can be placed.</summary>
    public void Preview(bool on)
    {
        _preview = on;
        QueueRedraw();
    }

    public override void _ExitTree() => GameCursor.SetAttack(this, false);

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        Enemy? target = PickTarget(dt);
        if (target != _target)
        {
            _target = target;
            _trail = target?.LifeFraction ?? 1f;
        }

        if (_target is not null)
        {
            float life = _target.LifeFraction;
            _trail = life > _trail ? life : Mathf.MoveToward(_trail, life, dt * 0.5f);
        }

        _alpha = Mathf.MoveToward(_alpha, _target is not null ? 1f : 0f, dt * 5f);
        QueueRedraw();
    }

    private static bool Alive(Enemy? e) => e is not null && IsInstanceValid(e) && e.IsInsideTree() && !e.Health.IsDead;

    private Enemy? PickTarget(float dt)
    {
        if (!IsInstanceValid(Player))
        {
            GameCursor.SetAttack(this, false);
            return null;
        }

        if (Boss() is { } boss && Alive(boss))
        {
            GameCursor.SetAttack(this, boss.GlobalPosition.DistanceTo(Player.GetGlobalMousePosition()) < 40f * boss.Scale.X);
            return boss;
        }

        // The monster under the mouse, kept a moment after the mouse moves on.
        Vector2 mouse = Player.GetGlobalMousePosition();
        Enemy? under = null;
        float best = 40f;
        // One pass, no LINQ: this runs every frame over every monster.
        Vector2 from = Player.GlobalPosition;
        Enemy? rare = null;
        float rareBest = RareRange;
        foreach (Node node in GetTree().GetNodesInGroup(EnemyGroupName))
        {
            if (node is not Enemy e || !Alive(e))
            {
                continue;
            }

            Vector2 at = e.GlobalPosition;
            float d = at.DistanceTo(mouse) - 10f * e.Scale.X;
            if (d < best)
            {
                best = d;
                under = e;
            }

            if (e.Rarity >= MonsterRarity.Rare && !e.Dormant)
            {
                float r = at.DistanceTo(from);
                if (r < rareBest)
                {
                    rareBest = r;
                    rare = e;
                }
            }
        }

        GameCursor.SetAttack(this, under is not null);
        if (under is not null)
        {
            _hovered = under;
            _hoverTime = StickySeconds;
        }
        else
        {
            _hoverTime -= dt;
        }

        if (_hoverTime > 0f && Alive(_hovered) && _hovered!.GlobalPosition.DistanceTo(Player.GlobalPosition) < 700f)
        {
            return _hovered;
        }

        // Otherwise the nearest awake rare or unique.
        return rare;
    }

    private static readonly StringName EnemyGroupName = CombatQuery.EnemyGroup;

    // --- drawing ---------------------------------------------------------------------

    private readonly record struct View(string Name, int Level, float Life, float Trail, Color Accent, bool Boss, MonsterRarity Rarity, float[] Phases, DamageType? Element, IReadOnlyList<string> Tags, string Cast, float CastProgress, Ailments? Ailments);

    private View? Current()
    {
        if (_preview && _target is null)
        {
            return new View("Boss name", 20, 0.62f, 0.75f, new Color(1f, 0.45f, 0.25f), true, MonsterRarity.Unique, new[] { 0.66f, 0.33f }, DamageType.Fire, new[] { "Hasted", "Volatile" }, "Attack name", 0.55f, null);
        }

        if (_target is null || !IsInstanceValid(_target))
        {
            return null;
        }

        Enemy e = _target;
        var boss = e as BossEnemy;
        string name = e.ShownName.Length > 0 ? e.ShownName : e.DisplayName;
        return new View(
            name,
            e.MonsterLevel,
            e.LifeFraction,
            _trail,
            boss is not null ? new Color(1f, 0.45f, 0.25f) : RarityColor(e.Rarity),
            boss is not null,
            e.Rarity,
            boss?.PhaseThresholds ?? Array.Empty<float>(),
            boss?.Element,
            e.Mods.Select(m => m.Name).ToList(),
            boss?.CurrentAttack ?? "",
            boss?.CastProgress ?? -1f,
            e.Ailments);
    }

    private static Color RarityColor(MonsterRarity r) => r switch
    {
        MonsterRarity.Magic => new Color(0.5f, 0.65f, 1f),
        MonsterRarity.Rare => new Color(1f, 0.85f, 0.35f),
        MonsterRarity.Unique => new Color(1f, 0.55f, 0.2f),
        _ => new Color(0.9f, 0.88f, 0.85f),
    };

    public override void _Draw()
    {
        if (Current() is not { } v || (_alpha <= 0.01f && !_preview))
        {
            return;
        }

        float a = _preview && _target is null ? 1f : _alpha;
        Font font = ThemeDB.FallbackFont;

        // --- name line: level badge + name in the rarity colour ---
        float nameW = font.GetStringSize(v.Name, HorizontalAlignment.Left, -1, 19).X;
        float nameX = Width / 2f - nameW / 2f + 14f;
        Vector2 lv = new(nameX - 22f, 16f);
        DrawCircle(lv, 12f, new Color(0.08f, 0.06f, 0.1f, 0.95f * a));
        DrawArc(lv, 12f, 0f, Mathf.Tau, 24, Bronze with { A = a }, 1.5f, true);
        Text(font, v.Level.ToString(), lv + new Vector2(0f, 5f), 13, Colors.White with { A = a }, HorizontalAlignment.Center);
        Text(font, v.Name, new Vector2(nameX, 23f), 19, v.Accent with { A = a }, HorizontalAlignment.Left);

        // --- the bar ---
        float mid = BarTop + BarH / 2f;
        var outer = new[]
        {
            new Vector2(BarLeft - 22f, mid), new Vector2(BarLeft, BarTop - 4f), new Vector2(BarRight, BarTop - 4f),
            new Vector2(BarRight + 22f, mid), new Vector2(BarRight, BarTop + BarH + 4f), new Vector2(BarLeft, BarTop + BarH + 4f),
        };
        DrawColoredPolygon(outer, new Color(0.02f, 0.015f, 0.03f, 0.92f * a));
        DrawRect(new Rect2(BarLeft, BarTop, BarRight - BarLeft, BarH), Track with { A = Track.A * a });
        float len = BarRight - BarLeft;
        if (v.Trail > v.Life + 0.005f)
        {
            DrawRect(new Rect2(BarLeft + len * v.Life, BarTop, len * (v.Trail - v.Life), BarH), new Color(1f, 0.95f, 0.8f, 0.75f * a));
        }

        if (v.Life > 0.002f)
        {
            Color top = new(0.95f, 0.28f, 0.2f, a);
            Color bottom = new(0.5f, 0.05f, 0.06f, a);
            var fill = new Rect2(BarLeft, BarTop, len * v.Life, BarH);
            DrawPolygon(new[] { fill.Position, new Vector2(fill.End.X, fill.Position.Y), fill.End, new Vector2(fill.Position.X, fill.End.Y) }, new[] { top, top, bottom, bottom });
            DrawLine(fill.Position + new Vector2(0f, 2f), new Vector2(fill.End.X, fill.Position.Y + 2f), new Color(1f, 1f, 1f, 0.3f * a), 2f);
        }

        // Phase marks; the next one glows as life closes in on it.
        foreach (float t in v.Phases)
        {
            float x = BarLeft + len * t;
            bool next = v.Life > t && v.Life - t < 0.1f;
            float glow = next ? 0.6f + 0.4f * Mathf.Sin(_time * 8f) : 0f;
            DrawLine(new Vector2(x, BarTop - 3f), new Vector2(x, BarTop + BarH + 3f), new Color(1f, 0.95f, 0.75f, (0.85f + 0.15f * glow) * a), next ? 3f : 2f);
        }

        DrawPolyline(outer.Append(outer[0]).ToArray(), Bronze with { A = 0.95f * a }, 2f, true);
        // Arrow-tip caps at both ends.
        foreach (int side in new[] { -1, 1 })
        {
            float x = side < 0 ? BarLeft - 22f : BarRight + 22f;
            DrawColoredPolygon(new[] { new Vector2(x, mid), new Vector2(x - side * 12f, mid - 7f), new Vector2(x - side * 8f, mid), new Vector2(x - side * 12f, mid + 7f) }, Bronze with { A = a });
        }

        Text(font, $"{Mathf.CeilToInt(v.Life * 100f)}%", new Vector2(BarRight - 8f, BarTop + 16f), 13, Colors.White with { A = a }, HorizontalAlignment.Right);

        // Central emblem: the boss's element (or a plain bronze stud for others).
        Vector2 c = new(Width / 2f, mid);
        DrawCircle(c, 19f, new Color(0.08f, 0.05f, 0.04f, a));
        DrawArc(c, 19f, 0f, Mathf.Tau, 32, Bronze with { A = a }, 2.5f, true);
        DrawArc(c, 14f, 0f, Mathf.Tau, 32, Bronze with { A = 0.5f * a }, 1f, true);
        Glyph(c, v.Element, a);

        // Rank: a star in a badge for bosses and uniques, a diamond for rares.
        if (v.Boss || v.Rarity >= MonsterRarity.Rare)
        {
            Vector2 s = new(BarRight + 12f, mid); // over the right tip, clear of the percentage
            if (v.Boss || v.Rarity == MonsterRarity.Unique)
            {
                DrawRect(new Rect2(s - new Vector2(10f, 12f), new Vector2(20f, 24f)), new Color(0.6f, 0.1f, 0.12f, 0.95f * a));
                Star(s, 8f, new Color(1f, 0.92f, 0.85f, a));
            }
            else
            {
                DrawColoredPolygon(new[] { s + new Vector2(0f, -8f), s + new Vector2(7f, 0f), s + new Vector2(0f, 8f), s + new Vector2(-7f, 0f) }, v.Accent with { A = a });
            }
        }

        // --- tags (monster modifiers) ---
        float tx = BarLeft + 8f;
        foreach (string tag in v.Tags)
        {
            float w = font.GetStringSize(tag, HorizontalAlignment.Left, -1, 12).X + 14f;
            var box = new Rect2(tx, 64f, w, 18f);
            DrawRect(box, new Color(0.35f, 0.12f, 0.1f, 0.85f * a));
            DrawRect(box, new Color(0.85f, 0.45f, 0.35f, 0.7f * a), false, 1f);
            Text(font, tag, new Vector2(tx + 7f, 77f), 12, new Color(1f, 0.85f, 0.8f, a), HorizontalAlignment.Left);
            tx += w + 5f;
        }

        // --- cast bar: fills until the telegraphed hit lands ---
        if (v.Cast.Length > 0 || v.CastProgress >= 0f)
        {
            float cw = 300f;
            var cast = new Rect2(Width / 2f - cw / 2f, 92f, cw, 7f);
            DrawRect(cast.Grow(1f), new Color(0f, 0f, 0f, 0.8f * a));
            if (v.CastProgress >= 0f)
            {
                DrawRect(new Rect2(cast.Position, new Vector2(cw * v.CastProgress, cast.Size.Y)), new Color(0.35f, 0.95f, 0.9f, a));
                DrawRect(new Rect2(cast.Position.X + cw * v.CastProgress - 2f, cast.Position.Y - 1f, 3f, cast.Size.Y + 2f), new Color(1f, 1f, 1f, a));
            }

            if (v.Cast.Length > 0)
            {
                Text(font, v.Cast, new Vector2(Width / 2f, 88f), 13, new Color(0.7f, 1f, 0.97f, a), HorizontalAlignment.Center);
            }
        }

        // --- ailments on the target ---
        DrawAilments(font, v.Ailments, a);
    }

    private void DrawAilments(Font font, Ailments? ail, float a)
    {
        var icons = new List<(Color Color, string Glyph, float Left, int Stacks)>();
        if (_preview && _target is null)
        {
            icons.Add((new Color(1f, 0.45f, 0.15f), "F", 0.7f, 0));
            icons.Add((new Color(0.45f, 0.85f, 1f), "C", 0.4f, 0));
            icons.Add((new Color(0.45f, 0.95f, 0.35f), "P", 0.9f, 4));
        }
        else if (ail is not null && IsInstanceValid(ail))
        {
            if (ail.IsIgnited) icons.Add((new Color(1f, 0.45f, 0.15f), "F", ail.IgniteRemaining / Ailments.IgniteDuration, 0));
            if (ail.IsChilled) icons.Add((new Color(0.45f, 0.85f, 1f), "C", ail.ChillRemaining / Ailments.ChillDuration, 0));
            if (ail.IsShocked) icons.Add((new Color(1f, 0.95f, 0.35f), "S", ail.ShockRemaining / Ailments.ShockDuration, 0));
            if (ail.PoisonStacks > 0) icons.Add((new Color(0.45f, 0.95f, 0.35f), "P", 1f, ail.PoisonStacks));
            if (ail.IsCursed) icons.Add((new Color(0.75f, 0.45f, 1f), "X", 1f, 0));
        }

        float x = Width / 2f - icons.Count * 13f;
        foreach ((Color color, string glyph, float left, int stacks) in icons)
        {
            var r = new Rect2(x, 108f, 22f, 22f);
            DrawRect(r, new Color(0.05f, 0.04f, 0.07f, 0.9f * a));
            DrawRect(new Rect2(r.Position.X, r.End.Y - r.Size.Y * Mathf.Clamp(left, 0f, 1f), r.Size.X, r.Size.Y * Mathf.Clamp(left, 0f, 1f)), color with { A = 0.35f * a });
            DrawRect(r, color with { A = a }, false, 1.5f);
            AilmentGlyph(r.GetCenter(), glyph, color with { A = a });
            if (stacks > 1)
            {
                Text(font, stacks.ToString(), r.End + new Vector2(-1f, -1f), 11, Colors.White with { A = a }, HorizontalAlignment.Right);
            }

            x += 26f;
        }
    }

    /// <summary>Tiny vector glyphs, so nothing depends on a font having emoji.</summary>
    private void AilmentGlyph(Vector2 c, string kind, Color color)
    {
        switch (kind)
        {
            case "F": // flame
                DrawColoredPolygon(new[] { c + new Vector2(0f, -7f), c + new Vector2(5f, 2f), c + new Vector2(0f, 6f), c + new Vector2(-5f, 2f) }, color);
                break;
            case "C": // snowflake
                for (int i = 0; i < 3; i++)
                {
                    Vector2 d = Vector2.Right.Rotated(i * Mathf.Pi / 3f) * 6f;
                    DrawLine(c - d, c + d, color, 1.5f);
                }

                break;
            case "S": // bolt
                DrawPolyline(new[] { c + new Vector2(2f, -7f), c + new Vector2(-3f, 0f), c + new Vector2(2f, 0f), c + new Vector2(-2f, 7f) }, color, 2f);
                break;
            case "P": // droplet
                DrawCircle(c + new Vector2(0f, 2f), 4f, color);
                DrawColoredPolygon(new[] { c + new Vector2(0f, -6f), c + new Vector2(3.5f, 1f), c + new Vector2(-3.5f, 1f) }, color);
                break;
            default: // curse: an X
                DrawLine(c + new Vector2(-5f, -5f), c + new Vector2(5f, 5f), color, 2f);
                DrawLine(c + new Vector2(5f, -5f), c + new Vector2(-5f, 5f), color, 2f);
                break;
        }
    }

    private void Glyph(Vector2 c, DamageType? element, float a)
    {
        float pulse = 0.85f + 0.15f * Mathf.Sin(_time * 3f);
        switch (element)
        {
            case DamageType.Fire:
                DrawCircle(c, 10f * pulse, new Color(1f, 0.4f, 0.1f, 0.45f * a));
                DrawColoredPolygon(new[] { c + new Vector2(0f, -9f), c + new Vector2(6f, 3f), c + new Vector2(0f, 8f), c + new Vector2(-6f, 3f) }, new Color(1f, 0.6f, 0.2f, a));
                DrawColoredPolygon(new[] { c + new Vector2(0f, -3f), c + new Vector2(3f, 3f), c + new Vector2(0f, 6f), c + new Vector2(-3f, 3f) }, new Color(1f, 0.95f, 0.6f, a));
                break;
            case DamageType.Cold:
                DrawCircle(c, 10f * pulse, new Color(0.3f, 0.7f, 1f, 0.45f * a));
                DrawCircle(c + new Vector2(0f, 2f), 5f, new Color(0.6f, 0.9f, 1f, a));
                DrawColoredPolygon(new[] { c + new Vector2(0f, -8f), c + new Vector2(4.5f, 1f), c + new Vector2(-4.5f, 1f) }, new Color(0.6f, 0.9f, 1f, a));
                break;
            case DamageType.Physical:
                // A skull: cranium, jaw, two eye sockets.
                DrawCircle(c + new Vector2(0f, -2f), 7f, new Color(0.92f, 0.88f, 0.8f, a));
                DrawRect(new Rect2(c + new Vector2(-4f, 3f), new Vector2(8f, 4f)), new Color(0.92f, 0.88f, 0.8f, a));
                DrawCircle(c + new Vector2(-2.6f, -2f), 1.8f, new Color(0.15f, 0.05f, 0.05f, a));
                DrawCircle(c + new Vector2(2.6f, -2f), 1.8f, new Color(0.15f, 0.05f, 0.05f, a));
                break;
            case DamageType.Lightning:
                DrawPolyline(new[] { c + new Vector2(3f, -9f), c + new Vector2(-4f, 0f), c + new Vector2(3f, 0f), c + new Vector2(-3f, 9f) }, new Color(1f, 0.95f, 0.4f, a), 2.5f);
                break;
            default:
                DrawCircle(c, 6f, Bronze with { A = 0.8f * a });
                break;
        }
    }

    private void Star(Vector2 c, float r, Color color)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float rad = i % 2 == 0 ? r : r * 0.45f;
            float ang = -Mathf.Pi / 2f + i * Mathf.Pi / 5f;
            pts[i] = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
        }

        DrawColoredPolygon(pts, color);
    }

    private void Text(Font font, string text, Vector2 at, int size, Color color, HorizontalAlignment align)
    {
        float w = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        float x = align switch { HorizontalAlignment.Right => at.X - w, HorizontalAlignment.Center => at.X - w / 2f, _ => at.X };
        DrawStringOutline(font, new Vector2(x, at.Y), text, HorizontalAlignment.Left, -1, size, 4, new Color(0f, 0f, 0f, 0.85f * color.A));
        DrawString(font, new Vector2(x, at.Y), text, HorizontalAlignment.Left, -1, size, color);
    }
}
