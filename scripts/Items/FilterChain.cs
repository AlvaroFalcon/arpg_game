#nullable enable
using System.Collections.Generic;

namespace PathToExile.Items;

/// <summary>
/// docs/design/items.md -> "Item filter": <c>Roll item -> ItemDescriptor ->
/// FilterChain.Evaluate(desc) -> DisplayStyle -> spawn label</c>.
///
/// "First matching rule wins" (PoE's semantics) is documented there specifically
/// so it is never ambiguous — <see cref="Evaluate"/> is the one place that rule is
/// implemented, and it must never grow "merge every matching rule" behaviour.
///
/// No filter UI exists yet, so nothing constructs a user chain in milestone 1, but
/// the shape is here: a player-authored filter is expected to <i>prepend</i> its
/// own rules ahead of <see cref="Default"/>'s, per "user filters simply prepend
/// rules."
/// </summary>
public sealed class FilterChain
{
    private readonly List<FilterRule> _rules;

    public IReadOnlyList<FilterRule> Rules => _rules;

    public FilterChain(IEnumerable<FilterRule>? rules = null)
    {
        _rules = rules is null ? new List<FilterRule>() : new List<FilterRule>(rules);
    }

    /// <summary>Inserts rules ahead of everything already in the chain — the
    /// "user filters simply prepend rules" mechanism from the design doc.</summary>
    public void PrependRules(IEnumerable<FilterRule> rules)
    {
        _rules.InsertRange(0, rules);
    }

    /// <summary>
    /// First matching rule wins. Returns <see cref="DisplayStyle.Baseline"/> if
    /// nothing matches — shouldn't happen once <see cref="Default"/>'s rules are
    /// included (its rarity rules cover every value of the enum), but a bare
    /// custom chain with no fallback is a legitimate thing to build and test.
    /// </summary>
    public DisplayStyle Evaluate(ItemDescriptor descriptor)
    {
        foreach (FilterRule rule in _rules)
        {
            if (rule.Matches(descriptor))
            {
                return rule.Style;
            }
        }

        return DisplayStyle.Baseline;
    }

    /// <summary>A fresh chain seeded with the built-in default rules — rarity
    /// colours plus the currency rule. Returns a new instance per call (rather
    /// than sharing one) because <see cref="PrependRules"/> mutates whatever chain
    /// it's called on.</summary>
    public static FilterChain CreateDefault() => new(BuiltInDefaultRules.All);

    /// <summary>Shared instance for code that never prepends user rules — i.e.
    /// everything in milestone 1, since no filter UI exists yet. Do not mutate
    /// this instance (don't call <see cref="PrependRules"/> on it); call
    /// <see cref="CreateDefault"/> instead for a chain you intend to prepend onto.</summary>
    public static readonly FilterChain Default = CreateDefault();
}
