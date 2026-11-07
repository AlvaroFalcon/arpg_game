#nullable enable
using System;

namespace ArpgGame.Core;

/// <summary>
/// A tiny static event bus for things the player did that other systems may want to
/// react to (the quest log, achievements, analytics) — docs/design/quests.md ->
/// "Where the events come from". Raisers don't know who listens; with no listener it
/// costs one null check, so tests that run scenes directly are unaffected.
///
/// Subscribers connect in <c>_EnterTree</c> and disconnect in <c>_ExitTree</c> (the
/// bus is static and outlives every scene). Event ids are the constants below.
/// </summary>
public static class GameEvents
{
    /// <summary>Escaldón's Buy skills tab sold a gem (Odran's Skill gems tab before 2026-11-01). key = the gem id ("spark"), value = 1 for a support, 0 for a skill.</summary>
    public const string GemBought = "gem_bought";

    /// <summary>An item was turned into dust at the Artificer's bench. key = the item's rarity name.</summary>
    public const string Disenchanted = "disenchanted";

    /// <summary>The Artificer forged an item with a guaranteed modifier.</summary>
    public const string Forged = "forged";

    /// <summary>Arehucaz sold a Mirror Shard. value = its tier.</summary>
    public const string ShardBought = "shard_bought";

    /// <summary>The altar's choice was confirmed. key = the ascendancy id.</summary>
    public const string AscendancyChosen = "ascendancy_chosen";

    /// <summary>A jewel was socketed in the Mirror's tree. key = the node id.</summary>
    public const string JewelSocketed = "jewel_socketed";

    /// <summary>Odran bought an item from the player.</summary>
    public const string ItemSold = "item_sold";

    /// <summary>A reality's boss fell (not the introduction's). value = the reality's tier.</summary>
    public const string BossKilled = "boss_killed";

    /// <summary>A Mirror Jewel was picked up (already counted in <c>Jewels.Found</c>).</summary>
    public const string JewelFound = "jewel_found";

    /// <summary>A Mirror Shard was picked up. value = its tier.</summary>
    public const string ShardFound = "shard_found";

    /// <summary>The hideout finished loading (every visit).</summary>
    public const string HideoutEntered = "hideout_entered";

    // --- Survival Rift (docs/design/survival-roadmap.md -> M0, M7) -----------------------
    // key carries the run's context as "tier=3;fears=2;coop=1;class=warrior;melee=1".

    /// <summary>A Rift chest was opened. value = its size (1, 3 or 5).</summary>
    public const string RiftChestOpened = "rift_chest";

    /// <summary>A Rift shrine was used. key = its kind ("greed", "challenge", "charge", "magnet") then the context.</summary>
    public const string RiftShrineUsed = "rift_shrine";

    /// <summary>A Rift skill evolved. key = the evolution id then the context.</summary>
    public const string RiftEvolved = "rift_evolved";

    /// <summary>An endless-phase milestone was reached. value = its minutes (5, 10, 20, 30).</summary>
    public const string RiftEndlessMinute = "rift_endless";

    /// <summary>The Colossus fell. value = the Rift tier.</summary>
    public const string RiftCleared = "rift_cleared";

    /// <summary>The run level went up. value = the new level.</summary>
    public const string RiftLevelReached = "rift_level";

    /// <summary>(eventId, value, key) — see the constants above for what each carries.</summary>
    public static event Action<string, int, string>? Raised;

    public static void Raise(string eventId, int value = 0, string key = "") => Raised?.Invoke(eventId, value, key);
}
