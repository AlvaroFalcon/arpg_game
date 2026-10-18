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
    /// <summary>Odran's Skill gems tab sold a gem. key = the gem id ("spark"), value = 1 for a support, 0 for a skill.</summary>
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

    /// <summary>(eventId, value, key) — see the constants above for what each carries.</summary>
    public static event Action<string, int, string>? Raised;

    public static void Raise(string eventId, int value = 0, string key = "") => Raised?.Invoke(eventId, value, key);
}
