#nullable enable
using System.Collections.Generic;
using Godot;

namespace ArpgGame.Combat;

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

    /// <summary>Volatile-style blasts — docs/design/performance.md -> "Mass deaths".
    /// Bursts sharing a group (a) hit each target at most once per
    /// <see cref="GroupHitWindow"/> seconds, whichever lands first, and (b) draw
    /// one merged telegraph per cluster instead of one disc each. Null = neither.</summary>
    public string? MergeGroup;

    /// <summary>Seconds a target stays immune to further hits of the same
    /// <see cref="MergeGroup"/> after one lands (covers a pack's staggered blasts).</summary>
    public const float GroupHitWindow = 0.25f;

    // Physics frame before which this burst may not detonate (on-kill bursts wait
    // one frame per hop so cascades ripple instead of resolving at once).
    private ulong _holdUntilFrame;

    // Merged telegraphs: a cluster's first burst (the leader) draws one outline
    // covering every member; members draw only their own impact flash.
    private AreaBurst? _leader;
    private float _clusterRadius;
    private float _clusterEnd;
    private static readonly List<AreaBurst> OpenLeaders = new();

    // (group, target instance id) -> physics frame until which the target ignores that group.
    private static readonly Dictionary<(string, ulong), ulong> GroupImmunity = new();

    private float _elapsed;

    /// <summary>Seconds until impact (0 once it has landed).</summary>
    public float TimeToImpact => _detonated ? 0f : Mathf.Max(0f, Delay - _elapsed);
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

    /// <summary>An on-kill explosion (Chain Reaction, Decay): like a delay-0
    /// <see cref="Spawn"/>, but it detonates on the physics frame after the death
    /// that caused it, so a cascade spreads one hop per frame.</summary>
    public static AreaBurst SpawnOnKill(Node parent, Vector2 position, float radius, Color tint, HitProfile hit)
    {
        AreaBurst burst = Spawn(parent, position, radius, 0f, tint, hit);
        burst._holdUntilFrame = Engine.GetPhysicsFrames() + 1;
        return burst;
    }

    /// <summary>A blast that merges with nearby blasts of the same group (one
    /// telegraph per cluster, one hit per target per <see cref="GroupHitWindow"/>).</summary>
    public static AreaBurst SpawnMerged(Node parent, Vector2 position, float radius, float delay, Color tint, HitProfile hit, string group)
    {
        AreaBurst burst = Spawn(parent, position, radius, delay, tint, hit);
        burst.MergeGroup = group;
        burst._clusterRadius = radius;
        burst._clusterEnd = delay;
        for (int i = OpenLeaders.Count - 1; i >= 0; i--)
        {
            AreaBurst l = OpenLeaders[i];
            if (!IsInstanceValid(l) || l._detonated || l.IsQueuedForDeletion())
            {
                OpenLeaders.RemoveAt(i);
                continue;
            }

            float d = l.GlobalPosition.DistanceTo(position);
            if (l.MergeGroup == group && l.GetParent() == parent && d <= l.Radius)
            {
                burst._leader = l;
                l._clusterRadius = Mathf.Max(l._clusterRadius, d + radius);
                l._clusterEnd = Mathf.Max(l._clusterEnd, l._elapsed + delay);
                return burst;
            }
        }

        OpenLeaders.Add(burst);
        return burst;
    }

    private bool HasLiveLeader => _leader is not null && IsInstanceValid(_leader);

    private bool IsHostileTelegraph => Hit.Faction != Faction.Player && !Harmless;

    public override void _PhysicsProcess(double delta)
    {
        if (_holdUntilFrame > 0 && Engine.GetPhysicsFrames() < _holdUntilFrame)
        {
            QueueRedraw();
            return;
        }

        _elapsed += (float)delta;
        if (!_detonated && _elapsed >= Delay)
        {
            Detonate((float)delta);
        }

        // A cluster leader stays (drawing the merged outline) until its last member lands.
        if (_detonated && _elapsed >= Mathf.Max(Delay, _clusterEnd) + FlashTime)
        {
            QueueFree();
        }

        // A merged member has nothing to draw until its own impact flash.
        if (!(HasLiveLeader && !_detonated))
        {
            QueueRedraw();
        }
    }

    /// <summary>True if <paramref name="target"/> is immune to this burst's group
    /// right now; otherwise marks it immune for <see cref="GroupHitWindow"/>.</summary>
    private bool GroupBlocks(Node target, float delta)
    {
        if (MergeGroup is null)
        {
            return false;
        }

        ulong now = Engine.GetPhysicsFrames();
        var key = (MergeGroup, target.GetInstanceId());
        if (GroupImmunity.TryGetValue(key, out ulong until) && now < until)
        {
            return true;
        }

        if (GroupImmunity.Count > 64)
        {
            PruneImmunity(now);
        }

        GroupImmunity[key] = now + (ulong)Mathf.CeilToInt(GroupHitWindow / Mathf.Max(delta, 1e-4f));
        return false;
    }

    private static readonly List<(string, ulong)> Expired = new();

    private static void PruneImmunity(ulong now)
    {
        Expired.Clear();
        foreach (var kv in GroupImmunity)
        {
            if (kv.Value <= now)
            {
                Expired.Add(kv.Key);
            }
        }

        foreach (var k in Expired)
        {
            GroupImmunity.Remove(k);
        }
    }

    private void Detonate(float delta)
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

            if (GroupBlocks(target, delta))
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
        if (HasLiveLeader && !_detonated)
        {
            return; // the cluster leader draws this member's warning
        }

        float clusterEnd = Mathf.Max(Delay, _clusterEnd);
        if (MergeGroup is not null && _elapsed < clusterEnd)
        {
            // A cluster leader (or a lone merged blast): one outline covering every
            // member still to land, filling up to the last impact.
            float ct = clusterEnd > 0f ? Mathf.Clamp(_elapsed / clusterEnd, 0f, 1f) : 1f;
            DrawCircle(Vector2.Zero, _clusterRadius, baseColor with { A = 0.16f });
            DrawCircle(Vector2.Zero, _clusterRadius * ct, baseColor with { A = 0.35f + 0.25f * ct });
            DrawArc(Vector2.Zero, _clusterRadius, 0f, Mathf.Tau, 64, baseColor with { A = 0.9f }, 3f);
            if (_detonated)
            {
                DrawFlash();
            }

            return;
        }

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

        DrawFlash();
    }

    private void DrawFlash()
    {
        if (_elapsed > Delay + FlashTime)
        {
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
