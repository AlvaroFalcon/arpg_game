#nullable enable
using System.Collections.Generic;
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

    /// <summary>
    /// docs/design/stats.md's third pass -> "Adding a damage type": the per-type
    /// switch that used to live inline in <see cref="TakeDamage"/> is now a table
    /// — <c>DamageType -&gt; (resistStatId, penetrationStatId)</c> — so a fourth
    /// element costs one <see cref="DamageType"/> entry, two <see cref="StatId"/>s,
    /// one row here, and one affix `.tres`, not a new switch arm to hand-write.
    /// <see cref="DamageType.Physical"/> is deliberately absent: it mitigates
    /// through <see cref="ActorStats.Armour"/>, not a resistance/penetration pair,
    /// so <see cref="TakeDamage"/> branches on it before ever consulting this table.
    /// </summary>
    private static readonly Dictionary<DamageType, (StatId Resistance, StatId Penetration)> ElementalMitigationStats = new()
    {
        { DamageType.Fire, (StatId.FireResistance, StatId.FirePenetration) },
        { DamageType.Cold, (StatId.ColdResistance, StatId.ColdPenetration) },
        { DamageType.Lightning, (StatId.LightningResistance, StatId.LightningPenetration) },
    };

    private ActorStats _stats = null!;

    /// <summary>Own RNG rather than a shared static one, matching
    /// <see cref="Items.DropSpawner"/>'s existing convention — every entity that
    /// rolls its own randomness owns its own generator.</summary>
    private readonly RandomNumberGenerator _critRng = new();

    public bool IsDead { get; private set; }

    /// <summary>
    /// docs/design/dodge.md -> "Invulnerability": seconds of remaining
    /// immunity. While above zero, <see cref="TakeDamage"/> ignores hits
    /// entirely — no damage, no ailments, no thorns. A timer rather than a bool
    /// so overlapping grants (a dodge during a post-hit grace window) simply
    /// take the longer of the two instead of one clearing the other.
    /// </summary>
    public float InvulnerableTime { get; private set; }

    public bool IsInvulnerable => InvulnerableTime > 0f;

    public void GrantInvulnerability(float seconds) => InvulnerableTime = Mathf.Max(InvulnerableTime, seconds);

    /// <summary>
    /// Fired after a hit has been fully resolved and applied, with the
    /// post-mitigation amount. A plain C# event rather than a Godot signal
    /// because the payload is a C# class; consumed by
    /// <see cref="Ailments"/> and on-hit feedback (flash, damage numbers).
    /// </summary>
    public event System.Action<DamagePayload, float>? HitResolved;

    /// <summary>Who landed the killing blow (or the last hit), for on-kill
    /// effects such as Life on Kill. Null when the hit had no attribution.</summary>
    public Node2D? LastHitSource { get; private set; }

    public override void _PhysicsProcess(double delta)
    {
        if (InvulnerableTime > 0f)
        {
            InvulnerableTime -= (float)delta;
        }
    }

    /// <summary>
    /// Damage that has already been resolved elsewhere — an ignite tick, a
    /// keystone explosion — and must not roll crit or mitigation a second time.
    /// Still respects invulnerability and death, and still counts as a hit for
    /// <see cref="Damaged"/>/<see cref="Died"/>.
    /// </summary>
    public void ApplyResolvedDamage(float amount, Node2D? source)
    {
        if (IsDead || IsInvulnerable || amount <= 0f || _stats.Life is not { } life)
        {
            return;
        }

        if (source is not null && IsInstanceValid(source))
        {
            LastHitSource = source;
        }

        life.Current = Mathf.Max(0f, life.Current - amount);
        EmitSignal(SignalName.Damaged, amount);
        if (life.Current <= 0f)
        {
            IsDead = true;
            EmitSignal(SignalName.Died);
        }
    }

    /// <summary>Restores life, clamped to Max. Never revives the dead.</summary>
    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f || _stats.Life is not { } life)
        {
            return;
        }

        life.Current = Mathf.Min(life.Max, life.Current + amount);
    }

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

        if (IsInvulnerable)
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

            if (type == DamageType.Physical)
            {
                totalTaken += DamageMitigation.MitigatePhysical(afterCrit, _stats.Armour.Value);
                continue;
            }

            // Table lookup rather than a hand-written switch arm per element —
            // see ElementalMitigationStats' doc comment.
            (StatId resistId, StatId penetrationId) = ElementalMitigationStats[type];
            float resistance = _stats.Get(resistId).Value;
            float penetration = sourceStats?.Get(penetrationId).Value ?? 0f;
            totalTaken += DamageMitigation.MitigateElemental(afterCrit, resistance, penetration);
        }

        // docs/design/progression.md -> "Run stats": a single base-1.0
        // multiplier for "less/more damage taken" (keystones, shock), applied
        // after mitigation so it scales what actually lands.
        totalTaken *= Mathf.Max(0f, _stats.Get(StatId.DamageTaken).Value);

        if (payload.SourceActor is not null && IsInstanceValid(payload.SourceActor))
        {
            LastHitSource = payload.SourceActor;
        }

        ResourcePool life = _stats.Life!;
        life.Current = Mathf.Max(0f, life.Current - totalTaken);
        EmitSignal(SignalName.Damaged, totalTaken);
        HitResolved?.Invoke(payload, totalTaken);

        if (life.Current <= 0f)
        {
            IsDead = true;
            EmitSignal(SignalName.Died);
        }

        ReflectThorns(payload);
    }

    /// <summary>
    /// docs/design/affixes.md -> "Thorns needs a recursion guard": returns
    /// <see cref="ActorStats.ThornsDamage"/> to the attacker as a fresh Physical
    /// hit, stamped <see cref="DamagePayload.IsThornsReflection"/> so the
    /// attacker's own <see cref="TakeDamage"/> call skips this step entirely —
    /// <b>the guard is the early-return at the top of this method</b>, checked
    /// before anything else runs. Without it, two Thorns-carrying entities
    /// trading hits would reflect into each other forever and hang the game.
    /// </summary>
    private void ReflectThorns(DamagePayload payload)
    {
        if (payload.IsThornsReflection)
        {
            return;
        }

        float thorns = _stats.ThornsDamage.Value;
        if (thorns <= 0f || payload.SourceActor is null || !IsInstanceValid(payload.SourceActor))
        {
            return;
        }

        Health? attackerHealth = payload.SourceActor.GetNodeOrNull<Health>("Health");
        if (attackerHealth is null || attackerHealth.IsDead)
        {
            return;
        }

        var reflected = new DamagePayload(
            thorns,
            DamageType.Physical,
            GetParent<Node2D>(),
            _stats.Faction,
            SkillTags.None,
            DamageDeliveryKind.Hit,
            isThornsReflection: true);

        attackerHealth.TakeDamage(reflected);
    }
}
