#nullable enable
using System.Collections.Generic;
using Godot;
using PathToExile.Combat;
using PathToExile.Stats;

namespace PathToExile.Skills;

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

    /// <summary>Multiplier on ignite damage (Warlock's Hellfire).</summary>
    public float IgniteDamageMultiplier = 1f;
    public float ShockChanceBonus;
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
