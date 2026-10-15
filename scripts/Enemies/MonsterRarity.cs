#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArpgGame.Enemies;

/// <summary>docs/design/endgame.md -> "Monster rarity".</summary>
public enum MonsterRarity
{
    Normal,
    Magic,
    Rare,
    Unique,
}

/// <summary>A monster modifier: shown under a magic/rare monster's name.</summary>
public sealed class MonsterMod
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Only valid on ranged monsters (extra projectiles).</summary>
    public bool RangedOnly { get; init; }
}

public static class MonsterRarities
{
    public static readonly IReadOnlyList<MonsterMod> Mods = new[]
    {
        new MonsterMod { Id = "hasted", Name = "Hasted" },
        new MonsterMod { Id = "armoured", Name = "Armoured" },
        new MonsterMod { Id = "fire_touched", Name = "Fire-touched" },
        new MonsterMod { Id = "vampiric", Name = "Vampiric" },
        new MonsterMod { Id = "volatile", Name = "Volatile" },
        new MonsterMod { Id = "frost_aura", Name = "Frost Aura" },
        new MonsterMod { Id = "splitter", Name = "Splitter" },
        new MonsterMod { Id = "regenerating", Name = "Regenerating" },
        new MonsterMod { Id = "empowered", Name = "Empowered" },
        new MonsterMod { Id = "barrage", Name = "Barrage", RangedOnly = true },
    };

    /// <summary>A mod by id (co-op replicas are told which mods the host rolled).</summary>
    public static MonsterMod? FindMod(string id)
    {
        foreach (MonsterMod m in Mods)
        {
            if (m.Id == id)
            {
                return m;
            }
        }

        return null;
    }

    public static Color Color(MonsterRarity r) => r switch
    {
        MonsterRarity.Magic => new Color(0.45f, 0.6f, 1f),
        MonsterRarity.Rare => new Color(1f, 0.88f, 0.35f),
        MonsterRarity.Unique => new Color(0.78f, 0.4f, 1f),
        _ => Colors.White,
    };

    public static float LifeMultiplier(MonsterRarity r) => r switch { MonsterRarity.Magic => 2f, MonsterRarity.Rare => 5f, MonsterRarity.Unique => 12f, _ => 1f };

    public static float DamageMultiplier(MonsterRarity r) => r switch { MonsterRarity.Magic => 1.15f, MonsterRarity.Rare => 1.3f, MonsterRarity.Unique => 1.6f, _ => 1f };

    public static float ExperienceMultiplier(MonsterRarity r) => r switch { MonsterRarity.Magic => 3f, MonsterRarity.Rare => 10f, MonsterRarity.Unique => 30f, _ => 1f };

    public static float Scale(MonsterRarity r) => r switch { MonsterRarity.Magic => 1.1f, MonsterRarity.Rare => 1.25f, MonsterRarity.Unique => 1.45f, _ => 1f };

    public static (int Min, int Max) ModCount(MonsterRarity r) => r switch
    {
        MonsterRarity.Magic => (1, 2),
        MonsterRarity.Rare => (2, 3),
        MonsterRarity.Unique => (3, 4),
        _ => (0, 0),
    };

    private static readonly string[] RarePrefix = { "Gloom", "Ash", "Grave", "Dread", "Rot", "Ember", "Hollow", "Bitter", "Vile", "Storm", "Mire", "Shard" };
    private static readonly string[] RareSuffix = { "maw", "fang", "howl", "spine", "eye", "heart", "wing", "gut", "horn", "claw", "veil", "mourn" };
    private static readonly string[] UniqueNames = { "Vorrath", "Ilsemar", "Kethra", "Morvain", "Sythe", "Oruun", "Valka", "Drezzik", "Halveth", "Nymra" };
    private static readonly string[] UniqueTitles = { "the Ember-Sworn", "the Mirror-Born", "the Hollow King", "the Shattered", "the Veiled", "the Undying", "the Tide-Caller", "the Bone-Crowned" };

    public static string RandomName(MonsterRarity r, string baseName, RandomNumberGenerator rng) => r switch
    {
        MonsterRarity.Rare => $"{RarePrefix[rng.RandiRange(0, RarePrefix.Length - 1)]}{RareSuffix[rng.RandiRange(0, RareSuffix.Length - 1)]}",
        MonsterRarity.Unique => $"{UniqueNames[rng.RandiRange(0, UniqueNames.Length - 1)]}, {UniqueTitles[rng.RandiRange(0, UniqueTitles.Length - 1)]}",
        _ => baseName,
    };

    public static List<MonsterMod> RollMods(MonsterRarity r, bool ranged, RandomNumberGenerator rng)
    {
        (int min, int max) = ModCount(r);
        int count = rng.RandiRange(min, max);
        var pool = Mods.Where(m => ranged || !m.RangedOnly).ToList();
        var picked = new List<MonsterMod>();
        while (picked.Count < count && pool.Count > 0)
        {
            MonsterMod m = pool[rng.RandiRange(0, pool.Count - 1)];
            pool.Remove(m);
            picked.Add(m);
        }

        return picked;
    }
}
