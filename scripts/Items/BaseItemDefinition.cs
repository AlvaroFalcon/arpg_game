using Godot;
using Godot.Collections;

namespace PathToExile.Items;

/// <summary>
/// Authored data for one equipment base type ("Iron Wand", "Ebony Wand") — the
/// schema from docs/design/items.md -> "Base items", unchanged. Base stats scale
/// by base type and affixes roll on top of them; without this every wand would be
/// identical and the only progression would be affix rolls.
///
/// <see cref="AllowedAffixTags"/> is authored per the design doc's schema but
/// currently always <see cref="ItemTags.None"/> ("any") on every milestone-2a
/// base — <see cref="AffixDefinition.AllowedItemClasses"/> (added by the newer
/// docs/design/affixes.md, which supersedes this field's role) is what actually
/// gates which affixes can roll on which base in the roller. The field is kept
/// because the design doc lists it as part of this resource's shape and a second,
/// finer-grained tag-based restriction may still want it later (e.g. "no
/// elemental affixes on a pure-physical base"); see the milestone report for the
/// doc inconsistency this reflects.
/// </summary>
[GlobalClass]
public partial class BaseItemDefinition : Resource
{
    [Export]
    public string Id = "";

    [Export]
    public string DisplayName = "";

    [Export]
    public ItemClass ItemClass;

    [Export]
    public int Width = 1;

    [Export]
    public int Height = 1;

    /// <summary>Intrinsic stats this base grants — see
    /// <see cref="BaseItemStatEntry"/>. Empty for pure affix carriers (belt,
    /// amulet, ring), per docs/design/items.md's own note that this is
    /// deliberate, not an oversight.</summary>
    [Export]
    public Array<BaseItemStatEntry> BaseStats = new();

    [Export]
    public int RequiredLevel = 1;

    [Export]
    public ItemTags AllowedAffixTags = ItemTags.None;
}
