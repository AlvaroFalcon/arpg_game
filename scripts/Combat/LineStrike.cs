#nullable enable
using Godot;

namespace ArpgGame.Combat;

/// <summary>
/// A rectangular telegraphed hit — docs/design/bosses.md -> "Telegraphs". A
/// lane from <see cref="Node2D.GlobalPosition"/> along <see cref="Node2D.Rotation"/>
/// of <see cref="Length"/> x <see cref="Width"/>, drawn on the ground and
/// filling from its origin for <see cref="Delay"/> seconds, then damaging every
/// hostile inside it once (beam flash). Same visual language as
/// <see cref="AreaBurst"/>: hostile red, faint zone + filling progress + solid
/// outline, so "how long until it hits" is always readable.
/// </summary>
public partial class LineStrike : Node2D
{
    private const float FlashTime = 0.3f;

    public float Length = 600f;
    public float Width = 60f;
    public float Delay = 1f;
    public HitProfile Hit = new();
    public System.Action? Impacted;

    /// <summary>Telegraph only (a charge lane: the boss's body is the hit).</summary>
    public bool Harmless;

    private float _elapsed;

    /// <summary>Seconds until impact (0 once it has landed).</summary>
    public float TimeToImpact => _fired ? 0f : Mathf.Max(0f, Delay - _elapsed);
    private bool _fired;

    public static LineStrike Spawn(Node parent, Vector2 origin, Vector2 direction, float length, float width, float delay, HitProfile hit)
    {
        var strike = new LineStrike { Length = length, Width = width, Delay = delay, Hit = hit, ZIndex = -1 };
        parent.AddChild(strike);
        strike.GlobalPosition = origin;
        strike.Rotation = direction.Angle();
        return strike;
    }

    public override void _PhysicsProcess(double delta)
    {
        _elapsed += (float)delta;
        if (!_fired && _elapsed >= Delay)
        {
            Fire();
        }

        if (_fired && _elapsed >= Delay + FlashTime)
        {
            QueueFree();
        }

        QueueRedraw();
    }

    private void Fire()
    {
        _fired = true;
        if (Harmless)
        {
            Impacted?.Invoke();
            return;
        }

        Vector2 dir = Vector2.Right.Rotated(Rotation);
        foreach (Node node in GetTree().GetNodesInGroup(CombatQuery.HostileGroupFor(Hit.Faction)))
        {
            if (node is not Node2D target || target.GetNodeOrNull<Health>("Health") is not { IsDead: false } health)
            {
                continue;
            }

            Vector2 rel = target.GlobalPosition - GlobalPosition;
            float along = rel.Dot(dir);
            float across = Mathf.Abs(rel.Cross(dir));
            if (along >= -10f && along <= Length + 10f && across <= Width / 2f + 10f)
            {
                health.TakeDamage(Hit.BuildPayload(new Vector2(-dir.Y, dir.X) * Mathf.Sign(rel.Cross(dir))));
            }
        }

        if (Hit.Faction != Faction.Player)
        {
            Core.CameraShake.Request(this, 4f);
        }
        Impacted?.Invoke();
    }

    public override void _Draw()
    {
        // Hostile lanes are always red; the player's own (Sunder) use warm white.
        var red = Hit.Faction == Faction.Player ? new Color(1f, 0.85f, 0.55f) : new Color(1f, 0.15f, 0.1f);
        float hw = Width / 2f;
        if (!_fired)
        {
            float t = Delay > 0f ? Mathf.Clamp(_elapsed / Delay, 0f, 1f) : 1f;
            DrawRect(new Rect2(0f, -hw, Length, Width), red with { A = 0.15f });
            DrawRect(new Rect2(0f, -hw, Length * t, Width), red with { A = 0.35f });
            DrawRect(new Rect2(0f, -hw, Length, Width), red with { A = 0.9f }, false, 2f);
            return;
        }

        float f = Mathf.Clamp((_elapsed - Delay) / FlashTime, 0f, 1f);
        DrawRect(new Rect2(0f, -hw * (1f - f * 0.5f), Length, Width * (1f - f * 0.5f)), new Color(1f, 0.75f, 0.4f, 0.85f * (1f - f)));
        DrawRect(new Rect2(0f, -hw * 0.3f, Length, Width * 0.6f * (1f - f)), new Color(1f, 1f, 0.85f, 1f - f));
    }
}
