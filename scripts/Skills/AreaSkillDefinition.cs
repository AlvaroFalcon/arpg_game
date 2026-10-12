using Godot;

namespace ArpgGame.Skills;

/// <summary>
/// The <c>Area</c> archetype — docs/design/progression.md -> "New archetypes".
/// One burst of damage to every hostile inside a circle, either centred on the
/// caster (Frost Nova) or landing on <see cref="CastContext.TargetPoint"/>
/// after a delay (Meteor). The same archetype backs boss slams: a boss slam is
/// just an area skill with a long, visible <see cref="Delay"/>, which is what
/// makes "every boss attack is telegraphed" a property of the data rather than
/// a promise each boss script has to keep by hand.
/// </summary>
[GlobalClass]
public partial class AreaSkillDefinition : SkillDefinition
{
    [Export]
    public float Radius = 90f;

    /// <summary>Seconds between cast and impact. While waiting, the impact
    /// circle is drawn on the ground and fills up — see <see cref="Combat.AreaBurst"/>.</summary>
    [Export]
    public float Delay;

    /// <summary>True: lands on the cast's target point. False: centred on the caster.</summary>
    [Export]
    public bool AtTarget;

    /// <summary>Max distance from the caster to the target point (targeted only).</summary>
    [Export]
    public float MaxRange = 360f;

    /// <summary>Knockback (px/s) pushed outwards from the centre.</summary>
    [Export]
    public float Knockback = 0f;

    /// <summary>Colour of the impact visual. Alpha is ignored.</summary>
    [Export]
    public Color Tint = new(1f, 0.5f, 0.2f);
}
