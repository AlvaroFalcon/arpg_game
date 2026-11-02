#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Enemies;
using ArpgGame.Stats;

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
            HomeDefences(e, kind);
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
        HomeDefences(boss, type);
        RegisterMonster(boss);
        boss.Wake();
        return boss;
    }

    /// <summary>The reality a monster comes from (its roster or its boss), for its defences.</summary>
    public static RealityTheme HomeOf(string type) =>
        RealityTheme.Themes.FirstOrDefault(t => t.Boss == type) ?? RealityTheme.Themes.FirstOrDefault(t => t.Roster.Contains(type)) ?? RealityTheme.Themes[0];

    /// <summary>
    /// A monster's defences in its own reality, not the playground's (whose theme is ashen):
    /// its home's affinity decides which resistance or armour is higher. The base defences of
    /// the level are the same in every reality, so only the affinity part is swapped.
    /// </summary>
    private void HomeDefences(Enemy e, string type)
    {
        DamageType? home = HomeOf(type).Affinity;
        if (home == Theme.Affinity)
        {
            return;
        }

        foreach (Stat stat in new[] { e.Stats.FireResistance, e.Stats.ColdResistance, e.Stats.LightningResistance, e.Stats.Armour })
        {
            stat.RemoveModifiersFromSource(MonsterScaling.DefenceSourceObject);
        }

        MonsterScaling.ApplyDefences(e, MonsterLevel, home);
    }

    /// <summary>A monster's numbers at this tier and rarity, for the playground's stat card.</summary>
    public sealed record MonsterCard(string Type, string Name, bool Boss, string Behaviour, int Level, float Life, float Armour, float PhysicalTaken,
        float Fire, float Cold, float Lightning, float Speed, float ContactDamage, float ContactCooldown, string? Attack, MonsterRarity Rarity);

    private Node? _probes;

    /// <summary>
    /// Builds the monster for real (hidden, not processing, out of every group), scales it
    /// exactly as a spawn would, reads its numbers and frees it.
    /// </summary>
    public MonsterCard Preview(string type, MonsterRarity rarity)
    {
        if (_probes is null || !IsInstanceValid(_probes))
        {
            _probes = new Node { Name = "Probes", ProcessMode = ProcessModeEnum.Disabled };
            AddChild(_probes);
        }

        var e = LoadEnemy(type).Instantiate<Enemy>();
        bool boss = e is BossEnemy;
        e.Visible = false;
        e.Dormant = true;
        e.MonsterLevel = MonsterLevel;
        _probes.AddChild(e);
        e.RemoveFromGroup(CombatQuery.EnemyGroup);
        if (boss)
        {
            RealityTheme home = HomeOf(type);
            float life = MonsterScaling.BossLifeMultiplier(MonsterLevel, e.BaseLife * home.BossLifeMultiplier) * home.BossLifeMultiplier;
            e.ApplyTierScaling(life, MonsterScaling.BossDamageMultiplier(MonsterLevel));
        }
        else
        {
            e.ApplyTierScaling(TierLife, RealityTiers.DamageMultiplier(Tier));
            if (rarity != MonsterRarity.Normal)
            {
                e.ApplyRarity(rarity);
            }

            ApplyRarityLife(e);
        }

        ApplyDefences(e);
        HomeDefences(e, type);
        string? attack = e switch
        {
            RangedEnemy { Attack: { } a } r => $"{a.DisplayName}: {Stat.ComputeFormula(a.BaseDamage, e.Stats.GetModifiers(new ModifierQuery(StatId.Damage, a.Tags)), a.AddedDamageEffectiveness):N0} damage every {r.AttackInterval:0.#} s",
            ExploderEnemy x => $"Explodes for {x.BlastDamage * e.DamageMultiplier:N0} in {x.BlastRadius:0} px",
            ChargerEnemy c => $"Charges from {c.ChargeRange:0} px",
            _ => null,
        };
        string behaviour = e switch
        {
            BossEnemy => "Boss: phases and telegraphed attacks",
            RangedEnemy => "Keeps its distance and shoots",
            ExploderEnemy => "Runs at you and explodes",
            ChargerEnemy => "Winds up and charges",
            _ => "Chases and hits on contact",
        };
        var card = new MonsterCard(type, e.DisplayName, boss, behaviour, MonsterLevel, e.Stats.Life?.Max ?? 0f, e.Stats.Armour.Value,
            DamageMitigation.MitigatePhysical(100f, e.Stats.Armour.Value), e.Stats.FireResistance.Value, e.Stats.ColdResistance.Value,
            e.Stats.LightningResistance.Value, e.Stats.MovementSpeed.Value, e.ContactDamage * e.DamageMultiplier, e.ContactCooldown, attack, boss ? MonsterRarity.Unique : e.Rarity);
        e.QueueFree();
        return card;
    }
}
