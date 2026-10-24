#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Skills;
using ArpgGame.Stats;

namespace ArpgGame.Progression;

/// <summary>A run skill the player can be offered.</summary>
public sealed class SkillEntry
{
    public string Id { get; init; } = "";
    public string ResourcePath { get; init; } = "";

    private SkillDefinition? _definition;

    public SkillDefinition Definition => _definition ??= GD.Load<SkillDefinition>(ResourcePath);
}

/// <summary>A stat passive. <see cref="Apply"/> receives the rarity multiplier.</summary>
public sealed class PassiveDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Text for a given rarity multiplier, e.g. "+18% increased Fire Damage".</summary>
    public Func<float, string> Describe { get; init; } = _ => "";

    public Action<ActorStats, float, object> Apply { get; init; } = (_, _, _) => { };

    public int MaxRank { get; init; } = 99;
    public float Weight { get; init; } = 1f;

    /// <summary>Discrete passives (+1 projectile) never roll rarity.</summary>
    public bool Scales { get; init; } = true;

    /// <summary>Optional gate (e.g. elemental damage only once you have that element).</summary>
    public Func<RunBuild, bool>? Requires { get; init; }
}

/// <summary>A build-defining keystone: big upside, real downside.</summary>
public sealed class KeystoneDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public Action<IBuildContext> Apply { get; init; } = _ => { };

    /// <summary>Which keystone levels offer it (docs/design/progression.md -> "Keystone cadence").</summary>
    public KeystoneTier Tier { get; init; } = KeystoneTier.Great;

    /// <summary>False for a keystone whose trade means nothing in the Survival Rift
    /// (Blood Magic: skills cost nothing there). It stays a passive-tree keystone.</summary>
    public bool InRift { get; init; } = true;
}

/// <summary>
/// Every upgrade a level-up can offer — docs/design/progression.md. Kept in
/// code rather than .tres files for now: most entries are a lambda over the
/// stat/cast-context API, which a Resource cannot hold, and the tuning pass
/// that would justify editor authoring has not happened yet.
/// </summary>
public static class UpgradeCatalog
{
    public const SkillTags Elements = SkillTags.Fire | SkillTags.Cold | SkillTags.Lightning;

    public static readonly IReadOnlyList<SkillEntry> Skills = new[]
    {
        new SkillEntry { Id = "fireball", ResourcePath = "res://resources/skills/run/fireball.tres" },
        new SkillEntry { Id = "spark", ResourcePath = "res://resources/skills/run/spark.tres" },
        new SkillEntry { Id = "frost_nova", ResourcePath = "res://resources/skills/run/frost_nova.tres" },
        new SkillEntry { Id = "blades", ResourcePath = "res://resources/skills/run/blades.tres" },
        new SkillEntry { Id = "meteor", ResourcePath = "res://resources/skills/run/meteor.tres" },
        new SkillEntry { Id = "cleave", ResourcePath = "res://resources/skills/run/cleave.tres" },
        new SkillEntry { Id = "ground_slam", ResourcePath = "res://resources/skills/run/ground_slam.tres" },
        new SkillEntry { Id = "sunder", ResourcePath = "res://resources/skills/run/sunder.tres" },
        new SkillEntry { Id = "double_strike", ResourcePath = "res://resources/skills/run/double_strike.tres" },
        new SkillEntry { Id = "throwing_knives", ResourcePath = "res://resources/skills/run/throwing_knives.tres" },
        // docs/design/hideout.md -> "Three new skills": fill the gaps in cold, lightning melee and the Rogue's list.
        new SkillEntry { Id = "frost_shards", ResourcePath = "res://resources/skills/run/frost_shards.tres" },
        new SkillEntry { Id = "storm_strike", ResourcePath = "res://resources/skills/run/storm_strike.tres" },
        new SkillEntry { Id = "glacial_hammer", ResourcePath = "res://resources/skills/run/glacial_hammer.tres" },
    };

    public static SkillEntry? FindSkill(string id)
    {
        foreach (SkillEntry s in Skills)
        {
            if (s.Id == id)
            {
                return s;
            }
        }

        return null;
    }

    public static string SkillIdOf(SkillDefinition definition)
    {
        foreach (SkillEntry s in Skills)
        {
            if (s.Definition == definition)
            {
                return s.Id;
            }
        }

        return definition.DisplayName;
    }

    private static void Convert(CastContext c, SkillTags to)
    {
        c.Tags = (c.Tags & ~Elements) | to;
    }

    public static readonly IReadOnlyList<SupportDefinition> Supports = new[]
    {
        new SupportDefinition
        {
            Id = "multiple_projectiles", Name = "Multiple Projectiles",
            Description = "+2 projectiles, fanned out.\n20% less damage.",
            RequiresAny = SkillTags.Projectile, Priority = 10,
            Apply = c =>
            {
                if (c.Projectile is null) return;
                c.Projectile.Count += 2;
                SkillUser.EnsureSpreadForCount(c.Projectile);
                c.DamageMultiplier *= 0.8f;
            },
        },
        new SupportDefinition
        {
            Id = "pierce", Name = "Pierce", Description = "Projectiles pierce 2 additional enemies.",
            RequiresAny = SkillTags.Projectile, Priority = 10,
            Apply = c => { if (c.Projectile is not null) c.Projectile.Pierce += 2; },
        },
        new SupportDefinition
        {
            Id = "fork", Name = "Fork", Description = "Projectiles split in two on hit.\n10% less damage.",
            RequiresAny = SkillTags.Projectile, Priority = 10,
            Apply = c => { if (c.Projectile is null) return; c.Projectile.Forks += 1; c.DamageMultiplier *= 0.9f; },
        },
        new SupportDefinition
        {
            Id = "chain", Name = "Chain", Description = "Projectiles chain to 2 more enemies.\n10% less damage.",
            RequiresAny = SkillTags.Projectile, Priority = 10,
            Apply = c => { if (c.Projectile is null) return; c.Projectile.Chains += 2; c.DamageMultiplier *= 0.9f; },
        },
        new SupportDefinition
        {
            Id = "faster_projectiles", Name = "Faster Projectiles", Description = "50% more projectile speed.\n15% more damage.",
            RequiresAny = SkillTags.Projectile, Priority = 30,
            Apply = c => { if (c.Projectile is null) return; c.Projectile.Speed *= 1.5f; c.Projectile.Range *= 1.3f; c.DamageMultiplier *= 1.15f; },
        },
        new SupportDefinition
        {
            Id = "increased_area", Name = "Increased Area", Description = "45% more area of effect.",
            RequiresAny = SkillTags.Area, Priority = 30,
            Apply = c => c.AreaMultiplier *= 1.45f,
        },
        new SupportDefinition
        {
            Id = "concentrated_effect", Name = "Concentrated Effect", Description = "30% less area of effect.\n45% more damage.",
            RequiresAny = SkillTags.Area, Priority = 30,
            Apply = c => { c.AreaMultiplier *= 0.7f; c.DamageMultiplier *= 1.45f; },
        },
        new SupportDefinition
        {
            Id = "spell_echo", Name = "Spell Echo", Description = "The skill repeats once.\n10% less damage.",
            RequiresAny = SkillTags.Spell, Excludes = SkillTags.Duration, Priority = 20,
            Apply = c => { c.Repeats += 1; c.DamageMultiplier *= 0.9f; },
        },
        new SupportDefinition
        {
            Id = "faster_casting", Name = "Faster Casting", Description = "25% less cast time and cooldown.",
            Priority = 30,
            Apply = c => { c.UseTimeMultiplier *= 0.75f; c.CooldownMultiplier *= 0.75f; },
        },
        new SupportDefinition
        {
            Id = "cold_conversion", Name = "Cold Conversion",
            Description = "Converts the skill's damage to Cold.\nCold hits chill (slow) enemies.",
            RequiresAny = SkillTags.Spell | SkillTags.Attack, Excludes = SkillTags.Cold, Priority = 5,
            Apply = c => Convert(c, SkillTags.Cold),
        },
        new SupportDefinition
        {
            Id = "fire_conversion", Name = "Fire Conversion",
            Description = "Converts the skill's damage to Fire.\n+15% chance to ignite.",
            RequiresAny = SkillTags.Spell | SkillTags.Attack, Excludes = SkillTags.Fire, Priority = 5,
            Apply = c => { Convert(c, SkillTags.Fire); c.IgniteChanceBonus += 0.15f; },
        },
        new SupportDefinition
        {
            Id = "lightning_conversion", Name = "Lightning Conversion",
            Description = "Converts the skill's damage to Lightning.\n+15% chance to shock.",
            RequiresAny = SkillTags.Spell | SkillTags.Attack, Excludes = SkillTags.Lightning, Priority = 5,
            Apply = c => { Convert(c, SkillTags.Lightning); c.ShockChanceBonus += 0.15f; },
        },
        new SupportDefinition
        {
            Id = "ignition", Name = "Ignition", Description = "+40% chance to ignite with Fire hits.",
            Priority = 30, Apply = c => c.IgniteChanceBonus += 0.4f,
        },
        new SupportDefinition
        {
            Id = "overcharge", Name = "Overcharge", Description = "+40% chance to shock with Lightning hits.",
            Priority = 30, Apply = c => c.ShockChanceBonus += 0.4f,
        },
        new SupportDefinition
        {
            Id = "elemental_focus", Name = "Elemental Focus", Description = "45% more damage.\nCannot inflict ailments.",
            Priority = 30, Apply = c => { c.DamageMultiplier *= 1.45f; c.CanInflictAilments = false; },
        },
        new SupportDefinition
        {
            Id = "multistrike", Name = "Multistrike", Description = "Melee strikes repeat twice more.\n25% less damage.",
            RequiresAny = SkillTags.Melee, Priority = 20,
            Apply = c => { c.Repeats += 2; c.DamageMultiplier *= 0.75f; c.UseTimeMultiplier *= 1.1f; },
        },
        new SupportDefinition
        {
            Id = "melee_splash", Name = "Melee Splash", Description = "40% more area for melee strikes.\n10% less damage.",
            RequiresAny = SkillTags.Melee, Priority = 30,
            Apply = c => { c.AreaMultiplier *= 1.4f; c.DamageMultiplier *= 0.9f; },
        },
        new SupportDefinition
        {
            Id = "brutality", Name = "Brutality", Description = "40% more damage with attacks.\nCannot inflict ailments.",
            RequiresAny = SkillTags.Attack, Priority = 30,
            Apply = c => { c.DamageMultiplier *= 1.4f; c.CanInflictAilments = false; },
        },
        new SupportDefinition
        {
            Id = "extended_duration", Name = "Extended Duration", Description = "60% more skill duration.",
            RequiresAny = SkillTags.Duration, Priority = 30, Apply = c => c.DurationMultiplier *= 1.6f,
        },
        new SupportDefinition
        {
            Id = "blade_storm", Name = "Blade Storm", Description = "+2 orbiting objects.\n10% less damage.",
            RequiresAny = SkillTags.Duration, Priority = 10,
            Apply = c => { c.ExtraCount += 2; c.DamageMultiplier *= 0.9f; },
        },
    };

    private static string Pct(float v) => $"{Mathf.RoundToInt(v)}%";

    private static PassiveDefinition DamagePassive(string id, string name, SkillTags tag, float pct, Func<RunBuild, bool>? requires = null) => new()
    {
        Id = id, Name = name, Requires = requires,
        Describe = m => $"{Pct(pct * m)} increased {name}",
        Apply = (s, m, src) => s.Damage.AddModifier(new StatModifier(ModifierType.Increased, pct * m, src, tag)),
    };

    private static PassiveDefinition Gated(PassiveDefinition p, SkillTags tag) => new()
    {
        Id = p.Id, Name = p.Name, Weight = p.Weight, MaxRank = p.MaxRank, Describe = p.Describe, Apply = p.Apply, Scales = p.Scales,
        Requires = b => b.HasTag(tag),
    };

    private static PassiveDefinition StatPassive(string id, string name, StatId stat, ModifierType type, float value, Func<float, string> describe, float weight = 1f, int maxRank = 99) => new()
    {
        Id = id, Name = name, Weight = weight, MaxRank = maxRank, Describe = describe,
        Apply = (s, m, src) => s.Get(stat).AddModifier(new StatModifier(type, value * m, src)),
    };

    public static readonly IReadOnlyList<PassiveDefinition> Passives = new[]
    {
        DamagePassive("fire_damage", "Fire Damage", SkillTags.Fire, 20f, b => b.HasTag(SkillTags.Fire)),
        DamagePassive("cold_damage", "Cold Damage", SkillTags.Cold, 20f, b => b.HasTag(SkillTags.Cold)),
        DamagePassive("lightning_damage", "Lightning Damage", SkillTags.Lightning, 20f, b => b.HasTag(SkillTags.Lightning)),
        DamagePassive("spell_damage", "Spell Damage", SkillTags.Spell, 14f, b => b.HasTag(SkillTags.Spell)),
        DamagePassive("melee_damage", "Melee Damage", SkillTags.Melee, 18f, b => b.HasTag(SkillTags.Melee)),
        new PassiveDefinition
        {
            Id = "damage", Name = "Might", Weight = 1.6f,
            Describe = m => $"{Pct(10 * m)} increased Damage",
            Apply = (s, m, src) => s.Damage.AddModifier(new StatModifier(ModifierType.Increased, 10f * m, src)),
        },
        DamagePassive("projectile_damage", "Projectile Damage", SkillTags.Projectile, 18f, b => b.HasTag(SkillTags.Projectile)),
        DamagePassive("area_damage", "Area Damage", SkillTags.Area, 18f, b => b.HasTag(SkillTags.Area)),
        new PassiveDefinition
        {
            Id = "max_life", Name = "Vitality",
            Describe = m => $"+{Mathf.RoundToInt(15 * m)} maximum Life\n{Pct(5 * m)} increased maximum Life",
            Apply = (s, m, src) =>
            {
                // Heals by exactly what the card adds to the maximum (flat part and the
                // increased part): a fixed +15 fell short of the new ceiling, and with no
                // regeneration (Vampiric Pact) the missing life stayed missing.
                float before = s.Get(StatId.MaxLife).Value;
                s.Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.Flat, 15f * m, src));
                s.Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.Increased, 5f * m, src));
                if (s.Life is { } life && !s.OwnerDead)
                {
                    life.Current = Mathf.Min(life.Max, life.Current + Mathf.Max(0f, life.Max - before));
                }
            },
        },
        StatPassive("life_regen", "Regeneration", StatId.LifeRegen, ModifierType.Flat, 1.5f, m => $"+{1.5f * m:0.#} Life regenerated per second"),
        StatPassive("armour", "Iron Skin", StatId.Armour, ModifierType.Flat, 40f, m => $"+{Mathf.RoundToInt(40 * m)} Armour"),
        StatPassive("move_speed", "Fleet Foot", StatId.MovementSpeed, ModifierType.Increased, 7f, m => $"{Pct(7 * m)} increased Movement Speed"),
        Gated(StatPassive("cast_speed", "Quick Hands", StatId.CastSpeed, ModifierType.Increased, 8f, m => $"{Pct(8 * m)} increased Cast Speed"), SkillTags.Spell),
        Gated(StatPassive("attack_speed", "Ferocity", StatId.AttackSpeed, ModifierType.Increased, 8f, m => $"{Pct(8 * m)} increased Attack Speed"), SkillTags.Attack),
        StatPassive("autocast", "Quickening", StatId.AutoCastSpeed, ModifierType.Increased, 12f, m => $"Skills fire {Pct(12 * m)} more often", 1.4f),
        StatPassive("cooldown", "Momentum", StatId.CooldownRecovery, ModifierType.Increased, 8f, m => $"{Pct(8 * m)} increased Cooldown Recovery"),
        StatPassive("crit_chance", "Precision", StatId.CritChance, ModifierType.Increased, 40f, m => $"{Pct(40 * m)} increased Critical Strike Chance"),
        StatPassive("crit_multi", "Ruthlessness", StatId.CritMultiplier, ModifierType.Flat, 0.25f, m => $"+{Pct(25 * m)} Critical Strike Multiplier"),
        StatPassive("area", "Expanse", StatId.AreaOfEffect, ModifierType.Increased, 10f, m => $"{Pct(10 * m)} increased Area of Effect"),
        StatPassive("projectile_speed", "Velocity", StatId.ProjectileSpeed, ModifierType.Increased, 12f, m => $"{Pct(12 * m)} increased Projectile Speed", 0.6f),
        StatPassive("pickup", "Magnetism", StatId.PickupRadius, ModifierType.Increased, 25f, m => $"{Pct(25 * m)} increased Pickup Radius", 0.9f),
        StatPassive("energy_shield", "Aegis", StatId.EnergyShield, ModifierType.Flat, 20f, m => $"+{Mathf.RoundToInt(20 * m)} Energy Shield\n(takes hits before Life, recharges after 2 s unhurt)"),
        StatPassive("leech", "Bloodthirst", StatId.LifeLeech, ModifierType.Flat, 0.5f, m => $"{0.5f * m:0.##}% of Damage leeched as Life", 0.7f),
        StatPassive("experience", "Insight", StatId.ExperienceGain, ModifierType.Increased, 10f, m => $"{Pct(10 * m)} increased Experience gained", 0.6f, 5),
        StatPassive("ignite", "Kindling", StatId.IgniteChance, ModifierType.Flat, 0.08f, m => $"+{Pct(8 * m)} chance to Ignite with Fire hits", 0.8f),
        StatPassive("shock", "Static", StatId.ShockChance, ModifierType.Flat, 0.08f, m => $"+{Pct(8 * m)} chance to Shock with Lightning hits", 0.8f),
        StatPassive("duration", "Persistence", StatId.SkillDuration, ModifierType.Increased, 12f, m => $"{Pct(12 * m)} increased Skill Duration", 0.6f),
        StatPassive("life_on_kill", "Feast", StatId.LifeOnKill, ModifierType.Flat, 1f, m => $"+{1f * m:0.#} Life gained per enemy killed", 0.8f),
        new PassiveDefinition
        {
            Id = "resistances", Name = "Elemental Ward",
            Describe = m => $"+{Pct(15 * m)} to all Elemental Resistances",
            Apply = (s, m, src) =>
            {
                foreach (StatId r in new[] { StatId.FireResistance, StatId.ColdResistance, StatId.LightningResistance })
                    s.Get(r).AddModifier(new StatModifier(ModifierType.Flat, 15f * m, src));
            },
        },
        StatPassive("dodge_recovery", "Light Step", StatId.DodgeRecovery, ModifierType.Increased, 15f, m => $"{Pct(15 * m)} faster Dodge recharge"),
        new PassiveDefinition
        {
            Id = "extra_projectile", Name = "Volley", Scales = false, MaxRank = 2, Weight = 0.35f,
            Describe = _ => "+1 Projectile for all projectile skills\n+1 orbiting blade",
            Apply = (s, _, src) => s.Get(StatId.ExtraProjectiles).AddModifier(new StatModifier(ModifierType.Flat, 1f, src)),
            Requires = b => b.HasTag(SkillTags.Projectile) || b.HasTag(SkillTags.Duration),
        },
        new PassiveDefinition
        {
            Id = "dodge_charge", Name = "Acrobatics", Scales = false, MaxRank = 2, Weight = 0.4f,
            Describe = _ => "+1 Dodge charge",
            Apply = (s, _, src) => s.Get(StatId.DodgeCharges).AddModifier(new StatModifier(ModifierType.Flat, 1f, src)),
        },
        new PassiveDefinition
        {
            Id = "extra_chain", Name = "Arc Weaving", Scales = false, MaxRank = 2, Weight = 0.35f,
            Describe = _ => "Projectiles chain +1 time",
            Apply = (s, _, src) => s.Get(StatId.ExtraChains).AddModifier(new StatModifier(ModifierType.Flat, 1f, src)),
            Requires = b => b.HasTag(SkillTags.Projectile),
        },
    };

    public static readonly IReadOnlyList<KeystoneDefinition> Keystones = new[]
    {
        new KeystoneDefinition
        {
            Id = "glass_cannon", Name = "Glass Cannon",
            Description = "60% more damage.\n40% less maximum Life.",
            Apply = b =>
            {
                b.Stats.Damage.AddModifier(new StatModifier(ModifierType.More, 60f, b.Source));
                b.Stats.Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.More, -40f, b.Source));
            },
        },
        new KeystoneDefinition
        {
            Id = "avatar_of_fire", Name = "Avatar of Fire",
            Description = "All skills deal Fire damage.\n25% more damage, +15% ignite chance.\nCold and Lightning bonuses no longer apply.",
            Apply = b =>
            {
                b.SkillUser.GlobalModifiers.Add(new GlobalCastModifier(6, c => Convert(c, SkillTags.Fire)));
                b.SkillUser.GlobalModifiers.Add(new GlobalCastModifier(30, c => c.DamageMultiplier *= 1.25f));
                b.Stats.Get(StatId.IgniteChance).AddModifier(new StatModifier(ModifierType.Flat, 0.15f, b.Source));
                b.ForcedElement = SkillTags.Fire;
            },
        },
        new KeystoneDefinition
        {
            Id = "avatar_of_frost", Name = "Avatar of Frost",
            Description = "All skills deal Cold damage.\n25% more damage, +15% chance to Freeze.\nFire and Lightning bonuses no longer apply.",
            Apply = b =>
            {
                b.SkillUser.GlobalModifiers.Add(new GlobalCastModifier(6, c => Convert(c, SkillTags.Cold)));
                b.SkillUser.GlobalModifiers.Add(new GlobalCastModifier(30, c => c.DamageMultiplier *= 1.25f));
                b.Stats.Get(StatId.FreezeChance).AddModifier(new StatModifier(ModifierType.Flat, 0.15f, b.Source));
                b.ForcedElement = SkillTags.Cold;
            },
        },
        new KeystoneDefinition
        {
            Id = "ricochet", Tier = KeystoneTier.Lesser, Name = "Ricochet",
            Description = "Your projectiles bounce off walls up to 2 times.\n15% less projectile damage.",
            Apply = b => b.SkillUser.GlobalModifiers.Add(new GlobalCastModifier(30, c =>
            {
                if (c.Projectile is null) return;
                c.Projectile.Bounces += 2;
                c.DamageMultiplier *= 0.85f;
            })),
        },
        new KeystoneDefinition
        {
            Id = "juggernaut", Tier = KeystoneTier.Lesser, Name = "Juggernaut",
            Description = "50% more Armour.\n10% less attack speed.",
            Apply = b =>
            {
                b.Stats.Armour.AddModifier(new StatModifier(ModifierType.More, 50f, b.Source));
                b.Stats.AttackSpeed.AddModifier(new StatModifier(ModifierType.More, -10f, b.Source));
            },
        },
        new KeystoneDefinition
        {
            Id = "prismatic_bulwark", Tier = KeystoneTier.Lesser, Name = "Prismatic Bulwark",
            Description = "25% less Elemental damage taken.\n15% more Physical damage taken.",
            Apply = b =>
            {
                b.Stats.Get(StatId.ElementalDamageTaken).AddModifier(new StatModifier(ModifierType.More, -25f, b.Source));
                b.Stats.Get(StatId.PhysicalDamageTaken).AddModifier(new StatModifier(ModifierType.More, 15f, b.Source));
            },
        },
        new KeystoneDefinition
        {
            Id = "chain_reaction", Name = "Chain Reaction",
            Description = "Enemies you kill explode, dealing 20% of their\nmaximum Life as Fire damage around them.",
            Apply = b => b.ChainReaction = true,
        },
        new KeystoneDefinition
        {
            Id = "phase_dancer", Name = "Phase Dancer",
            Description = "+2 Dodge charges, 50% faster Dodge recharge.\nDodging releases a Frost Nova.\nYou take 15% more damage.",
            Apply = b =>
            {
                b.Stats.Get(StatId.DodgeCharges).AddModifier(new StatModifier(ModifierType.Flat, 2f, b.Source));
                b.Stats.Get(StatId.DodgeRecovery).AddModifier(new StatModifier(ModifierType.Increased, 50f, b.Source));
                b.Stats.Get(StatId.DamageTaken).AddModifier(new StatModifier(ModifierType.More, 15f, b.Source));
                b.PhaseDancer = true;
            },
        },
        new KeystoneDefinition
        {
            Id = "blood_magic", InRift = false, Name = "Blood Magic",
            Description = "Skills cost Life instead of Mana,\nbut never take you below 40% Life.\n30% more maximum Life, +3 Life regenerated per second.",
            Apply = b =>
            {
                b.SkillUser.GlobalModifiers.Add(new GlobalCastModifier(1, c =>
                {
                    c.LifeCostFloorFraction = 0.4f;
                    for (int i = 0; i < c.Costs.Count; i++)
                    {
                        CostEntry cost = c.Costs[i];
                        if (cost.Resource == ResourceType.Mana)
                        {
                            c.Costs[i] = new CostEntry { Resource = ResourceType.Life, Amount = cost.Amount, Mode = cost.Mode };
                        }
                    }
                }));
                b.Stats.Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.More, 30f, b.Source));
                b.Stats.Get(StatId.LifeRegen).AddModifier(new StatModifier(ModifierType.Flat, 3f, b.Source));
            },
        },
        new KeystoneDefinition
        {
            Id = "echoing_mind", Name = "Echoing Mind",
            Description = "Every spell repeats one additional time.\n35% less cast speed.",
            Apply = b =>
            {
                b.SkillUser.GlobalModifiers.Add(new GlobalCastModifier(20, c => c.Repeats += 1));
                b.Stats.CastSpeed.AddModifier(new StatModifier(ModifierType.More, -35f, b.Source));
            },
        },
        new KeystoneDefinition
        {
            Id = "iron_will", Tier = KeystoneTier.Lesser, Name = "Iron Will",
            Description = "20% less damage taken.\n15% less movement speed.",
            Apply = b =>
            {
                b.Stats.Get(StatId.DamageTaken).AddModifier(new StatModifier(ModifierType.More, -20f, b.Source));
                b.Stats.MovementSpeed.AddModifier(new StatModifier(ModifierType.More, -15f, b.Source));
            },
        },
        new KeystoneDefinition
        {
            Id = "vampiric_pact", Name = "Vampiric Pact",
            Description = "+5 Life gained per enemy killed.\nLife on kill is tripled.\nYou cannot regenerate Life.",
            Apply = b =>
            {
                b.Stats.Get(StatId.LifeOnKill).AddModifier(new StatModifier(ModifierType.Flat, 5f, b.Source));
                b.Stats.Get(StatId.LifeOnKill).AddModifier(new StatModifier(ModifierType.More, 200f, b.Source));
                b.Stats.Get(StatId.LifeRegen).AddModifier(new StatModifier(ModifierType.More, -100f, b.Source));
            },
        },

        // Lesser keystones made for the Rift's cards (docs/design/progression.md ->
        // "Keystone cadence"): one stat bent, a modest cost. Not on the passive tree.
        new KeystoneDefinition
        {
            Id = "scavenger", Tier = KeystoneTier.Lesser, Name = "Scavenger",
            Description = "100% increased Pickup Radius.\n20% increased Experience gained.\n10% less damage.",
            Apply = b =>
            {
                b.Stats.Get(StatId.PickupRadius).AddModifier(new StatModifier(ModifierType.Increased, 100f, b.Source));
                b.Stats.Get(StatId.ExperienceGain).AddModifier(new StatModifier(ModifierType.Increased, 20f, b.Source));
                b.Stats.Damage.AddModifier(new StatModifier(ModifierType.More, -10f, b.Source));
            },
        },
        new KeystoneDefinition
        {
            Id = "overclock", Tier = KeystoneTier.Lesser, Name = "Overclock",
            Description = "Skills fire 35% more often.\n20% less damage.",
            Apply = b =>
            {
                b.Stats.Get(StatId.AutoCastSpeed).AddModifier(new StatModifier(ModifierType.More, 35f, b.Source));
                b.Stats.Damage.AddModifier(new StatModifier(ModifierType.More, -20f, b.Source));
            },
        },
        new KeystoneDefinition
        {
            Id = "shield_conduit", Tier = KeystoneTier.Lesser, Name = "Shield Conduit",
            Description = "+60 Energy Shield.\n15% less maximum Life.",
            Apply = b =>
            {
                b.Stats.Get(StatId.EnergyShield).AddModifier(new StatModifier(ModifierType.Flat, 60f, b.Source));
                b.Stats.Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.More, -15f, b.Source));
            },
        },
        new KeystoneDefinition
        {
            Id = "wind_runner", Tier = KeystoneTier.Lesser, Name = "Wind Runner",
            Description = "20% more Movement Speed, +1 Dodge charge.\nYou take 10% more damage.",
            Apply = b =>
            {
                b.Stats.MovementSpeed.AddModifier(new StatModifier(ModifierType.More, 20f, b.Source));
                b.Stats.Get(StatId.DodgeCharges).AddModifier(new StatModifier(ModifierType.Flat, 1f, b.Source));
                b.Stats.Get(StatId.DamageTaken).AddModifier(new StatModifier(ModifierType.More, 10f, b.Source));
            },
        },
    };
}
