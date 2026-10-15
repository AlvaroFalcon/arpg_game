#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Stats;

namespace ArpgGame.Enemies;

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

    /// <summary>Rare or unique-rare (the survivor run called rares "elites").</summary>
    public bool IsElite => Rarity >= MonsterRarity.Rare;
    public virtual bool IsBoss => false;

    /// <summary>docs/design/endgame.md -> "Monster rarity".</summary>
    public MonsterRarity Rarity { get; private set; } = MonsterRarity.Normal;

    public IReadOnlyList<MonsterMod> Mods => _mods;
    private readonly List<MonsterMod> _mods = new();

    /// <summary>Monster level (reality tier → level); drives experience and item level.</summary>
    public int MonsterLevel { get; set; } = 1;

    /// <summary>Name shown on the plate (generated for rares and unique-rares).</summary>
    public string ShownName { get; private set; } = "";

    public bool HasMod(string id) => _mods.Exists(m => m.Id == id);

    /// <summary>
    /// A dormant monster stands still until the player comes within
    /// <see cref="AggroRadius"/> or it is hit; waking wakes its whole pack.
    /// Placed packs in a layout start dormant so a reality is fought room by
    /// room, not as one wave converging on the entrance.
    /// </summary>
    public bool Dormant { get; set; }

    [Export]
    public float AggroRadius = 430f;

    /// <summary>Packmates woken together.</summary>
    public List<Enemy>? Pack { get; set; }

    /// <summary>Raised when this enemy dies, before it frees itself.</summary>
    public event System.Action<Enemy>? Killed;

    /// <summary>Every enemy death, globally (on-kill effects).</summary>
    public static event System.Action<Enemy>? AnyKilled;

    /// <summary>A splitter's child was spawned (the owner registers it for loot/XP).</summary>
    public static event System.Action<Enemy>? SplitSpawned;

    public const string DecoyGroup = "decoys";

    protected Node2D? Target { get; private set; }

    // --- co-op (docs/design/multiplayer.md -> "Replication") ----------------------

    /// <summary>
    /// Co-op client: a view of a monster the host simulates. It never thinks,
    /// moves itself or deals damage; snapshots place it and set its life, and
    /// the host's despawn event removes it (<see cref="ReplicaDie"/>).
    /// Set before the node enters the tree.
    /// </summary>
    public bool IsReplica { get; set; }

    /// <summary>Co-op: the host's id for this monster (0 = not networked).</summary>
    public ushort NetId { get; set; }

    /// <summary>Co-op client: ailment tint code from the host (see <see cref="Ailments.TintCode"/>).</summary>
    public byte ReplicaTint { get; set; }

    /// <summary>Engine msec of the last time this monster was damaged (hit flash on clients).</summary>
    public ulong LastHurtMsec { get; private set; }

    /// <summary>Seconds until the target is re-chosen (nearest player, co-op aggro).</summary>
    private float _retarget;

    /// <summary>Movement the behaviour wants this frame (px/s).</summary>
    protected Vector2 DesiredVelocity;

    private Vector2 _knockback;
    private float _contactTimer;
    private float _flashTimer;
    private Color _eliteTint = Colors.White;
    private float _damageMultiplier = 1f;
    private float _modTimer;
    private float _signatureTimer = 3f;
    private readonly RandomNumberGenerator _rng = new();

    /// <summary>Damage type of contact hits ("Fire-touched" makes it Fire).</summary>
    protected DamageType ContactType = DamageType.Physical;

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

        _rng.Randomize();
        ShownName = DisplayName;
        AddToGroup(CombatQuery.EnemyGroup);
        AddChild(new RarityPlate { Name = "RarityPlate" });
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
    public void ApplyScaling(float lifeMultiplier, float damageMultiplier) =>
        ApplyTierScaling(Mathf.Pow(lifeMultiplier, LifeScalingExponent), Mathf.Pow(damageMultiplier, DamageScalingExponent));

    /// <summary>
    /// Raw scaling without the survival-mode exponents: realities (docs/design/endgame.md
    /// -> "Tiers 1-20") already compute the exact multiplier per tier, bosses included.
    /// </summary>
    public void ApplyTierScaling(float lifeMultiplier, float damageMultiplier)
    {
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
    public void MakeElite() => ApplyRarity(MonsterRarity.Rare);

    private static readonly object RaritySource = new();

    /// <summary>
    /// Promotes this monster — docs/design/endgame.md -> "Monster rarity": life,
    /// damage, size and experience multipliers, a generated name for rares and
    /// unique-rares, and rolled modifiers (pass <paramref name="mods"/> to force).
    /// </summary>
    public void ApplyRarity(MonsterRarity rarity, IReadOnlyList<MonsterMod>? mods = null)
    {
        if (rarity == MonsterRarity.Normal || Rarity != MonsterRarity.Normal)
        {
            return;
        }

        Rarity = rarity;
        Scale *= MonsterRarities.Scale(rarity);
        ExperienceValue = Mathf.RoundToInt(ExperienceValue * MonsterRarities.ExperienceMultiplier(rarity));
        KnockbackResistance = Mathf.Max(KnockbackResistance, rarity switch { MonsterRarity.Magic => 0.3f, MonsterRarity.Rare => 0.6f, _ => 0.85f });
        Stats.Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.More, (MonsterRarities.LifeMultiplier(rarity) - 1f) * 100f, RaritySource));
        float dmg = MonsterRarities.DamageMultiplier(rarity);
        _damageMultiplier *= dmg;
        Stats.Damage.AddModifier(new StatModifier(ModifierType.More, (dmg - 1f) * 100f, RaritySource));
        ShownName = MonsterRarities.RandomName(rarity, DisplayName, _rng);
        _mods.Clear();
        _mods.AddRange(mods ?? MonsterRarities.RollMods(rarity, this is RangedEnemy, _rng));
        foreach (MonsterMod mod in _mods)
        {
            ApplyMod(mod);
        }

        Color c = MonsterRarities.Color(rarity);
        _eliteTint = rarity == MonsterRarity.Magic ? Colors.White : c.Lerp(Colors.White, 0.45f);
        RefillLife();
    }

    private void ApplyMod(MonsterMod mod)
    {
        switch (mod.Id)
        {
            case "hasted":
                Stats.MovementSpeed.AddModifier(new StatModifier(ModifierType.More, 35f, RaritySource));
                ContactCooldown *= 0.7f;
                break;
            case "armoured":
                Stats.Armour.AddModifier(new StatModifier(ModifierType.Flat, 40f + 6f * MonsterLevel, RaritySource));
                Stats.Get(StatId.DamageTaken).AddModifier(new StatModifier(ModifierType.More, -15f, RaritySource));
                break;
            case "fire_touched":
                ContactType = DamageType.Fire;
                Stats.FireResistance.AddModifier(new StatModifier(ModifierType.Flat, 40f, RaritySource));
                _damageMultiplier *= 1.2f;
                break;
            case "empowered":
                _damageMultiplier *= 1.4f;
                Stats.Damage.AddModifier(new StatModifier(ModifierType.More, 40f, RaritySource));
                break;
            case "barrage":
                Stats.Get(StatId.ExtraProjectiles).AddModifier(new StatModifier(ModifierType.Flat, 2f, RaritySource));
                break;
        }
    }

    /// <summary>Bosses scale more gently than the swarm (docs/design/bosses.md
    /// -> "Tuning"): their numbers are authored for the minute they appear.</summary>
    protected virtual float LifeScalingExponent => 1f;

    protected virtual float DamageScalingExponent => 1f;

    public float LifeFraction => Stats.Life is { } l && l.Max > 0f ? l.Current / l.Max : 0f;

    public override void _PhysicsProcess(double delta)
    {
        if (IsReplica)
        {
            UpdateVisual((float)delta);
            return;
        }

        if (Health.IsDead)
        {
            return;
        }

        float dt = (float)delta;
        Target = FindTarget(dt);

        if (Dormant)
        {
            if (Target is not null && GlobalPosition.DistanceTo(Target.GlobalPosition) <= AggroRadius)
            {
                Wake();
            }
            else
            {
                Velocity = _knockback = _knockback.MoveToward(Vector2.Zero, 900f * dt);
                MoveAndSlide();
                UpdateVisual(dt);
                return;
            }
        }

        if (Ailments is { IsFrozen: true })
        {
            // Frozen: no thinking, no walking, no contact damage — only knockback drift.
            _knockback = _knockback.MoveToward(Vector2.Zero, 900f * dt);
            Velocity = _knockback;
            MoveAndSlide();
            UpdateVisual(dt);
            return;
        }

        TickMods(dt);

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

        float dealt = amount * _damageMultiplier;
        health.TakeDamage(new DamagePayload(dealt, ContactType, this, Faction.Enemy, SkillTags.Melee, DamageDeliveryKind.Hit));
        if (HasMod("vampiric"))
        {
            Health.Heal(dealt * 0.5f + (Stats.Life?.Max ?? 0f) * 0.02f);
        }
    }

    /// <summary>Wakes this monster and its pack.</summary>
    public void Wake()
    {
        if (!Dormant)
        {
            return;
        }

        Dormant = false;
        if (Pack is not null)
        {
            foreach (Enemy mate in Pack)
            {
                if (IsInstanceValid(mate) && mate != this)
                {
                    mate.Wake();
                }
            }
        }
    }

    /// <summary>Per-frame monster-mod behaviour and the unique-rare signature ability.</summary>
    private void TickMods(float dt)
    {
        _modTimer -= dt;
        if (_modTimer <= 0f)
        {
            _modTimer = 0.5f;
            if (HasMod("regenerating"))
            {
                Health.Heal((Stats.Life?.Max ?? 0f) * 0.015f);
            }

            if (HasMod("frost_aura") && Target is not null && GlobalPosition.DistanceTo(Target.GlobalPosition) < 150f)
            {
                Target.GetNodeOrNull<Ailments>("Ailments")?.ApplyChill();
            }
        }

        if (Rarity == MonsterRarity.Unique && Target is not null)
        {
            _signatureTimer -= dt;
            if (_signatureTimer <= 0f && DistanceToTarget < 600f)
            {
                _signatureTimer = _rng.RandfRange(4.5f, 6f);
                Signature();
            }
        }
    }

    /// <summary>
    /// Unique-rare signature ability — docs/design/endgame.md: always one of the
    /// boss toolkit's telegraphed attacks, so the boss reading rules apply to
    /// mini-bosses too.
    /// </summary>
    private void Signature()
    {
        Node scene = GetTree().CurrentScene;
        float dmg = 10f + 1.6f * MonsterLevel;
        switch (_rng.RandiRange(0, 3))
        {
            case 0:
                AreaBurst.Spawn(scene, Target!.GlobalPosition, 95f, 1.1f, Colors.Red, MakeHit(dmg * 1.6f, DamageType.Physical, SkillTags.Area));
                break;
            case 1:
                for (int i = 0; i < 3; i++)
                {
                    Vector2 at = Target!.GlobalPosition + new Vector2(_rng.RandfRange(-90f, 90f), _rng.RandfRange(-90f, 90f));
                    AreaBurst.Spawn(scene, at, 65f, 1.0f + i * 0.25f, Colors.Red, MakeHit(dmg, DamageType.Fire, SkillTags.Area));
                }

                break;
            case 2:
                for (int i = 0; i < 12; i++)
                {
                    BossBullet.Spawn(scene, GlobalPosition, Vector2.Right.Rotated(Mathf.Tau * i / 12f) * 190f, MakeHit(dmg * 0.7f, DamageType.Cold, SkillTags.Projectile), null, 2f);
                }

                break;
            default:
                Vector2 dir = DirectionToTarget;
                for (int i = -1; i <= 1; i++)
                {
                    LineStrike.Spawn(scene, GlobalPosition, dir.Rotated(i * 0.45f), 420f, 46f, 1.0f, MakeHit(dmg * 1.2f, DamageType.Physical));
                }

                break;
        }
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

    /// <summary>Direction to walk toward the target: around walls via the
    /// reality's <see cref="World.FlowField"/> when there is one, else straight.</summary>
    protected Vector2 DirectionToTarget
    {
        get
        {
            if (Target is null)
            {
                return Vector2.Zero;
            }

            if (World.FlowField.Current is { } field && Target.IsInGroup(CombatQuery.PlayerGroup) && GlobalPosition.DistanceSquaredTo(Target.GlobalPosition) > 60f * 60f)
            {
                Vector2 d = field.Direction(GlobalPosition);
                if (d != Vector2.Zero)
                {
                    return d;
                }
            }

            return GlobalPosition.DirectionTo(Target.GlobalPosition);
        }
    }

    protected float DistanceToTarget => Target is null ? float.MaxValue : GlobalPosition.DistanceTo(Target.GlobalPosition);

    protected float Speed => Stats.MovementSpeed.Value;

    /// <summary>
    /// Decoys first, then the nearest player that is alive and not warded —
    /// re-chosen a couple of times a second, so in co-op a pack splits between
    /// players and turns to whoever is closest (docs/design/multiplayer.md ->
    /// "Aggro"). With one player this is the old rule: that player while alive.
    /// </summary>
    private Node2D? FindTarget(float dt)
    {
        // Decoys (Ninja shadow clones) taunt anything nearby.
        foreach (Node node in GetTree().GetNodesInGroup(DecoyGroup))
        {
            if (node is Node2D decoy && decoy.GlobalPosition.DistanceSquaredTo(GlobalPosition) < 420f * 420f)
            {
                return decoy;
            }
        }

        _retarget -= dt;
        if (_retarget > 0f && Target is Player current && IsInstanceValid(current) && current.IsInsideTree()
            && !current.Warded && current.Health is { IsDead: false })
        {
            return current;
        }

        _retarget = 0.4f + _rng.Randf() * 0.3f;
        return CombatQuery.NearestTargetablePlayer(GetTree(), GlobalPosition);
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

        Color tint = IsReplica ? Ailments.TintForCode(ReplicaTint) : Ailments?.Tint ?? Colors.White;
        Sprite.Modulate = tint * _eliteTint;
    }

    private void OnHitResolved(DamagePayload payload, float taken)
    {
        Wake();
        if (payload.Knockback != Vector2.Zero)
        {
            _knockback += payload.Knockback * (1f - KnockbackResistance);
        }
    }

    private void OnDamaged(float amount)
    {
        LastHurtMsec = Time.GetTicksMsec();
        _flashTimer = 0.08f;
        SpawnDamageNumber(amount);
    }

    private void SpawnDamageNumber(float amount)
    {
        if (DamageNumberScene is null || amount < 0.5f || !(Core.GameSettings.Get(this)?.DamageNumbers ?? true))
        {
            return;
        }

        var number = UI.DamageNumber.Spawn(DamageNumberScene, GetTree().CurrentScene, GlobalPosition + new Vector2(GD.Randf() * 16f - 8f, -18f * Scale.Y));
        number.Display(amount);
    }

    /// <summary>Co-op client: a hit landed on the host (white flash).</summary>
    public void ReplicaFlash() => _flashTimer = 0.08f;

    /// <summary>Co-op client: shows a damage number for a hit the host resolved.</summary>
    public void ReplicaDamageNumber(float amount) => SpawnDamageNumber(amount);

    /// <summary>
    /// Co-op client: the host says this monster is gone — a death puff when it
    /// died, nothing when it was simply removed. No loot, no events: the host
    /// already handled all of that.
    /// </summary>
    public void ReplicaDie(bool died)
    {
        if (IsQueuedForDeletion())
        {
            return;
        }

        if (died && IsInsideTree())
        {
            DeathPuff.Spawn(GetTree().CurrentScene, GlobalPosition, IsElite || IsBoss ? 2.2f : 1f, Sprite?.Modulate ?? Colors.White);
        }

        QueueFree();
    }

    /// <summary>
    /// Co-op client: dresses a replica like the host's monster — rarity (size,
    /// tint, plate), its rolled mods, its name and life total. Stats beyond the
    /// life total don't matter on a client: it never fights.
    /// </summary>
    public void ApplyReplicaLook(MonsterRarity rarity, IReadOnlyList<string> modIds, string shownName, float scale, float maxLife)
    {
        if (rarity != MonsterRarity.Normal)
        {
            var mods = new List<MonsterMod>();
            foreach (string id in modIds)
            {
                if (MonsterRarities.FindMod(id) is { } mod)
                {
                    mods.Add(mod);
                }
            }

            ApplyRarity(rarity, mods);
        }

        if (shownName.Length > 0)
        {
            ShownName = shownName;
        }

        Scale = Vector2.One * scale;
        if (maxLife > 0f && Stats.Life is { Max: > 0f } life && !Mathf.IsEqualApprox(life.Max, maxLife))
        {
            Stats.Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.More, (maxLife / life.Max - 1f) * 100f, this));
        }

        RefillLife();
    }

    protected virtual void OnDied()
    {
        Killed?.Invoke(this);
        AnyKilled?.Invoke(this);
        if (HasMod("volatile"))
        {
            // Staggered ±0.1 s so a pack's blasts spread over frames, and merged: one
            // telegraph per cluster, one hit per target per window (performance.md).
            float delay = 1.0f + (GD.Randf() - 0.5f) * 0.2f;
            AreaBurst.SpawnMerged(GetTree().CurrentScene, GlobalPosition, 90f, delay, Colors.Red, MakeHit(15f + 3f * MonsterLevel, DamageType.Fire, SkillTags.Area), "volatile");
        }

        if (HasMod("splitter") && SceneFilePath.Length > 0)
        {
            var scene = GD.Load<PackedScene>(SceneFilePath);
            for (int i = 0; i < 2; i++)
            {
                var child = scene.Instantiate<Enemy>();
                child.MonsterLevel = MonsterLevel;
                GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, child);
                child.Position = GlobalPosition + new Vector2(i == 0 ? -18f : 18f, 0f);
                child.Scale = Vector2.One * 0.75f;
                SplitSpawned?.Invoke(child);
            }
        }

        DeathPuff.Spawn(GetTree().CurrentScene, GlobalPosition, IsElite || IsBoss ? 2.2f : 1f, Sprite?.Modulate ?? Colors.White);
        QueueFree();
    }
}
