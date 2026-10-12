using ArpgGame.Stats;

namespace ArpgGame.Skills;

public enum CastFailureKind
{
    SlotEmpty,
    StillRecovering,
    InsufficientResource,
}

/// <summary>
/// Typed reason a cast didn't happen, per docs/design/skills.md -> "Insufficient
/// resource": "A failed cast returns a reason ... rather than silently doing
/// nothing." Nothing consumes this yet (no UI/sound/flash exists), but returning
/// it now means <see cref="Skills.SkillUser.TryCast"/>'s signature never has to
/// change to add it later.
/// </summary>
public sealed class CastFailure
{
    public CastFailureKind Kind { get; }

    /// <summary>Set only when <see cref="Kind"/> is
    /// <see cref="CastFailureKind.InsufficientResource"/>.</summary>
    public ResourceType? Resource { get; }

    private CastFailure(CastFailureKind kind, ResourceType? resource)
    {
        Kind = kind;
        Resource = resource;
    }

    public static CastFailure SlotEmpty() => new(CastFailureKind.SlotEmpty, null);

    public static CastFailure StillRecovering() => new(CastFailureKind.StillRecovering, null);

    public static CastFailure InsufficientResource(ResourceType resource) =>
        new(CastFailureKind.InsufficientResource, resource);

    public override string ToString() => Resource is { } r ? $"{Kind}({r})" : Kind.ToString();
}
