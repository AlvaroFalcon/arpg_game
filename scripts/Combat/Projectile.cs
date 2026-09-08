#nullable enable
using System.Collections.Generic;
using Godot;
using PathToExile.Skills;

namespace PathToExile.Combat;

/// <summary>
/// The linear projectile executor's runtime instance — docs/design/skills.md ->
/// "Linear projectile — first skill spec". Aim direction is sampled once (passed
/// in at <see cref="Initialize"/>) and locked for the projectile's whole flight:
/// no homing, no re-aim, no in-flight correction, even though
/// <see cref="Skills.ProjectileParams.HomingStrength"/> is authored on the params
/// block (not implemented).
///
/// An Area2D rather than a CharacterBody2D/RigidBody2D: a projectile never needs
/// collision *response* (it doesn't get pushed around), only overlap detection,
/// which is exactly what Area2D is for.
/// </summary>
[GlobalClass]
public partial class Projectile : Area2D
{
    /// <summary>
    /// Half-angle (degrees) each fork child deviates from the parent's heading,
    /// so a fork opens a 2 * this wide "V". Not authored per-skill: a fork that
    /// barely deviates is indistinguishable from pierce, and one that deviates
    /// too far flies backwards. Promote to a field if a skill ever needs its own.
    /// </summary>
    private const float ForkHalfAngleDegrees = 30f;

    private Vector2 _direction;
    private float _speed;
    private float _range;
    private float _traveled;
    private int _pierceRemaining;
    private int _forksRemaining;
    private Faction _sourceFaction;
    private Node2D? _sourceActor;
    private SkillTags _tags;
    private float _damage;
    private DamageType _damageType;
    private SkillUser? _ownerSkillUser;

    // Tracked even at Pierce = 0. Two lines now; load-bearing for pierce and
    // split later (a pierce/chain projectile must never hit the same target
    // twice, including immediately re-hitting the parent's target after a
    // split) — see docs/design/skills.md -> "The already-hit set".
    private readonly HashSet<ulong> _alreadyHit = new();

    /// <summary>
    /// Called by <see cref="Skills.SkillUser"/> immediately after instancing, before
    /// this node enters the tree's next physics frame. <paramref name="damage"/> is
    /// the already-resolved (base + caster modifiers) amount — the skill supplies
    /// the base, the caster supplies the modifier stack, resolved once at cast time.
    /// </summary>
    public void Initialize(SkillUser owner, CastContext context, Vector2 direction, float damage)
    {
        _ownerSkillUser = owner;
        _direction = direction;
        _speed = context.Projectile!.Speed;
        _range = context.Projectile.Range;
        _pierceRemaining = context.Projectile.Pierce;
        _forksRemaining = context.Projectile.Forks;
        _sourceFaction = context.CasterFaction;
        _sourceActor = context.Caster;
        _tags = context.Tags;
        _damage = damage;

        // docs/design/stats.md -> "Damage types": derived from the cast's tags
        // once here, at spawn, rather than re-derived per hit — a fork child
        // inherits it directly (see InitializeFork) rather than re-deriving from
        // its own copy of the tags, so a CastModifier that adds a tag mid-flight
        // (none exist yet) can't retroactively change an already-flying
        // projectile's damage type.
        _damageType = DamageTypeResolution.Resolve(_tags);

        Rotation = direction.Angle();

        // Layers/mask resolved here, at spawn, from the caster's faction — never
        // authored on the definition. This is what lets one Fireball.tres be cast
        // by either side (docs/design/skills.md -> "Faction / friendly fire").
        CollisionLayer = CollisionLayers.Projectile;
        CollisionMask = _sourceFaction == Faction.Player
            ? CollisionLayers.EnemyFaction | CollisionLayers.World
            : CollisionLayers.PlayerFaction | CollisionLayers.World;
    }

    /// <summary>
    /// Initializes a fork child from its parent. Everything is inherited except
    /// the heading, one generation of forks, and the pierce budget.
    /// </summary>
    public void InitializeFork(Projectile parent, Vector2 direction)
    {
        _ownerSkillUser = parent._ownerSkillUser;
        _direction = direction;
        _speed = parent._speed;
        _range = parent._range;
        _pierceRemaining = parent._pierceRemaining;
        _forksRemaining = parent._forksRemaining - 1;
        _sourceFaction = parent._sourceFaction;
        _sourceActor = parent._sourceActor;
        _tags = parent._tags;
        _damage = parent._damage;
        _damageType = parent._damageType;

        // Children inherit the parent's travelled distance, so a fork shares one
        // total range budget rather than resetting it — forking trades reach for
        // coverage instead of being strictly better than not forking.
        _traveled = parent._traveled;

        // Inherit the already-hit set: without this a child immediately re-hits
        // the target its parent just forked off, and forks loop. This is the
        // case docs/design/skills.md called the already-hit set "load-bearing" for.
        _alreadyHit.UnionWith(parent._alreadyHit);

        Rotation = direction.Angle();
        CollisionLayer = parent.CollisionLayer;
        CollisionMask = parent.CollisionMask;
    }

    public override void _Ready()
    {
        AreaEntered += OnAreaEntered;
        BodyEntered += OnBodyEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        float step = _speed * (float)delta;
        GlobalPosition += _direction * step;
        _traveled += step;

        // Range is the authored, design-facing number (players reason in
        // distance, not seconds); this max-lifetime-by-distance is purely a leak
        // guard against a projectile that never hits anything.
        if (_traveled >= _range)
        {
            QueueFree();
        }
    }

    private void OnAreaEntered(Area2D area) => ResolveHit(area);

    private void OnBodyEntered(Node2D body) => ResolveHit(body);

    private void ResolveHit(Node2D other)
    {
        if (other is StaticBody2D or TileMap)
        {
            QueueFree();
            return;
        }

        Health? health = other.GetNodeOrNull<Health>("Health");
        if (health is null)
        {
            return;
        }

        if (health.Faction == _sourceFaction)
        {
            return; // same faction — collision layers should already prevent this
        }

        ulong id = other.GetInstanceId();
        if (!_alreadyHit.Add(id))
        {
            return;
        }

        var payload = new DamagePayload(_damage, _damageType, _sourceActor, _sourceFaction, _tags, DamageDeliveryKind.Hit);
        health.TakeDamage(payload);
        _ownerSkillUser?.ReportHit(other, _damage);

        OnHitOutcome outcome = ResolveOnHit();
        switch (outcome.Kind)
        {
            case OnHitOutcomeKind.Despawn:
                QueueFree();
                break;
            case OnHitOutcomeKind.Continue:
                _pierceRemaining--;
                break;
            case OnHitOutcomeKind.Redirect:
                _direction = outcome.NewDirection;
                break;
            case OnHitOutcomeKind.Fork:
                SpawnForkChildren();
                QueueFree();
                break;
        }
    }

    /// <summary>
    /// A fork replaces this projectile with two children opening a "V" around its
    /// current heading. Spawning is deferred: this runs inside an Area2D overlap
    /// signal, i.e. mid-physics-step, and adding collision nodes to the tree there
    /// is what produces Godot's "can't change state while flushing queries" error.
    /// </summary>
    private void SpawnForkChildren()
    {
        if (_ownerSkillUser is null)
        {
            return;
        }

        float half = Mathf.DegToRad(ForkHalfAngleDegrees);
        _ownerSkillUser.SpawnForkDeferred(this, _direction.Rotated(-half), GlobalPosition);
        _ownerSkillUser.SpawnForkDeferred(this, _direction.Rotated(half), GlobalPosition);
    }

    /// <summary>
    /// The explicit on-hit resolution step required by docs/design/skills.md ->
    /// "Collision and hit resolution". Pierce and Fork are wired up; chains and
    /// bounces would extend this method, not restructure the caller.
    ///
    /// <b>Pierce outranks fork</b> when a skill has both: the projectile passes
    /// through and keeps its fork budget for a later hit. The alternative
    /// (forking on the first hit while pierce is unspent) discards the pierce the
    /// player paid for. Arbitrary but deterministic, and it matches PoE, where a
    /// piercing projectile does not fork on the targets it pierces.
    /// </summary>
    private OnHitOutcome ResolveOnHit()
    {
        if (_pierceRemaining > 0)
        {
            return OnHitOutcome.ContinueOutcome;
        }

        return _forksRemaining > 0 ? OnHitOutcome.ForkOutcome : OnHitOutcome.DespawnOutcome;
    }
}
