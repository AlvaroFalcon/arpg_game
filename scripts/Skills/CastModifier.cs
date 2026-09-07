using PathToExile.Combat;

namespace PathToExile.Skills;

/// <summary>
/// Seam 6 (docs/design/skills.md -> "CastModifier — behaviour transforms"): a
/// behaviour transform distinct from <see cref="Stats.StatModifier"/>. A
/// StatModifier is a number applied to an actor-owned Stat via the
/// Flat/Increased/More formula and is order-independent by construction. A
/// CastModifier is an *operation* on the per-cast parameter block/tag set
/// (<see cref="CastContext"/>) — "+1 projectile", "projectiles split on hit",
/// "add the Fire tag" — and is order-dependent, hence <see cref="Priority"/>.
///
/// Zero subclasses exist in the first slice. The type and the collect-and-apply
/// pipeline stage in <see cref="Skills.SkillUser.TryCast"/> exist now, running
/// over an empty list, because every future upgrade shape (artifact, tree node,
/// bound item aspect) needs this stage to already be real infrastructure rather
/// than a rewrite of the cast path.
/// </summary>
public abstract class CastModifier
{
    /// <summary>
    /// Application order when multiple CastModifiers apply to the same cast.
    /// Convention: additive-count modifiers occupy a lower priority band than
    /// multiplicative ones, so "+1 arrow" resolves before "double your arrows".
    /// Application is a stable sort by this value.
    /// </summary>
    public abstract int Priority { get; }

    /// <summary>Required tag set for this modifier to apply to a given cast.
    /// <see cref="SkillTags.None"/> (the default) means "always applies". Reuses
    /// the same condition shape as <see cref="Stats.StatModifier.Condition"/>
    /// (seam 4) since both express the same predicate.</summary>
    public virtual SkillTags Condition => SkillTags.None;

    public abstract void Apply(CastContext context);
}
