#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Stats;

namespace ArpgGame.Skills;

/// <summary>
/// Layer 3 of the three-layer split (docs/design/skills.md -> "The three
/// layers"): the executor. Owns the loadout, the per-skill use timers, builds the
/// cast context, pays costs, and triggers the cast. A player and an enemy both
/// have one; the only difference is what feeds it — input (see
/// <see cref="ArpgGame.PlayerSkillInput"/>) vs AI, which doesn't exist yet.
///
/// This component deliberately knows nothing about input actions, the mouse, or
/// WASD — see the class doc on <see cref="ArpgGame.PlayerSkillInput"/> for why
/// that split exists.
///
/// Expects sibling nodes on its parent: "Stats" (an <see cref="ActorStats"/>) and
/// the parent itself acting as the caster's <see cref="Node2D"/>.
/// </summary>
[GlobalClass]
public partial class SkillUser : Node
{
    public const int SlotCount = 6;

    [Signal]
    public delegate void CastStartedEventHandler(int slotIndex);

    [Signal]
    public delegate void CastFiredEventHandler(int slotIndex);

    [Signal]
    public delegate void HitEventHandler(Node2D target, float amount);

    /// <summary>The scene instantiated for each spawned projectile. Must have a
    /// <see cref="Projectile"/> script on its root.</summary>
    [Export]
    public PackedScene? ProjectileScene;

    /// <summary>
    /// Convenience-only Inspector wiring: if set, equipped into the RMB slot on
    /// _Ready. Not part of the design's loadout/equip model itself (that's
    /// <see cref="EquipSkill"/>) — this just exists so a skill can be wired into
    /// a scene without a bespoke bootstrap script for every test/demo scene.
    /// </summary>
    [Export]
    public SkillDefinition? DefaultRmbSkill;

    /// <summary>
    /// Caster-wide behaviour transforms that apply to every skill this caster
    /// uses — docs/design/progression.md -> "Keystones" ("Echoing Mind",
    /// "Avatar of Fire", "Blood Magic"). Per-skill supports live on
    /// <see cref="SkillInstance.Attached"/> instead; both are collected by
    /// <see cref="CollectCastModifiers"/>, the stage seam 6 reserved for exactly
    /// this.
    /// </summary>
    public List<CastModifier> GlobalModifiers { get; } = new();

    /// <summary>Fired when a slot's contents change (HUD refresh). Not a Godot
    /// signal so it can carry a plain C# reference.</summary>
    public event System.Action<SkillInstance?>? SkillEquipped;

    private readonly LoadoutSlot[] _loadout = new LoadoutSlot[SlotCount];

    private Node2D _caster = null!;
    private ActorStats _stats = null!;

    public override void _Ready()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            _loadout[i] = new LoadoutSlot();
        }

        // LMB defaults to the built-in Move occupant, per docs/design/skills.md ->
        // "The right-click conflict — resolved". RMB starts empty.
        _loadout[(int)SkillSlotId.Lmb].BuiltIn = BuiltInAction.Move;

        _caster = GetParent<Node2D>();
        _stats = _caster.GetNode<ActorStats>("Stats");

        if (DefaultRmbSkill is not null)
        {
            EquipSkill(SkillSlotId.Rmb, new SkillInstance(DefaultRmbSkill));
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        foreach (LoadoutSlot slot in _loadout)
        {
            if (slot.Skill is not { } instance)
            {
                continue;
            }

            if (instance.UseTimeRemaining > 0f)
            {
                instance.UseTimeRemaining -= dt;
            }

            TickCharge(instance, dt);
        }
    }

    /// <summary>Whether the given slot currently holds the built-in Move
    /// sentinel — asked by <see cref="Player"/> so it doesn't need to know
    /// loadout internals to decide whether to path-follow on LMB.</summary>
    public bool SlotIsBuiltInMove(SkillSlotId slot) => _loadout[(int)slot].BuiltIn == BuiltInAction.Move;

    /// <summary>Equips a skill instance into a slot, replacing whatever was
    /// there (including a BuiltInAction sentinel).</summary>
    public void EquipSkill(SkillSlotId slot, SkillInstance instance)
    {
        _loadout[(int)slot] = new LoadoutSlot { Skill = instance };
        SkillEquipped?.Invoke(instance);
    }

    /// <summary>Empties a slot entirely (no skill, no built-in).</summary>
    public void ClearSlot(SkillSlotId slot)
    {
        _loadout[(int)slot] = new LoadoutSlot();
        SkillEquipped?.Invoke(null);
    }

    /// <summary>Puts the built-in Move occupant back into a slot (LMB click-to-move).</summary>
    public void RestoreMove(SkillSlotId slot)
    {
        _loadout[(int)slot] = new LoadoutSlot { BuiltIn = BuiltInAction.Move };
        SkillEquipped?.Invoke(null);
    }

    /// <summary>The skill instance in a slot, or null.</summary>
    public SkillInstance? GetSkill(SkillSlotId slot) => _loadout[(int)slot].Skill;

    /// <summary>Every equipped skill instance, in slot order.</summary>
    public IEnumerable<(SkillSlotId Slot, SkillInstance Skill)> EquippedSkills()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (_loadout[i].Skill is { } skill)
            {
                yield return ((SkillSlotId)i, skill);
            }
        }
    }

    /// <summary>Fraction (0..1) of the current recharge remaining, for HUD
    /// cooldown sweeps. Uses whichever gate (use time or charge) is longer.</summary>
    public float CooldownFraction(SkillInstance instance)
    {
        float useFrac = instance.UseTimeRemaining > 0f ? instance.UseTimeRemaining / Mathf.Max(0.01f, EffectiveUseTime(instance.Definition)) : 0f;
        float chargeFrac = instance.ChargesAvailable <= 0 && instance.RechargeTimer > 0f
            ? instance.RechargeTimer / Mathf.Max(0.01f, EffectiveCooldown(instance))
            : 0f;
        return Mathf.Clamp(Mathf.Max(useFrac, chargeFrac), 0f, 1f);
    }

    /// <summary>
    /// Attempts to use whatever is in the given slot. Safe to call every physics
    /// frame the input is held (hold-to-repeat, docs/design/skills.md -> "Input
    /// behaviour") — internal per-skill timers decide whether anything actually
    /// happens. Returns null on success, or a typed reason on failure (nothing
    /// consumes the failure yet).
    /// </summary>
    public CastFailure? TryCast(SkillSlotId slotId, Vector2 aimDirection) =>
        TryCast(slotId, aimDirection, null);

    /// <summary>Overload carrying an explicit world target point, for targeted
    /// archetypes (<see cref="AreaSkillDefinition.AtTarget"/>).</summary>
    public CastFailure? TryCast(SkillSlotId slotId, Vector2 aimDirection, Vector2? targetPoint)
    {
        LoadoutSlot slot = _loadout[(int)slotId];
        if (slot.Skill is not { } instance)
        {
            return CastFailure.SlotEmpty();
        }

        bool chargeReady = instance.ChargesAvailable > 0;
        bool useTimeReady = instance.UseTimeRemaining <= 0f;
        if (!chargeReady || !useTimeReady)
        {
            return CastFailure.StillRecovering();
        }

        SkillDefinition definition = instance.Definition;
        CastContext context = BuildCastContext(definition, aimDirection);
        if (targetPoint is { } point)
        {
            context.TargetPoint = point;
        }

        // Seam 6: collect -> apply, in priority order, over an empty list today.
        List<CastModifier> modifiers = CollectCastModifiers(instance, context);
        ApplyCastModifiers(context, modifiers);

        if (!_stats.IgnoresResourceCosts && !TryPayCosts(context.Costs, context.LifeCostFloorFraction, out ResourceType insufficientResource))
        {
            // Atomic: nothing was spent, and the cast never commits (no cooldown
            // or use-time is started) — see docs/design/skills.md -> "All-or-
            // nothing, never partial."
            return CastFailure.InsufficientResource(insufficientResource);
        }

        // Cooldown/use-time start on cast start (this instant), not on cast
        // resolution — see docs/design/skills.md -> "Cooldown starts on cast
        // start". Committing here, after costs are confirmed payable, is what
        // keeps the whole operation atomic: a failed cost check never partially
        // consumes a charge.
        instance.UseTimeRemaining = EffectiveUseTime(definition) * context.UseTimeMultiplier;
        instance.ChargesAvailable--;
        if (instance.RechargeTimer <= 0f)
        {
            instance.RechargeTimer = EffectiveCooldown(instance) * context.CooldownMultiplier;
        }

        EmitSignal(SignalName.CastStarted, (int)slotId);

        Execute(instance, context);
        ScheduleRepeats(instance, context);

        EmitSignal(SignalName.CastFired, (int)slotId);
        return null;
    }

    /// <summary>
    /// "Spell Echo"-style repeats (docs/design/progression.md -> "Supports"):
    /// the same resolved context executes again, staggered, at the caster's
    /// position at that moment. Costs and cooldowns were paid once, at cast start.
    /// </summary>
    private void ScheduleRepeats(SkillInstance instance, CastContext context)
    {
        for (int i = 1; i <= context.Repeats; i++)
        {
            SceneTreeTimer timer = GetTree().CreateTimer(0.18 * i, processAlways: false, processInPhysics: true);
            timer.Timeout += () =>
            {
                if (!IsInstanceValid(this) || !IsInstanceValid(_caster))
                {
                    return;
                }

                Execute(instance, context);
            };
        }
    }

    /// <summary>Called by a spawned projectile (or any future archetype) when it
    /// lands a hit, so the caster's own Hit event fires regardless of which
    /// executor produced the hit.</summary>
    public void ReportHit(Node2D target, float amount)
    {
        EmitSignal(SignalName.Hit, target, amount);
    }

    private CastContext BuildCastContext(SkillDefinition definition, Vector2 aimDirection)
    {
        var context = new CastContext(_caster, _stats, _stats.Faction, _caster.GlobalPosition, aimDirection)
        {
            Tags = definition.Tags,
            // Per-cast mutable copy of the cost list (seam 1) — a cost-converting
            // modifier would rewrite entries in this list, never the definition's.
            Costs = new List<CostEntry>(definition.Costs),
            MovementDuringUse = definition.MovementDuringUse,
        };

        if (definition is ProjectileSkillDefinition projectileDefinition)
        {
            context.Projectile = ProjectileParams.FromDefinition(projectileDefinition);
        }

        return context;
    }

    private List<CastModifier> CollectCastModifiers(SkillInstance instance, CastContext context)
    {
        // Per-skill supports first, then caster-wide keystones; the stable
        // priority sort in ApplyCastModifiers decides the real order.
        var modifiers = new List<CastModifier>(instance.Attached.OfType<CastModifier>());
        modifiers.AddRange(GlobalModifiers);
        return modifiers;
    }

    private static void ApplyCastModifiers(CastContext context, List<CastModifier> modifiers)
    {
        if (modifiers.Count == 0)
        {
            return;
        }

        // Stable sort by priority, per docs/design/skills.md -> "the ordering
        // problem" — CastModifiers are order-dependent, unlike StatModifiers.
        modifiers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        foreach (CastModifier modifier in modifiers)
        {
            if ((context.Tags & modifier.Condition) != modifier.Condition)
            {
                continue;
            }

            modifier.Apply(context);
        }
    }

    private bool TryPayCosts(List<CostEntry> costs, float lifeFloorFraction, out ResourceType insufficient)
    {
        insufficient = default;
        var pending = new List<(ResourcePool pool, float amount)>();

        foreach (CostEntry cost in costs)
        {
            ResourcePool? pool = _stats.GetPool(cost.Resource);
            if (pool is null)
            {
                // This actor doesn't have this resource at all — nothing to pay.
                // See docs/design/skills.md -> "Resources are per-actor".
                continue;
            }

            float amount = cost.Mode switch
            {
                CostMode.Flat => cost.Amount,
                CostMode.PercentOfMax => pool.Max * cost.Amount / 100f,
                CostMode.PercentOfCurrent => pool.Current * cost.Amount / 100f,
                _ => cost.Amount,
            };

            // docs/design/stats.md's third pass -> ManaCostMultiplier row ("reduced
            // by negative Increased modifiers") and docs/design/affixes.md ->
            // "of Efficiency": the multiplier is base 1.0, so this is a no-op for
            // every skill until an affix like "of Efficiency" adds a negative
            // Increased modifier to it. Not specified as a wiring point by either
            // doc explicitly — this is the one place a mana cost is actually
            // resolved, so it's where the stat has to apply to mean anything.
            if (cost.Resource == ResourceType.Mana)
            {
                amount *= _stats.ManaCostMultiplier.Value;
            }

            float floor = FloorFor(cost.Resource);
            if (cost.Resource == ResourceType.Life)
            {
                floor = Mathf.Max(floor, pool.Max * lifeFloorFraction);
            }
            if (pool.Current - amount < floor)
            {
                insufficient = cost.Resource;
                return false;
            }

            pending.Add((pool, amount));
        }

        // All entries are payable — commit them together. This loop cannot fail:
        // the check above already validated every entry against its floor.
        foreach ((ResourcePool pool, float amount) in pending)
        {
            pool.Current -= amount;
        }

        return true;
    }

    /// <summary>Mana floor 0, Life floor 1 — a skill can never kill its own
    /// caster (docs/design/skills.md -> "A skill can never kill its own caster").</summary>
    private static float FloorFor(ResourceType type) => type switch
    {
        ResourceType.Life => 1f,
        _ => 0f,
    };

    private float EffectiveCooldown(SkillInstance instance)
    {
        float recovery = _stats.CooldownRecovery.Value;
        return recovery > 0f ? instance.Definition.Cooldown / recovery : instance.Definition.Cooldown;
    }

    private float EffectiveUseTime(SkillDefinition definition)
    {
        SpeedStatKind kind = SkillSpeed.ResolveSpeedStat(definition.Tags);
        float speed = kind switch
        {
            SpeedStatKind.Attack => _stats.AttackSpeed.Value,
            SpeedStatKind.Spell => _stats.CastSpeed.Value,
            _ => 1f,
        };

        return speed > 0f ? definition.BaseUseTime / speed : definition.BaseUseTime;
    }

    private void TickCharge(SkillInstance instance, float dt)
    {
        if (instance.ChargesAvailable >= instance.Definition.MaxCharges)
        {
            return;
        }

        instance.RechargeTimer -= dt;
        if (instance.RechargeTimer <= 0f)
        {
            instance.ChargesAvailable++;
            instance.RechargeTimer = instance.ChargesAvailable < instance.Definition.MaxCharges
                ? EffectiveCooldown(instance)
                : 0f;
        }
    }

    private void Execute(SkillInstance instance, CastContext context)
    {
        switch (instance.Definition)
        {
            case ProjectileSkillDefinition:
                ExecuteProjectile(instance, context);
                break;
            case AreaSkillDefinition area:
                ExecuteArea(instance, area, context);
                break;
            case OrbitSkillDefinition orbit:
                ExecuteOrbit(instance, orbit, context);
                break;
            case MeleeSkillDefinition melee:
                ExecuteMelee(instance, melee, context);
                break;
            default:
                GD.PushWarning($"SkillUser: no executor implemented for {instance.Definition.GetType().Name}");
                break;
        }
    }

    /// <summary>
    /// Resolved once per cast: the skill supplies the base, the caster supplies
    /// the modifier stack (docs/design/skills.md -> "How skills interact with
    /// the stat system"), then the instance's level and any per-cast
    /// More/Less from supports multiply on top.
    /// </summary>
    private HitProfile BuildHitProfile(SkillInstance instance, CastContext context, float knockback = 0f)
    {
        IReadOnlyList<StatModifier> damageMods = _stats.GetModifiers(new ModifierQuery(StatId.Damage, context.Tags));
        float damage = Stat.ComputeFormula(instance.Definition.BaseDamage, damageMods, instance.Definition.AddedDamageEffectiveness)
            * instance.LevelDamageMultiplier
            * context.DamageMultiplier;

        return new HitProfile
        {
            Damage = damage,
            Type = DamageTypeResolution.Resolve(context.Tags),
            Source = _caster,
            Faction = context.CasterFaction,
            Tags = context.Tags,
            IgniteChance = _stats.Get(StatId.IgniteChance).Value + context.IgniteChanceBonus,
            ShockChance = _stats.Get(StatId.ShockChance).Value + context.ShockChanceBonus,
            CanInflictAilments = context.CanInflictAilments,
            IgniteDamageMultiplier = context.IgniteDamageMultiplier,
            KnockbackForce = knockback,
        };
    }

    private void ExecuteArea(SkillInstance instance, AreaSkillDefinition definition, CastContext context)
    {
        float radius = definition.Radius * context.AreaMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.AreaOfEffect).Value);
        Vector2 centre = _caster.GlobalPosition;
        if (definition.AtTarget)
        {
            Vector2 offset = context.TargetPoint - _caster.GlobalPosition;
            centre = _caster.GlobalPosition + offset.LimitLength(definition.MaxRange);
        }

        HitProfile hit = BuildHitProfile(instance, context, definition.Knockback);
        AreaBurst.Spawn(GetTree().CurrentScene, centre, radius, definition.Delay, definition.Tint, hit);
    }

    private void ExecuteMelee(SkillInstance instance, MeleeSkillDefinition definition, CastContext context)
    {
        float area = context.AreaMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.AreaOfEffect).Value);
        HitProfile hit = BuildHitProfile(instance, context, definition.Knockback);
        Vector2 aim = context.AimDirection == Vector2.Zero ? Vector2.Right : context.AimDirection.Normalized();

        if (definition.Line)
        {
            float length = definition.Range * area;
            LineStrike lane = LineStrike.Spawn(GetTree().CurrentScene, _caster.GlobalPosition, aim, length, definition.LineWidth * Mathf.Sqrt(area), definition.LineDelay, hit);
            lane.Impacted += () => ReportHit(_caster, hit.Damage);
            return;
        }

        for (int i = 0; i < Mathf.Max(1, definition.Strikes); i++)
        {
            if (i == 0)
            {
                Strike(definition, hit, aim, area);
                continue;
            }

            SceneTreeTimer timer = GetTree().CreateTimer(0.09 * i, processAlways: false, processInPhysics: true);
            timer.Timeout += () =>
            {
                if (IsInstanceValid(this) && IsInstanceValid(_caster))
                {
                    Strike(definition, hit, aim, area);
                }
            };
        }
    }

    /// <summary>One melee swing: every hostile inside the arc takes the hit.</summary>
    private void Strike(MeleeSkillDefinition definition, HitProfile hit, Vector2 aim, float area)
    {
        float range = definition.Range * area;
        float halfArc = Mathf.DegToRad(definition.ArcDegrees) / 2f;
        Vector2 origin = _caster.GlobalPosition;
        SlashEffect.Spawn(GetTree().CurrentScene, origin, aim, range, definition.ArcDegrees, definition.Tint);
        Core.Sfx.Play(this, "hit", -4f);
        foreach (Health health in CombatQuery.HostilesInRadius(GetTree(), origin, range, hit.Faction, 16f))
        {
            Vector2 to = health.GetParent<Node2D>().GlobalPosition - origin;
            if (to.LengthSquared() > 1f && Mathf.Abs(aim.AngleTo(to)) > halfArc + 0.15f)
            {
                continue;
            }

            health.TakeDamage(hit.BuildPayload(to.Normalized()));
            ReportHit(health.GetParent<Node2D>(), hit.Damage);
        }
    }

    private void ExecuteOrbit(SkillInstance instance, OrbitSkillDefinition definition, CastContext context)
    {
        float area = context.AreaMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.AreaOfEffect).Value);
        var blades = new OrbitBlades
        {
            Anchor = _caster,
            Count = definition.Count + context.ExtraCount + Mathf.RoundToInt(_stats.Get(StatId.ExtraProjectiles).Value),
            Radius = definition.Radius * area,
            Duration = definition.Duration * context.DurationMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.SkillDuration).Value),
            AngularSpeed = definition.AngularSpeed,
            HitRadius = definition.HitRadius * Mathf.Sqrt(area),
            HitCooldown = definition.HitCooldown,
            Hit = BuildHitProfile(instance, context, 60f),
            Frames = definition.Frames,
        };
        GetTree().CurrentScene.AddChild(blades);
        blades.GlobalPosition = _caster.GlobalPosition;
    }

    private void ExecuteProjectile(SkillInstance instance, CastContext context)
    {
        if (ProjectileScene is null || context.Projectile is null)
        {
            GD.PushWarning("SkillUser: ProjectileSkillDefinition cast with no ProjectileScene assigned.");
            return;
        }

        ApplyProjectileStats(context.Projectile);
        HitProfile hit = BuildHitProfile(instance, context, 40f);

        foreach (ProjectileSpawnRequest request in GenerateProjectileSpawnRequests(context))
        {
            var projectile = ProjectileScene.Instantiate<Projectile>();
            GetTree().CurrentScene.AddChild(projectile);
            projectile.GlobalPosition = context.Origin + request.Direction * request.ForwardOffset;
            projectile.Initialize(this, context, request.Direction, hit.Damage);
            projectile.ApplyHitProfile(hit, instance.Definition.Frames);
        }
    }

    /// <summary>
    /// The run layer's actor-level projectile stats (docs/design/progression.md
    /// -> "Run stats"): extra projectiles/pierce/chains and projectile speed,
    /// folded into the per-cast params after supports have run. Zero-valued for
    /// every actor that never takes a run upgrade, so the sandbox and the
    /// existing tests are unaffected.
    /// </summary>
    private void ApplyProjectileStats(ProjectileParams p)
    {
        int authoredCount = p.Count;
        p.Count += Mathf.RoundToInt(_stats.Get(StatId.ExtraProjectiles).Value);
        p.Pierce += Mathf.RoundToInt(_stats.Get(StatId.ExtraPierce).Value);
        p.Chains += Mathf.RoundToInt(_stats.Get(StatId.ExtraChains).Value);
        p.Speed *= Mathf.Max(0.1f, _stats.Get(StatId.ProjectileSpeed).Value);
        if (p.Count > authoredCount)
        {
            EnsureSpreadForCount(p);
        }
    }

    /// <summary>
    /// An authored 0° spread with Count &gt; 1 is a deliberate "slug" (see
    /// <see cref="ProjectileSkillDefinition.SpreadDegrees"/>), but a skill that
    /// <i>gained</i> projectiles from the run layer should fan out, not stack.
    /// Only ever widens; never narrows an authored spread.
    /// </summary>
    public static void EnsureSpreadForCount(ProjectileParams p)
    {
        if (p.Count <= 1 || Mathf.Abs(p.SpreadDegrees) >= 360f)
        {
            return;
        }

        p.SpreadDegrees = Mathf.Max(p.SpreadDegrees, Mathf.Min(90f, 12f * (p.Count - 1)));
    }

    /// <summary>
    /// Spawns one fork child of <paramref name="parent"/>. Called from inside an
    /// Area2D overlap signal, so the tree insertion has to be deferred — but the
    /// child is initialized <i>synchronously</i>, while the parent is still alive.
    /// Deferring the initialization too would read a parent that
    /// <see cref="Node.QueueFree"/> may already have collected.
    /// </summary>
    public void SpawnForkDeferred(Projectile parent, Vector2 direction, Vector2 position)
    {
        if (ProjectileScene is null)
        {
            return;
        }

        var child = ProjectileScene.Instantiate<Projectile>();
        child.InitializeFork(parent, direction);
        CallDeferred(nameof(AttachProjectile), child, position);
    }

    private void AttachProjectile(Projectile projectile, Vector2 position)
    {
        GetTree().CurrentScene.AddChild(projectile);
        projectile.GlobalPosition = position;
    }

    /// <summary>
    /// Fans <see cref="ProjectileParams.Count"/> projectiles across
    /// <see cref="ProjectileParams.SpreadDegrees"/>, centred on the aim
    /// direction. Adding this capability was a change to this method alone —
    /// the caller already consumed a <see cref="List{ProjectileSpawnRequest}"/>,
    /// which is exactly what docs/design/skills.md -> "seam 6 step 6" was for.
    ///
    /// Chains, bounces, homing, size and on-hit-explode remain authored-but-inert.
    /// </summary>
    private static List<ProjectileSpawnRequest> GenerateProjectileSpawnRequests(CastContext context)
    {
        ProjectileParams p = context.Projectile!;
        int count = Mathf.Max(1, p.Count);
        var requests = new List<ProjectileSpawnRequest>(count);

        if (count == 1)
        {
            requests.Add(new ProjectileSpawnRequest(context.AimDirection, p.SpawnOffset));
            return requests;
        }

        float baseAngle = context.AimDirection.Angle();

        // A full circle divides by Count (else the first and last projectile
        // land on top of each other); a cone spans edge to edge, so it divides
        // by Count - 1. See SpreadDegrees' doc comment.
        bool isFullCircle = Mathf.Abs(p.SpreadDegrees) >= 360f;
        float spread = Mathf.DegToRad(p.SpreadDegrees);
        float step = isFullCircle ? spread / count : spread / (count - 1);
        float start = isFullCircle ? baseAngle : baseAngle - spread * 0.5f;

        for (int i = 0; i < count; i++)
        {
            Vector2 direction = Vector2.Right.Rotated(start + step * i);
            requests.Add(new ProjectileSpawnRequest(direction, p.SpawnOffset));
        }

        return requests;
    }
}
