#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Items;
using ArpgGame.Quests;

namespace ArpgGame.Characters;

/// <summary>One owned gem: a skill gem or a support gem (docs/design/classes.md ->
/// "How it plugs into what exists": skills and supports are items, not level-up cards).</summary>
public sealed class GemData
{
    public string Uid { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Id { get; set; } = "";
    public bool IsSupport { get; set; }
    public int Level { get; set; } = 1;
    public float Experience { get; set; }

    public const int MaxLevel = 20;

    /// <summary>Gem experience to the next level.</summary>
    public static float ExperienceToNext(int level) => 60f * Mathf.Pow(level, 1.8f);

    /// <summary>Adds experience; returns true if the gem levelled.</summary>
    public bool AddExperience(float amount)
    {
        if (IsSupport || Level >= MaxLevel)
        {
            return false;
        }

        Experience += amount;
        bool levelled = false;
        while (Level < MaxLevel && Experience >= ExperienceToNext(Level))
        {
            Experience -= ExperienceToNext(Level);
            Level++;
            levelled = true;
        }

        return levelled;
    }
}

/// <summary>A loadout slot: one skill gem plus the support gems socketed into it.</summary>
public sealed class LoadoutSlot
{
    public string? SkillUid { get; set; }
    public List<string> SupportUids { get; set; } = new();
}

/// <summary>
/// One persistent character — docs/design/endgame.md / classes.md. Everything
/// that belongs to a character and survives between realities: class, level and
/// experience, passive tree and ascendancy, gems and loadout, gear, gold, shards
/// and records. <see cref="Core.Profile"/> holds a list of these.
///
/// Gear lives here as live (detached) <see cref="EquipmentItem"/> nodes, the
/// same representation the inventory itself uses; the profile serialises them.
/// </summary>
public sealed class CharacterData
{
    public const int MaxLevel = 70;
    /// <summary>LMB, RMB, 1, 2, 3, 4 — see <see cref="SlotLabels"/>.</summary>
    public const int LoadoutSlots = 6;

    public static readonly string[] SlotLabels = { "LMB", "RMB", "1", "2", "3", "4" };

    public string Name { get; set; } = "Hero";
    public string ClassId { get; set; } = "arcanist";
    public int Level { get; set; } = 1;
    public float Experience { get; set; }

    public HashSet<string> Passives { get; set; } = new();

    public string? Ascendancy { get; set; }
    public HashSet<string> AscendancyNodes { get; set; } = new();

    /// <summary>Ascendancy points unlocked by trials (tier 5 boss: 4, tier 12 boss: +2).</summary>
    public int AscendancyPoints { get; set; }

    public List<GemData> Gems { get; set; } = new();
    public LoadoutSlot[] Loadout { get; set; } = Enumerable.Range(0, LoadoutSlots).Select(_ => new LoadoutSlot()).ToArray();

    /// <summary>Mirror Shards owned, by tier (1-20).</summary>
    public Dictionary<int, int> Shards { get; set; } = new();

    public long Gold { get; set; }

    /// <summary>Orbs and dust from saves older than account-wide currency; Profile.LoadJson
    /// moves them into Profile.Currency (docs/design/crafting.md).</summary>
    public Dictionary<string, long> Currency { get; set; } = new();
    public int HighestTierCleared { get; set; }
    public int RealitiesCleared { get; set; }
    public int Deaths { get; set; }

    /// <summary>Arehucaz's shards left for this character (World.ShardVendor).</summary>
    public int ShardVendorStock { get; set; } = World.ShardVendor.StockSize;

    /// <summary><see cref="RealitiesCleared"/> at Arehucaz's last restock.</summary>
    public int ShardVendorSince { get; set; }

    /// <summary>The character level at which the player last opened Odran's Skill gems
    /// tab; gems unlocked since then are "new" (<see cref="GemShop.NewCount"/>).</summary>
    public int GemShopSeenLevel { get; set; }

    /// <summary>False for a freshly created character until they finish (or
    /// skip) the introduction reality — docs/design/tutorial.md.</summary>
    public bool TutorialDone { get; set; } = true;
    public int TotalKills { get; set; }
    public int SurvivalRuns { get; set; }
    public float BestSurvivalTime { get; set; }

    /// <summary>Progress of the character-scope quests (docs/design/quests.md); the account's are in <c>Profile.Quests</c>.</summary>
    public Dictionary<string, QuestProgress> Quests { get; set; } = new();

    // Runtime gear (see class doc).
    public List<(EquipmentItem Item, Vector2I Cell)> Inventory { get; } = new();
    public Dictionary<EquipmentSlot, EquipmentItem> Equipped { get; } = new();

    public ClassDefinition Class => ClassCatalog.Get(ClassId);

    public AscendancyDefinition? AscendancyDef => ClassCatalog.GetAscendancy(Ascendancy);

    /// <summary>docs/design/endgame.md -> "Persistent character": 40·L^1.6.</summary>
    public static float ExperienceToNext(int level) => 40f * Mathf.Pow(level, 1.6f);

    public int PassivePointsTotal => Level - 1;

    /// <summary>Allocated points (start nodes are free).</summary>
    public int PassivePointsSpent => Passives.Count(id => !id.StartsWith("start_"));

    public int PassivePointsFree => PassivePointsTotal - PassivePointsSpent;

    public int AscendancyPointsFree => AscendancyPoints - AscendancyNodes.Count;

    /// <summary>Support sockets per skill: 2, +1 at level 12, +1 at level 28
    /// (the Sage adds one more on top).</summary>
    public int SupportSockets => 2 + (Level >= 12 ? 1 : 0) + (Level >= 28 ? 1 : 0) + (AscendancyNodes.Contains("sage_insight") ? 1 : 0);

    /// <summary>Adds experience; returns the number of levels gained.</summary>
    public int AddExperience(float amount)
    {
        if (Level >= MaxLevel)
        {
            return 0;
        }

        Experience += amount;
        int gained = 0;
        while (Level < MaxLevel && Experience >= ExperienceToNext(Level))
        {
            Experience -= ExperienceToNext(Level);
            Level++;
            gained++;
        }

        return gained;
    }

    public GemData? FindGem(string? uid) => uid is null ? null : Gems.FirstOrDefault(g => g.Uid == uid);

    public GemData AddGem(string id, bool support, int level = 1)
    {
        var gem = new GemData { Id = id, IsSupport = support, Level = level };
        Gems.Add(gem);
        return gem;
    }

    public int ShardCount(int tier) => Shards.TryGetValue(tier, out int n) ? n : 0;

    public void AddShards(int tier, int count = 1)
    {
        tier = Mathf.Clamp(tier, 1, 20);
        Shards[tier] = ShardCount(tier) + count;
    }

    /// <summary>Spends a shard. Tier 1 is always free (docs/design/endgame.md ->
    /// "Mirror Shards": you can never lock yourself out of the endgame).</summary>
    public bool TrySpendShard(int tier)
    {
        if (tier <= 1)
        {
            return true;
        }

        int have = ShardCount(tier);
        if (have <= 0)
        {
            return false;
        }

        Shards[tier] = have - 1;
        return true;
    }

    /// <summary>A brand-new character of a class: starting gems socketed and ready.</summary>
    public static CharacterData Create(string name, string classId)
    {
        ClassDefinition cls = ClassCatalog.Get(classId);
        var data = new CharacterData { Name = name, ClassId = cls.Id };
        data.Passives.Add(cls.TreeStart);
        GemData skill = data.AddGem(cls.StartingSkill, false);
        GemData support = data.AddGem(cls.StartingSupport, true);
        data.Loadout[0].SkillUid = skill.Uid;
        data.Loadout[0].SupportUids.Add(support.Uid);
        return data;
    }
}
