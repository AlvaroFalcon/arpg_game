namespace PathToExile.Items;

/// <summary>
/// Five tiers, decided in docs/design/items.md -> "Rarity". Majestic and Set are
/// siblings (solo power vs. commitment power), not a ladder — see the doc's "why
/// the top is a fork not a ladder" for why a sixth "which is better" reading is
/// wrong.
///
/// Colour is the *default presentation only*, produced by the built-in filter
/// (<see cref="BuiltInDefaultRules"/>) — nothing outside filter code should ever
/// map this enum to a colour directly. See
/// docs/design/items.md -> "Rarity colour is filter output, not label state".
///
/// No producer exists above <see cref="Common"/> until milestone 2 (affix rolling)
/// and milestone 4 (Set/Majestic). The field is on <see cref="ItemDescriptor"/>
/// today anyway, because the filter's vocabulary has to be fixed before any of
/// those milestones ship — see docs/design/items.md's governing rule.
/// </summary>
public enum Rarity
{
    Common,
    Magic,
    Rare,
    Majestic,
    Set,
}
