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

    /// <summary>Crafting orbs by id (rolled with the normal drop weights).</summary>
    public List<string> Orbs { get; init; } = new();

    /// <summary>Item cards offered.</summary>
    public int Choices { get; init; }

    /// <summary>How many of them may be kept.</summary>
    public int Keep { get; init; }

    /// <summary>Scales every chance of the reward's rarity cascade (DropTables, RiftReward).</summary>
    public float RarityMultiplier { get; init; } = 1f;
    public int ItemLevel { get; init; }

    public static RunRewards For(float elapsed, int bosses, int kills, bool victory, RandomNumberGenerator rng)
    {
        float m = elapsed / 60f;
        int orbCount = Mathf.FloorToInt(m / 1.5f) + bosses + (victory ? 2 : 0);
        var orbs = new List<string>();
        for (int i = 0; i < orbCount; i++)
        {
            orbs.Add(DropTables.RollOrb(rng, 1f + m * DropTables.PerRiftMinute).Id);
        }

        bool earned = EarnsItems(elapsed, kills);
        return new RunRewards
        {
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
