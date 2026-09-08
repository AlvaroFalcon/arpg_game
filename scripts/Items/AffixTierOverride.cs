using Godot;

namespace PathToExile.Items;

/// <summary>
/// The per-tier escape hatch from docs/design/affixes.md -> "Authoring shape":
/// an explicit <see cref="Min"/>/<see cref="Max"/> for one tier, bypassing
/// <see cref="AffixDefinition"/>'s T1-T7 curve interpolation for that tier only.
/// None of the 28 milestone-2a affixes need one — every value comes straight off
/// the authored table — but the mechanism is authored now for the same reason
/// <c>ProjectileSkillDefinition</c> authors capabilities it doesn't use yet: a
/// future affix that doesn't fit the curve is then a data change, not a schema
/// change.
/// </summary>
[GlobalClass]
public partial class AffixTierOverride : Resource
{
    /// <summary>1-7.</summary>
    [Export]
    public int Tier = 1;

    [Export]
    public float Min;

    [Export]
    public float Max;
}
