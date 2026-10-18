#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Items;

namespace ArpgGame.Run;

/// <summary>
/// What a Survival Rift pays out at the end — docs/design/run.md -> "Rewards".
/// Everything grows with the time survived, so the endless phase after the
/// Colossus is a real choice: step through the portal now, or risk the run for
/// more gold, more orbs, more item cards to choose from and more of them to keep.
/// </summary>
public sealed class RunRewards
{
    /// <summary>docs/design/run.md -> "Minimum for an item": a run earns item
    /// cards once it lasted this long OR killed this many — leaving at once
    /// (or standing still in the first seconds) earns gold and orbs by the
    /// formula, which is about nothing, and no item.</summary>
    public const float MinItemSeconds = 60f;
    public const int MinItemKills = 30;

    public static bool EarnsItems(float elapsed, int kills) => elapsed >= MinItemSeconds || kills >= MinItemKills;

    /// <summary>False when the run was too short for an item (<see cref="EarnsItems"/>).</summary>
    public bool ItemsEarned { get; init; } = true;

    /// <summary>The results screen's explanation when <see cref="ItemsEarned"/> is false.</summary>
    public string NoItemReason { get; init; } = "";

    public long Gold { get; init; }

    /// <summary>Crafting orbs by id (rolled with the normal drop weights). Empty in a
    /// <see cref="Preview"/>, which only counts them (<see cref="OrbCount"/>).</summary>
    public List<string> Orbs { get; init; } = new();

    /// <summary>How many orbs are earned, the bonus Ascendant Orbs of <see cref="BonusAscendant"/> included.</summary>
    public int OrbCount { get; init; }

    /// <summary>Reward tier (docs/design/run.md -> "Reward tiers"): 0 before the Colossus
    /// falls, 1 from its kill, then one per step of <see cref="Tiers"/>.</summary>
    public int Tier { get; init; }

    /// <summary>How many of the item cards are guaranteed Rare or better (tier V on).</summary>
    public int GuaranteedRare { get; init; }

    /// <summary>Ascendant Orbs the tiers from X on add to the orbs.</summary>
    public int BonusAscendant { get; init; }

    /// <summary>When the next tier is reached (run seconds).</summary>
    public float NextTierAt { get; init; }

    /// <summary>"Tier III at 12:00: 5 cards" (empty before the Colossus).</summary>
    public string NextTierText { get; init; } = "";

    /// <summary>Item cards offered.</summary>
    public int Choices { get; init; }

    /// <summary>How many of them may be kept.</summary>
    public int Keep { get; init; }

    /// <summary>Scales every chance of the reward's rarity cascade (DropTables, RiftReward).</summary>
    public float RarityMultiplier { get; init; } = 1f;
    public int ItemLevel { get; init; }

    // --- tiers (docs/design/run.md -> "Reward tiers") -----------------------------------

    /// <summary>Seconds at which tiers II to IX are reached; tier X and up follow every <see cref="TierStepAfterIX"/>.</summary>
    private static readonly float[] TierTimes = { 0f, 600f, 720f, 900f, 1200f, 1500f, 1800f, 2100f, 2400f };

    public const float FirstAscendantTier = 2700f;
    public const float TierStepAfterIX = 300f;

    private static readonly string[] TierAdds =
    {
        "4 item cards, keep 1", "keep 2", "5 cards", "keep 3",
        "1 card guaranteed Rare or better", "2 cards guaranteed Rare or better", "3 cards guaranteed Rare or better",
        "4 cards guaranteed Rare or better", "5 cards guaranteed Rare or better",
    };

    /// <summary>The tier reached at <paramref name="elapsed"/> seconds: 0 without the victory, I from the kill, II at 10:00 …</summary>
    public static int TierAt(float elapsed, bool victory)
    {
        if (!victory)
        {
            return 0;
        }

        int tier = 1;
        for (int i = 1; i < TierTimes.Length; i++)
        {
            if (elapsed >= TierTimes[i])
            {
                tier = i + 1;
            }
        }

        if (elapsed >= FirstAscendantTier)
        {
            tier = 10 + Mathf.FloorToInt((elapsed - FirstAscendantTier) / TierStepAfterIX);
        }

        return tier;
    }

    /// <summary>When tier <paramref name="tier"/> (2 and up) begins, in run seconds.</summary>
    public static float TierStart(int tier) => tier <= 9 ? TierTimes[Mathf.Max(0, tier - 1)] : FirstAscendantTier + (tier - 10) * TierStepAfterIX;

    /// <summary>What a tier adds, for "Tier III at 12:00: 5 cards".</summary>
    public static string TierAdd(int tier) => tier <= 9 ? TierAdds[Mathf.Max(0, tier - 1)] : "+1 Ascendant Orb";

    /// <summary>The toast when a tier is reached: "Reward tier III: 5 item cards".</summary>
    public static string TierToast(int tier) => $"Reward tier {Roman(tier)}: " + tier switch
    {
        2 => "keep 2 items",
        3 => "5 item cards",
        4 => "keep 3 items",
        >= 5 and <= 9 => TierAdd(tier),
        _ => TierAdd(tier),
    };

    public static string Roman(int n)
    {
        string[] ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
        string r = "";
        while (n >= 10)
        {
            r += "X";
            n -= 10;
        }

        return r + ones[Mathf.Clamp(n, 0, 9)];
    }

    /// <summary>The cards that must be Rare or better at tier <paramref name="tier"/>.</summary>
    public static int GuaranteedRareAt(int tier) => Mathf.Clamp(tier - 4, 0, 5);

    /// <summary>Ascendant Orbs the tier adds (tier X and up: one each).</summary>
    public static int BonusAscendantAt(int tier) => Mathf.Max(0, tier - 9);

    /// <summary>
    /// The reward as it stands, without rolling anything (the cash-out panel and the HUD,
    /// four times a second): the same gold, orb count, cards, keep, item level and rarity
    /// as <see cref="For"/> at the same moment, with no orb kinds.
    /// </summary>
    public static RunRewards Preview(float elapsed, int bosses, int kills, bool victory) => Build(elapsed, bosses, kills, victory, null);

    public static RunRewards For(float elapsed, int bosses, int kills, bool victory, RandomNumberGenerator rng) => Build(elapsed, bosses, kills, victory, rng);

    private static RunRewards Build(float elapsed, int bosses, int kills, bool victory, RandomNumberGenerator? rng)
    {
        float m = elapsed / 60f;
        int tier = TierAt(elapsed, victory);
        int bonusAscendant = BonusAscendantAt(tier);
        int baseOrbs = Mathf.FloorToInt(m / 1.5f) + bosses + (victory ? 2 : 0);
        var orbs = new List<string>();
        if (rng is not null)
        {
            for (int i = 0; i < baseOrbs; i++)
            {
                orbs.Add(DropTables.RollOrb(rng, 1f + m * DropTables.PerRiftMinute).Id);
            }

            for (int i = 0; i < bonusAscendant; i++)
            {
                orbs.Add("ascendant_orb");
            }
        }

        bool earned = EarnsItems(elapsed, kills);
        int nextTier = tier + 1;
        return new RunRewards
        {
            Tier = tier,
            GuaranteedRare = earned ? GuaranteedRareAt(tier) : 0,
            BonusAscendant = bonusAscendant,
            OrbCount = baseOrbs + bonusAscendant,
            NextTierAt = tier == 0 ? 0f : TierStart(nextTier),
            NextTierText = tier == 0 ? "" : $"Tier {Roman(nextTier)} at {UI.UiTheme.FormatTime(TierStart(nextTier))}: {TierAdd(nextTier)}",
            ItemsEarned = earned,
            NoItemReason = earned ? "" : $"No item this time: a Rift pays an item after {Mathf.RoundToInt(MinItemSeconds)} s or {MinItemKills} kills (this one: {Mathf.FloorToInt(elapsed)} s, {kills} kills).",
            Gold = Mathf.RoundToInt(15f * Mathf.Pow(m, 1.6f) + kills * 0.05f + (victory ? 150f : 0f)),
            Orbs = orbs,
            Choices = earned ? 3 + (m >= 7f ? 1 : 0) + (m >= 12f ? 1 : 0) : 0,
            Keep = earned ? 1 + (m >= 10f ? 1 : 0) + (m >= 15f ? 1 : 0) : 0,
            // +12 % per minute, +15 % per boss, +30 % for the win: a 7:30 win with three
            // bosses ×2.65 (Rare 40 %), sixteen endless minutes ×4.2 (Rare 63 %).
            RarityMultiplier = 1f + 0.12f * m + 0.15f * bosses + (victory ? 0.3f : 0f),
            ItemLevel = Mathf.Clamp(3 + Mathf.RoundToInt(m * 5f) + bosses * 8, 1, 100),
        };
    }

    /// <summary>Puts the gold and orbs in the player's wallet (captured into the
    /// profile right after).</summary>
    public void GrantTo(Player player)
    {
        if (Gold > 0)
        {
            player.Wallet.Add(Core.Profile.GoldId, Gold);
        }

        foreach (string id in Orbs)
        {
            player.Wallet.Add(id, 1);
        }
    }
}
