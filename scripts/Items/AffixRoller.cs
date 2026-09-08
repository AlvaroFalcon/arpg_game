#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using PathToExile.Stats;

namespace PathToExile.Items;

/// <summary>
/// The six-step roll procedure from docs/design/affixes.md -> "Roll procedure",
/// as a stateless static function over an item's identity (base, rarity, item
/// level) and the pool it draws from. Kept free of any scene-tree dependency —
/// same rationale <see cref="DamageMitigation"/> gives for being static/stateless
/// — so it's directly unit-testable from <c>AffixSmokeTest</c> without an
/// <see cref="EquipmentItem"/> node.
/// </summary>
public static class AffixRoller
{
    /// <summary>docs/design/affixes.md -> "The two pools": "max 2 Temper + 2
    /// Sigil" is the whole reason the pool split exists.</summary>
    public const int MaxPerPool = 2;

    /// <summary>One resolved affix on a specific item: which definition, which
    /// tier it landed at, and the concrete value rolled within that tier's
    /// range. Immutable — matches docs/design/items.md's "the descriptor is
    /// immutable after drop" for the roll that feeds it.</summary>
    public sealed class RolledAffix
    {
        public AffixDefinition Definition { get; }
        public int Tier { get; }
        public float Value { get; }

        public RolledAffix(AffixDefinition definition, int tier, float value)
        {
            Definition = definition;
            Tier = tier;
            Value = value;
        }
    }

    /// <summary>
    /// Steps 2-6 of the roll procedure (step 1, "pick the base item," is the
    /// caller's job — see <see cref="EquipmentDropSpawner"/>). Returns fewer
    /// than the rolled affix count only if the pool runs out of eligible,
    /// not-yet-used stats before the budget is spent (a narrow pool at a very low
    /// item level, for example) — never throws for that case.
    /// </summary>
    public static List<RolledAffix> Roll(
        BaseItemDefinition baseItem,
        Rarity rarity,
        int itemLevel,
        IReadOnlyList<AffixDefinition> pool,
        RandomNumberGenerator rng)
    {
        var result = new List<RolledAffix>();

        // Step 2: affix count from rarity.
        int count = RollAffixCount(rarity, rng);
        if (count == 0)
        {
            return result;
        }

        // Step 3+4 combined: the 2-Temper/2-Sigil split is enforced per-pick
        // below (PoolHasRoom) rather than as a separate up-front split, which is
        // functionally identical — a pick is only eligible if its pool still has
        // room, so the loop can never overrun either cap.
        int temperCount = 0;
        int sigilCount = 0;
        var usedStats = new HashSet<StatId>();

        for (int i = 0; i < count; i++)
        {
            List<AffixDefinition> eligible = pool.Where(a =>
                IsEligibleAffix(a, baseItem, itemLevel)
                && !ClaimedStatsOf(a).Any(usedStats.Contains)
                && PoolHasRoom(a.Pool, temperCount, sigilCount)).ToList();

            if (eligible.Count == 0)
            {
                break;
            }

            AffixDefinition chosen = WeightedPick(eligible, a => a.Weight, rng);

            // Step 5: tier, then a value within that tier's range.
            int tier = RollTier(chosen, itemLevel, rng);
            (float min, float max) = chosen.ValueRangeForTier(tier);
            float value = min >= max ? min : rng.RandfRange(min, max);

            result.Add(new RolledAffix(chosen, tier, value));

            // "Without replacement by stat" (docs/design/affixes.md -> step 4):
            // of Resilience claims all three resistances at once so a later pick
            // can't also roll a single-element resistance affix, and vice versa —
            // see AffixDefinition.IsAllResistances' doc comment and ClaimedStatsOf.
            foreach (StatId stat in ClaimedStatsOf(chosen))
            {
                usedStats.Add(stat);
            }

            if (chosen.Pool == AffixPoolKind.Temper)
            {
                temperCount++;
            }
            else
            {
                sigilCount++;
            }
        }

        return result;
    }

    /// <summary>docs/design/items.md -> "Rarity": Common 0, Magic 1-2, Rare 3-4,
    /// Majestic/Set 4 (fixed).</summary>
    private static int RollAffixCount(Rarity rarity, RandomNumberGenerator rng) => rarity switch
    {
        Rarity.Common => 0,
        Rarity.Magic => rng.RandiRange(1, 2),
        Rarity.Rare => rng.RandiRange(3, 4),
        Rarity.Majestic => 4,
        Rarity.Set => 4,
        _ => 0,
    };

    /// <summary>
    /// The stat(s) picking <paramref name="affix"/> would claim for the
    /// "without replacement by stat" rule. Almost always the affix's own
    /// <see cref="AffixDefinition.Stat"/>, except <see cref="AffixDefinition.IsAllResistances"/>
    /// ("of Resilience"), which claims Fire/Cold/Lightning Resistance
    /// simultaneously even though its own <c>Stat</c> field only names one of
    /// them representatively. <b>Both eligibility filtering and the
    /// post-pick usedStats update must go through this same helper</b> — an
    /// earlier version checked <c>a.Stat</c> directly for eligibility, which let
    /// of Resilience roll alongside a single already-picked resistance affix
    /// whenever its own representative stat (Fire) happened not to be the one
    /// already taken (Lightning/Cold). See the milestone report.
    /// </summary>
    private static IEnumerable<StatId> ClaimedStatsOf(AffixDefinition affix)
    {
        if (affix.IsAllResistances)
        {
            yield return StatId.FireResistance;
            yield return StatId.ColdResistance;
            yield return StatId.LightningResistance;
        }
        else
        {
            yield return affix.Stat;
        }
    }

    private static bool IsEligibleAffix(AffixDefinition affix, BaseItemDefinition baseItem, int itemLevel)
    {
        if (affix.AllowedItemClasses.Count > 0 && !affix.AllowedItemClasses.Contains(baseItem.ItemClass))
        {
            return false;
        }

        return affix.MaxReachableTier(itemLevel) >= 1;
    }

    private static bool PoolHasRoom(AffixPoolKind pool, int temperCount, int sigilCount) => pool switch
    {
        AffixPoolKind.Temper => temperCount < MaxPerPool,
        AffixPoolKind.Sigil => sigilCount < MaxPerPool,
        _ => false,
    };

    /// <summary>
    /// Which tier (1..MaxReachableTier) an eligible affix lands at. Weighted
    /// toward the highest reachable tier via a simple harmonic decay — tier T
    /// gets weight <c>1 / (1 + (maxReachable - T))</c> — rather than a uniform
    /// pick, per docs/design/items.md -> "Low tiers are weight-decayed at high
    /// item level, not removed": as an item's level climbs and maxReachable
    /// grows, every tier below it keeps a nonzero chance but the top tiers
    /// dominate. The exact curve is a placeholder — "weights are not set" is
    /// explicitly a balance pass, not a design one (see affixes.md's Open
    /// Questions #4).
    /// </summary>
    private static int RollTier(AffixDefinition affix, int itemLevel, RandomNumberGenerator rng)
    {
        int maxReachable = affix.MaxReachableTier(itemLevel);
        var tiers = Enumerable.Range(1, maxReachable).ToList();
        return WeightedPick(tiers, t => 1f / (1f + (maxReachable - t)), rng);
    }

    private static T WeightedPick<T>(IReadOnlyList<T> items, System.Func<T, float> weightOf, RandomNumberGenerator rng)
    {
        if (items.Count == 1)
        {
            return items[0];
        }

        float total = 0f;
        foreach (T item in items)
        {
            total += Mathf.Max(0f, weightOf(item));
        }

        if (total <= 0f)
        {
            return items[rng.RandiRange(0, items.Count - 1)];
        }

        float roll = rng.RandfRange(0f, total);
        float cumulative = 0f;
        foreach (T item in items)
        {
            cumulative += Mathf.Max(0f, weightOf(item));
            if (roll <= cumulative)
            {
                return item;
            }
        }

        return items[^1];
    }
}
