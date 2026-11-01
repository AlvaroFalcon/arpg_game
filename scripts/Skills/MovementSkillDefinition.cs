#nullable enable
using Godot;

namespace ArpgGame.Skills;

/// <summary>How a movement skill moves its caster — docs/design/skills.md -> "The Space slot".</summary>
public enum MovementKind
{
    /// <summary>An instant hop to the cursor (up to <see cref="MovementSkillDefinition.Range"/>), stopped by walls.</summary>
    Blink,

    /// <summary>A jump to the cursor that lands with a slam: damage and knockback around the landing point.</summary>
    Leap,
}

/// <summary>
/// The Movement archetype (owner's notes, 2026-11-01): skills that move the caster. They
/// fit any slot, and the <b>Space</b> slot takes only these — with Space empty, Space is
/// the dodge roll. A movement skill reuses the roll's machinery (<see cref="Dodge"/>
/// owns the body while it moves and grants invulnerability while it is in the air).
/// </summary>
[GlobalClass]
public partial class MovementSkillDefinition : SkillDefinition
{
    [Export]
    public MovementKind Kind;

    /// <summary>Longest distance moved (to the cursor if nearer).</summary>
    [Export]
    public float Range = 220f;

    /// <summary>Seconds in transit: short for a blink, longer for a leap.</summary>
    [Export]
    public float TravelTime = 0.06f;

    /// <summary>Leap: radius of the landing slam (0 = no slam).</summary>
    [Export]
    public float LandRadius;

    [Export]
    public float Knockback;

    [Export]
    public Color Tint = new(0.6f, 0.75f, 1f);
}
