#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArpgGame.Run;

/// <summary>A Fear card (docs/design/survival-roadmap.md -> M4): chosen before the run, it
/// makes the Rift harder in one way and raises every reward multiplier.</summary>
public sealed class FearDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public float Life { get; init; } = 1f;
    public float Speed { get; init; } = 1f;
    public float Spawn { get; init; } = 1f;
    public float SwarmInterval { get; init; } = 1f;
    public bool NoLifeOrbs { get; init; }
    public bool VolatileElites { get; init; }
}

/// <summary>
/// How hard this Survival Rift is beyond its clock — docs/design/survival-roadmap.md ->
/// M0. The director's curves (<see cref="RunDirector.SpawnRate"/>, <see cref="RunDirector.Scaling"/>...)
/// stay pure functions of time; this multiplies them. Three sources: the Rift
/// <see cref="Tier"/> chosen at the portal (M4), the <see cref="Fears"/> taken with it (M4), and
/// the <see cref="Greed"/> stacks bought at shrines during the run (M3). Every reward the
/// harder Rift pays back is here too, so a number lives in one place. All multipliers are
/// exactly 1 for a default instance: the Rift plays as it did before.
/// </summary>
public sealed class RiftDifficulty
{
    public const int MaxTier = 10;

    /// <summary>Rift tier, 1..<see cref="MaxTier"/>.</summary>
    public int Tier { get; set; } = 1;

    /// <summary>Greed shrines used this run (party-wide).</summary>
    public int Greed { get; set; }

    public const int MaxGreed = 10;
    public const float GreedHarder = 0.06f;
    public const float GreedRicher = 0.10f;

    /// <summary>Every Fear card raises the reward multipliers by this much.</summary>
    public const float FearReward = 0.20f;
    public const int MaxFears = 3;

    public List<FearDefinition> Fears { get; } = new();

    public static readonly IReadOnlyList<FearDefinition> AllFears = new[]
    {
        new FearDefinition { Id = "swift", Name = "Swift", Description = "Monsters move 20% faster.", Speed = 1.2f },
        new FearDefinition { Id = "thick", Name = "Thick", Description = "Monsters have 40% more life.", Life = 1.4f },
        new FearDefinition { Id = "hordes", Name = "Hordes", Description = "25% more monsters, and a higher crowd cap.", Spawn = 1.25f },
        new FearDefinition { Id = "volatile", Name = "Volatile", Description = "Elites burst when they die. Step away.", VolatileElites = true },
        new FearDefinition { Id = "famine", Name = "Famine", Description = "No life orbs drop.", NoLifeOrbs = true },
        new FearDefinition { Id = "short_fuse", Name = "Short fuse", Description = "Swarms come twice as often.", SwarmInterval = 0.5f },
    };

    public static FearDefinition? FindFear(string id) => AllFears.FirstOrDefault(f => f.Id == id);

    private int T => Mathf.Clamp(Tier, 1, MaxTier) - 1;
    private float GreedHard => 1f + GreedHarder * Greed;

    /// <summary>Monster life: ×1.5 per tier, Greed, Thick.</summary>
    public float Life => (1f + 0.5f * T) * GreedHard * Fears.Aggregate(1f, (m, f) => m * f.Life);

    /// <summary>Monster damage: ×1.2 per tier, Greed.</summary>
    public float Damage => (1f + 0.2f * T) * GreedHard;

    /// <summary>Spawns per second (and the alive cap, through <see cref="Cap"/>).</summary>
    public float Spawn => (1f + 0.06f * T) * GreedHard * Fears.Aggregate(1f, (m, f) => m * f.Spawn);

    public float Cap => Spawn;

    /// <summary>Monster move speed.</summary>
    public float Speed => Fears.Aggregate(1f, (m, f) => m * f.Speed);

    /// <summary>Seconds between swarms.</summary>
    public float SwarmInterval => Fears.Aggregate(1f, (m, f) => m * f.SwarmInterval);

    public bool NoLifeOrbs => Fears.Any(f => f.NoLifeOrbs);
    public bool VolatileElites => Fears.Any(f => f.VolatileElites);

    /// <summary>Chance an ordinary spawn comes as a Magic monster (one rolled modifier): from
    /// tier 4 10 %, from tier 7 20 %.</summary>
    public float MagicChance => Tier >= 7 ? 0.2f : Tier >= 4 ? 0.1f : 0f;

    private float FearBonus => FearReward * Fears.Count;

    /// <summary>Experience from gems: Greed.</summary>
    public float Experience => 1f + GreedRicher * Greed;

    /// <summary>Gold and orb drops and the end-of-run gold and orbs.</summary>
    public float Gold => (1f + 0.15f * T + FearBonus) * (1f + GreedRicher * Greed);

    /// <summary>Added to the item rarity multiplier at the end of the run.</summary>
    public float RarityBonus => 0.2f * T + FearBonus;

    /// <summary>Added to the item level of every Rift item.</summary>
    public int ItemLevelBonus => 6 * T;

    public bool IsDefault => Tier <= 1 && Greed == 0 && Fears.Count == 0;

    /// <summary>"Tier 3 · Swift, Thick · Greed ×2", or "" for a default Rift.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Tier > 1)
        {
            parts.Add($"Tier {Tier}");
        }

        if (Fears.Count > 0)
        {
            parts.Add(string.Join(", ", Fears.Select(f => f.Name)));
        }

        if (Greed > 0)
        {
            parts.Add($"Greed ×{Greed}");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>The run's context for quest events (docs/design/survival-roadmap.md -> M7).</summary>
    public string Context => $"tier={Tier};fears={Fears.Count};greed={Greed}";
}
