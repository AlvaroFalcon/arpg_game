#nullable enable
using Godot;
using PathToExile.Stats;

namespace PathToExile.Combat;

/// <summary>
/// The damage receiver. Takes a <see cref="DamagePayload"/>, resolves crit and
/// mitigation, mutates the owning actor's <see cref="ActorStats.Life"/> pool, and
/// fires <see cref="Died"/> at zero. No AI, no movement, no retaliation — that
/// belongs to whatever is attached to the entity this component lives on, not to
/// this component.
///
/// Expects a sibling "Stats" node (an <see cref="ActorStats"/>) on the same parent,
/// mirroring the convention used by <see cref="Skills.SkillUser"/>. Life itself
/// lives on ActorStats (not duplicated here) so cost-resolution code and the
/// damage receiver share one source of truth for an actor's life pool.
///
/// docs/design/stats.md's second pass -> "Mitigation" names this class ("Applied
/// in Health.TakeDamage, which is the right owner: the target knows its own
/// defences") as the owner of armour/resistance mitigation, so crit is resolved
/// here too rather than at the source: both need to run "once per landed hit,"
/// and <see cref="TakeDamage"/> is called exactly once per hit already (a
/// piercing/forking projectile calls it once per target, guarded by its own
/// already-hit set).
/// </summary>
[GlobalClass]
public partial class Health : Node
{
    [Signal]
    public delegate void DiedEventHandler();

    [Signal]
    public delegate void DamagedEventHandler(float amount);

    private ActorStats _stats = null!;

    /// <summary>Own RNG rather than a shared static one, matching
    /// <see cref="Items.DropSpawner"/>'s existing convention — every entity that
    /// rolls its own randomness owns its own generator.</summary>
    private readonly RandomNumberGenerator _critRng = new();

    public bool IsDead { get; private set; }

    public Faction Faction => _stats.Faction;

    public override void _Ready()
    {
        _stats = GetParent().GetNode<ActorStats>("Stats");
        _critRng.Randomize();
    }

    public void TakeDamage(DamagePayload payload)
    {
        if (IsDead)
        {
            return;
        }

        // Defense in depth: collision layers already keep same-faction hits from
        // ever reaching here in the normal case, but this check is what makes
        // correctness independent of getting the physics layers exactly right.
        if (payload.SourceFaction == _stats.Faction)
        {
            return;
        }

        // The attacker's stats, if it still has any (SourceActor is null for the
        // "no attribution" hits some test/tool code deals, and a dead attacker's
        // node may already be gone) — Crit and Penetration are the *attacker's*
        // numbers, not the target's, so they're read off here rather than _stats.
        ActorStats? sourceStats = payload.SourceActor is not null && IsInstanceValid(payload.SourceActor)
            ? payload.SourceActor.GetNodeOrNull<ActorStats>("Stats")
            : null;

        bool isCrit = sourceStats is not null && _critRng.Randf() < sourceStats.CritChance.Value;
        payload.IsCrit = isCrit;
        float critMultiplier = isCrit ? sourceStats!.CritMultiplier.Value : 1f;

        float totalTaken = 0f;
        foreach ((DamageType type, float amount) in payload.Components)
        {
            // Crit is rolled once per hit and applied *before* mitigation — see
            // docs/design/stats.md -> "Critical strikes": this is what makes
            // armour meaningfully worse against crits.
            float afterCrit = amount * critMultiplier;
            totalTaken += type switch
            {
                DamageType.Physical => DamageMitigation.MitigatePhysical(afterCrit, _stats.Armour.Value),
                DamageType.Fire => DamageMitigation.MitigateElemental(
                    afterCrit, _stats.FireResistance.Value, sourceStats?.FirePenetration.Value ?? 0f),
                DamageType.Cold => DamageMitigation.MitigateElemental(
                    afterCrit, _stats.ColdResistance.Value, sourceStats?.ColdPenetration.Value ?? 0f),
                DamageType.Lightning => DamageMitigation.MitigateElemental(
                    afterCrit, _stats.LightningResistance.Value, sourceStats?.LightningPenetration.Value ?? 0f),
                _ => afterCrit,
            };
        }

        ResourcePool life = _stats.Life!;
        life.Current = Mathf.Max(0f, life.Current - totalTaken);
        EmitSignal(SignalName.Damaged, totalTaken);

        if (life.Current <= 0f)
        {
            IsDead = true;
            EmitSignal(SignalName.Died);
        }
    }
}
