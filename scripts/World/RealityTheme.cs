#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace PathToExile.World;

/// <summary>A reality's look and population (docs/design/endgame.md -> "Layout" table).</summary>
public sealed class RealityTheme
{
    public string Id { get; init; } = "";
    public string TilesetPath { get; init; } = "";
    public IReadOnlyList<string> Roster { get; init; } = new List<string>();
    public string Boss { get; init; } = "";

    /// <summary>Multiplier on the boss's authored base life (the Herald was
    /// authored as a mid-run boss and needs more to anchor a reality).</summary>
    public float BossLifeMultiplier { get; init; } = 1f;

    public Color Ambient { get; init; } = Colors.White;
    public string Music { get; init; } = "music_run";
    public Color Void { get; init; } = new(0.03f, 0.02f, 0.03f);

    public static readonly IReadOnlyList<RealityTheme> Themes = new[]
    {
        new RealityTheme
        {
            Id = "ashen", TilesetPath = "res://assets/tiles/ashen_arena_tileset.tres",
            Roster = new[] { "Slime", "Bat", "Imp", "Brute", "Caster" }, Boss = "EmberColossus",
            BossLifeMultiplier = 0.45f, Ambient = new Color(1f, 0.92f, 0.86f), Void = new Color(0.06f, 0.02f, 0.01f),
        },
        new RealityTheme
        {
            Id = "crypt", TilesetPath = "res://assets/tiles/bone_crypt_tileset.tres",
            Roster = new[] { "Skeleton", "Bat", "Caster", "Skeleton", "Brute" }, Boss = "BoneHerald",
            BossLifeMultiplier = 2.6f, Ambient = new Color(0.82f, 0.88f, 0.95f), Void = new Color(0.02f, 0.03f, 0.04f),
        },
        new RealityTheme
        {
            Id = "drowned", TilesetPath = "res://assets/tiles/drowned_mirror_tileset.tres",
            Roster = new[] { "Thrall", "Siren", "Thrall", "Bat", "Imp" }, Boss = "Nereth",
            BossLifeMultiplier = 0.5f, Ambient = new Color(0.8f, 0.92f, 1f), Void = new Color(0.01f, 0.03f, 0.05f),
            Music = "music_boss",
        },
    };

    public static RealityTheme Get(string id) => Themes.FirstOrDefault(t => t.Id == id) ?? Themes[0];
}

/// <summary>
/// Tier math — docs/design/endgame.md -> "Tiers 1-20". One number drives
/// monster level, life, damage, pack density, rarity odds and drops.
/// </summary>
public static class RealityTiers
{
    public const int MaxTier = 20;

    public static int MonsterLevel(int tier) => Mathf.RoundToInt(1f + (tier - 1) * 4.4f);

    /// <summary>×1 at tier 1, ×25 at tier 20 (exponential).</summary>
    public static float LifeMultiplier(int tier) => Mathf.Pow(25f, (tier - 1) / 19f);

    /// <summary>×1 at tier 1, ×5 at tier 20.</summary>
    public static float DamageMultiplier(int tier) => Mathf.Pow(5f, (tier - 1) / 19f);

    public static float Lerp(int tier, float a, float b) => Mathf.Lerp(a, b, (tier - 1) / 19f);

    public static float NormalPackChance(int tier) => Lerp(tier, 0.8f, 1f);
    public static float MagicPackChance(int tier) => Lerp(tier, 0.45f, 0.8f);
    public static float RarePackChance(int tier) => Lerp(tier, 0.3f, 0.7f);
    public static float UniqueChance(int tier) => Lerp(tier, 0.12f, 0.4f);

    /// <summary>Portals (lives) per reality: 3, or 1 from tier 15 (stakes rise with tier).</summary>
    public static int Portals(int tier) => tier >= 15 ? 1 : 3;

    /// <summary>
    /// Boss shard drop — docs/design/endgame.md: none 25 %, same tier 45 %,
    /// +1 25 %, +2 5 %. Returns 0 for no shard. Capped at tier 20.
    /// </summary>
    public static int RollBossShard(int tier, RandomNumberGenerator rng)
    {
        float r = rng.Randf();
        int result = r < 0.25f ? 0 : r < 0.70f ? tier : r < 0.95f ? tier + 1 : tier + 2;
        return result == 0 ? 0 : Mathf.Min(MaxTier, result);
    }

    /// <summary>Shard colour band (PoE map tiers): white/yellow/orange/red.</summary>
    public static string ShardSprite(int tier) => tier <= 5 ? "shard_low" : tier <= 10 ? "shard_mid" : tier <= 15 ? "shard_high" : "shard_top";

    public static Color ShardColor(int tier) => tier <= 5 ? new Color(0.8f, 0.9f, 1f) : tier <= 10 ? new Color(1f, 0.92f, 0.5f) : tier <= 15 ? new Color(1f, 0.65f, 0.3f) : new Color(1f, 0.4f, 0.45f);
}
