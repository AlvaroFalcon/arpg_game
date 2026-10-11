#nullable enable
using System.Collections.Generic;
using Godot;

namespace PathToExile.Combat;

/// <summary>
/// The runtime instance of an area hit — docs/design/progression.md -> "New
/// archetypes" and docs/design/bosses.md -> "Telegraphs". Lives at a fixed
/// point, waits <see cref="Delay"/> seconds while drawing its impact circle on
/// the ground (an outline that fills from the centre, so the moment of impact
/// is readable at a glance), then damages every hostile inside the radius
/// exactly once, plays a short expanding ring, and frees itself.
///
/// Used by player skills (Frost Nova, Meteor) and by every boss slam alike.
/// Hostile telegraphs (enemy-sourced) always draw in warning red regardless of
/// element, so players never have to learn per-boss colour codes for "this
/// will hurt me".
/// </summary>
[GlobalClass]
public partial class AreaBurst : Node2D
{
    private const float FlashTime = 0.25f;

    public float Radius = 80f;
    public float Delay;
    public Color Tint = new(1f, 0.5f, 0.2f);
    public HitProfile Hit = new();

    /// <summary>Optional inner safe radius (a ring/donut attack). Hostiles closer
    /// than this to the centre are not hit.</summary>
    public float InnerRadius;

    /// <summary>Telegraph only: draws and "lands" but deals no damage (spawn
    /// markers for summons).</summary>
    public bool Harmless;

    /// <summary>Emitted at impact with the number of targets hit.</summary>
    public System.Action<int>? Impacted;

    private float _elapsed;
    private bool _detonated;

    public static AreaBurst Spawn(Node parent, Vector2 position, float radius, float delay, Color tint, HitProfile hit, float innerRadius = 0f, bool harmless = false)
    {
        var burst = new AreaBurst
        {
            Harmless = harmless,
            Radius = radius,
            Delay = delay,
            Tint = tint,
            Hit = hit,
            InnerRadius = innerRadius,
            ZIndex = -1,
        };
        parent.AddChild(burst);
        burst.GlobalPosition = position;
        return burst;
    }

    private bool IsHostileTelegraph => Hit.Faction != Faction.Player && !Harmless;

    public override void _PhysicsProcess(double delta)
    {
        _elapsed += (float)delta;
        if (!_detonated && _elapsed >= Delay)
        {
            Detonate();
        }

        if (_detonated && _elapsed >= Delay + FlashTime)
        {
            QueueFree();
        }

        QueueRedraw();
    }

    private void Detonate()
    {
        _detonated = true;
        int hits = 0;
        if (Harmless)
        {
            Impacted?.Invoke(0);
            return;
        }

        foreach (Health health in CombatQuery.HostilesInRadius(GetTree(), GlobalPosition, Radius, Hit.Faction))
        {
            var target = health.GetParent<Node2D>();
            Vector2 offset = target.GlobalPosition - GlobalPosition;
            if (InnerRadius > 0f && offset.Length() < InnerRadius - 8f)
            {
                continue;
            }

            health.TakeDamage(Hit.BuildPayload(offset.Normalized()));
            hits++;
        }

        if (IsHostileTelegraph)
        {
            Core.CameraShake.Request(this, Mathf.Clamp(Radius / 30f, 2f, 10f));
        }

        Impacted?.Invoke(hits);
    }

    public override void _Draw()
    {
        Color baseColor = IsHostileTelegraph ? new Color(1f, 0.15f, 0.1f) : Tint;
        if (!_detonated)
        {
            float t = Delay > 0f ? Mathf.Clamp(_elapsed / Delay, 0f, 1f) : 1f;
            DrawZone(baseColor with { A = 0.16f }, 1f);
            DrawZone(baseColor with { A = 0.35f + 0.25f * t }, t);
            DrawArc(Vector2.Zero, Radius, 0f, Mathf.Tau, 64, baseColor with { A = 0.9f }, 3f);
            if (InnerRadius > 0f)
            {
                DrawArc(Vector2.Zero, InnerRadius, 0f, Mathf.Tau, 48, baseColor with { A = 0.9f }, 3f);
            }

            return;
        }

        float f = Mathf.Clamp((_elapsed - Delay) / FlashTime, 0f, 1f);
        Color flash = (IsHostileTelegraph ? new Color(1f, 0.55f, 0.3f) : Tint.Lightened(0.3f)) with { A = 0.7f * (1f - f) };
        DrawZone(flash, 1f);
        DrawArc(Vector2.Zero, Radius * (0.9f + 0.2f * f), 0f, Mathf.Tau, 64, flash with { A = 1f - f }, 4f);
    }

    /// <summary>Fills the hittable zone, scaled from its inner edge by <paramref name="progress"/>.</summary>
    private void DrawZone(Color color, float progress)
    {
        if (InnerRadius <= 0f)
        {
            DrawCircle(Vector2.Zero, Radius * progress, color);
            return;
        }

        // Donut: fill from the outer edge inwards as a thick arc.
        float thickness = (Radius - InnerRadius) * progress;
        float mid = Radius - thickness / 2f;
        DrawArc(Vector2.Zero, mid, 0f, Mathf.Tau, 72, color, thickness);
    }
}
