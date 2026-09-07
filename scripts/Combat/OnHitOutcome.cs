using Godot;

namespace PathToExile.Combat;

public enum OnHitOutcomeKind
{
    Despawn,
    Continue,
    Redirect,
    Fork,
}

/// <summary>
/// The result of a projectile's on-hit resolution step, made an explicit,
/// inspectable value rather than the hit-handler just deciding inline — see
/// docs/design/skills.md -> "Collision and hit resolution": "On-hit resolution is
/// an explicit step returning Despawn|Continue|Redirect(newDirection)." Continue
/// is what pierce uses; Redirect is reserved for chains (not implemented yet).
///
/// <para><b>Fork is a fourth kind, and the design doc was wrong about it.</b> The
/// doc asserts "Chain and fork are both 'Redirect with a decremented counter'".
/// That holds for chain (1 projectile continues toward a new target) but not for
/// fork: a fork turns one projectile into <i>two</i>, and a single
/// <c>NewDirection</c> can't express that. Rather than overload Redirect, Fork is
/// its own outcome — the parent despawns and the executor spawns the children.
/// Noted in docs/design/skills.md.</para>
/// </summary>
public readonly struct OnHitOutcome
{
    public OnHitOutcomeKind Kind { get; }
    public Vector2 NewDirection { get; }

    private OnHitOutcome(OnHitOutcomeKind kind, Vector2 newDirection)
    {
        Kind = kind;
        NewDirection = newDirection;
    }

    public static OnHitOutcome DespawnOutcome { get; } = new(OnHitOutcomeKind.Despawn, Vector2.Zero);

    public static OnHitOutcome ContinueOutcome { get; } = new(OnHitOutcomeKind.Continue, Vector2.Zero);

    public static OnHitOutcome Redirect(Vector2 direction) => new(OnHitOutcomeKind.Redirect, direction);

    public static OnHitOutcome ForkOutcome { get; } = new(OnHitOutcomeKind.Fork, Vector2.Zero);
}
