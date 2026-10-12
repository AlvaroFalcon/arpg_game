namespace ArpgGame.Skills;

/// <summary>
/// Whether casting a skill restricts the caster's movement. Authored on every
/// skill definition, defaulting to <see cref="Free"/> — the only value implemented
/// in the first slice. <see cref="Slowed"/>/<see cref="Rooted"/> are reserved so
/// that "should casting lock you in place" can be decided per-skill after playing,
/// rather than as a global change. See docs/design/skills.md -> "Movement is FREE
/// during casting" for why free movement is the deliberate first-slice default and
/// why this field exists at all (easy to add commitment later, hard to remove it).
/// </summary>
public enum MovementDuringUse
{
    Free,
    Slowed,
    Rooted,
}
