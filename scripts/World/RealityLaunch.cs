#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArpgGame.World;

/// <summary>
/// A shard modifier — docs/design/endgame.md -> "Shard modifiers". Empowering a
/// shard at the Mirror (for gold) rolls one to three of these: each makes the
/// reality harder and pays for it with item quantity and rarity.
/// </summary>
public sealed class RealityModifier
{
    public string Id { get; init; } = "";
    public string Text { get; init; } = "";
    public float Quantity { get; init; }
    public float Rarity { get; init; }

    public static readonly IReadOnlyList<RealityModifier> All = new[]
    {
        new RealityModifier { Id = "hardened", Text = "Monsters have 40% more life", Quantity = 0.15f, Rarity = 0.1f },
        new RealityModifier { Id = "searing", Text = "Monsters deal 25% more damage", Quantity = 0.15f, Rarity = 0.15f },
        new RealityModifier { Id = "swift", Text = "Monsters move 20% faster", Quantity = 0.1f, Rarity = 0.1f },
        new RealityModifier { Id = "hordes", Text = "Packs are larger and appear more often", Quantity = 0.25f, Rarity = 0.05f },
        new RealityModifier { Id = "elite", Text = "More magic and rare packs", Quantity = 0.15f, Rarity = 0.2f },
        new RealityModifier { Id = "volatile", Text = "A quarter of monsters explode on death", Quantity = 0.12f, Rarity = 0.1f },
        new RealityModifier { Id = "frail", Text = "Players have 15% less maximum life", Quantity = 0.12f, Rarity = 0.12f },
        new RealityModifier { Id = "exposed", Text = "Players have -20% to all elemental resistances", Quantity = 0.1f, Rarity = 0.1f },
    };

    public static RealityModifier? Find(string id) => All.FirstOrDefault(m => m.Id == id);
}

/// <summary>What the next reality is: set by the Mirror, read once by the
/// reality controller (same one-shot pattern as the survival run's options).</summary>
public static class RealityLaunch
{
    public static int Tier = 1;
    public static string? LayoutId;
    public static List<string> Modifiers = new();

    /// <summary>The map seed (docs/design/endgame.md -> "Caverns"): picks the cavern
    /// style and everything about its shape. Null = random.</summary>
    public static ulong? Seed;

    /// <summary>
    /// The seed of the next shard of <paramref name="tier"/> for this character: the
    /// same shard always opens the same cavern (the Mirror can preview it), and each
    /// shard opened moves on to a new one. FNV-1a over the character, the tier and how
    /// many of that tier were opened — stable across runs, unlike string hashes.
    /// </summary>
    public static ulong ShardSeed(Characters.CharacterData c, int tier)
    {
        int opened = c.ShardsOpened.TryGetValue(tier, out int n) ? n : 0;
        ulong hash = 14695981039346656037UL;
        foreach (char ch in $"{c.Name}|{c.ClassId}|{tier}|{opened}")
        {
            hash = (hash ^ ch) * 1099511628211UL;
        }

        return hash;
    }

    /// <summary>The cavern a seed opens.</summary>
    public static LayoutDef LayoutFor(ulong seed) => LayoutCatalog.Layouts[(int)(seed % (ulong)LayoutCatalog.Layouts.Count)];

    /// <summary>The next reality is the introduction (docs/design/tutorial.md).</summary>
    public static bool Tutorial;

    /// <summary>Gold to empower a shard of a tier.</summary>
    public static long EmpowerCost(int tier) => 15 + tier * 12;

    public static List<string> RollModifiers(int tier, RandomNumberGenerator rng)
    {
        int count = Mathf.Clamp(1 + tier / 7 + (rng.Randf() < 0.4f ? 1 : 0), 1, 3);
        return RealityModifier.All.OrderBy(_ => rng.Randf()).Take(count).Select(m => m.Id).ToList();
    }

    public static (float Quantity, float Rarity) Bonuses(IEnumerable<string> mods)
    {
        float q = 0f;
        float r = 0f;
        foreach (string id in mods)
        {
            if (RealityModifier.Find(id) is { } m)
            {
                q += m.Quantity;
                r += m.Rarity;
            }
        }

        return (q, r);
    }

    public static void Reset()
    {
        Tier = 1;
        Seed = null;
        LayoutId = null;
        Modifiers = new List<string>();
        Tutorial = false;
    }
}
