#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Stats;

namespace ArpgGame.Skills;

/// <summary>
/// Layer 2 of the three-layer split (docs/design/skills.md -> "The three
/// layers"): the per-cast bundle assembled by whoever is casting. Carries the
/// caster, its stats, its faction, the cast origin/aim direction, and —
/// critically — a *mutable copy* of the skill's tags and numeric parameters
/// (seams 1 and 2). This is the seam every future skill-modifies-skill mechanism
/// writes through, and it's what lets the exact same definition serve a player
/// (aim from the mouse) and an enemy (aim from its target) identically.
/// </summary>
public sealed class CastContext
{
    public Node2D Caster { get; }
    public ActorStats CasterStats { get; }
    public Faction CasterFaction { get; }
    public Vector2 Origin { get; }
    public Vector2 AimDirection { get; }

    /// <summary>Mutable per-cast copy of the definition's tags — seam 2. A
    /// CastModifier can add a tag (e.g. "this cast also counts as Area") without
    /// touching the shared definition.</summary>
    public SkillTags Tags;

    /// <summary>Mutable per-cast copy of the definition's cost list — seam 1.
    /// A cost-converting modifier rewrites entries here, never on the definition.</summary>
    public List<CostEntry> Costs = new();

    /// <summary>Non-null only when the skill being cast is a
    /// <see cref="ProjectileSkillDefinition"/>. Archetype-specific parameter
    /// blocks are added the same way as more archetypes are implemented.</summary>
    public ProjectileParams? Projectile;

    public MovementDuringUse MovementDuringUse;

    // --- Run-layer per-cast knobs (docs/design/progression.md -> "Supports") ---
    // Every support gem and keystone is a CastModifier that writes into these,
    // never into the shared definition. Defaults are all "no change".

    /// <summary>Product of every More/Less damage multiplier a CastModifier
    /// applied to this cast (1 = unchanged). Applied on top of the caster's
    /// own Damage stat resolution.</summary>
    public float DamageMultiplier = 1f;

    /// <summary>Multiplier on every resource cost of this cast: the skill's level
    /// (<see cref="SkillInstance.LevelCostMultiplier"/>) times any upgrade that makes it cheaper.</summary>
    public float CostMultiplier = 1f;

    // --- Curses (CurseSkillDefinition) ---

    /// <summary>Enemies a curse jumps to when its bearer dies.</summary>
    public int CurseSpread;

    /// <summary>Added to the curse's potency (resistance lowered, % slow).</summary>
    public float CursePotencyBonus;

    /// <summary>% more damage taken by enemies under this curse (Elements upgrades).</summary>
    public float CurseVulnerability;

    /// <summary>Extra enemies a single-target curse marks (nearest to the point first).</summary>
    public int CurseExtraTargets;

    /// <summary>Radius multiplier for area archetypes (nova, burst, orbit).</summary>
    public float AreaMultiplier = 1f;

    /// <summary>Duration multiplier for archetypes that persist (orbit).</summary>
    public float DurationMultiplier = 1f;

    /// <summary>Multiplier on the use time this cast commits (less = faster).</summary>
    public float UseTimeMultiplier = 1f;

    /// <summary>Multiplier on the cooldown this cast starts (less = faster).</summary>
    public float CooldownMultiplier = 1f;

    /// <summary>How many extra times the cast repeats ("Spell Echo").</summary>
    public int Repeats;

    /// <summary>Extra summoned/orbiting objects for archetypes that have a count
    /// but no projectile params (orbit).</summary>
    public int ExtraCount;

    /// <summary>A Life cost never takes the caster below this fraction of max
    /// Life ("Blood Magic" with auto-cast would otherwise pin you at 1 Life).</summary>
    public float LifeCostFloorFraction;

    public float IgniteChanceBonus;

    /// <summary>Multiplier on ignite damage (supports, skill upgrades).</summary>
    public float IgniteDamageMultiplier = 1f;
    public float ShockChanceBonus;
    public float FreezeChanceBonus;

    /// <summary>Chance hits make the target bleed (physical damage over time).</summary>
    public float BleedChance;

    /// <summary>Multiplier on how long this cast's bleed stacks last.</summary>
    public float BleedDurationMultiplier = 1f;

    /// <summary>Extra bleed stacks each successful bleed roll adds.</summary>
    public int ExtraBleedStacks;

    /// <summary>Multiplier on how long this cast's ignite stacks last.</summary>
    public float IgniteDurationMultiplier = 1f;

    /// <summary>Aura, totem and affliction knobs (docs/design/skills.md -> "Auras, totems
    /// and stacks"); null for every other archetype.</summary>
    public AuraParams? Aura;

    /// <summary>Extra totems a totem skill may have up at once.</summary>
    public int ExtraTotems;

    /// <summary>Afflictions (Douse, Combust, Exsanguinate): potency added to the effect
    /// (oil: % more fire damage taken; detonations: % more of the consumed damage).</summary>
    public float AfflictionPotency;

    /// <summary>Combust/Exsanguinate: the burst also hits enemies around each detonated
    /// one for this fraction of the consumed damage (0 = no splash).</summary>
    public float DetonationSplash;

    /// <summary>Combust/Exsanguinate: a detonated enemy keeps this many stacks back.</summary>
    public int StacksKept;

    /// <summary>Douse: the oil also slows by this %.</summary>
    public float OilSlow;

    /// <summary>Multiplier on how far the skill reaches (movement range, totem sight).</summary>
    public float RangeMultiplier = 1f;

    /// <summary>Knockback (px/s) added to the skill's own (Frost Nova's Gale, Leap Slam).</summary>
    public float ExtraKnockback;

    /// <summary>Movement skills: extra seconds untouchable after arriving.</summary>
    public float ExtraInvulnerability;

    /// <summary>Blink: % more movement speed for 3 s after arriving.</summary>
    public float AfterMoveSpeed;

    /// <summary>Blink: the point left behind bursts for this fraction of the skill's damage.</summary>
    public float DepartBurst;

    /// <summary>Blink: the arrival point bursts too (same fraction, at least 60%).</summary>
    public bool ArriveBurst;

    /// <summary>Douse: ignites on an oiled enemy last this much longer (0.5 = +50%).</summary>
    public float OilIgniteDuration;

    /// <summary>Leap Slam: the landing repeats this many more times (aftershocks).</summary>
    public int Aftershocks;

    /// <summary>Kills by this cast burst for this fraction of the hit (0 = none), in <see cref="KillBlastRadius"/> px.</summary>
    public float KillBlastFraction;
    public float KillBlastRadius = 70f;
    public bool CanInflictAilments = true;

    /// <summary>World point a targeted archetype lands on (meteor). Defaults to
    /// <see cref="Origin"/> plus the aim direction.</summary>
    public Vector2 TargetPoint;

    public CastContext(Node2D caster, ActorStats casterStats, Faction casterFaction, Vector2 origin, Vector2 aimDirection)
    {
        Caster = caster;
        CasterStats = casterStats;
        CasterFaction = casterFaction;
        Origin = origin;
        AimDirection = aimDirection;
        TargetPoint = origin + aimDirection * 200f;
    }
}
