#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Skills;

namespace ArpgGame.Progression;

/// <summary>One skill evolution (docs/design/survival-roadmap.md -> M5): the skill at the run's
/// level cap plus a named power-up becomes its evolved form, delivered by the next chest of
/// 3 or more.</summary>
public sealed class EvolutionDefinition
{
    /// <summary>The base skill's id (UpgradeCatalog.Skills).</summary>
    public string SkillId { get; init; } = "";

    /// <summary>The partner power-up's id (UpgradeCatalog.Passives), owned at least once.</summary>
    public string PartnerId { get; init; } = "";

    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>The evolved form's cast changes, on top of <see cref="EvolutionCatalog.DamageBonus"/>.</summary>
    public Action<CastContext> Apply { get; init; } = _ => { };

    /// <summary>Locked until a Rift challenge unlocks it (M7); null = always known.</summary>
    public string? RequiresUnlock { get; init; }
}

/// <summary>The evolved form as a cast modifier on the skill instance (late, after supports and trees).</summary>
public sealed class EvolutionUpgrade : CastModifier, ISkillUpgrade
{
    public EvolutionUpgrade(EvolutionDefinition definition) => Definition = definition;

    public EvolutionDefinition Definition { get; }

    public override int Priority => 900;

    public override void Apply(CastContext context)
    {
        context.DamageMultiplier *= EvolutionCatalog.DamageBonus;
        Definition.Apply(context);
    }
}

/// <summary>Every evolution — docs/design/survival-roadmap.md -> M5. Built from existing cast
/// fields (projectile count, chains, pierce, area, repeats, extra orbit objects), so an
/// evolved skill composes with everything that already reads a <see cref="CastContext"/>.</summary>
public static class EvolutionCatalog
{
    /// <summary>Every evolved form deals this much more damage on top of its change.</summary>
    public const float DamageBonus = 1.5f;

    public static readonly IReadOnlyList<EvolutionDefinition> All = new[]
    {
        new EvolutionDefinition
        {
            SkillId = "fireball", PartnerId = "area", Name = "Firestorm",
            Description = "Three fireballs that burst on impact.",
            Apply = c =>
            {
                if (c.Projectile is { } p)
                {
                    p.Count += 2;
                    p.SpreadDegrees = Mathf.Max(p.SpreadDegrees, 24f);
                    p.OnHitExplode = true;
                    p.ExplodeRadius = Mathf.Max(p.ExplodeRadius, 70f);
                    p.ExplodeDamageFraction = Mathf.Max(p.ExplodeDamageFraction, 0.6f);
                }
            },
        },
        new EvolutionDefinition
        {
            SkillId = "spark", PartnerId = "shock", Name = "Storm Lattice",
            Description = "Three more sparks, and every spark chains twice.",
            Apply = c =>
            {
                if (c.Projectile is { } p)
                {
                    p.Count += 3;
                    p.Chains += 2;
                }
            },
        },
        new EvolutionDefinition
        {
            SkillId = "frost_nova", PartnerId = "duration", Name = "Permafrost",
            Description = "A wider nova that pulses a second time.",
            Apply = c =>
            {
                c.AreaMultiplier *= 1.5f;
                c.Repeats += 1;
            },
        },
        new EvolutionDefinition
        {
            SkillId = "cleave", PartnerId = "attack_speed", Name = "Reaper's Arc",
            Description = "A far wider arc that swings twice.",
            Apply = c =>
            {
                c.AreaMultiplier *= 1.6f;
                c.Repeats += 1;
            },
        },
        new EvolutionDefinition
        {
            SkillId = "throwing_knives", PartnerId = "extra_projectile", Name = "Fan of Blades",
            Description = "A full circle of knives that pierce.",
            Apply = c =>
            {
                if (c.Projectile is { } p)
                {
                    p.Count += 8;
                    p.SpreadDegrees = 360f;
                    p.Pierce += 2;
                }
            },
        },
        new EvolutionDefinition
        {
            SkillId = "blades", PartnerId = "cooldown", Name = "Whirling Wall",
            Description = "Three more orbiting blades on a wider ring.",
            Apply = c =>
            {
                c.ExtraCount += 3;
                c.AreaMultiplier *= 1.3f;
            },
        },
        new EvolutionDefinition
        {
            SkillId = "meteor", PartnerId = "ignite", Name = "Starfall", RequiresUnlock = Run.RiftChallenges.Starfall,
            Description = "Two more meteors fall after the first.",
            Apply = c => c.Repeats += 2,
        },
        new EvolutionDefinition
        {
            SkillId = "frost_shards", PartnerId = "projectile_speed", Name = "Hailstorm", RequiresUnlock = Run.RiftChallenges.Hailstorm,
            Description = "Four more shards that pierce two enemies.",
            Apply = c =>
            {
                if (c.Projectile is { } p)
                {
                    p.Count += 4;
                    p.Pierce += 2;
                }
            },
        },
    };

    public static EvolutionDefinition? ForSkill(string skillId) => All.FirstOrDefault(e => e.SkillId == skillId);

    /// <summary>The evolution a partner power-up leads to for an owned skill (level-up card hints).</summary>
    public static IEnumerable<EvolutionDefinition> ForPartner(string passiveId) => All.Where(e => e.PartnerId == passiveId);

    /// <summary>Whether the evolution is known (not locked behind a Rift challenge, M7).</summary>
    public static bool Known(EvolutionDefinition e) => e.RequiresUnlock is null || Core.Unlocks.Has(e.RequiresUnlock);
}
