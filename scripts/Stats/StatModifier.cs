#nullable enable

using PathToExile.Combat;

namespace PathToExile.Stats;

/// <summary>
/// A single modifier applied to a <see cref="Stat"/>.
/// Mirrors Path of Exile's modifier taxonomy: Flat, Increased (additive %),
/// and More (multiplicative %).
/// </summary>
public enum ModifierType
{
    /// <summary>Added directly to the stat's base value before percentage modifiers.</summary>
    Flat,

    /// <summary>
    /// Summed with all other Increased/Reduced modifiers, then applied as a single
    /// combined multiplier (e.g. two +10% Increased modifiers = +20%, not +21%).
    /// Use a negative value for "reduced".
    /// </summary>
    Increased,

    /// <summary>
    /// Applied as its own independent multiplier, stacking multiplicatively with
    /// other More/Less modifiers (e.g. two +10% More modifiers = *1.1 *1.1 = *1.21).
    /// Use a negative value for "less".
    /// </summary>
    More,
}

/// <summary>
/// A single stat modification. <see cref="Source"/> is an optional tag (e.g. the
/// equipment item or talent node instance that granted this modifier) so that a
/// future gear/talent system can call <see cref="Stat.RemoveModifiersFromSource"/>
/// on unequip/unlearn without having to track modifier references itself.
///
/// <see cref="Condition"/> is the required-tag-set field from
/// docs/design/skills.md -> seam 4, empty (<see cref="SkillTags.None"/>) meaning
/// "always applies" — <c>Condition ⊆ tags</c>. <see cref="ConditionAny"/> is the
/// second field added by docs/design/affixes.md -> "Condition semantics": an
/// any-of set, empty meaning "no any-of requirement" —
/// <c>ConditionAny == None || (ConditionAny ∩ tags) != ∅</c>. Two fields rather
/// than a mode enum because a single affix can legitimately need both at once
/// ("increased Elemental Spell Damage": <c>Condition = Spell</c>,
/// <c>ConditionAny = Fire|Cold|Lightning</c>). Both are actually applied now by
/// <see cref="Stats.ActorStats.GetModifiers"/> — this is the pass that switches
/// seam 4 on for the first time; see that method's doc comment.
/// </summary>
public sealed class StatModifier
{
    public ModifierType Type { get; }
    public float Value { get; }
    public object? Source { get; }
    public SkillTags Condition { get; }
    public SkillTags ConditionAny { get; }

    public StatModifier(
        ModifierType type,
        float value,
        object? source = null,
        SkillTags condition = SkillTags.None,
        SkillTags conditionAny = SkillTags.None)
    {
        Type = type;
        Value = value;
        Source = source;
        Condition = condition;
        ConditionAny = conditionAny;
    }

    /// <summary>The predicate from docs/design/affixes.md -> "Condition
    /// semantics", applied against a cast/query's tag set.</summary>
    public bool AppliesTo(SkillTags tags)
    {
        bool allOf = (Condition & tags) == Condition;
        bool anyOf = ConditionAny == SkillTags.None || (ConditionAny & tags) != 0;
        return allOf && anyOf;
    }
}
