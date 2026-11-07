#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Core;

namespace ArpgGame.Run;

/// <summary>One Rift challenge (docs/design/survival-roadmap.md -> M7).</summary>
public sealed class ChallengeDef
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>The <see cref="GameEvents"/> id it listens to.</summary>
    public string Event { get; init; } = "";

    /// <summary>Which raises count: (value, key) → true. Null: every raise.</summary>
    public Func<int, string, bool>? Match { get; init; }

    public int Count { get; init; } = 1;

    /// <summary>An unlock id (<see cref="Unlocks"/>), or null.</summary>
    public string? Unlock { get; init; }

    /// <summary>Orbs paid on completion (id, amount), or null.</summary>
    public (string Id, int Amount)? Currency { get; init; }

    /// <summary>"Unlocks the Starfall evolution", "2 Crown Orbs".</summary>
    public string RewardText { get; init; } = "";
}

/// <summary>
/// Rift challenges — docs/design/survival-roadmap.md -> M7. Account-wide goals about the
/// Survival Rift itself, fed by the <see cref="GameEvents"/> the Rift raises (through the
/// <see cref="Profile"/> autoload, like the quests). They pay with unlocks (an evolution recipe,
/// a keystone, an extra Skip or Banish per run) or orbs — never with percent stats. Completed
/// on the spot, no turn-in: a toast in the Rift, the list in the Rift's entry panel. Progress is
/// <see cref="Profile.RiftChallenges"/> (id → count).
/// </summary>
public static class RiftChallenges
{
    // Unlock ids.
    public const string ExtraBanish = "rift_extra_banish";
    public const string ExtraSkip = "rift_extra_skip";
    public const string Starfall = "evo_starfall";
    public const string Hailstorm = "evo_hailstorm";
    public const string TreasureHunter = "ks_treasure_hunter";

    private static bool Has(string key, string part) => key.Split(';').Contains(part);

    public static readonly IReadOnlyList<ChallengeDef> All = new ChallengeDef[]
    {
        new() { Id = "rift_win", Title = "Heart of embers", Description = "Slay the Ember Colossus.", Event = GameEvents.RiftCleared,
            Unlock = ExtraBanish, RewardText = "+1 Banish every run" },
        new() { Id = "rift_tier3", Title = "Deeper still", Description = "Slay the Colossus on tier 3 or higher.", Event = GameEvents.RiftCleared,
            Match = (v, _) => v >= 3, Unlock = Starfall, RewardText = "Unlocks the Starfall evolution (Meteor + Kindling)" },
        new() { Id = "rift_tier5", Title = "The fifth circle", Description = "Slay the Colossus on tier 5 or higher.", Event = GameEvents.RiftCleared,
            Match = (v, _) => v >= 5, Currency = ("crown_orb", 2), RewardText = "2 Crown Orbs" },
        new() { Id = "rift_tier10", Title = "Bottom of the Rift", Description = "Slay the Colossus on tier 10.", Event = GameEvents.RiftCleared,
            Match = (v, _) => v >= 10, Currency = ("ascendant_orb", 3), RewardText = "3 Ascendant Orbs" },
        new() { Id = "rift_fears3", Title = "Fearless", Description = "Slay the Colossus with three Fear cards.", Event = GameEvents.RiftCleared,
            Match = (_, k) => Has(k, "fears=3"), Unlock = TreasureHunter, RewardText = "Unlocks the Treasure Hunter keystone" },
        new() { Id = "rift_melee", Title = "Up close", Description = "Slay the Colossus with only melee skills.", Event = GameEvents.RiftCleared,
            Match = (_, k) => Has(k, "melee=1"), Currency = ("ash_orb", 3), RewardText = "3 Ash Orbs" },
        new() { Id = "rift_coop", Title = "Shoulder to shoulder", Description = "Slay the Colossus in a party.", Event = GameEvents.RiftCleared,
            Match = (_, k) => Has(k, "coop=1"), Currency = ("crown_orb", 2), RewardText = "2 Crown Orbs" },
        new() { Id = "rift_endless10", Title = "Ten minutes in the deep", Description = "Last 10 minutes of the endless phase.", Event = GameEvents.RiftEndlessMinute,
            Match = (v, _) => v >= 10, Unlock = ExtraSkip, RewardText = "+1 Skip every run" },
        new() { Id = "rift_endless30", Title = "Half an hour of night", Description = "Last 30 minutes of the endless phase.", Event = GameEvents.RiftEndlessMinute,
            Match = (v, _) => v >= 30, Currency = ("tempest_orb", 3), RewardText = "3 Tempest Orbs" },
        new() { Id = "rift_evolve", Title = "Metamorphosis", Description = "Evolve a skill.", Event = GameEvents.RiftEvolved,
            Unlock = Hailstorm, RewardText = "Unlocks the Hailstorm evolution (Frost Shards + Velocity)" },
        new() { Id = "rift_evolve5", Title = "Collector of forms", Description = "Evolve 5 skills (over any runs).", Event = GameEvents.RiftEvolved,
            Count = 5, Currency = ("sundering_orb", 2), RewardText = "2 Sundering Orbs" },
        new() { Id = "rift_golden", Title = "Gold beneath the ash", Description = "Open a golden chest.", Event = GameEvents.RiftChestOpened,
            Match = (v, _) => v >= 5, Currency = ("crown_orb", 1), RewardText = "1 Crown Orb" },
        new() { Id = "rift_greed5", Title = "More, always more", Description = "Reach Greed ×5 in one run.", Event = GameEvents.RiftShrineUsed,
            Match = (v, k) => k.StartsWith("greed") && v >= 5, Currency = ("tuning_orb", 2), RewardText = "2 Tuning Orbs" },
        new() { Id = "rift_level40", Title = "Burning bright", Description = "Reach run level 40.", Event = GameEvents.RiftLevelReached,
            Match = (v, _) => v >= 40, Currency = ("tempest_orb", 1), RewardText = "1 Tempest Orb" },
    };

    public static ChallengeDef? Get(string id) => All.FirstOrDefault(c => c.Id == id);

    /// <summary>A challenge was completed (the Rift HUD's toast).</summary>
    public static event Action<ChallengeDef>? Completed;

    /// <summary>Progress of a challenge (0..Count).</summary>
    public static int ProgressOf(IReadOnlyDictionary<string, int> progress, ChallengeDef c) => Math.Clamp(progress.GetValueOrDefault(c.Id), 0, c.Count);

    public static bool IsDone(IReadOnlyDictionary<string, int> progress, ChallengeDef c) => ProgressOf(progress, c) >= c.Count;

    /// <summary>
    /// Feeds one event to every challenge (pure over <paramref name="progress"/>): advances those
    /// it matches and returns those it completed. The caller pays them (<see cref="Pay"/>).
    /// </summary>
    public static List<ChallengeDef> Advance(Dictionary<string, int> progress, string eventId, int value, string key)
    {
        var done = new List<ChallengeDef>();
        if (!Features.RiftChallenges)
        {
            return done;
        }

        foreach (ChallengeDef c in All)
        {
            if (c.Event != eventId || IsDone(progress, c) || (c.Match is { } m && !m(value, key ?? "")))
            {
                continue;
            }

            progress[c.Id] = ProgressOf(progress, c) + 1;
            if (IsDone(progress, c))
            {
                done.Add(c);
            }
        }

        return done;
    }

    /// <summary>Pays a completed challenge: its unlock, and its orbs into the local player's wallet
    /// (which the profile captures) or straight into the account when no player is loaded.</summary>
    public static void Pay(Profile profile, ChallengeDef c)
    {
        if (c.Unlock is { } unlock)
        {
            profile.GrantUnlock(unlock);
        }

        if (c.Currency is { } currency && currency.Amount > 0)
        {
            if (profile.IsInsideTree() && Combat.CombatQuery.LocalPlayer(profile.GetTree()) is { } player)
            {
                player.Wallet.Add(currency.Id, currency.Amount);
            }
            else
            {
                profile.Currency[currency.Id] = profile.Currency.GetValueOrDefault(currency.Id) + currency.Amount;
            }
        }

        Completed?.Invoke(c);
    }

    /// <summary>The profile's hook: advance, pay, save.</summary>
    public static void Handle(Profile profile, string eventId, int value, string key)
    {
        if (!eventId.StartsWith("rift_"))
        {
            return;
        }

        List<ChallengeDef> done = Advance(profile.RiftChallenges, eventId, value, key);
        foreach (ChallengeDef c in done)
        {
            Pay(profile, c);
        }
    }
}
