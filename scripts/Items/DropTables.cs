#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArpgGame.Items;

/// <summary>Where a dropping item comes from (each source has its own rarity table).</summary>
public enum DropSource
{
    NormalMonster,
    MagicMonster,
    RareMonster,
    UniqueMonster,
    Boss,
    RiftElite,
    RiftBoss,
    RiftReward,
}

/// <summary>One step of a cascade: its rarity and the chance to land on it.</summary>
public readonly record struct DropTier(Rarity Rarity, float Chance);

/// <summary>
/// Cascade ("waterfall") drop rolls — docs/design/items.md -> "Drop tables (cascade)".
/// THE place to tune drop rarities: the tables below are mirrored in that doc,
/// so change both together.
///
/// An item that is dropping (whether one drops at all is a separate quantity roll)
/// gets its rarity by walking its source's table from the rarest tier down: each
/// tier rolls its own chance, a hit stops the cascade, and a miss falls through to
/// the next tier; Common is the fallback. Every rarity bonus — reality modifiers,
/// Mirror jewels, the reality tier, time in the Rift — is a multiplier on every
/// tier's chance, so rarer tiers gain the most. Crafting orbs drop the same way.
/// </summary>
public static class DropTables
{
    /// <summary>No tier's chance goes above this, however big the bonus.</summary>
    public const float MaxChance = 0.95f;

    /// <summary>Realities: each tier above 1 multiplies the chances by this much more (+4 %).</summary>
    public const float PerRealityTier = 0.04f;

    /// <summary>Survival Rift drops: each minute survived multiplies the chances by this much more (+5 %).</summary>
    public const float PerRiftMinute = 0.05f;

    /// <summary>Item rarity per source, rarest tier first; anything that misses is Common.
    /// Majestic stays at 0 % until Majestic items are authored.</summary>
    public static readonly IReadOnlyDictionary<DropSource, DropTier[]> Items = new Dictionary<DropSource, DropTier[]>
    {
        [DropSource.NormalMonster] = new DropTier[] { new(Rarity.Rare, 0.015f), new(Rarity.Magic, 0.50f) },
        [DropSource.MagicMonster] = new DropTier[] { new(Rarity.Rare, 0.03f), new(Rarity.Magic, 0.55f) },
        [DropSource.RareMonster] = new DropTier[] { new(Rarity.Rare, 0.15f), new(Rarity.Magic, 0.60f) },
        [DropSource.UniqueMonster] = new DropTier[] { new(Rarity.Rare, 0.30f), new(Rarity.Magic, 0.60f) },
        [DropSource.Boss] = new DropTier[] { new(Rarity.Majestic, 0f), new(Rarity.Rare, 0.35f), new(Rarity.Magic, 0.55f) },
        [DropSource.RiftElite] = new DropTier[] { new(Rarity.Rare, 0.10f), new(Rarity.Magic, 0.55f) },
        [DropSource.RiftBoss] = new DropTier[] { new(Rarity.Majestic, 0f), new(Rarity.Rare, 0.35f), new(Rarity.Magic, 0.55f) },
        [DropSource.RiftReward] = new DropTier[] { new(Rarity.Majestic, 0f), new(Rarity.Rare, 0.15f), new(Rarity.Magic, 0.60f) },
    };

    /// <summary>Crafting orbs, rarest first; anything that misses is a Kindling Shard.</summary>
    public static readonly IReadOnlyList<(string Orb, float Chance)> Orbs = new[]
    {
        ("ascendant_orb", 0.01f),
        ("sundering_orb", 0.03f),
        ("tuning_orb", 0.04f),
        ("tempest_orb", 0.05f),
        ("crown_orb", 0.07f),
        ("ash_orb", 0.12f),
        ("graft_shard", 0.24f),
        ("flux_shard", 0.46f),
    };

    public const string FallbackOrb = "kindling_shard";

    /// <summary>The cascade itself: rarest tier first, the first hit wins, Common if none.</summary>
    public static Rarity RollRarity(DropSource source, float multiplier, RandomNumberGenerator rng)
    {
        foreach (DropTier tier in Items[source])
        {
            if (tier.Chance > 0f && rng.Randf() < Chance(tier.Chance, multiplier))
            {
                return tier.Rarity;
            }
        }

        return Rarity.Common;
    }

    /// <summary>A crafting orb by cascade (the same multiplier makes rare orbs likelier).</summary>
    public static CraftingCurrency RollOrb(RandomNumberGenerator rng, float multiplier = 1f)
    {
        foreach ((string orb, float chance) in Orbs)
        {
            if (rng.Randf() < Chance(chance, multiplier))
            {
                return Crafting.Get(orb);
            }
        }

        return Crafting.Get(FallbackOrb);
    }

    /// <summary>A tier's chance after a multiplier, capped at <see cref="MaxChance"/>.</summary>
    public static float Chance(float chance, float multiplier) => Mathf.Min(MaxChance, chance * Mathf.Max(0f, multiplier));

    /// <summary>A reality's multiplier: (1 + increased rarity) × (1 + 4 % per tier above 1).</summary>
    public static float RealityMultiplier(float increasedRarity, int tier) =>
        (1f + increasedRarity) * (1f + PerRealityTier * (Mathf.Max(1, tier) - 1));

    /// <summary>A Survival Rift multiplier: +5 % per minute survived.</summary>
    public static float RiftMultiplier(float elapsedSeconds) => 1f + PerRiftMinute * elapsedSeconds / 60f;

    /// <summary>The chance an orb has on the first roll of its cascade (rare orbs get a beam on the floor).</summary>
    public static float OrbChance(string orb) => Orbs.FirstOrDefault(o => o.Orb == orb).Chance is var c && c > 0f ? c : 1f;
}
