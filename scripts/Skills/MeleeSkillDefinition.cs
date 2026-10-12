using Godot;

namespace ArpgGame.Skills;

/// <summary>
/// The <c>Melee</c> archetype — docs/design/classes.md -> "The base classes".
/// An instant hit on every hostile in front of the caster: either an arc
/// (<see cref="ArcDegrees"/> wide, <see cref="Range"/> deep — Cleave, Double
/// Strike) or, with <see cref="Line"/>, a lane <see cref="LineWidth"/> wide that
/// lands after <see cref="LineDelay"/> (Sunder). Weapon damage reaches it the
/// same way every other bonus does: weapon bases roll flat Damage conditioned on
/// the Attack tag, which the caster's Damage stat applies to any Attack skill.
/// </summary>
[GlobalClass]
public partial class MeleeSkillDefinition : SkillDefinition
{
    [Export]
    public float Range = 90f;

    [Export]
    public float ArcDegrees = 120f;

    /// <summary>How many times the strike repeats per use (Double Strike = 2).</summary>
    [Export]
    public int Strikes = 1;

    [Export]
    public float Knockback = 120f;

    [Export]
    public bool Line;

    [Export]
    public float LineWidth = 56f;

    [Export]
    public float LineDelay = 0.12f;

    [Export]
    public Color Tint = new(1f, 0.9f, 0.75f);
}
