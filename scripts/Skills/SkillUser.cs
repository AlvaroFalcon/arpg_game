#nullable enable
using System.Collections.Generic;
using Godot;
using PathToExile.Combat;
using PathToExile.Stats;

namespace PathToExile.Skills;

/// <summary>
/// Layer 3 of the three-layer split (docs/design/skills.md -> "The three
/// layers"): the executor. Owns the loadout, the per-skill use timers, builds the
/// cast context, pays costs, and triggers the cast. A player and an enemy both
/// have one; the only difference is what feeds it — input (see
/// <see cref="PathToExile.PlayerSkillInput"/>) vs AI, which doesn't exist yet.
///
/// This component deliberately knows nothing about input actions, the mouse, or
/// WASD — see the class doc on <see cref="PathToExile.PlayerSkillInput"/> for why
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

    // Seam 6's collect-and-apply stage runs over this list every cast. It is
    // always empty in the first slice — zero CastModifier subclasses exist, and
    // nothing can attach one yet — but the stage itself is real, not a comment,
    // so wiring in the first artifact later touches CollectCastModifiers only.
    private static readonly List<CastModifier> EmptyModifiers = new();

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
    }

    /// <summary>
    /// Attempts to use whatever is in the given slot. Safe to call every physics
    /// frame the input is held (hold-to-repeat, docs/design/skills.md -> "Input
    /// behaviour") — internal per-skill timers decide whether anything actually
    /// happens. Returns null on success, or a typed reason on failure (nothing
    /// consumes the failure yet).
    /// </summary>
    public CastFailure? TryCast(SkillSlotId slotId, Vector2 aimDirection)
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

        // Seam 6: collect -> apply, in priority order, over an empty list today.
        List<CastModifier> modifiers = CollectCastModifiers(instance, context);
        ApplyCastModifiers(context, modifiers);

        if (!_stats.IgnoresResourceCosts && !TryPayCosts(context.Costs, out ResourceType insufficientResource))
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
        instance.UseTimeRemaining = EffectiveUseTime(definition);
        instance.ChargesAvailable--;
        if (instance.RechargeTimer <= 0f)
        {
            instance.RechargeTimer = EffectiveCooldown(instance);
        }

        EmitSignal(SignalName.CastStarted, (int)slotId);

        Execute(definition, context);

        EmitSignal(SignalName.CastFired, (int)slotId);
        return null;
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

    private static List<CastModifier> CollectCastModifiers(SkillInstance instance, CastContext context)
    {
        // Always empty today: no CastModifier subclass exists, and nothing
        // (artifact, tree node, item aspect) can attach to instance.Attached yet.
        return EmptyModifiers;
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

    private bool TryPayCosts(List<CostEntry> costs, out ResourceType insufficient)
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

    private void Execute(SkillDefinition definition, CastContext context)
    {
        switch (definition)
        {
            case ProjectileSkillDefinition:
                ExecuteProjectile(definition, context);
                break;
            default:
                GD.PushWarning($"SkillUser: no executor implemented for {definition.GetType().Name}");
                break;
        }
    }

    private void ExecuteProjectile(SkillDefinition definition, CastContext context)
    {
        if (ProjectileScene is null || context.Projectile is null)
        {
            GD.PushWarning("SkillUser: ProjectileSkillDefinition cast with no ProjectileScene assigned.");
            return;
        }

        // Resolved once per cast: the skill supplies the base, the caster
        // supplies the modifier stack (docs/design/skills.md -> "How skills
        // interact with the stat system").
        IReadOnlyList<StatModifier> damageMods = _stats.GetModifiers(new ModifierQuery(StatId.Damage, context.Tags));
        float damage = Stat.ComputeFormula(definition.BaseDamage, damageMods);

        foreach (ProjectileSpawnRequest request in GenerateProjectileSpawnRequests(context))
        {
            var projectile = ProjectileScene.Instantiate<Projectile>();
            GetTree().CurrentScene.AddChild(projectile);
            projectile.GlobalPosition = context.Origin + request.Direction * request.ForwardOffset;
            projectile.Initialize(this, context, request.Direction, damage);
        }
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
