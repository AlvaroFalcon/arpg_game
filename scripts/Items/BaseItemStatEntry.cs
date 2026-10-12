using Godot;
using ArpgGame.Combat;
using ArpgGame.Stats;

namespace ArpgGame.Items;

/// <summary>
/// One stat a <see cref="BaseItemDefinition"/> grants intrinsically — "wand
/// damage, armour value" per docs/design/items.md -> "Base items". A range
/// (<see cref="Min"/>..<see cref="Max"/>) rather than a fixed number so a base
/// like Iron Wand's "+3-6 Damage (Spell)" can roll once per drop, the same way an
/// affix rolls within its tier's range — a fixed base stat (armour bases in the
/// first pass) is just a range where <see cref="Min"/> == <see cref="Max"/>.
///
/// <see cref="Condition"/> exists specifically for Iron Wand: docs/design/items.md
/// -> "Weapon base damage is granted as a flat Damage modifier" pairs with the
/// design's own "+3-6 Damage <i>(Spell)</i>" notation, which only makes sense if
/// the granted modifier is conditioned on the Spell tag the same way a
/// <see cref="Stats.StatModifier"/>'s <see cref="Stats.StatModifier.Condition"/>
/// is — otherwise a wand would buff attack skills too.
/// </summary>
[GlobalClass]
public partial class BaseItemStatEntry : Resource
{
    [Export]
    public StatId Stat = StatId.Damage;

    [Export]
    public ModifierType Type = ModifierType.Flat;

    [Export]
    public float Min;

    [Export]
    public float Max;

    [Export]
    public SkillTags Condition = SkillTags.None;
}
