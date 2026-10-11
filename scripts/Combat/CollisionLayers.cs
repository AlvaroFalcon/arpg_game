namespace PathToExile.Combat;

/// <summary>
/// Named physics-layer bits shared by every combat scene. Kept as a small static
/// table rather than magic numbers scattered across scenes/scripts.
///
/// Faction is deliberately *not* baked into the collision setup alone — see
/// <see cref="Health"/>, which re-checks faction in code before applying damage.
/// Layers are a coarse physics-level filter (so a player-sourced projectile's
/// Area2D never even reports an overlap with the player), and the code-level check
/// is the authoritative one. Belt and suspenders, and it means getting a layer bit
/// wrong is a missed hit rather than friendly fire.
/// </summary>
public static class CollisionLayers
{
    public const uint World = 1 << 0;
    public const uint PlayerFaction = 1 << 1;
    public const uint EnemyFaction = 1 << 2;
    public const uint Projectile = 1 << 3;

    /// <summary>Liquid hazards in a reality (water, lava): they stop bodies but
    /// not projectiles (docs/design/endgame.md -> "Layout").</summary>
    public const uint Hazard = 1 << 4;
}
