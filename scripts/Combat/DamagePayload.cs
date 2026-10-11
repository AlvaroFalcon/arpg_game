#nullable enable
using System.Collections.Generic;
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
/// What a hit actually carries, per docs/design/skills.md -> "Damage payload" and
/// docs/design/stats.md's second pass -> "DamagePayload carries a breakdown, not
/// a float". Never a bare float: damage-taken modifiers, DoT, status effects,
/// on-hit triggers and floating damage numbers all need somewhere to hang, and
/// mitigation cannot be written without knowing *what kind* of damage arrived —
/// a single float forces every mitigation decision to be made at the source
/// instead of at the target, which is backwards, since the target owns armour
/// and resistances.
/// </summary>
public sealed class DamagePayload
{
    /// <summary>The raw, per-type breakdown as authored/rolled at cast time —
    /// before crit and before mitigation. Almost always one entry; a hit that
    /// deals both Physical and Fire components (e.g. a future "gain X% of
    /// physical damage as extra fire") would carry two.</summary>
    public IReadOnlyList<(DamageType Type, float Amount)> Components { get; }

    /// <summary>Sum of <see cref="Components"/>, computed once at construction.
    /// This is what keeps <see cref="TargetDummy"/>'s floating damage numbers and
    /// the pre-mitigation tests working unchanged — see the design doc note above.</summary>
    public float Total { get; }

    public Node2D? SourceActor { get; }
    public Faction SourceFaction { get; }
    public SkillTags Tags { get; }
    public DamageDeliveryKind Kind { get; }

    /// <summary>
    /// Stamped by <see cref="Health.TakeDamage"/> after rolling crit — see
    /// docs/design/stats.md -> "Critical strikes". False until TakeDamage runs;
    /// exists so damage numbers and future on-crit effects can read it after the
    /// fact. A plain mutable field rather than a constructor parameter, matching
    /// the mutable-after-construction convention <see cref="Skills.CastContext"/>
    /// already uses for per-cast/per-hit values that aren't known up front.
    /// </summary>
    public bool IsCrit;

    /// <summary>
    /// docs/design/affixes.md -> "Thorns needs a recursion guard": true only for
    /// the payload <see cref="Health.TakeDamage"/> synthesizes to return Thorns
    /// damage to an attacker. <see cref="Health.TakeDamage"/> checks this flag
    /// before applying its own Thorns step, so a reflected hit never itself
    /// reflects — without it, two Thorns-carrying entities hitting each other
    /// would recurse forever and hang the game.
    /// </summary>
    public bool IsThornsReflection { get; }

    /// <summary>
    /// docs/design/progression.md -> "Ailments": the chance this hit ignites
    /// (Fire) or shocks (Lightning) its target, already resolved from the
    /// attacker's stats plus any per-cast support bonuses. Cold hits always
    /// chill, so there is no chill chance. Mutable-after-construction for the
    /// same reason <see cref="IsCrit"/> is: the executor that builds the payload
    /// knows these, the constructor's many call sites do not.
    /// </summary>
    public float IgniteChance;

    public float ShockChance;

    /// <summary>False for a hit from a skill supported by "Elemental Focus"-style
    /// supports (trade ailments for damage).</summary>
    public bool CanInflictAilments = true;

    /// <summary>Optional knockback impulse (px/s) applied to a target that can be
    /// knocked back. Zero for most hits.</summary>
    public Vector2 Knockback;

    public DamagePayload(
        IReadOnlyList<(DamageType Type, float Amount)> components,
        Node2D? sourceActor,
        Faction sourceFaction,
        SkillTags tags,
        DamageDeliveryKind kind,
        bool isThornsReflection = false)
    {
        Components = components;

        float total = 0f;
        foreach ((DamageType _, float amount) in components)
        {
            total += amount;
        }

        Total = total;
        SourceActor = sourceActor;
        SourceFaction = sourceFaction;
        Tags = tags;
        Kind = kind;
        IsThornsReflection = isThornsReflection;
    }

    /// <summary>Convenience constructor for the overwhelmingly common case of a
    /// hit dealing a single damage type — avoids every call site building a
    /// one-element array by hand.</summary>
    public DamagePayload(
        float amount,
        DamageType type,
        Node2D? sourceActor,
        Faction sourceFaction,
        SkillTags tags,
        DamageDeliveryKind kind,
        bool isThornsReflection = false)
        : this(new[] { (type, amount) }, sourceActor, sourceFaction, tags, kind, isThornsReflection)
    {
    }
}
