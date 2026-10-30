using Godot;

namespace ArpgGame.Skills;

/// <summary>What a curse does while it lasts — docs/design/classes.md -> "The Witch's curses".</summary>
public enum CurseKind
{
    /// <summary>Shadow damage over time (the skill's damage is per second).</summary>
    Corruption,

    /// <summary>Lowers fire, cold and lightning resistance by <see cref="CurseSkillDefinition.Potency"/>.</summary>
    Elements,

    /// <summary>Slows movement by <see cref="CurseSkillDefinition.Potency"/>%.</summary>
    Time,
}

/// <summary>
/// The <c>Curse</c> archetype (2026-10-30): marks the enemies inside a circle at the
/// target point with an effect for <see cref="Duration"/> seconds instead of hitting
/// them once. <see cref="MaxTargets"/> 1 curses the enemy nearest the point (Corruption
/// "sticks to an enemy"); 0 curses everyone in the circle. The marks live on the enemy
/// (<see cref="Combat.Curses"/>), so they keep working after the caster looks away.
/// </summary>
[GlobalClass]
public partial class CurseSkillDefinition : SkillDefinition
{
    [Export]
    public CurseKind Kind;

    [Export]
    public float Radius = 90f;

    [Export]
    public int MaxTargets;

    [Export]
    public float Duration = 5f;

    /// <summary>Resistance lowered (Elements) or % slow (Time); unused by Corruption.</summary>
    [Export]
    public float Potency = 20f;

    /// <summary>Max distance from the caster to the circle's centre.</summary>
    [Export]
    public float MaxRange = 420f;

    [Export]
    public Color Tint = new(0.7f, 0.35f, 1f);
}
