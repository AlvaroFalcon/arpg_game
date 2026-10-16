#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Skills;

namespace ArpgGame.Combat;

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
    private int _chainsRemaining;
    private int _bouncesRemaining;
    private bool _bouncePending;
    private float _lastStep;
    private HitProfile? _hit;

    /// <summary>Max distance a chain may jump to its next target.</summary>
    private const float ChainRange = 300f;

    // --- co-op (docs/design/multiplayer.md -> "Effects") ---------------------------

    public Vector2 Direction => _direction;
    public float Speed => _speed;
    public float RangeLeft => Mathf.Max(0f, _range - _traveled);
    public Faction SourceFaction => _sourceFaction;

    /// <summary>The flight's animation (null = the default arrow shape).</summary>
    public SpriteFrames? Frames => _frames;

    private SpriteFrames? _frames;

    /// <summary>Raised when the heading changes mid-flight (chain, ricochet): the
    /// host tells clients so their copy turns too.</summary>
    public event System.Action<Projectile>? Turned;

    /// <summary>Co-op client: a picture of the host's projectile — flies straight,
    /// touches nothing, and is turned or ended by the host's events.</summary>
    public bool Cosmetic { get; private set; }

    public void InitializeCosmetic(Vector2 direction, float speed, float range, float scale, SpriteFrames? frames)
    {
        Cosmetic = true;
        _direction = direction;
        _speed = speed;
        _range = range;
        Rotation = direction.Angle();
        Scale = Vector2.One * Mathf.Max(0.2f, scale);
        CollisionLayer = 0;
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = false;
        if (frames is not null)
        {
            UseFrames(frames);
        }
    }

    /// <summary>Co-op client: the host's projectile changed heading here.</summary>
    public void NetTurn(Vector2 position, Vector2 direction)
    {
        GlobalPosition = position;
        _direction = direction;
        _traveled = 0f;
        Rotation = direction.Angle();
    }

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
        _chainsRemaining = context.Projectile.Chains;
        _bouncesRemaining = context.CasterFaction == Faction.Player ? context.Projectile.Bounces : 0;
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
        Scale = Vector2.One * Mathf.Max(0.2f, context.Projectile.Size);

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
        _chainsRemaining = parent._chainsRemaining;
        _bouncesRemaining = parent._bouncesRemaining;
        _hit = parent._hit;
        Scale = parent.Scale;
        if (parent.GetNodeOrNull<AnimatedSprite2D>("Sprite") is { SpriteFrames: { } frames })
        {
            _frames = frames;
            CallDeferred(nameof(UseFrames), frames);
        }
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

    /// <summary>
    /// Run-layer hit data (ailment chances, knockback) and the skill's own
    /// animated visual — docs/design/progression.md. Optional: a projectile
    /// that never receives one behaves exactly as before.
    /// </summary>
    public void ApplyHitProfile(HitProfile hit, SpriteFrames? frames)
    {
        _hit = hit;
        _damageType = hit.Type;
        if (frames is not null)
        {
            UseFrames(frames);
        }
    }

    private void UseFrames(SpriteFrames frames)
    {
        _frames = frames;
        if (GetNodeOrNull<AnimatedSprite2D>("Sprite") is not { } sprite)
        {
            return;
        }

        sprite.SpriteFrames = frames;
        sprite.Play();
        sprite.Visible = true;
        if (GetNodeOrNull<Polygon2D>("Visual") is { } visual)
        {
            visual.Visible = false;
        }
    }

    public override void _Ready()
    {
        AreaEntered += OnAreaEntered;
        BodyEntered += OnBodyEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_bouncePending)
        {
            _bouncePending = false;
            Bounce();
        }

        float step = _speed * (float)delta;
        _lastStep = step;
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

    /// <summary>
    /// Ricochet (keystone): reflects the heading on the wall's normal found by a short ray
    /// along the flight path, steps back out of the wall, and forgets already-hit targets
    /// so the rebound can strike them again. Falls back to a straight reversal if the
    /// ray finds nothing (the projectile started inside geometry).
    /// </summary>
    private void Bounce()
    {
        _bouncesRemaining--;
        Vector2 from = GlobalPosition - _direction * (_lastStep + 8f);
        Vector2 to = GlobalPosition + _direction * 24f;
        var query = PhysicsRayQueryParameters2D.Create(from, to, CollisionLayers.World);
        Godot.Collections.Dictionary hit = GetWorld2D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0 && hit["normal"].AsVector2() != Vector2.Zero)
        {
            Vector2 normal = hit["normal"].AsVector2();
            _direction = _direction.Bounce(normal).Normalized();
            GlobalPosition = hit["position"].AsVector2() + normal * 3f;
        }
        else
        {
            _direction = -_direction;
            GlobalPosition += _direction * (_lastStep + 3f);
        }

        Rotation = _direction.Angle();
        _alreadyHit.Clear();
        Turned?.Invoke(this);
    }

    private void OnAreaEntered(Area2D area) => ResolveHit(area);

    private void OnBodyEntered(Node2D body) => ResolveHit(body);

    private void ResolveHit(Node2D other)
    {
        if (other is StaticBody2D or TileMap)
        {
            if (_bouncesRemaining > 0)
            {
                // Resolved at the top of the next physics step: querying the space from
                // inside an overlap callback is not safe.
                _bouncePending = true;
                return;
            }

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

        DamagePayload payload = _hit is not null
            ? _hit.BuildPayload(_direction)
            : new DamagePayload(_damage, _damageType, _sourceActor, _sourceFaction, _tags, DamageDeliveryKind.Hit);
        health.TakeDamage(payload);
        // The caster can be gone by the time its projectile lands (a co-op player
        // who disconnected, a dead enemy): only report to a live skill user.
        if (_ownerSkillUser is { } owner && IsInstanceValid(owner))
        {
            owner.ReportHit(other, _damage);
        }

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
                Rotation = _direction.Angle();
                _chainsRemaining--;
                Turned?.Invoke(this);

                // A chain is a fresh leg: it gets a fresh range budget, or a
                // long first flight would leave nothing to chain with.
                _traveled = 0f;
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

        if (_forksRemaining > 0)
        {
            return OnHitOutcome.ForkOutcome;
        }

        // Chain: redirect toward the nearest hostile this projectile has not
        // already hit. PoE's order — pierce, then fork, then chain.
        if (_chainsRemaining > 0
            && CombatQuery.NearestHostile(GetTree(), GlobalPosition, _sourceFaction, ChainRange, _alreadyHit) is { } next)
        {
            return OnHitOutcome.Redirect(GlobalPosition.DirectionTo(next.GlobalPosition));
        }

        return OnHitOutcome.DespawnOutcome;
    }
}
