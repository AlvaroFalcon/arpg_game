namespace ArpgGame.Skills;

/// <summary>
/// The per-cast mutable copy of a <see cref="ProjectileSkillDefinition"/>'s
/// numeric parameters — seam 1 from docs/design/skills.md. Copied fresh from the
/// definition on every cast (never mutated in place on the shared `.tres`
/// Resource), so a future <see cref="CastModifier"/> has somewhere to write
/// "+1 projectile" or "+1 pierce" without corrupting every other caster of the
/// same skill.
///
/// Every capability the projectile archetype claims exists here, all at their
/// "off" value, even though only Count/Pierce are read by the executor today —
/// see <see cref="ProjectileSkillDefinition"/>'s doc comment.
/// </summary>
public sealed class ProjectileParams
{
    public float Speed;
    public float Range;
    public float SpawnOffset;
    public int Count = 1;
    public float SpreadDegrees;
    public int Pierce;
    public int Forks;
    public int Chains;
    public int Bounces;
    public float HomingStrength;
    public float Size = 1f;
    public bool OnHitExplode;

    public static ProjectileParams FromDefinition(ProjectileSkillDefinition definition) => new()
    {
        Speed = definition.Speed,
        Range = definition.Range,
        SpawnOffset = definition.SpawnOffset,
        Count = definition.Count,
        SpreadDegrees = definition.SpreadDegrees,
        Pierce = definition.Pierce,
        Forks = definition.Forks,
        Chains = definition.Chains,
        Bounces = definition.Bounces,
        HomingStrength = definition.HomingStrength,
        Size = definition.Size,
        OnHitExplode = definition.OnHitExplode,
    };
}
