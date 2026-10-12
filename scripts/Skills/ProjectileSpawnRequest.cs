using Godot;

namespace ArpgGame.Skills;

/// <summary>
/// One projectile to spawn: a direction and a forward offset from the cast
/// origin. The projectile executor always produces a *list* of these — never a
/// single projectile directly — so that count/spread (fanning N projectiles
/// across an angle) is a change to the generator that builds this list, not a
/// change to every caller that consumes it. The trivial, actually-implemented
/// case (docs/design/skills.md priority item 6) is a list of exactly one.
/// </summary>
public readonly struct ProjectileSpawnRequest
{
    public Vector2 Direction { get; }
    public float ForwardOffset { get; }

    public ProjectileSpawnRequest(Vector2 direction, float forwardOffset)
    {
        Direction = direction;
        ForwardOffset = forwardOffset;
    }
}
