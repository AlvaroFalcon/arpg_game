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
    public bool CanInflictAilments = true;
    public float KnockbackForce;
    public float IgniteDamageMultiplier = 1f;

    public DamagePayload BuildPayload(Vector2 knockbackDirection)
    {
        Node2D? source = Source is not null && GodotObject.IsInstanceValid(Source) ? Source : null;
        return new DamagePayload(Damage, Type, source, Faction, Tags, DamageDeliveryKind.Hit)
        {
            IgniteChance = IgniteChance,
            ShockChance = ShockChance,
            CanInflictAilments = CanInflictAilments,
            IgniteDamageMultiplier = IgniteDamageMultiplier,
            Knockback = knockbackDirection * KnockbackForce,
        };
    }

    public HitProfile Clone() => (HitProfile)MemberwiseClone();
}
