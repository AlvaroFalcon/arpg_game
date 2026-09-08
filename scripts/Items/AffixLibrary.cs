using Godot;
using Godot.Collections;

namespace PathToExile.Items;

/// <summary>
/// A flat pool of <see cref="AffixDefinition"/>s to roll against — one `.tres`
/// (<c>res://resources/items/AffixLibrary.tres</c>) referencing all 28
/// milestone-2a affixes as external resources, mirroring how
/// <c>Stats.StatBlock</c> wraps an array of sub-resources so the Inspector/roller
/// has one asset to point at instead of 28 separate `[Export]` fields.
/// </summary>
[GlobalClass]
public partial class AffixLibrary : Resource
{
    [Export]
    public Array<AffixDefinition> Entries = new();
}
