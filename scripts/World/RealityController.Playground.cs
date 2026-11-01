#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Enemies;

namespace ArpgGame.World;

/// <summary>The playground's spawning (docs/design/playground.md): monsters and bosses of any
/// tier, scaled exactly as in a real reality of that tier.</summary>
public partial class RealityController
{
    /// <summary>Every monster the realities use, and every boss.</summary>
    public static IReadOnlyList<string> AllMonsters => RealityTheme.Themes.SelectMany(t => t.Roster).Distinct().OrderBy(n => n).ToList();

    public static IReadOnlyList<string> AllBosses => RealityTheme.Themes.Select(t => t.Boss).Distinct().ToList();

    /// <summary>The playground's tier: what the next spawns are scaled to.</summary>
    public void SetPlaygroundTier(int tier) => Tier = Mathf.Clamp(tier, 1, RealityTiers.MaxTier);

    /// <summary>A horde of <paramref name="count"/> monsters around <paramref name="centre"/>, awake.
    /// <paramref name="type"/> null = random from every roster; <paramref name="rarity"/> null = a
    /// reality's usual mix for the tier (mostly normal, some magic, the odd rare).</summary>
    public List<Enemy> SpawnPlaygroundHorde(int count, string? type, MonsterRarity? rarity, Vector2 centre)
    {
        var spawned = new List<Enemy>();
        IReadOnlyList<string> roster = AllMonsters;
        float turn = _rng.Randf() * Mathf.Tau;
        for (int i = 0; i < count; i++)
        {
            string kind = type ?? roster[_rng.RandiRange(0, roster.Count - 1)];
            MonsterRarity r = rarity ?? (i == 0 && count >= 8 ? MonsterRarity.Rare : _rng.Randf() < RealityTiers.MagicPackChance(Tier) * 0.5f ? MonsterRarity.Magic : MonsterRarity.Normal);
            Enemy e = SpawnMonster(kind, centre, r, i, turn);
            e.Wake();
            spawned.Add(e);
        }

        return spawned;
    }

    /// <summary>A boss of this tier at <paramref name="at"/>, scaled as the reality's own and awake.</summary>
    public BossEnemy SpawnPlaygroundBoss(string type, Vector2 at)
    {
        var boss = LoadEnemy(type).Instantiate<BossEnemy>();
        boss.MonsterLevel = MonsterLevel;
        boss.Dormant = true;
        AddChild(boss);
        boss.GlobalPosition = at;
        // The boss's own reality decides its life multiplier, not the playground's theme.
        float themeLife = RealityTheme.Themes.FirstOrDefault(t => t.Boss == type)?.BossLifeMultiplier ?? Theme.BossLifeMultiplier;
        float life = MonsterScaling.BossLifeMultiplier(MonsterLevel, boss.BaseLife * themeLife) * themeLife;
        boss.ApplyTierScaling(life, MonsterScaling.BossDamageMultiplier(MonsterLevel));
        ApplyDefences(boss);
        RegisterMonster(boss);
        boss.Wake();
        return boss;
    }
}
