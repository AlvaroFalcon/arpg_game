#nullable enable
using Godot;

namespace PathToExile.Combat;

/// <summary>
/// Whether damage was delivered as an instantaneous hit or as a periodic tick over
/// time. A dedicated field on the payload rather than something read off the
/// skill's tags, because they don't line up: a fire skill can carry the `Duration`
/// tag (its identity involves damage over time) while only *part* of a given cast's
/// damage is delivered that way. A "+% Damage over Time" modifier that filtered on
/// the skill's tag would wrongly also buff the hit portion. See
/// docs/design/skills.md -> "Sharpening 3 — Damage over time is both".
/// Only <see cref="Hit"/> is produced in the first slice; <see cref="OverTime"/> is
/// reserved.
/// </summary>
public enum DamageDeliveryKind
{
    Hit,
    OverTime,
}

/// <summary>
/// What a hit actually carries, per docs/design/skills.md -> "Damage payload".
/// Never a bare float: damage-taken modifiers, DoT, status effects, on-hit
/// triggers and floating damage numbers all need somewhere to hang, and every one
/// of those is a rewrite if the interface were `TakeDamage(float)`.
/// </summary>
public sealed class DamagePayload
{
    public float Amount { get; }
    public Node2D? SourceActor { get; }
    public Faction SourceFaction { get; }
    public SkillTags Tags { get; }
    public DamageDeliveryKind Kind { get; }

    public DamagePayload(float amount, Node2D? sourceActor, Faction sourceFaction, SkillTags tags, DamageDeliveryKind kind)
    {
        Amount = amount;
        SourceActor = sourceActor;
        SourceFaction = sourceFaction;
        Tags = tags;
        Kind = kind;
    }
}
