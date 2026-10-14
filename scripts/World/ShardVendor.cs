#nullable enable
using Godot;
using ArpgGame.Characters;
using ArpgGame.Items;

namespace ArpgGame.World;

/// <summary>
/// Arehucaz, the shard dealer — docs/design/endgame.md -> "Arehucaz". Sells Mirror
/// Shards for gold, but only of tiers the character has already cleared, and only
/// five at a time: the stock comes back after ten more cleared realities, or at
/// once for 10,000 gold. A safety valve against a dry spell of boss shards, not a
/// replacement for them: buying never gets you past your own best tier.
/// Stock is per character, like shards and the tier record.
/// </summary>
public static class ShardVendor
{
    public const int StockSize = 5;
    public const int RestockClears = 10;
    public const long RestockGold = 10_000;

    /// <summary>Gold for one shard: 79 at tier 2... 700 at tier 10, 2,150 at tier 20.</summary>
    public static long Price(int tier) => 50 + 25 * tier + 4 * tier * tier;

    /// <summary>Highest tier on sale (tier 1 is always free at the Mirror, so it isn't sold).</summary>
    public static int MaxTier(CharacterData c) => Mathf.Clamp(c.HighestTierCleared, 0, RealityTiers.MaxTier);

    public static bool Sells(CharacterData c, int tier) => tier >= 2 && tier <= MaxTier(c);

    /// <summary>Cleared realities still needed for the free restock.</summary>
    public static int ClearsUntilRestock(CharacterData c) =>
        Mathf.Max(0, RestockClears - (c.RealitiesCleared - c.ShardVendorSince));

    /// <summary>Applies a restock earned by clearing realities. Returns true if it happened.</summary>
    public static bool Update(CharacterData c)
    {
        if (c.ShardVendorStock >= StockSize || ClearsUntilRestock(c) > 0)
        {
            return false;
        }

        Restock(c);
        return true;
    }

    private static void Restock(CharacterData c)
    {
        c.ShardVendorStock = StockSize;
        c.ShardVendorSince = c.RealitiesCleared;
    }

    /// <summary>Why a shard of this tier can't be bought now, or null.</summary>
    public static string? CannotBuy(CharacterData c, Wallet wallet, int tier)
    {
        if (!Sells(c, tier))
        {
            return tier < 2 ? "Tier 1 is always free at the Mirror." : $"Clear a tier-{tier} reality first.";
        }

        if (c.ShardVendorStock <= 0)
        {
            return "Sold out.";
        }

        return wallet.GetBalance(Core.Profile.GoldId) < Price(tier) ? "Not enough gold." : null;
    }

    public static bool TryBuy(CharacterData c, Wallet wallet, int tier, out string message)
    {
        if (CannotBuy(c, wallet, tier) is { } why)
        {
            message = why;
            return false;
        }

        wallet.TrySpend(Core.Profile.GoldId, Price(tier));
        c.ShardVendorStock--;
        c.AddShards(tier);
        message = $"A tier-{tier} Mirror Shard changes hands.";
        return true;
    }

    /// <summary>Pays <see cref="RestockGold"/> for a full stock now (the counter restarts).</summary>
    public static bool TryRestock(CharacterData c, Wallet wallet, out string message)
    {
        if (c.ShardVendorStock >= StockSize)
        {
            message = "His wares are already full.";
            return false;
        }

        if (!wallet.TrySpend(Core.Profile.GoldId, RestockGold))
        {
            message = $"Restocking costs {RestockGold:N0} gold.";
            return false;
        }

        Restock(c);
        message = "He reaches into the dark and the cloth is full again.";
        return true;
    }
}
