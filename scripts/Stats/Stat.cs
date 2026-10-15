using System.Collections.Generic;

namespace ArpgGame.Stats;

/// <summary>
/// A single numeric stat with a base value and a stack of modifiers, computed using
/// Path of Exile's modifier formula:
///
///   Value = (BaseValue + sum of Flat) * (1 + sum of Increased / 100) * product of (1 + More / 100)
///
/// Increased/Reduced modifiers all sum together additively before being applied as one
/// combined multiplier; each More/Less modifier applies as its own independent multiplier.
/// Plain C# class (not a Godot Node) — stats are data, not scene-tree citizens.
///
/// Most actor stats introduced by combat (docs/design/skills.md) are "modifier
/// carrier with no meaningful base of its own" — the actor doesn't own a `Damage`
/// stat with a real base value, the skill supplies the base and the actor supplies
/// only the modifier stack. <see cref="Compute"/> and the static
/// <see cref="ComputeFormula"/> exist for exactly that shape: a caller can run this
/// stat's modifier list against an externally supplied base instead of
/// <see cref="BaseValue"/>.
/// </summary>
public sealed class Stat
{
    private readonly List<StatModifier> _modifiers = new();

    public float BaseValue { get; set; }

    /// <summary>Read-only view of this stat's modifiers, for callers like
    /// <see cref="ActorStats.GetModifiers"/> that need to hand the raw list to
    /// another stat's <see cref="ComputeFormula"/> call.</summary>
    public IReadOnlyList<StatModifier> Modifiers => _modifiers;

    public Stat(float baseValue)
    {
        BaseValue = baseValue;
    }

    public float Value => Compute(BaseValue);

    /// <summary>
    /// Runs this stat's own modifier stack against a caller-supplied base instead
    /// of <see cref="BaseValue"/>. This is what lets a skill's base damage be
    /// resolved through the caster's damage-modifier stack without the actor
    /// needing to own a "Damage" stat with a real base.
    /// </summary>
    public float Compute(float baseValue) => ComputeFormula(baseValue, _modifiers);

    /// <summary>
    /// The Flat -> Increased -> More formula, as a pure function over an arbitrary
    /// base and modifier list. Static so it can be used without an owning
    /// <see cref="Stat"/> instance at all (e.g. a skill's base damage combined with
    /// a modifier list fetched via <see cref="ActorStats.GetModifiers"/>).
    /// </summary>
    /// <param name="flatEffectiveness">Scales the Flat sum only (a skill's added damage effectiveness).</param>
    public static float ComputeFormula(float baseValue, IReadOnlyList<StatModifier> modifiers, float flatEffectiveness = 1f)
    {
        float flatSum = 0f;
        float increasedSum = 0f;
        float moreProduct = 1f;

        foreach (StatModifier modifier in modifiers)
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

        return (baseValue + flatSum * flatEffectiveness) * (1f + increasedSum / 100f) * moreProduct;
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
