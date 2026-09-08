using Godot;
using Godot.Collections;
using PathToExile.Combat;
using PathToExile.Stats;

namespace PathToExile.Items;

/// <summary>
/// Which of the two pools an affix belongs to — docs/design/affixes.md -> "The
/// two pools". Temper supplies an item's leading name word, Sigil the trailing
/// clause; the 4-affix budget caps at 2 of each (<see cref="AffixRoller"/>), which
/// is the entire reason the split exists. Named <c>AffixPoolKind</c> rather than
/// <c>AffixPool</c> to avoid colliding with a future "the pool of affix
/// definitions" collection type.
/// </summary>
public enum AffixPoolKind
{
    Temper,
    Sigil,
}

/// <summary>
/// Authored data for one rollable affix — the schema from docs/design/affixes.md
/// -> "Authoring shape", implemented field-for-field. "Adding an affix that
/// targets an existing stat is a `.tres` file and nothing else" is the property
/// this shape exists to buy; nothing here should grow a hardcoded per-affix
/// branch in code (see <see cref="AffixRoller"/> for the one deliberate
/// exception, <see cref="IsAllResistances"/>).
///
/// Tiers 2-6 interpolate between <see cref="ValueAtT1Min"/>/Max and
/// <see cref="ValueAtT7Min"/>/Max via <see cref="ValueRangeForTier"/>; a
/// <see cref="TierOverrides"/> entry for a given tier bypasses the curve for that
/// tier only.
/// </summary>
[GlobalClass]
public partial class AffixDefinition : Resource
{
    [Export]
    public string Id = "";

    /// <summary>"Blazing" or "of Vigor" — the word/clause this affix contributes
    /// to a generated item name. See docs/design/affixes.md -> "Name generation".</summary>
    [Export]
    public string DisplayWord = "";

    [Export]
    public AffixPoolKind Pool = AffixPoolKind.Temper;

    [Export]
    public StatId Stat = StatId.Damage;

    [Export]
    public ModifierType Type = ModifierType.Flat;

    /// <summary>Required-tag set (all-of) — empty means "always applies." See
    /// docs/design/affixes.md -> "Condition semantics" and
    /// <see cref="StatModifier.Condition"/>.</summary>
    [Export]
    public SkillTags Condition = SkillTags.None;

    /// <summary>Any-of set — empty means "no any-of requirement." Together with
    /// <see cref="Condition"/> this is what lets "Volatile" express "any
    /// elemental" (<c>ConditionAny = Fire|Cold|Lightning</c>, <c>Condition =
    /// None</c>) while "Arcane Elemental" (not in this pass's pool, but
    /// representative) could express both at once.</summary>
    [Export]
    public SkillTags ConditionAny = SkillTags.None;

    /// <summary>Item level at which Tier 1 becomes reachable — a hard gate, per
    /// docs/design/items.md -> "Min-ilvl is a wall".</summary>
    [Export]
    public int MinIlvl = 1;

    /// <summary>Hard cutoff — 0 means none. docs/design/items.md -> "An explicit
    /// hard MaxIlvl per affix stays available, used sparingly": no milestone-2a
    /// affix uses one.</summary>
    [Export]
    public int MaxIlvl;

    [Export]
    public float ValueAtT1Min;

    [Export]
    public float ValueAtT1Max;

    [Export]
    public float ValueAtT7Min;

    [Export]
    public float ValueAtT7Max;

    /// <summary>Interpolation exponent applied to the 0..1 tier fraction before
    /// lerping between the T1 and T7 ranges. 1 = linear (every milestone-2a
    /// affix). &gt;1 back-loads power toward T7; &lt;1 front-loads it toward T1 —
    /// reserved for a future rebalance, not exercised here.</summary>
    [Export]
    public float Curve = 1f;

    [Export]
    public Array<AffixTierOverride> TierOverrides = new();

    /// <summary>Relative roll frequency. Every milestone-2a affix is 1 —
    /// docs/design/affixes.md -> Open Questions #4: "weights are not set,"
    /// balance tuning, not design.</summary>
    [Export]
    public float Weight = 1f;

    /// <summary>Empty = any item class. This is the field that actually gates
    /// which bases an affix can roll on in this pass — see
    /// <see cref="BaseItemDefinition.AllowedAffixTags"/>'s doc comment for why
    /// that older field isn't the one doing the gating.</summary>
    [Export]
    public Array<ItemClass> AllowedItemClasses = new();

    /// <summary>
    /// Escape hatch for "of Resilience," the one affix in the pool that writes to
    /// three stats at once (docs/design/affixes.md -> "of Resilience writes to
    /// three resistances from one affix... it is why AffixDefinition may
    /// eventually need a list of (StatId, ModifierType) pairs"). Rather than
    /// generalise the schema for a single occurrence, this bool tells
    /// <see cref="AffixRoller"/> to grant the rolled value to Fire, Cold, *and*
    /// Lightning Resistance simultaneously; <see cref="Stat"/> still points at
    /// <see cref="StatId.FireResistance"/> as a representative value so
    /// tier/filter code has something to read. See the milestone report for the
    /// full rationale and when to revisit it (a second multi-stat affix).
    /// </summary>
    [Export]
    public bool IsAllResistances;

    /// <summary>
    /// The maximum item level in the game, per docs/design/items.md -> "Affix tiers
    /// and item level". Tier gates interpolate across <see cref="MinIlvl"/>..this,
    /// so tier 7 lands exactly here for every affix.
    /// </summary>
    public const int MaxItemLevel = 120;

    /// <summary>
    /// The item level at which the given tier (1-7) unlocks. Tier 1 gates at
    /// <see cref="MinIlvl"/> and tier 7 at <see cref="MaxItemLevel"/>, with the
    /// rest spaced evenly between.
    ///
    /// A uniform per-tier step was tried first and is wrong: with a fixed step of
    /// 17, tier 7 gates at <c>MinIlvl + 102</c>, so every affix introduced above
    /// item level 18 could never reach its own top tier inside the 1-120 range —
    /// Titanic (MinIlvl 30) gated tier 7 at 132. A top roll the player can never
    /// see is strictly worse than not authoring it. Interpolating instead means a
    /// late-unlocking affix climbs its tiers faster, which is the intended
    /// reading of "seven tiers over item level 1-120".
    /// </summary>
    public int MinIlvlForTier(int tier) =>
        MinIlvl + Mathf.RoundToInt((tier - 1) * (MaxItemLevel - MinIlvl) / 6f);

    /// <summary>
    /// The highest tier reachable at <paramref name="itemLevel"/>, 0 if none
    /// (item level below <see cref="MinIlvl"/>, or above <see cref="MaxIlvl"/>
    /// when one is set). Tiers are a hard gate going up (docs/design/items.md ->
    /// "Min-ilvl is a wall") — this never returns a tier whose own gate exceeds
    /// <paramref name="itemLevel"/>.
    /// </summary>
    public int MaxReachableTier(int itemLevel)
    {
        if (MaxIlvl > 0 && itemLevel > MaxIlvl)
        {
            return 0;
        }

        int reachable = 0;
        for (int tier = 1; tier <= 7; tier++)
        {
            if (MinIlvlForTier(tier) > itemLevel)
            {
                break;
            }

            reachable = tier;
        }

        return reachable;
    }

    /// <summary>
    /// The roll range for a given tier: a <see cref="TierOverrides"/> entry if
    /// one exists for it, else linear (or <see cref="Curve"/>-adjusted)
    /// interpolation between the T1 and T7 authored ranges.
    /// </summary>
    public (float Min, float Max) ValueRangeForTier(int tier)
    {
        foreach (AffixTierOverride over in TierOverrides)
        {
            if (over.Tier == tier)
            {
                return (over.Min, over.Max);
            }
        }

        float fraction = Mathf.Clamp((tier - 1) / 6f, 0f, 1f);
        float adjusted = Curve == 1f ? fraction : Mathf.Pow(fraction, Curve);
        float min = Mathf.Lerp(ValueAtT1Min, ValueAtT7Min, adjusted);
        float max = Mathf.Lerp(ValueAtT1Max, ValueAtT7Max, adjusted);
        return (min, max);
    }
}
