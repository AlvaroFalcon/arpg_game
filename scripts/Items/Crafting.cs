#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Stats;

namespace ArpgGame.Items;

/// <summary>What a crafting orb does to an item — docs/design/crafting.md -> "Orbs".</summary>
public enum CraftAction
{
    None,
    Kindle,
    Flux,
    Graft,
    Crown,
    Tempest,
    Ascend,
    Sunder,
    Ash,
    Tune,
}

/// <summary>One crafting currency: an orb that changes an item, or a dust
/// (docs/design/crafting.md -> "Dust") that pays for forging.</summary>
public sealed class CraftingCurrency
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Effect { get; init; } = "";
    public Color Color { get; init; } = Colors.White;
    public CraftAction Action { get; init; }

    /// <summary>What the merchant charges for one (0 = not for sale).</summary>
    public long BuyPrice { get; init; }

    /// <summary>For dust: the item rarity it is made from.</summary>
    public Rarity? DustOf { get; init; }

    public bool IsDust => DustOf is not null;
}

/// <summary>
/// The crafting system — docs/design/crafting.md. Orbs change an item in place
/// (PoE-style: upgrade its rarity, reroll it, add or remove a modifier, reroll
/// its numbers), the merchant buys gear for gold, disenchanting turns gear into
/// dust by rarity, and the forge spends dust to make a new item that carries a
/// modifier the player chose.
/// </summary>
public static class Crafting
{
    public static readonly IReadOnlyList<CraftingCurrency> Orbs = new CraftingCurrency[]
    {
        new() { Id = "kindling_shard", Name = "Kindling Shard", Action = CraftAction.Kindle, BuyPrice = 20, Color = new Color(1f, 0.62f, 0.3f),
            Effect = "Upgrades a common item to magic with 1–2 random modifiers." },
        new() { Id = "flux_shard", Name = "Flux Shard", Action = CraftAction.Flux, BuyPrice = 30, Color = new Color(0.45f, 0.7f, 1f),
            Effect = "Rerolls every modifier on a magic item." },
        new() { Id = "graft_shard", Name = "Graft Shard", Action = CraftAction.Graft, BuyPrice = 35, Color = new Color(0.55f, 0.95f, 0.75f),
            Effect = "Adds a random modifier to a magic item with room for one." },
        new() { Id = "ash_orb", Name = "Ash Orb", Action = CraftAction.Ash, BuyPrice = 60, Color = new Color(0.7f, 0.68f, 0.66f),
            Effect = "Burns every modifier away, leaving a common item." },
        new() { Id = "crown_orb", Name = "Crown Orb", Action = CraftAction.Crown, Color = new Color(1f, 0.85f, 0.35f),
            Effect = "Upgrades a magic item to rare, adding one random modifier." },
        new() { Id = "tempest_orb", Name = "Tempest Orb", Action = CraftAction.Tempest, Color = new Color(0.75f, 0.45f, 1f),
            Effect = "Rerolls a rare item with a new name and 3–4 new modifiers." },
        new() { Id = "tuning_orb", Name = "Tuning Orb", Action = CraftAction.Tune, Color = new Color(0.95f, 0.95f, 0.75f),
            Effect = "Rerolls the numbers of every modifier and base stat within their tiers." },
        new() { Id = "sundering_orb", Name = "Sundering Orb", Action = CraftAction.Sunder, Color = new Color(1f, 0.4f, 0.35f),
            Effect = "Removes one random modifier from a magic or rare item." },
        new() { Id = "ascendant_orb", Name = "Ascendant Orb", Action = CraftAction.Ascend, Color = new Color(1f, 1f, 1f),
            Effect = "Adds a random modifier to a rare item with room for one." },
    };

    public static readonly IReadOnlyList<CraftingCurrency> Dusts = new CraftingCurrency[]
    {
        new() { Id = "grey_dust", Name = "Grey Dust", DustOf = Rarity.Common, Color = new Color(0.75f, 0.75f, 0.78f),
            Effect = "Disenchanted from common items. Pays for forging." },
        new() { Id = "azure_dust", Name = "Azure Dust", DustOf = Rarity.Magic, Color = new Color(0.45f, 0.6f, 1f),
            Effect = "Disenchanted from magic items. Pays for forging." },
        new() { Id = "gilded_dust", Name = "Gilded Dust", DustOf = Rarity.Rare, Color = new Color(1f, 0.85f, 0.35f),
            Effect = "Disenchanted from rare items. Pays for forging." },
        new() { Id = "prismatic_dust", Name = "Prismatic Dust", DustOf = Rarity.Majestic, Color = new Color(1f, 0.55f, 0.85f),
            Effect = "Disenchanted from majestic and set items. Pays for the finest forging." },
    };

    public static IEnumerable<CraftingCurrency> All => Orbs.Concat(Dusts);

    public static CraftingCurrency? Find(string id) => All.FirstOrDefault(c => c.Id == id);

    public static CraftingCurrency Get(string id) => Find(id) ?? throw new ArgumentException($"Unknown crafting currency '{id}'");

    public static CraftingCurrency DustFor(Rarity rarity) =>
        Dusts.First(d => d.DustOf == (rarity >= Rarity.Majestic ? Rarity.Majestic : rarity));

    /// <summary>A random orb by cascade (docs/design/items.md -> "Drop tables (cascade)").</summary>
    public static CraftingCurrency RollOrb(RandomNumberGenerator rng, float multiplier = 1f) => DropTables.RollOrb(rng, multiplier);

    private static IReadOnlyList<AffixDefinition> Pool => ItemFactory.Affixes.Entries;

    // --- orbs ----------------------------------------------------------------------

    /// <summary>Null when <paramref name="orb"/> can be used on <paramref name="item"/>,
    /// otherwise why not (shown to the player).</summary>
    public static string? CannotApply(CraftingCurrency orb, EquipmentItem item, IReadOnlyList<AffixDefinition>? pool = null)
    {
        if (item.Base is null || orb.Action == CraftAction.None)
        {
            return "That can't be used on items";
        }

        if (item.Rarity >= Rarity.Majestic)
        {
            return $"{item.Rarity} items can't be altered";
        }

        pool ??= Pool;
        int count = item.RolledAffixes.Count;
        int ilvl = item.Descriptor.ItemLevel;
        return orb.Action switch
        {
            CraftAction.Kindle when item.Rarity != Rarity.Common => $"{orb.Name} needs a common item",
            CraftAction.Flux when item.Rarity != Rarity.Magic => $"{orb.Name} needs a magic item",
            CraftAction.Graft when item.Rarity != Rarity.Magic => $"{orb.Name} needs a magic item",
            CraftAction.Graft when count >= AffixRoller.MaxAffixes(Rarity.Magic) => "This item has no room for another modifier",
            CraftAction.Crown when item.Rarity != Rarity.Magic => $"{orb.Name} needs a magic item",
            CraftAction.Tempest when item.Rarity != Rarity.Rare => $"{orb.Name} needs a rare item",
            CraftAction.Ascend when item.Rarity != Rarity.Rare => $"{orb.Name} needs a rare item",
            CraftAction.Ascend when count >= AffixRoller.MaxAffixes(Rarity.Rare) => "This item has no room for another modifier",
            CraftAction.Sunder when count == 0 => "This item has no modifiers to remove",
            CraftAction.Sunder when item.RolledAffixes.All(a => a.Sealed) => "Only the sealed modifier is left",
            CraftAction.Ash when item.Rarity == Rarity.Common => "This item is already common",
            CraftAction.Tune when count == 0 && item.RolledBaseStats.Count == 0 => "This item has nothing to tune",
            CraftAction.Graft or CraftAction.Crown or CraftAction.Ascend
                when AffixRoller.Addable(item.Base, ilvl, pool, item.RolledAffixes).Count == 0 => "No modifier can be added to this item",
            CraftAction.Kindle
                when AffixRoller.Addable(item.Base, ilvl, pool, Array.Empty<AffixRoller.RolledAffix>()).Count == 0 => "No modifier can roll on this item",
            _ => null,
        };
    }

    /// <summary>Uses <paramref name="orb"/> on <paramref name="item"/>, changing it in
    /// place. Returns false (and leaves the item alone) when it can't be used.</summary>
    public static bool Apply(CraftingCurrency orb, EquipmentItem item, RandomNumberGenerator rng, out string message,
        IReadOnlyList<AffixDefinition>? pool = null)
    {
        pool ??= Pool;
        if (CannotApply(orb, item, pool) is { } reason)
        {
            message = reason;
            return false;
        }

        BaseItemDefinition baseItem = item.Base!;
        int ilvl = item.Descriptor.ItemLevel;
        var affixes = item.RolledAffixes.ToList();
        // A sealed modifier survives the orb untouched (docs/design/crafting.md -> "Seals").
        AffixRoller.RolledAffix? sealedMod = affixes.FirstOrDefault(a => a.Sealed);
        switch (orb.Action)
        {
            case CraftAction.Kindle:
                Rewrite(item, Rarity.Magic, AffixRoller.Roll(baseItem, Rarity.Magic, ilvl, pool, rng), rng, rename: true);
                break;
            case CraftAction.Flux:
                Rewrite(item, Rarity.Magic, Reroll(baseItem, Rarity.Magic, ilvl, pool, sealedMod, rng), rng, rename: true);
                break;
            case CraftAction.Graft:
            case CraftAction.Ascend:
                affixes.Add(AffixRoller.RollAdditional(baseItem, ilvl, pool, affixes, rng)!);
                Rewrite(item, item.Rarity, affixes, rng, rename: item.Rarity == Rarity.Magic);
                break;
            case CraftAction.Crown:
                affixes.Add(AffixRoller.RollAdditional(baseItem, ilvl, pool, affixes, rng)!);
                Rewrite(item, Rarity.Rare, affixes, rng, rename: true);
                break;
            case CraftAction.Tempest:
                Rewrite(item, Rarity.Rare, Reroll(baseItem, Rarity.Rare, ilvl, pool, sealedMod, rng), rng, rename: true);
                break;
            case CraftAction.Sunder:
                var removable = affixes.Where(a => !a.Sealed).ToList();
                affixes.Remove(removable[rng.RandiRange(0, removable.Count - 1)]);
                Rewrite(item, item.Rarity, affixes, rng, rename: item.Rarity == Rarity.Magic);
                break;
            case CraftAction.Ash:
                var kept = sealedMod is null ? new List<AffixRoller.RolledAffix>() : new List<AffixRoller.RolledAffix> { sealedMod };
                Rewrite(item, kept.Count == 0 ? Rarity.Common : item.Rarity, kept, rng, rename: kept.Count == 0);
                break;
            case CraftAction.Tune:
                Retune(item, rng);
                break;
        }

        message = $"{orb.Name}: {item.Descriptor.DisplayName}";
        if (item.RolledAffixes.Any(a => a.Sealed))
        {
            // The seal holds for one orb only.
            Unseal(item);
            message += " — the seal breaks";
        }

        return true;
    }

    /// <summary>New modifiers for a reroll, keeping <paramref name="keep"/> (a sealed one).</summary>
    private static List<AffixRoller.RolledAffix> Reroll(BaseItemDefinition baseItem, Rarity rarity, int ilvl,
        IReadOnlyList<AffixDefinition> pool, AffixRoller.RolledAffix? keep, RandomNumberGenerator rng)
    {
        if (keep is null)
        {
            return AffixRoller.Roll(baseItem, rarity, ilvl, pool, rng);
        }

        int count = rarity == Rarity.Magic ? rng.RandiRange(1, 2) : rng.RandiRange(3, 4);
        var result = new List<AffixRoller.RolledAffix> { keep };
        while (result.Count < count && AffixRoller.RollAdditional(baseItem, ilvl, pool, result, rng) is { } more)
        {
            result.Add(more);
        }

        return result;
    }

    private static void Unseal(EquipmentItem item) =>
        item.Restore(item.Base!, item.Rarity, item.Descriptor.ItemLevel, item.Descriptor.DisplayName, item.RolledBaseStats,
            item.RolledAffixes.Select(a => a.WithSeal(false)).ToList());

    // --- seals -----------------------------------------------------------------------

    /// <summary>What sealing a modifier on this item costs: doubling gold and one
    /// more dust of its rarity each time the same item is sealed again.</summary>
    public static (long Gold, CraftingCurrency Dust, int Amount) SealCost(EquipmentItem item) =>
        (40L << Mathf.Min(item.SealCount, 20), DustFor(item.Rarity), 1 + item.SealCount);

    public static string? CannotSeal(EquipmentItem item)
    {
        if (item.Base is null || item.RolledAffixes.Count == 0)
        {
            return "Only items with modifiers can be sealed";
        }

        return item.Rarity >= Rarity.Majestic ? $"{item.Rarity} items can't be altered" : null;
    }

    /// <summary>Seals modifier <paramref name="index"/> of <paramref name="item"/> (any
    /// earlier seal moves to it), paying from <paramref name="wallet"/>.</summary>
    public static bool Seal(EquipmentItem item, int index, Wallet wallet, out string message)
    {
        if (CannotSeal(item) is { } reason)
        {
            message = reason;
            return false;
        }

        if (index < 0 || index >= item.RolledAffixes.Count)
        {
            message = "No such modifier";
            return false;
        }

        (long gold, CraftingCurrency dust, int amount) = SealCost(item);
        if (wallet.GetBalance(Core.Profile.GoldId) < gold || wallet.GetBalance(dust.Id) < amount)
        {
            message = $"Sealing costs {gold} gold and {amount} {dust.Name}";
            return false;
        }

        wallet.TrySpend(Core.Profile.GoldId, gold);
        wallet.TrySpend(dust.Id, amount);
        item.SealCount++;
        item.Restore(item.Base!, item.Rarity, item.Descriptor.ItemLevel, item.Descriptor.DisplayName, item.RolledBaseStats,
            item.RolledAffixes.Select((a, i) => a.WithSeal(i == index)).ToList());
        message = $"Sealed {ItemTooltipText.AffixLine(item.RolledAffixes[index])} — safe from the next orb";
        return true;
    }

    /// <summary>Replaces the item's rarity and modifiers, keeping its base stats.</summary>
    private static void Rewrite(EquipmentItem item, Rarity rarity, IReadOnlyList<AffixRoller.RolledAffix> affixes,
        RandomNumberGenerator rng, bool rename)
    {
        BaseItemDefinition baseItem = item.Base!;
        string name = rename || rarity != Rarity.Rare
            ? ItemNaming.Generate(rarity, baseItem.DisplayName, affixes, rng)
            : item.Descriptor.DisplayName;
        item.Restore(baseItem, rarity, item.Descriptor.ItemLevel, name, item.RolledBaseStats, affixes);
    }

    private static void Retune(EquipmentItem item, RandomNumberGenerator rng)
    {
        BaseItemDefinition baseItem = item.Base!;
        var baseStats = item.RolledBaseStats.Select((s, i) =>
        {
            if (i < baseItem.BaseStats.Count && baseItem.BaseStats[i] is { } entry && entry.Stat == s.Stat)
            {
                return (s.Stat, s.Type, AffixRoller.RollValue(entry.Min, entry.Max, rng), s.Condition);
            }

            return s;
        }).ToList();
        var affixes = item.RolledAffixes.Select(a =>
        {
            if (a.Sealed)
            {
                return a;
            }

            (float min, float max) = a.Definition.ValueRangeForTier(a.Tier);
            return new AffixRoller.RolledAffix(a.Definition, a.Tier, AffixRoller.RollValue(min, max, rng), a.Crafted);
        }).ToList();
        item.Restore(baseItem, item.Rarity, item.Descriptor.ItemLevel, item.Descriptor.DisplayName, baseStats, affixes);
    }

    // --- merchant ------------------------------------------------------------------

    /// <summary>Gold the merchant pays for an item (docs/design/crafting.md -> "Merchant").</summary>
    public static long SellPrice(EquipmentItem item)
    {
        int ilvl = item.Descriptor.ItemLevel;
        int mods = item.RolledAffixes.Count;
        return item.Rarity switch
        {
            Rarity.Magic => 4 + ilvl / 3 + 2 * mods,
            Rarity.Rare => 12 + ilvl / 2 + 4 * mods,
            Rarity.Majestic or Rarity.Set => 60 + ilvl,
            _ => 1 + ilvl / 8,
        };
    }

    // --- disenchanting -------------------------------------------------------------

    /// <summary>The dust an item breaks down into: always the dust of its own
    /// rarity, more for higher item levels and fuller rares.</summary>
    public static (CraftingCurrency Dust, int Amount) DisenchantYield(EquipmentItem item)
    {
        int ilvl = item.Descriptor.ItemLevel;
        int amount = item.Rarity switch
        {
            Rarity.Magic => 1 + ilvl / 30,
            Rarity.Rare => 1 + ilvl / 40 + (item.RolledAffixes.Count >= 4 ? 1 : 0),
            Rarity.Majestic or Rarity.Set => 1,
            _ => 1 + ilvl / 20,
        };
        return (DustFor(item.Rarity), amount);
    }

    // --- forging -------------------------------------------------------------------

    /// <summary>A way to forge: what it costs and how good the chosen modifier is.</summary>
    public sealed class ForgeGrade
    {
        public string Name { get; init; } = "";
        public Rarity Result { get; init; }

        /// <summary>Tiers below the best one reachable at the forge's item level.</summary>
        public int TiersBelowBest { get; init; }

        /// <summary>The chosen modifier rolls the top of its tier's range.</summary>
        public bool PerfectValue { get; init; }

        public int ExtraMin { get; init; }
        public int ExtraMax { get; init; }
        public long Gold { get; init; }
        public (string Dust, int Amount)[] Cost { get; init; } = Array.Empty<(string, int)>();
        public string Summary { get; init; } = "";
    }

    public static readonly IReadOnlyList<ForgeGrade> Grades = new ForgeGrade[]
    {
        new() { Name = "Grey forging", Result = Rarity.Magic, TiersBelowBest = 2, Gold = 20, Cost = new[] { ("grey_dust", 6) },
            Summary = "Magic item, your modifier two tiers below the best." },
        new() { Name = "Azure forging", Result = Rarity.Magic, TiersBelowBest = 1, ExtraMin = 1, ExtraMax = 1, Gold = 60, Cost = new[] { ("azure_dust", 4), ("grey_dust", 4) },
            Summary = "Magic item, your modifier one tier below the best, plus one random modifier." },
        new() { Name = "Gilded forging", Result = Rarity.Rare, ExtraMin = 2, ExtraMax = 3, Gold = 150, Cost = new[] { ("gilded_dust", 3), ("azure_dust", 4) },
            Summary = "Rare item, your modifier at the best tier, plus 2–3 random modifiers." },
        new() { Name = "Prismatic forging", Result = Rarity.Rare, PerfectValue = true, ExtraMin = 3, ExtraMax = 3, Gold = 400, Cost = new[] { ("prismatic_dust", 1), ("gilded_dust", 3) },
            Summary = "Rare item, your modifier at the best tier with a perfect roll, plus 3 random modifiers." },
    };

    /// <summary>Item level of forged gear: the monster level of the highest reality
    /// tier the character has cleared, so forging keeps pace with drops.</summary>
    public static int ForgeItemLevel(int highestTierCleared) =>
        World.RealityTiers.MonsterLevel(Mathf.Clamp(highestTierCleared, 1, World.RealityTiers.MaxTier));

    /// <summary>The modifiers a base can be forged with at an item level.</summary>
    public static List<AffixDefinition> ForgeableAffixes(BaseItemDefinition baseItem, int itemLevel, IReadOnlyList<AffixDefinition>? pool = null) =>
        AffixRoller.Addable(baseItem, itemLevel, pool ?? Pool, Array.Empty<AffixRoller.RolledAffix>())
            .OrderBy(a => a.Pool).ThenBy(a => a.Stat.ToString()).ToList();

    /// <summary>The tier a grade forges <paramref name="affix"/> at.</summary>
    public static int ForgeTier(AffixDefinition affix, ForgeGrade grade, int itemLevel) =>
        Mathf.Max(1, affix.MaxReachableTier(itemLevel) - grade.TiersBelowBest);

    /// <summary>Builds the forged item (the caller pays and stores it). The chosen
    /// modifier is marked as crafted.</summary>
    public static EquipmentItem Forge(BaseItemDefinition baseItem, AffixDefinition affix, ForgeGrade grade, int itemLevel,
        RandomNumberGenerator rng, IReadOnlyList<AffixDefinition>? pool = null)
    {
        pool ??= Pool;
        int tier = ForgeTier(affix, grade, itemLevel);
        (float min, float max) = affix.ValueRangeForTier(tier);
        float value = grade.PerfectValue ? (Mathf.Abs(max) >= 1f ? Mathf.Floor(max) : max) : AffixRoller.RollValue(min, max, rng);
        var affixes = new List<AffixRoller.RolledAffix> { new(affix, tier, value, crafted: true) };
        int extra = rng.RandiRange(grade.ExtraMin, grade.ExtraMax);
        for (int i = 0; i < extra && affixes.Count < AffixRoller.MaxAffixes(grade.Result); i++)
        {
            if (AffixRoller.RollAdditional(baseItem, itemLevel, pool, affixes, rng) is not { } more)
            {
                break;
            }

            affixes.Add(more);
        }

        EquipmentItem item = ItemFactory.NewBlank();
        item.Initialize(baseItem, Rarity.Common, itemLevel, Array.Empty<AffixDefinition>(), rng);
        string name = ItemNaming.Generate(grade.Result, baseItem.DisplayName, affixes, rng);
        item.Restore(baseItem, grade.Result, itemLevel, name, item.RolledBaseStats, affixes);
        return item;
    }

    /// <summary>Whether <paramref name="wallet"/> can pay for a grade.</summary>
    public static bool CanAfford(Wallet wallet, ForgeGrade grade) =>
        wallet.GetBalance(Core.Profile.GoldId) >= grade.Gold && grade.Cost.All(c => wallet.GetBalance(c.Dust) >= c.Amount);

    public static bool TryPay(Wallet wallet, ForgeGrade grade)
    {
        if (!CanAfford(wallet, grade))
        {
            return false;
        }

        wallet.TrySpend(Core.Profile.GoldId, grade.Gold);
        foreach ((string dust, int amount) in grade.Cost)
        {
            wallet.TrySpend(dust, amount);
        }

        return true;
    }

    /// <summary>"6 Grey Dust · 20 gold".</summary>
    public static string CostText(ForgeGrade grade) =>
        string.Join(" · ", grade.Cost.Select(c => $"{c.Amount} {Get(c.Dust).Name}").Append($"{grade.Gold} gold"));
}
