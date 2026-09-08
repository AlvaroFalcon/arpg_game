#nullable enable
using System;

namespace PathToExile.Items;

/// <summary>
/// One entry in a <see cref="FilterChain"/>: a predicate over an
/// <see cref="ItemDescriptor"/> plus the <see cref="DisplayStyle"/> to use when it
/// matches. The predicate can only read fields the descriptor actually exposes —
/// docs/design/items.md's governing rule, "a filter can only act on fields it can
/// see," is enforced simply by <see cref="ItemDescriptor"/> being the only thing
/// this delegate is handed.
/// </summary>
public sealed class FilterRule
{
    private readonly Func<ItemDescriptor, bool> _condition;

    public DisplayStyle Style { get; }

    public FilterRule(Func<ItemDescriptor, bool> condition, DisplayStyle style)
    {
        _condition = condition;
        Style = style;
    }

    public bool Matches(ItemDescriptor descriptor) => _condition(descriptor);
}
