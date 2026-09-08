namespace PathToExile.Items;

/// <summary>
/// Which broad family of item this is. Part of the fixed filter vocabulary from
/// docs/design/items.md -> "Item filter" (<c>ItemClass, BaseTypeId, Rarity, ...</c>),
/// even though only <see cref="Currency"/> has a producer today.
///
/// Equipment item classes (Wand, Amulet, BodyArmour, ...) are named on
/// <c>BaseItemDefinition.ItemClass</c> in docs/design/items.md -> "Base items",
/// which is milestone 2 work (see "Build order"). They are deliberately *not*
/// added here yet — the task for milestone 1 is proving the drop/filter/label
/// pipeline holds with the one family that exists, not scaffolding classes for
/// items that can't drop yet.
/// </summary>
public enum ItemClass
{
    Currency,
}
