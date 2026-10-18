#nullable enable
using System.Collections.Generic;
using System.Linq;
using ArpgGame.Characters;
using ArpgGame.World;

namespace ArpgGame.Quests;

/// <summary>
/// The six tutorial quests — docs/design/quests.md -> "The six quests" and "Copy".
/// Each teaches one station the moment it becomes available: it unlocks by itself,
/// has one objective (an event), a reward, and a turn-in station.
/// </summary>
public static class QuestCatalog
{
    public const string OdranId = "odran_wares";
    public const string DisenchantId = "forge_disenchant";
    public const string ForgeId = "forge_forge";
    public const string ArehucazId = "arehucaz_shards";
    public const string JewelId = "mirror_jewel";
    public const string AscendId = "ascend";

    public static readonly IReadOnlyList<QuestDef> All = new QuestDef[]
    {
        new()
        {
            Id = OdranId,
            Title = "Odran's wares",
            Station = "merchant",
            Speaker = "Odran the Trader",
            Scope = QuestScope.Character,
            Unlock = c => c.Character.TutorialDone,
            Event = QuestEvents.GemBought,
            Toast = "Odran the Trader, in the plaza",
            OpenTab = "gems",
            OpenText = "Open the Skill gems tab",
            Explain = _ =>
                "\"Welcome, welcome! Gold for your gear, and gear for your gold.\"\n\n"
                + "**Trade:** right-click an item in your bag to sell it. Anything you sold this visit can be bought back for the same price until you walk away. He also sells four cheap crafting shards: Kindling, Flux, Graft and Ash.\n\n"
                + $"**Skill gems:** every skill and support gem in the game, unlocked as your character levels. A bought gem goes straight to your skills (K) at level 1. Skill gems level up as you use them; you can own up to {GemLoot.MaxSupportCopies} of each support, one per loadout slot.",
            Objective = "Buy a gem from Odran's Skill gems tab",
            Hint = "Greyed-out gems unlock at a higher character level.",
            TurnIn = "Socket it in the Skills panel (K): click a slot, then the gem.",
            Rewards = new[] { new QuestReward(RewardKind.Gold, "", 300) },
        },
        new()
        {
            Id = DisenchantId,
            Title = "Breaking it down",
            Station = "forge",
            Speaker = "the Artificer",
            Scope = QuestScope.Account,
            After = OdranId,
            AfterNextVisit = true,
            Event = QuestEvents.Disenchanted,
            Toast = "the Artificer's Forge, in the smithy",
            OpenTab = "disenchant",
            OpenText = "Open Disenchant",
            Explain = _ =>
                "\"Gear you'll never wear still has worth, if you break it right.\"\n\n"
                + "In the **Disenchant** tab, right-click an item in your bag. It turns into dust of its rarity: Grey from common items, Azure from magic, Gilded from rare. Higher item levels give more.\n\n"
                + "Selling gives you gold now. Dust is what the forge spends later.",
            Objective = "Disenchant an item at the Artificer's Forge",
            Hint = "The tooltip shows how much dust an item gives while the tab is open.",
            TurnIn = "That's enough dust for your first forge. Come back when you're ready.",
            Rewards = new[]
            {
                new QuestReward(RewardKind.Currency, "azure_dust", 6),
                new QuestReward(RewardKind.Currency, "grey_dust", 6),
                new QuestReward(RewardKind.Gold, "", 100),
            },
        },
        new()
        {
            Id = ForgeId,
            Title = "A modifier of your choosing",
            Station = "forge",
            Speaker = "the Artificer",
            Scope = QuestScope.Account,
            After = DisenchantId,
            Event = QuestEvents.Forged,
            Toast = "the Artificer's Forge",
            OpenTab = "forge",
            OpenText = "Open Forge",
            Explain = _ =>
                "\"Now make something. You pick the modifier; the forge guarantees it.\"\n\n"
                + "In the **Forge** tab: choose a base, then a modifier it can carry, then a grade. Grey and Azure make a magic item, Gilded and Prismatic a rare one. Better grades give a higher tier of your modifier and more random extras, and cost more dust.\n\n"
                + "How high a tier you can forge depends on the highest reality tier you've cleared, so the forge keeps pace with you.",
            Objective = "Forge an item",
            Hint = "Your reward from the last quest pays for an Azure forge.",
            TurnIn = "Use the Crown Orb from the inventory's Crafting tab (I) to turn a magic item rare. Your new one, perhaps.",
            Rewards = new[]
            {
                new QuestReward(RewardKind.Currency, "crown_orb", 1),
                new QuestReward(RewardKind.Currency, "graft_shard", 2),
            },
        },
        new()
        {
            Id = ArehucazId,
            Title = "The shard dealer",
            Station = "arehucaz",
            Speaker = "Arehucaz",
            Scope = QuestScope.Character,
            Unlock = c => c.Character.HighestTierCleared >= 2,
            AlreadyDone = c => c.Character.ShardVendorStock < ShardVendor.StockSize || c.Character.ShardVendorSince > 0,
            Event = QuestEvents.ShardBought,
            Toast = "Arehucaz, by the north gate",
            OpenText = "Open Arehucaz",
            Explain = c =>
            {
                int best = System.Math.Max(2, c.Character.HighestTierCleared);
                string prices = best <= 2
                    ? $"A tier-2 shard costs {ShardVendor.Price(2)} gold."
                    : $"A tier-2 shard costs {ShardVendor.Price(2)} gold; tier {best} costs {ShardVendor.Price(best):N0}.";
                string range = best <= 2 ? "tier 2" : $"tier 2 up to your best, tier {best}";
                return $"\"You have walked through tier {best}. Now we can deal.\"\n\n"
                    + $"Arehucaz sells Mirror Shards for gold, but only of tiers you've already cleared: {range}. Tier 1 is always free at the Mirror, so he doesn't sell it. {prices}\n\n"
                    + $"He keeps {ShardVendor.StockSize} shards on his cloth. They come back after {ShardVendor.RestockClears} more cleared realities, or at once for {ShardVendor.RestockGold:N0} gold. Buy from him when bosses haven't dropped the tier you want to run.";
            },
            Objective = "Buy a Mirror Shard from Arehucaz",
            Hint = "He never sells a tier above your best cleared one.",
            TurnIn = "\"Take one more. You'll be back.\"",
            Rewards = new[]
            {
                new QuestReward(RewardKind.Gold, "", 1000),
                new QuestReward(RewardKind.Shard, "best", 1),
            },
        },
        new()
        {
            Id = JewelId,
            Title = "A jewel for the Mirror",
            Station = "mirror",
            Speaker = "the Mirror of Realities",
            Scope = QuestScope.Account,
            Unlock = c => c.Jewels is { Found: >= 1 },
            AlreadyDone = c => c.Jewels is { } j && j.Socketed.Count > 0,
            Event = QuestEvents.JewelSocketed,
            Toast = "the Mirror of Realities, in the plaza",
            OpenTab = "jewels",
            OpenText = "Open the Jewels tab",
            Explain = _ =>
                "You found a **Mirror Jewel**. Jewels power the Mirror itself, and every character on your account shares them.\n\n"
                + "Open the Mirror's **Jewels** tab and click a glowing socket next to the Mirror's Heart. Each socket adds a bonus to every Altered Reality you open: more items, rarer items, more shards, more monsters, more gold or more crafting orbs. The large sockets at the end of each branch are stronger.\n\n"
                + "A jewel comes back out for free as long as the rest stay connected, so experiment. Jewels work in Altered Realities, not in the Survival Rift. Reality bosses drop them.",
            Objective = "Socket a jewel in the Mirror's Jewels tab",
            Hint = "Sockets you can fill now glow green.",
            TurnIn = "The Mirror hums. Here is another jewel to place.",
            Rewards = new[]
            {
                new QuestReward(RewardKind.Jewel, "", 1),
                new QuestReward(RewardKind.Gold, "", 1000),
            },
        },
        new()
        {
            Id = AscendId,
            Title = "Choose an ascendancy",
            Station = "altar",
            Speaker = "the Altar of Ascension",
            Scope = QuestScope.Character,
            Unlock = c => c.Character.AscendancyPoints > 0 && c.Character.Ascendancy is null,
            AlreadyDone = c => c.Character.Ascendancy is not null,
            Event = QuestEvents.AscendancyChosen,
            Toast = "the Altar of Ascension, in the temple",
            OpenText = "Open the altar",
            Explain = c =>
            {
                CharacterData ch = c.Character;
                string[] names = ch.Class.Ascendancies.Select(id => ClassCatalog.GetAscendancy(id)?.Name ?? id).Select(Article).ToArray();
                string list = names.Length <= 1 ? string.Concat(names) : string.Join(", ", names[..^1]) + " or " + names[^1];
                int total = AscendancyCatalog.FirstPoints + AscendancyCatalog.SecondPoints;
                int nodes = ch.Class.Ascendancies.Count > 0 ? AscendancyCatalog.For(ch.Class.Ascendancies[0]).Count() : 8;
                return $"You defeated a tier-{AscendancyCatalog.Tier1Unlock} guardian, and the altar has woken for you.\n\n"
                    + $"An **ascendancy** is your class's specialisation. As a {ch.Class.Name} you can become {list}. Each has {nodes} nodes with effects found nowhere else.\n\n"
                    + $"You have **{ch.AscendancyPoints} ascendancy points** now, and a tier-{AscendancyCatalog.Tier2Unlock} guardian grants {AscendancyCatalog.SecondPoints} more: {total} points for {nodes} nodes, so pick the ones that fit your build.\n\n"
                    + "**The choice of ascendancy is permanent.** Its nodes can be refunded later for gold.";
            },
            Objective = "Choose an ascendancy at the Altar of Ascension",
            Hint = "Read all of them first; you can't change your mind.",
            TurnIn = "The path is chosen. Spend your points on its nodes; hover one to read it.",
            Rewards = new[]
            {
                new QuestReward(RewardKind.Gold, "", 1000),
                new QuestReward(RewardKind.Currency, "crown_orb", 2),
                new QuestReward(RewardKind.Currency, "tempest_orb", 2),
                new QuestReward(RewardKind.Currency, "tuning_orb", 1),
            },
        },
    };

    private static string Article(string name) => (name.Length > 0 && "AEIOUaeiou".Contains(name[0]) ? "an " : "a ") + name;

    public static QuestDef? Get(string id) => All.FirstOrDefault(q => q.Id == id);

    public static bool Exists(string id) => Get(id) is not null;

    /// <summary>The quests whose turn-in is a station.</summary>
    public static IEnumerable<QuestDef> At(string station) => All.Where(q => q.Station == station);
}
