using Godot;

namespace PathToExile.Skills;

/// <summary>
/// The <c>Orbit</c> archetype — docs/design/progression.md -> "New archetypes".
/// Summons <see cref="Count"/> objects circling the caster for
/// <see cref="Duration"/> seconds; each damages any hostile it touches, at most
/// once per <see cref="HitCooldown"/> per target. The "protect your personal
/// space" archetype every survivor game has, and the one that most rewards
/// area and duration investment.
/// </summary>
[GlobalClass]
public partial class OrbitSkillDefinition : SkillDefinition
{
    [Export]
    public int Count = 2;

    [Export]
    public float Radius = 70f;

    [Export]
    public float Duration = 4f;

    /// <summary>Radians per second.</summary>
    [Export]
    public float AngularSpeed = 3.5f;

    /// <summary>Hit radius of each orbiting object.</summary>
    [Export]
    public float HitRadius = 14f;

    [Export]
    public float HitCooldown = 0.5f;
}
