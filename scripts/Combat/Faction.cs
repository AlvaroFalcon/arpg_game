namespace PathToExile.Combat;

/// <summary>
/// A combat entity's allegiance. A hit only resolves against a target of a
/// *different* faction (see docs/design/skills.md -> "Faction / friendly fire").
/// Carried on the cast context and stamped onto projectiles at spawn, rather than
/// being part of a skill definition — that is what lets one "Fireball.tres" serve
/// both the player and an enemy without a duplicate "enemy version."
/// </summary>
public enum Faction
{
    Neutral,
    Player,
    Enemy,
}
