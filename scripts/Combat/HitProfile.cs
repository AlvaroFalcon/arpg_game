#nullable enable
using Godot;

namespace ArpgGame.Combat;

/// <summary>
/// Everything a spawned hit-dealer (projectile, burst, orbiting blade) needs to
/// build a <see cref="DamagePayload"/> for each target it touches, resolved
/// once at cast time — the same "resolve at cast, apply per hit" split the
/// projectile's damage number already follows (docs/design/skills.md).
/// </summary>
public sealed class HitProfile
{
    public float Damage;
    public DamageType Type;
    public Node2D? Source;
    public Faction Faction;
    public SkillTags Tags;
    public float IgniteChance;
    public float ShockChance;
    public float FreezeChance;
    public bool CanInflictAilments = true;
    public float KnockbackForce;
    public float IgniteDamageMultiplier = 1f;
    public float BleedChance;
    public float BleedDurationMultiplier = 1f;
    public int BleedStacks = 1;
    public float IgniteDurationMultiplier = 1f;
    public float KillBlastFraction;
    public float KillBlastRadius;

    /// <summary>This fraction of <see cref="Damage"/> lands as cold instead (<see cref="Skills.CastContext.ColdConversion"/>).</summary>
    public float ColdConversion;

    /// <summary>The casting skill, for the damage number's icon (presentation only).</summary>
    public Skills.SkillDefinition? Skill;

    public DamagePayload BuildPayload(Vector2 knockbackDirection)
    {
        Node2D? source = Source is not null && GodotObject.IsInstanceValid(Source) ? Source : null;
        float cold = Type == DamageType.Cold ? 0f : Mathf.Clamp(ColdConversion, 0f, 1f);
        DamagePayload payload = cold > 0f
            ? new DamagePayload(new[] { (Type, Damage * (1f - cold)), (DamageType.Cold, Damage * cold) }, source, Faction, Tags | SkillTags.Cold, DamageDeliveryKind.Hit)
            : new DamagePayload(Damage, Type, source, Faction, Tags, DamageDeliveryKind.Hit);
        return Fill(payload, knockbackDirection);
    }

    private DamagePayload Fill(DamagePayload p, Vector2 knockbackDirection)
    {
        p.IgniteChance = IgniteChance;
        p.ShockChance = ShockChance;
        p.FreezeChance = FreezeChance;
        p.CanInflictAilments = CanInflictAilments;
        p.IgniteDamageMultiplier = IgniteDamageMultiplier;
        p.BleedChance = BleedChance;
        p.BleedDurationMultiplier = BleedDurationMultiplier;
        p.BleedStacks = BleedStacks;
        p.IgniteDurationMultiplier = IgniteDurationMultiplier;
        p.KillBlastFraction = KillBlastFraction;
        p.KillBlastRadius = KillBlastRadius;
        p.Knockback = knockbackDirection * KnockbackForce;
        p.Skill = Skill;
        return p;
    }

    public HitProfile Clone() => (HitProfile)MemberwiseClone();
}
