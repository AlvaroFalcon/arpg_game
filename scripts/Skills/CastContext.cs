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

    public CastContext(Node2D caster, ActorStats casterStats, Faction casterFaction, Vector2 origin, Vector2 aimDirection)
    {
        Caster = caster;
        CasterStats = casterStats;
        CasterFaction = casterFaction;
        Origin = origin;
        AimDirection = aimDirection;
    }
}
