#nullable enable
using System;
using PathToExile.Combat;
using PathToExile.Skills;

namespace PathToExile.Progression;

/// <summary>
/// A support attached to one skill — docs/design/progression.md -> "Supports".
/// The first real <see cref="CastModifier"/> subclass: seam 6 was built for
/// exactly this ("+1 projectile", "projectiles fork on hit", "convert to
/// cold"), and a support is just one of those transforms with a name, living
/// on <see cref="SkillInstance.Attached"/>.
/// </summary>
public sealed class SupportGem : CastModifier, ISkillUpgrade
{
    public SupportDefinition Definition { get; }

    public SupportGem(SupportDefinition definition)
    {
        Definition = definition;
    }

    public override int Priority => Definition.Priority;

    public override void Apply(CastContext context) => Definition.Apply(context);
}

/// <summary>Authored data for a support: what it does, and which skills accept it.</summary>
public sealed class SupportDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>The skill must carry at least one of these tags (None = any skill).</summary>
    public SkillTags RequiresAny { get; init; }

    /// <summary>The skill must carry none of these tags.</summary>
    public SkillTags Excludes { get; init; }

    /// <summary>Conventions: tag conversions 5, additive counts 10, repeats 20,
    /// multiplicative damage/area 30 — see <see cref="CastModifier.Priority"/>.</summary>
    public int Priority { get; init; } = 30;

    public Action<CastContext> Apply { get; init; } = _ => { };

    public bool Accepts(SkillDefinition skill) =>
        (RequiresAny == SkillTags.None || (skill.Tags & RequiresAny) != 0)
        && (skill.Tags & Excludes) == 0;
}

/// <summary>
/// A caster-wide transform granted by a keystone (docs/design/progression.md ->
/// "Keystones") — the same mechanism as a support, applied to every skill via
/// <see cref="SkillUser.GlobalModifiers"/>.
/// </summary>
public sealed class GlobalCastModifier : CastModifier
{
    private readonly int _priority;
    private readonly Action<CastContext> _apply;

    public GlobalCastModifier(int priority, Action<CastContext> apply)
    {
        _priority = priority;
        _apply = apply;
    }

    public override int Priority => _priority;

    public override void Apply(CastContext context) => _apply(context);
}
