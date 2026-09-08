using PathToExile;

namespace PathToExile.Items;

/// <summary>
/// The shared contract every drop family implements, per docs/design/items.md ->
/// "Drop pipeline". Deliberately this narrow: "Currency and flask orbs must not
/// be forced through an inventory abstraction neither of them needs." All three
/// families (currency, equipment, flask orbs) share the drop/spawn/label/filter
/// path and diverge only at <see cref="OnPickup"/>.
/// </summary>
public interface IDroppable
{
    /// <summary>What the filter reads. Immutable from the moment the item drops —
    /// see docs/design/items.md -> "The descriptor is immutable after drop".</summary>
    ItemDescriptor Descriptor { get; }

    /// <summary>
    /// What actually happens on pickup. Must never read a <see cref="DisplayStyle"/>
    /// or a <see cref="FilterChain"/> result — display and behaviour are two
    /// independent consumers of the same <see cref="Descriptor"/>, per
    /// docs/design/items.md -> "Filters control display, never behaviour". See
    /// <see cref="CurrencyDrop.OnPickup"/> for why this is structural rather than
    /// a convention to remember.
    /// </summary>
    void OnPickup(Player player);
}
