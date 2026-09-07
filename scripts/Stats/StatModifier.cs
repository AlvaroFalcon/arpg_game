#nullable enable

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
/// </summary>
public sealed class StatModifier
{
    public ModifierType Type { get; }
    public float Value { get; }
    public object? Source { get; }

    public StatModifier(ModifierType type, float value, object? source = null)
    {
        Type = type;
        Value = value;
        Source = source;
    }
}
