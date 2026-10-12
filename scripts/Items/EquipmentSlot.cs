namespace PathToExile.Items;

/// <summary>
/// The nine equipment slots from docs/design/items.md -> "Equipment slots":
/// Weapon, Offhand, Helmet, Body Armour, Gloves, Boots, Belt, Amulet, and two
/// Ring slots. <see cref="Equipment"/> is the component that owns slot -&gt;
/// <see cref="EquipmentItem"/>; this enum is just the key.
///
/// <b>Values are explicit and pinned</b>, for exactly the reason
/// <see cref="Stats.StatId"/>'s doc comment gives: any future save file or
/// authored resource that serialises a slot as this enum's integer value would
/// silently rebind to a different slot if a member were ever inserted or
/// reordered rather than appended. No save system exists yet in milestone 2b,
/// but the ordinal-shift hazard is identical either way and costs nothing to
/// avoid now — add new slots with the next unused number, never fill a
/// retired one's gap.
/// </summary>
public enum EquipmentSlot
{
    Weapon = 0,
    Offhand = 1,
    Helmet = 2,
    BodyArmour = 3,
    Gloves = 4,
    Boots = 5,
    Belt = 6,
    Amulet = 7,
    Ring1 = 8,
    Ring2 = 9,
    Pants = 10,
}
