#nullable enable
using Godot;

namespace ArpgGame.Skills;

/// <summary>What an affliction does to the enemies in its circle — docs/design/skills.md -> "Oil and detonations".</summary>
public enum AfflictionKind
{
    /// <summary>Douses them in oil: more fire damage taken, and every fire hit ignites them.</summary>
    Oil,

    /// <summary>Detonates their ignite stacks: all the fire damage left, at once.</summary>
    Combust,

    /// <summary>Detonates their bleed stacks: all the physical damage left, at once.</summary>
    Exsanguinate,
}

/// <summary>
/// Afflictions (owner's brief, 2026-11-01): skills that work on what is already on an
/// enemy rather than hitting it. Douse prepares fire builds; Combust and Exsanguinate
/// cash in ignite and bleed stacks — every second of damage the stacks had left lands
/// at once, raised by <see cref="Potency"/>%.
/// </summary>
[GlobalClass]
public partial class AfflictionSkillDefinition : SkillDefinition
{
    [Export]
    public AfflictionKind Kind;

    [Export]
    public float Radius = 100f;

    /// <summary>Max distance from the caster to the circle's centre.</summary>
    [Export]
    public float MaxRange = 420f;

    /// <summary>Oil: % more fire damage taken. Detonations: % more of the consumed damage.</summary>
    [Export]
    public float Potency = 25f;

    /// <summary>Oil: seconds it lasts.</summary>
    [Export]
    public float Duration = 6f;

    [Export]
    public Color Tint = new(0.8f, 0.6f, 0.25f);
}
