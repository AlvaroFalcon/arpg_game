namespace PathToExile.Items;

/// <summary>
/// Which broad family of item this is. Part of the fixed filter vocabulary from
/// docs/design/items.md -> "Item filter" (<c>ItemClass, BaseTypeId, Rarity, ...</c>).
///
/// Milestone 2a (docs/design/items.md -> "The first base items") adds one entry
/// per authored base item's category, named on <c>BaseItemDefinition.ItemClass</c>
/// — this is also the granularity <see cref="AffixDefinition.AllowedItemClasses"/>
/// gates against ("Plated" should only roll on armour pieces, not a wand). One
/// entry per slot rather than a separate slot enum, since milestone 2a authors
/// exactly one base per slot; a slot concept (needed once a second Wand-class
/// weapon or the two Ring slots matter) is equip-system work and stays out of
/// scope here — see docs/design/items.md's "Equipment slots".
/// </summary>
public enum ItemClass
{
    Currency,
    Wand,
    Offhand,
    Helmet,
    BodyArmour,
    Gloves,
    Boots,
    Belt,
    Amulet,
    Ring,
}
