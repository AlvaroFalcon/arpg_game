#nullable enable
using Godot;
using PathToExile.Combat;
using PathToExile.Stats;

namespace PathToExile.Enemies;

/// <summary>
/// Base for every hostile in a run — docs/design/enemies.md. Owns the parts all
/// enemies share: finding the player, moving with knockback, contact damage,
/// hit feedback, scaling, elite promotion, and reporting its death. What an
/// enemy actually <i>does</i> lives in <see cref="Think"/>, overridden per
/// behaviour (<see cref="ChaserEnemy"/>, <see cref="ChargerEnemy"/>,
/// <see cref="RangedEnemy"/>, <see cref="ExploderEnemy"/>, bosses).
///
/// Expects children: "Stats" (<see cref="ActorStats"/>), "Health",
/// optionally "Ailments" and a "Sprite" (<see cref="AnimatedSprite2D"/>).
/// Base stats are set from the exports below in <c>_Ready</c> rather than from a
/// StatBlock .tres per enemy — one number per enemy is easier to tune here, and
/// enemies have no attributes (they are zeroed so Strength's derived life does
/// not silently pad every monster).
///
/// Physics: layer EnemyFaction, mask World|EnemyFaction. Enemies push each
/// other apart but pass through the player (the player's layer is
/// PlayerFaction only) — being body-blocked into a corner by a wall of slimes
/// reads as unfair in this genre; being hurt by touching them does not.
/// </summary>
[GlobalClass]
public partial class Enemy : CharacterBody2D
{
    [Export]
    public string DisplayName = "Enemy";

    [Export]
    public float BaseLife = 30f;

    [Export]
    public float BaseSpeed = 80f;

    [Export]
    public float BaseArmour;

    [Export]
    public float ContactDamage = 8f;

    /// <summary>Distance (centre to centre) at which contact damage applies.</summary>
    [Export]
    public float ContactRadius = 22f;

    [Export]
    public float ContactCooldown = 0.8f;

    [Export]
    public int ExperienceValue = 1;

    /// <summary>0 = full knockback, 1 = immovable.</summary>
    [Export(PropertyHint.Range, "0,1")]
    public float KnockbackResistance;

    [Export]
    public PackedScene? DamageNumberScene;

    public ActorStats Stats { get; private set; } = null!;
    public Health Health { get; private set; } = null!;
    public Ailments? Ailments { get; private set; }
    public AnimatedSprite2D? Sprite { get; private set; }

    public bool IsElite { get; private set; }
    public virtual bool IsBoss => false;

    /// <summary>Raised when this enemy dies, before it frees itself.</summary>
    public event System.Action<Enemy>? Killed;

    protected Node2D? Target { get; private set; }

    /// <summary>Movement the behaviour wants this frame (px/s).</summary>
    protected Vector2 DesiredVelocity;

    private Vector2 _knockback;
    private float _contactTimer;
    private float _flashTimer;
    private Color _eliteTint = Colors.White;
    private float _damageMultiplier = 1f;

    public override void _Ready()
    {
        Stats = GetNode<ActorStats>("Stats");
        Health = GetNode<Health>("Health");
        Ailments = GetNodeOrNull<Ailments>("Ailments");
        Sprite = GetNodeOrNull<AnimatedSprite2D>("Sprite");

        Stats.Strength.BaseValue = 0f;
        Stats.Dexterity.BaseValue = 0f;
        Stats.Intelligence.BaseValue = 0f;
        Stats.Get(StatId.MaxLife).BaseValue = BaseLife;
        Stats.MovementSpeed.BaseValue = BaseSpeed;
        Stats.Armour.BaseValue = BaseArmour;
        Stats.RecomputeAttributeDerivations();
        RefillLife();

        AddToGroup(CombatQuery.EnemyGroup);
        Health.Damaged += OnDamaged;
        Health.Died += OnDied;
        Health.HitResolved += OnHitResolved;
    }

    private void RefillLife()
    {
        if (Stats.Life is { } life)
        {
            life.Current = life.Max;
        }
    }

    /// <summary>
    /// Run-time difficulty scaling (docs/design/enemies.md -> "Scaling"): applied
    /// by the director right after spawning. Life and damage scale separately so
    /// late enemies get tankier faster than they get deadlier — the player's
    /// damage scales with their build, their life mostly does not.
    /// </summary>
    public void ApplyScaling(float lifeMultiplier, float damageMultiplier)
    {
        lifeMultiplier = Mathf.Pow(lifeMultiplier, LifeScalingExponent);
        damageMultiplier = Mathf.Pow(damageMultiplier, DamageScalingExponent);
        if (lifeMultiplier != 1f)
        {
            Stats.Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.More, (lifeMultiplier - 1f) * 100f, this));
        }

        _damageMultiplier *= damageMultiplier;
        if (damageMultiplier != 1f)
        {
            Stats.Damage.AddModifier(new StatModifier(ModifierType.More, (damageMultiplier - 1f) * 100f, this));
        }

        RefillLife();
    }

    /// <summary>
    /// docs/design/enemies.md -> "Elites": a bigger, tougher, glowing version of
    /// a normal enemy worth much more experience and with a chance to drop gear.
    /// </summary>
    public void MakeElite()
    {
        if (IsElite)
        {
            return;
        }

        IsElite = true;
        Scale *= 1.35f;
        ExperienceValue *= 6;
        KnockbackResistance = Mathf.Max(KnockbackResistance, 0.6f);
        Stats.Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.More, 300f, "elite"));
        Stats.MovementSpeed.AddModifier(new StatModifier(ModifierType.More, 15f, "elite"));
        _damageMultiplier *= 1.4f;
        Stats.Damage.AddModifier(new StatModifier(ModifierType.More, 40f, "elite"));
        _eliteTint = new Color(1f, 0.85f, 0.4f);
        RefillLife();
    }

    /// <summary>Bosses scale more gently than the swarm (docs/design/bosses.md
    /// -> "Tuning"): their numbers are authored for the minute they appear.</summary>
    protected virtual float LifeScalingExponent => 1f;

    protected virtual float DamageScalingExponent => 1f;

    public float LifeFraction => Stats.Life is { } l && l.Max > 0f ? l.Current / l.Max : 0f;

    public override void _PhysicsProcess(double delta)
    {
        if (Health.IsDead)
        {
            return;
        }

        float dt = (float)delta;
        Target = FindTarget();

        DesiredVelocity = Vector2.Zero;
        if (Target is not null)
        {
            Think(dt);
        }

        _knockback = _knockback.MoveToward(Vector2.Zero, 900f * dt);
        Velocity = DesiredVelocity + _knockback;
        MoveAndSlide();

        if (_contactTimer > 0f)
        {
            _contactTimer -= dt;
        }

        if (Target is not null && _contactTimer <= 0f && ContactDamage > 0f && IsTouching(Target))
        {
            DealContactDamage(Target, ContactDamage * ContactDamageScale());
            _contactTimer = ContactCooldown;
        }

        UpdateVisual(dt);
    }

    /// <summary>Behaviours may raise contact damage (a charging brute).</summary>
    protected virtual float ContactDamageScale() => 1f;

    protected virtual bool IsTouching(Node2D target) =>
        GlobalPosition.DistanceTo(target.GlobalPosition) <= ContactRadius * Scale.X;

    protected void DealContactDamage(Node2D target, float amount)
    {
        if (target.GetNodeOrNull<Health>("Health") is not { IsDead: false } health)
        {
            return;
        }

        health.TakeDamage(new DamagePayload(amount * _damageMultiplier, DamageType.Physical, this, Faction.Enemy, SkillTags.Melee, DamageDeliveryKind.Hit));
    }

    /// <summary>Damage dealt by this enemy's own scripted attacks, with scaling applied.</summary>
    protected float ScaledDamage(float baseDamage) => baseDamage * _damageMultiplier;

    protected HitProfile MakeHit(float baseDamage, DamageType type, SkillTags tags = SkillTags.None) => new()
    {
        Damage = ScaledDamage(baseDamage),
        Type = type,
        Source = this,
        Faction = Faction.Enemy,
        Tags = tags,
        CanInflictAilments = false,
    };

    protected virtual void Think(float delta)
    {
    }

    protected Vector2 DirectionToTarget => Target is null ? Vector2.Zero : GlobalPosition.DirectionTo(Target.GlobalPosition);

    protected float DistanceToTarget => Target is null ? float.MaxValue : GlobalPosition.DistanceTo(Target.GlobalPosition);

    protected float Speed => Stats.MovementSpeed.Value;

    private Node2D? FindTarget()
    {
        if (Target is not null && IsInstanceValid(Target) && Target.IsInsideTree())
        {
            return Target.GetNodeOrNull<Health>("Health") is { IsDead: false } ? Target : null;
        }

        return GetTree().GetFirstNodeInGroup(CombatQuery.PlayerGroup) as Node2D;
    }

    protected virtual void UpdateVisual(float delta)
    {
        if (Sprite is null)
        {
            return;
        }

        if (Mathf.Abs(Velocity.X) > 2f)
        {
            Sprite.FlipH = Velocity.X < 0f;
        }

        if (_flashTimer > 0f)
        {
            _flashTimer -= delta;
            Sprite.Modulate = new Color(3f, 3f, 3f);
            return;
        }

        Color tint = Ailments?.Tint ?? Colors.White;
        Sprite.Modulate = tint * _eliteTint;
    }

    private void OnHitResolved(DamagePayload payload, float taken)
    {
        if (payload.Knockback != Vector2.Zero)
        {
            _knockback += payload.Knockback * (1f - KnockbackResistance);
        }
    }

    private void OnDamaged(float amount)
    {
        _flashTimer = 0.08f;
        SpawnDamageNumber(amount);
    }

    private void SpawnDamageNumber(float amount)
    {
        if (DamageNumberScene is null || amount < 0.5f || !(Core.GameSettings.Get(this)?.DamageNumbers ?? true))
        {
            return;
        }

        var number = DamageNumberScene.Instantiate<UI.DamageNumber>();
        GetTree().CurrentScene.AddChild(number);
        number.GlobalPosition = GlobalPosition + new Vector2(GD.Randf() * 16f - 8f, -18f * Scale.Y);
        number.Display(amount);
    }

    protected virtual void OnDied()
    {
        Killed?.Invoke(this);
        DeathPuff.Spawn(GetTree().CurrentScene, GlobalPosition, IsElite || IsBoss ? 2.2f : 1f, Sprite?.Modulate ?? Colors.White);
        QueueFree();
    }
}
