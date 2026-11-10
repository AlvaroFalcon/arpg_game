#nullable enable
using System;
using Godot;
using ArpgGame.Enemies;

namespace ArpgGame.UI;

/// <summary>
/// An arrow at the edge of the screen pointing at something the camera does not show —
/// docs/design/bosses.md -> "Off-screen indicator". The HUDs track the boss with it (a
/// reality's guardian during its fight, the Rift's Colossus); it is generic so allies, the
/// exit portal or a quest can use it later.
///
/// A full-rect, click-through control on a HUD layer. Every frame it maps the target's
/// world position to the screen through the viewport's canvas transform (the Camera2D;
/// the voxel view lines up with it pixel for pixel). While that point is inside the
/// screen nothing is drawn; once it leaves, a badge sits where the line from the centre
/// of the screen to the target crosses the <see cref="Insets"/> rectangle, with an arrow
/// on its outer side. The insets keep it clear of the HUD's top bar and bottom vitals.
/// </summary>
public partial class OffscreenIndicator : Control
{
    /// <summary>What to point at; null (or a dead or freed enemy) hides the indicator.</summary>
    public Func<Node2D?> Target = () => null;

    /// <summary>True while something else covers the screen (the Tab map): no indicator.</summary>
    public Func<bool> Suppressed = () => false;

    /// <summary>The badge's ring and arrow.</summary>
    public Color Accent = new(1f, 0.3f, 0.25f);

    /// <summary>Distance kept from each screen edge (left, top, right, bottom): the
    /// boss bar is at the top, the life globes and skills at the bottom.</summary>
    public (float Left, float Top, float Right, float Bottom) Insets = (56f, 130f, 56f, 170f);

    /// <summary>The target counts as on screen while it is this far inside the edge.</summary>
    public const float OnScreenMargin = 24f;

    public const float BadgeRadius = 15f;

    /// <summary>Whether the indicator is showing this frame (tests).</summary>
    public bool Showing { get; private set; }

    /// <summary>Where the badge is, in screen pixels, while <see cref="Showing"/> (tests).</summary>
    public Vector2 BadgeAt { get; private set; }

    /// <summary>Direction from the badge towards the target (unit vector).</summary>
    public Vector2 Pointing { get; private set; } = Vector2.Right;

    /// <summary>The target's last screen position, in pixels (tests).</summary>
    public Vector2 ScreenPoint { get; private set; }

    private float _time;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        bool was = Showing;
        Showing = false;

        Node2D? target = Target();
        if (target is not null && IsInstanceValid(target) && target.IsInsideTree() && !target.IsQueuedForDeletion()
            && target is not Enemy { Health.IsDead: true } && !Suppressed())
        {
            Vector2 screenSize = GetViewportRect().Size;
            Vector2 at = GetViewport().CanvasTransform * target.GlobalPosition;
            ScreenPoint = at;
            var screen = new Rect2(Vector2.Zero, screenSize);
            if (!screen.Grow(-OnScreenMargin).HasPoint(at))
            {
                var area = new Rect2(Insets.Left, Insets.Top, screenSize.X - Insets.Left - Insets.Right, screenSize.Y - Insets.Top - Insets.Bottom);
                if (area.Size.X > 0f && area.Size.Y > 0f)
                {
                    BadgeAt = EdgePoint(area, at);
                    Pointing = (at - BadgeAt).Normalized();
                    Showing = true;
                }
            }
        }

        if (Showing || was)
        {
            QueueRedraw();
        }
    }

    /// <summary>
    /// Where the line from the centre of <paramref name="area"/> to <paramref name="at"/>
    /// leaves the area (<paramref name="at"/> itself when it is inside).
    /// </summary>
    public static Vector2 EdgePoint(Rect2 area, Vector2 at)
    {
        if (area.HasPoint(at))
        {
            return at;
        }

        Vector2 c = area.GetCenter();
        Vector2 d = at - c;
        float t = float.MaxValue;
        if (d.X > 0f)
        {
            t = Mathf.Min(t, (area.End.X - c.X) / d.X);
        }
        else if (d.X < 0f)
        {
            t = Mathf.Min(t, (area.Position.X - c.X) / d.X);
        }

        if (d.Y > 0f)
        {
            t = Mathf.Min(t, (area.End.Y - c.Y) / d.Y);
        }
        else if (d.Y < 0f)
        {
            t = Mathf.Min(t, (area.Position.Y - c.Y) / d.Y);
        }

        return t == float.MaxValue ? c : c + d * Mathf.Clamp(t, 0f, 1f);
    }

    public override void _Draw()
    {
        if (!Showing)
        {
            return;
        }

        // A slow pulse so it reads as "over there" rather than as part of the HUD.
        float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 5f);
        Vector2 c = BadgeAt;
        Vector2 dir = Pointing;
        Vector2 side = new(-dir.Y, dir.X);

        // The arrow, outside the badge, pointing at the target.
        float r = BadgeRadius;
        Vector2 tip = c + dir * (r + 13f + 3f * pulse);
        Vector2 baseL = c + dir * (r + 2f) + side * 9f;
        Vector2 baseR = c + dir * (r + 2f) - side * 9f;
        var arrow = new[] { tip, baseL, baseR };
        DrawColoredPolygon(new[] { tip + dir * 2f, baseL + side * 2f - dir, baseR - side * 2f - dir }, new Color(0f, 0f, 0f, 0.75f));
        DrawColoredPolygon(arrow, Accent);

        // The badge: a dark disc, a ring in the accent and the boss star inside.
        DrawCircle(c, r + 2f, new Color(0f, 0f, 0f, 0.7f));
        DrawCircle(c, r, new Color(0.18f, 0.05f, 0.05f, 0.92f));
        DrawArc(c, r, 0f, Mathf.Tau, 32, Accent, 2.5f, true);
        DrawArc(c, r + 4f + 3f * pulse, 0f, Mathf.Tau, 32, Accent with { A = 0.35f * (1f - pulse) + 0.1f }, 1.5f, true);
        Star(c, 8f, new Color(1f, 0.92f, 0.85f));
    }

    private void Star(Vector2 c, float r, Color color)
    {
        var points = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = -Mathf.Pi / 2f + i * Mathf.Pi / 5f;
            float radius = i % 2 == 0 ? r : r * 0.45f;
            points[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        DrawColoredPolygon(points, color);
    }
}
