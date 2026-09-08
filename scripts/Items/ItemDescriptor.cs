#nullable enable
using System;
using System.Collections.Generic;

namespace PathToExile.Items;

/// <summary>
/// The fixed vocabulary a filter can see, per docs/design/items.md -> "Item
/// filter": "A filter can only act on fields it can see. Whatever is not in the
/// descriptor at drop time can never be filtered on." Every field from the design
/// doc's list is present here now, even the ones with no producer in milestone 1
/// (<see cref="AffixTiers"/>, <see cref="SetId"/>, <see cref="MajesticEffectId"/>,
/// <see cref="SocketCount"/>, ...) — unused fields today are the point, not
/// waste, because the vocabulary has to be settled before equipment, sets, or
/// majestics exist to produce them.
///
/// Immutable after construction (init-only properties) — this is the mechanism
/// behind "The descriptor is immutable after drop": there are no unidentified
/// items in this game, so nothing should ever need to mutate a descriptor once an
/// <see cref="IDroppable"/> has computed it.
/// </summary>
public sealed class ItemDescriptor
{
    public ItemClass ItemClass { get; init; }

    /// <summary>
    /// Which <c>BaseItemDefinition</c> this item rolled on (docs/design/items.md
    /// -> "Base items"). Null for every milestone-1 drop — currency has no base
    /// item, it's just a counter.
    /// </summary>
    public string? BaseTypeId { get; init; }

    /// <summary>
    /// Defaults to <see cref="Rarity.Common"/> for every descriptor, including
    /// currency, where the value is meaningless (currency never rolls a rarity —
    /// see docs/design/items.md -> "Currency"). The built-in filter's currency
    /// rule matches on <see cref="CurrencyId"/> and must be ordered ahead of the
    /// rarity rules for exactly this reason — see <see cref="BuiltInDefaultRules"/>.
    /// </summary>
    public Rarity Rarity { get; init; } = Rarity.Common;

    /// <summary>1-120, immutable once rolled (docs/design/items.md -> "Affix tiers
    /// and item level"). No producer in milestone 1; stays 0.</summary>
    public int ItemLevel { get; init; }

    /// <summary>The area/monster level the drop occurred at. No producer in
    /// milestone 1; stays 0.</summary>
    public int AreaLevel { get; init; }

    /// <summary>No producer until milestone 2's affix rolling. Stays 0.</summary>
    public int AffixCount { get; init; }

    /// <summary>
    /// One entry per rolled affix's tier (1-7). Player-visible per docs/design/items.md
    /// -> "Tiers are player-visible" — "show me items with any T1-T2 affix" is the
    /// single most-wanted filter rule in the genre. No producer in milestone 1;
    /// stays empty, never null, so a filter predicate can iterate it unconditionally.
    /// </summary>
    public IReadOnlyList<int> AffixTiers { get; init; } = Array.Empty<int>();

    /// <summary>Convenience derivative of <see cref="AffixTiers"/> a filter can
    /// query without scanning the list itself. No producer in milestone 1.</summary>
    public int? HighestAffixTier { get; init; }

    /// <summary>Which <c>SetDefinition</c> this piece belongs to, or null — set
    /// membership is a field, not a separate rarity axis (docs/design/items.md ->
    /// "Sets"). No producer until milestone 4.</summary>
    public string? SetId { get; init; }

    /// <summary>Which authored majestic effect this item carries, or null
    /// (docs/design/items.md -> "Majestic effects"). No producer until milestone 4.</summary>
    public string? MajesticEffectId { get; init; }

    /// <summary>Reserved — no socket system is designed yet (docs/design/items.md
    /// -> Open Questions #6). Stays 0.</summary>
    public int SocketCount { get; init; }

    /// <summary>How many units one grid slot of this item represents. Meaningless
    /// for currency, which is never a grid item (docs/design/items.md ->
    /// "Currency is never a grid item — it's a counter"). No producer in milestone 1.</summary>
    public int StackSize { get; init; }

    /// <summary>Grid footprint (docs/design/items.md -> "Grid inventory").
    /// Meaningless for currency; no producer in milestone 1.</summary>
    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>
    /// Name-generation varies per family and lives entirely behind this one field
    /// — docs/design/items.md -> "Drop pipeline": "The label never switches on
    /// type." For currency it's <c>CurrencyDefinition.StackFormat</c> applied to
    /// <see cref="Amount"/> (e.g. "250 Gold"); later families format it their own
    /// way, and label code never needs to know which.
    /// </summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Set only for currency drops.</summary>
    public CurrencyId? CurrencyId { get; init; }

    /// <summary>Set only for currency drops.</summary>
    public long? Amount { get; init; }

    /// <summary>Escape hatch for filter conditions not anticipated by the rest of
    /// this type (docs/design/items.md -> "Item filter"). Empty in milestone 1 —
    /// see <see cref="ItemTags"/>.</summary>
    public ItemTags Tags { get; init; } = ItemTags.None;
}
