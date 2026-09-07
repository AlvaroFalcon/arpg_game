using System.Collections.Generic;

namespace PathToExile.Stats;

/// <summary>
/// A single numeric stat with a base value and a stack of modifiers, computed using
/// Path of Exile's modifier formula:
///
///   Value = (BaseValue + sum of Flat) * (1 + sum of Increased / 100) * product of (1 + More / 100)
///
/// Increased/Reduced modifiers all sum together additively before being applied as one
/// combined multiplier; each More/Less modifier applies as its own independent multiplier.
/// Plain C# class (not a Godot Node) — stats are data, not scene-tree citizens.
/// </summary>
public sealed class Stat
{
    private readonly List<StatModifier> _modifiers = new();

    public float BaseValue { get; set; }

    public Stat(float baseValue)
    {
        BaseValue = baseValue;
    }

    public float Value
    {
        get
        {
            float flatSum = 0f;
            float increasedSum = 0f;
            float moreProduct = 1f;

            foreach (StatModifier modifier in _modifiers)
            {
                switch (modifier.Type)
                {
                    case ModifierType.Flat:
                        flatSum += modifier.Value;
                        break;
                    case ModifierType.Increased:
                        increasedSum += modifier.Value;
                        break;
                    case ModifierType.More:
                        moreProduct *= 1f + modifier.Value / 100f;
                        break;
                }
            }

            return (BaseValue + flatSum) * (1f + increasedSum / 100f) * moreProduct;
        }
    }

    public void AddModifier(StatModifier modifier)
    {
        _modifiers.Add(modifier);
    }

    public void RemoveModifier(StatModifier modifier)
    {
        _modifiers.Remove(modifier);
    }

    /// <summary>
    /// Removes all modifiers tagged with the given source (e.g. called by an item's
    /// unequip logic or a talent's unlearn logic, without the caller needing to track
    /// individual modifier instances).
    /// </summary>
    public void RemoveModifiersFromSource(object source)
    {
        _modifiers.RemoveAll(m => Equals(m.Source, source));
    }
}
