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
    /// <summary>LMB, RMB, Q, E, R, T and Space (<see cref="SkillSlotId"/>).</summary>
    public const int SlotCount = 7;

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
        Auras = new AuraController(this, _caster, _stats);

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

        Auras.Tick(dt);
    }

    /// <summary>The auras' passive and active effects (docs/design/skills.md -> "Auras").</summary>
    public AuraController Auras { get; private set; } = null!;

    /// <summary>Totems this caster has up (docs/design/skills.md -> "Totems"), oldest first.</summary>
    public List<Totem> Totems { get; } = new();

    /// <summary>Whether the given slot currently holds the built-in Move
    /// sentinel — asked by <see cref="Player"/> so it doesn't need to know
    /// loadout internals to decide whether to path-follow on LMB.</summary>
    public bool SlotIsBuiltInMove(SkillSlotId slot) => _loadout[(int)slot].BuiltIn == BuiltInAction.Move;

    /// <summary>Equips a skill instance into a slot, replacing whatever was
    /// there (including a BuiltInAction sentinel).</summary>
    public void EquipSkill(SkillSlotId slot, SkillInstance instance)
    {
        _loadout[(int)slot] = new LoadoutSlot { Skill = instance };
        Auras?.Refresh();
        SkillEquipped?.Invoke(instance);
    }

    /// <summary>Empties a slot entirely (no skill, no built-in).</summary>
    public void ClearSlot(SkillSlotId slot)
    {
        _loadout[(int)slot] = new LoadoutSlot();
        Auras?.Refresh();
        SkillEquipped?.Invoke(null);
    }

    /// <summary>Puts the built-in Move occupant back into a slot (LMB click-to-move).</summary>
    public void RestoreMove(SkillSlotId slot)
    {
        _loadout[(int)slot] = new LoadoutSlot { BuiltIn = BuiltInAction.Move };
        Auras?.Refresh();
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
        if (definition is TotemSkillDefinition && instance.Socketed is null)
        {
            // An empty totem has nothing to cast: don't spend mana on a bare pole.
            return CastFailure.SlotEmpty();
        }

        CastContext context = ResolveContext(instance, aimDirection, targetPoint);

        if (!_stats.IgnoresResourceCosts && !TryPayCosts(context.Costs, context.LifeCostFloorFraction, context.CostMultiplier, out ResourceType insufficientResource))
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

        Execute(instance, context, _caster);
        if (definition is not (AuraSkillDefinition or MovementSkillDefinition or TotemSkillDefinition))
        {
            ScheduleRepeats(instance, context);
        }

        EmitSignal(SignalName.CastFired, (int)slotId);
        return null;
    }

    /// <summary>The cast context of one use of <paramref name="instance"/>: the definition's
    /// numbers with the gem's supports, tree and the caster's keystones applied. Also what
    /// the auras read their passive numbers from.</summary>
    public CastContext ResolveContext(SkillInstance instance, Vector2 aimDirection, Vector2? targetPoint = null, Node2D? anchor = null, IEnumerable<CastModifier>? extra = null)
    {
        CastContext context = BuildCastContext(instance.Definition, aimDirection, anchor ?? _caster);
        context.CostMultiplier = instance.LevelCostMultiplier;
        if (targetPoint is { } point)
        {
            context.TargetPoint = point;
        }

        // Seam 6: collect -> apply, in priority order.
        List<CastModifier> modifiers = CollectCastModifiers(instance, context);
        if (extra is not null)
        {
            modifiers.AddRange(extra);
        }

        ApplyCastModifiers(context, modifiers);
        return context;
    }

    /// <summary>
    /// A cast that comes from something other than the caster's own hands — a totem
    /// (docs/design/skills.md -> "Totems"): the skill fires from <paramref name="anchor"/>,
    /// with the anchor's own <paramref name="extra"/> modifiers (the totem's tree) on top of
    /// the gem's, costs nothing and leaves the gem's own timers alone.
    /// </summary>
    public void CastFrom(SkillInstance instance, Node2D anchor, Vector2 aimDirection, Vector2 targetPoint, IEnumerable<CastModifier> extra, float damageMultiplier)
    {
        if (instance.Definition is AuraSkillDefinition or MovementSkillDefinition or TotemSkillDefinition)
        {
            return;
        }

        CastContext context = ResolveContext(instance, aimDirection, targetPoint, anchor, extra);
        context.Tags |= SkillTags.Totem;
        context.DamageMultiplier *= damageMultiplier;
        Execute(instance, context, anchor);
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

    private CastContext BuildCastContext(SkillDefinition definition, Vector2 aimDirection, Node2D anchor)
    {
        var context = new CastContext(_caster, _stats, _stats.Faction, anchor.GlobalPosition, aimDirection)
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

        if (definition is AuraSkillDefinition aura)
        {
            context.Aura = new AuraParams();
            AuraCatalog.Seed(aura.Kind, context.Aura);
        }

        return context;
    }

    private List<CastModifier> CollectCastModifiers(SkillInstance instance, CastContext context)
    {
        // Per-skill supports first, then caster-wide keystones; the stable
        // priority sort in ApplyCastModifiers decides the real order.
        var modifiers = new List<CastModifier>(instance.Attached.OfType<CastModifier>());
        modifiers.AddRange(instance.TreeModifiers);
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

    private bool TryPayCosts(List<CostEntry> costs, float lifeFloorFraction, float costMultiplier, out ResourceType insufficient)
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

            // The skill's level (a stronger gem costs more) and its upgrade tree.
            amount *= Mathf.Max(0f, costMultiplier);

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

    private void Execute(SkillInstance instance, CastContext context) => Execute(instance, context, _caster);

    private void Execute(SkillInstance instance, CastContext context, Node2D anchor)
    {
        switch (instance.Definition)
        {
            case ProjectileSkillDefinition:
                ExecuteProjectile(instance, context);
                break;
            case AreaSkillDefinition area:
                ExecuteArea(instance, area, context, anchor);
                break;
            case OrbitSkillDefinition orbit:
                ExecuteOrbit(instance, orbit, context, anchor);
                break;
            case MeleeSkillDefinition melee:
                ExecuteMelee(instance, melee, context, anchor);
                break;
            case CurseSkillDefinition curse:
                ExecuteCurse(instance, curse, context, anchor);
                break;
            case AuraSkillDefinition aura:
                Auras.Activate(instance, aura, context);
                break;
            case MovementSkillDefinition movement:
                ExecuteMovement(instance, movement, context);
                break;
            case TotemSkillDefinition totem:
                ExecuteTotem(instance, totem, context);
                break;
            case AfflictionSkillDefinition affliction:
                ExecuteAffliction(instance, affliction, context, anchor);
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
    internal HitProfile BuildHitProfile(SkillInstance instance, CastContext context, float knockback = 0f)
    {
        IReadOnlyList<StatModifier> damageMods = _stats.GetModifiers(new ModifierQuery(StatId.Damage, context.Tags));
        float damage = Stat.ComputeFormula(instance.Definition.BaseDamage, damageMods, instance.Definition.AddedDamageEffectiveness)
            * instance.LevelDamageMultiplier
            * context.DamageMultiplier;

        DamageType type = DamageTypeResolution.Resolve(context.Tags);
        // Bloodthirst and the like: physical attacks get the caster's own bleed chance.
        float bleed = context.BleedChance
            + (type == DamageType.Physical && context.Tags.HasFlag(SkillTags.Attack) ? _stats.Get(StatId.BleedChance).Value : 0f);
        return new HitProfile
        {
            Damage = damage,
            Type = type,
            Source = _caster,
            Faction = context.CasterFaction,
            Tags = context.Tags,
            IgniteChance = _stats.Get(StatId.IgniteChance).Value + context.IgniteChanceBonus,
            ShockChance = _stats.Get(StatId.ShockChance).Value + context.ShockChanceBonus,
            FreezeChance = _stats.Get(StatId.FreezeChance).Value + context.FreezeChanceBonus,
            BleedChance = bleed,
            BleedDurationMultiplier = context.BleedDurationMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.BleedDuration).Value),
            BleedStacks = 1 + context.ExtraBleedStacks + Mathf.RoundToInt(_stats.Get(StatId.BleedStacksPerHit).Value),
            IgniteDurationMultiplier = context.IgniteDurationMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.IgniteDuration).Value),
            KillBlastFraction = context.KillBlastFraction,
            KillBlastRadius = context.KillBlastRadius,
            CanInflictAilments = context.CanInflictAilments,
            IgniteDamageMultiplier = context.IgniteDamageMultiplier,
            KnockbackForce = knockback,
            Skill = instance.Definition,
        };
    }

    private void ExecuteArea(SkillInstance instance, AreaSkillDefinition definition, CastContext context, Node2D anchor)
    {
        float radius = definition.Radius * context.AreaMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.AreaOfEffect).Value);
        Vector2 centre = anchor.GlobalPosition;
        if (definition.AtTarget)
        {
            Vector2 offset = context.TargetPoint - anchor.GlobalPosition;
            centre = anchor.GlobalPosition + offset.LimitLength(definition.MaxRange);
        }

        // Knockback is an upgrade, not a default, for skills whose tree grants it (Frost Nova's Gale).
        HitProfile hit = BuildHitProfile(instance, context, definition.Knockback + context.ExtraKnockback);
        AreaBurst.Spawn(GetTree().CurrentScene, centre, radius, definition.Delay, definition.Tint, hit);
    }

    /// <summary>
    /// Curses (docs/design/classes.md -> "The Witch's curses"): every hostile in the circle
    /// at the target point — or the one nearest it, for a single-target curse — gets the
    /// curse for its duration. A harmless ring shows where it landed.
    /// </summary>
    private void ExecuteCurse(SkillInstance instance, CurseSkillDefinition definition, CastContext context, Node2D anchor)
    {
        float radius = definition.Radius * context.AreaMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.AreaOfEffect).Value);
        Vector2 offset = context.TargetPoint - anchor.GlobalPosition;
        Vector2 centre = anchor.GlobalPosition + offset.LimitLength(definition.MaxRange);
        HitProfile hit = BuildHitProfile(instance, context);
        float duration = definition.Duration * context.DurationMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.SkillDuration).Value);

        List<Health> targets = CombatQuery.HostilesInRadius(GetTree(), centre, radius, _stats.Faction);
        if (definition.MaxTargets > 0)
        {
            targets.Sort((a, b) => a.GetParent<Node2D>().GlobalPosition.DistanceSquaredTo(centre).CompareTo(b.GetParent<Node2D>().GlobalPosition.DistanceSquaredTo(centre)));
            int keep = definition.MaxTargets + context.CurseExtraTargets;
            if (targets.Count > keep)
            {
                targets.RemoveRange(keep, targets.Count - keep);
            }
        }

        foreach (Health target in targets)
        {
            Curses.On(target.GetParent())?.Apply(new CurseEffect
            {
                Kind = definition.Kind,
                Duration = duration,
                DamagePerSecond = definition.Kind == CurseKind.Corruption ? hit.Damage : 0f,
                Potency = definition.Potency + context.CursePotencyBonus,
                Spread = context.CurseSpread,
                Vulnerability = context.CurseVulnerability,
                DeathBurst = context.KillBlastFraction,
                Source = _caster,
                Tint = definition.Tint,
            });
        }

        AreaBurst.Spawn(GetTree().CurrentScene, centre, definition.MaxTargets > 0 ? 34f : radius, 0f, definition.Tint, hit, harmless: true);
    }

    private void ExecuteMelee(SkillInstance instance, MeleeSkillDefinition definition, CastContext context, Node2D anchor)
    {
        float area = context.AreaMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.AreaOfEffect).Value);
        HitProfile hit = BuildHitProfile(instance, context, definition.Knockback);
        Vector2 aim = context.AimDirection == Vector2.Zero ? Vector2.Right : context.AimDirection.Normalized();

        if (definition.Line)
        {
            float length = definition.Range * area;
            LineStrike lane = LineStrike.Spawn(GetTree().CurrentScene, anchor.GlobalPosition, aim, length, definition.LineWidth * Mathf.Sqrt(area), definition.LineDelay, hit);
            lane.Impacted += () =>
            {
                if (IsInstanceValid(this) && IsInstanceValid(_caster))
                {
                    ReportHit(_caster, hit.Damage);
                }
            };
            return;
        }

        for (int i = 0; i < Mathf.Max(1, definition.Strikes + context.ExtraCount); i++)
        {
            if (i == 0)
            {
                Strike(definition, hit, aim, area, anchor);
                continue;
            }

            SceneTreeTimer timer = GetTree().CreateTimer(0.09 * i, processAlways: false, processInPhysics: true);
            timer.Timeout += () =>
            {
                if (IsInstanceValid(this) && IsInstanceValid(_caster) && IsInstanceValid(anchor))
                {
                    Strike(definition, hit, aim, area, anchor);
                }
            };
        }
    }

    /// <summary>One melee swing: every hostile inside the arc takes the hit.</summary>
    private void Strike(MeleeSkillDefinition definition, HitProfile hit, Vector2 aim, float area, Node2D anchor)
    {
        float range = definition.Range * area;
        float halfArc = Mathf.DegToRad(definition.ArcDegrees) / 2f;
        Vector2 origin = anchor.GlobalPosition;
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

    private void ExecuteOrbit(SkillInstance instance, OrbitSkillDefinition definition, CastContext context, Node2D anchor)
    {
        float area = context.AreaMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.AreaOfEffect).Value);
        var blades = new OrbitBlades
        {
            Anchor = anchor,
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
        blades.GlobalPosition = anchor.GlobalPosition;
    }

    /// <summary>
    /// Movement skills (docs/design/skills.md -> "The Space slot"): Blink hops to the cursor,
    /// Leap Slam jumps there and slams on landing. The <see cref="Dodge"/> component moves
    /// the body (walls stop it) and grants the invulnerability; an actor without one stays put.
    /// </summary>
    private void ExecuteMovement(SkillInstance instance, MovementSkillDefinition definition, CastContext context)
    {
        if (_caster.GetNodeOrNull<Dodge>("Dodge") is not { } dodge)
        {
            return;
        }

        Vector2 offset = context.TargetPoint - _caster.GlobalPosition;
        float range = definition.Range * context.RangeMultiplier;
        if (offset.Length() < 24f)
        {
            offset = (context.AimDirection == Vector2.Zero ? Vector2.Right : context.AimDirection) * range;
        }

        offset = offset.LimitLength(range);
        float time = Mathf.Max(0.04f, definition.TravelTime);
        float invulnerable = time + (definition.Kind == MovementKind.Leap ? 0.12f : 0.15f) + context.ExtraInvulnerability;
        if (definition.Kind == MovementKind.Blink)
        {
            Core.Sfx.Play(this, "dodge");
            HitProfile wake = BuildHitProfile(instance, context);
            wake.Damage *= Mathf.Max(context.DepartBurst, context.ArriveBurst ? 0.6f : 0f);
            if (context.DepartBurst > 0f)
            {
                AreaBurst.Spawn(GetTree().CurrentScene, _caster.GlobalPosition, 60f * context.AreaMultiplier, 0f, definition.Tint, wake);
            }

            float speed = context.AfterMoveSpeed;
            bool arrive = context.ArriveBurst;
            dodge.Travel(offset, time, invulnerable, definition.Tint, () =>
            {
                if (!IsInstanceValid(this) || !IsInstanceValid(_caster))
                {
                    return;
                }

                if (arrive)
                {
                    AreaBurst.Spawn(GetTree().CurrentScene, _caster.GlobalPosition, 60f * context.AreaMultiplier, 0f, new Color(0.55f, 0.85f, 1f), wake);
                }

                if (speed > 0f)
                {
                    var source = new object();
                    _stats.MovementSpeed.AddModifier(new StatModifier(ModifierType.More, speed, source));
                    GetTree().CreateTimer(3.0, processAlways: false, processInPhysics: true).Timeout += () =>
                    {
                        if (IsInstanceValid(this))
                        {
                            _stats.MovementSpeed.RemoveModifiersFromSource(source);
                        }
                    };
                }
            });
            return;
        }

        HitProfile hit = BuildHitProfile(instance, context, definition.Knockback + context.ExtraKnockback);
        float radius = definition.LandRadius * context.AreaMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.AreaOfEffect).Value);
        int aftershocks = context.Aftershocks;
        Color tint = definition.Tint;
        dodge.Travel(offset, time, invulnerable, tint, () =>
        {
            if (!IsInstanceValid(this) || !IsInstanceValid(_caster) || radius <= 0f)
            {
                return;
            }

            Core.Sfx.Play(this, "hit");
            AreaBurst.Spawn(GetTree().CurrentScene, _caster.GlobalPosition, radius, 0f, tint, hit);
            ReportHit(_caster, hit.Damage);
            for (int i = 1; i <= aftershocks; i++)
            {
                HitProfile echo = hit.Clone();
                echo.Damage *= 0.6f;
                AreaBurst.Spawn(GetTree().CurrentScene, _caster.GlobalPosition, radius * 1.15f, 0.35f * i, tint, echo);
            }
        });
    }

    /// <summary>Totems (docs/design/skills.md -> "Totems"): a pole at the cursor that casts
    /// the socketed skill. Placing one past the limit removes the oldest.</summary>
    private void ExecuteTotem(SkillInstance instance, TotemSkillDefinition definition, CastContext context)
    {
        if (instance.Socketed is not { } inner)
        {
            return;
        }

        Totems.RemoveAll(t => !IsInstanceValid(t) || t.IsQueuedForDeletion());
        int max = Mathf.Max(1, definition.MaxTotems + context.ExtraTotems);
        while (Totems.Count >= max)
        {
            Totems[0].Expire();
            Totems.RemoveAt(0);
        }

        Vector2 offset = (context.TargetPoint - _caster.GlobalPosition).LimitLength(definition.MaxRange);
        var totem = new Totem
        {
            Owner = this,
            Skill = inner,
            Modifiers = instance.TreeModifiers,
            Lifetime = definition.Duration * context.DurationMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.SkillDuration).Value),
            Interval = Mathf.Max(0.15f, definition.CastInterval * context.UseTimeMultiplier),
            Reach = definition.Reach * context.RangeMultiplier,
            DamageEffectiveness = definition.DamageEffectiveness * instance.LevelDamageMultiplier,
            Tint = definition.Tint,
            Faction = _stats.Faction,
        };
        GetTree().CurrentScene.AddChild(totem);
        totem.GlobalPosition = _caster.GlobalPosition + offset;
        Totems.Add(totem);
        Core.Sfx.Play(this, "hit", -6f);
    }

    /// <summary>
    /// Afflictions (docs/design/skills.md -> "Oil and detonations"): every hostile in the
    /// circle at the target point is doused in oil, or has its ignite / bleed stacks
    /// detonated — all the damage they had left lands at once.
    /// </summary>
    private void ExecuteAffliction(SkillInstance instance, AfflictionSkillDefinition definition, CastContext context, Node2D anchor)
    {
        float radius = definition.Radius * context.AreaMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.AreaOfEffect).Value);
        Vector2 offset = context.TargetPoint - anchor.GlobalPosition;
        Vector2 centre = anchor.GlobalPosition + offset.LimitLength(definition.MaxRange);
        HitProfile hit = BuildHitProfile(instance, context);
        float level = instance.LevelDamageMultiplier;
        float potency = definition.Kind == AfflictionKind.Oil
            ? (definition.Potency + context.AfflictionPotency) * (1f + AuraCatalog.EffectPerLevel * (instance.Level - 1))
            : definition.Potency + context.AfflictionPotency;
        AreaBurst.Spawn(GetTree().CurrentScene, centre, radius, 0f, definition.Tint, hit, harmless: true);

        foreach (Health target in CombatQuery.HostilesInRadius(GetTree(), centre, radius, _stats.Faction))
        {
            if (target.GetParent().GetNodeOrNull<Ailments>("Ailments") is not { } ailments)
            {
                continue;
            }

            Node2D body = target.GetParent<Node2D>();
            switch (definition.Kind)
            {
                case AfflictionKind.Oil:
                    ailments.ApplyOil(potency, definition.Duration * context.DurationMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.SkillDuration).Value), context.OilIgniteDuration);
                    if (context.OilSlow > 0f)
                    {
                        Curses.On(body)?.Apply(new CurseEffect { Kind = CurseKind.Time, Duration = definition.Duration * context.DurationMultiplier, Potency = context.OilSlow, Source = _caster, Tint = definition.Tint });
                    }

                    if (hit.Damage > 0f)
                    {
                        target.TakeDamage(hit.BuildPayload(Vector2.Zero));
                    }

                    break;
                case AfflictionKind.Combust:
                case AfflictionKind.Exsanguinate:
                    Detonate(definition.Kind, ailments, target, body, potency, context, hit, level);
                    break;
            }
        }
    }

    private void Detonate(AfflictionKind kind, Ailments ailments, Health target, Node2D body, float potency, CastContext context, HitProfile hit, float level)
    {
        bool fire = kind == AfflictionKind.Combust;
        int stacks = fire ? ailments.IgniteStacks : ailments.BleedStacks;
        if (stacks <= 0)
        {
            return;
        }

        float stored = fire ? ailments.ConsumeIgnites() : ailments.ConsumeBleeds();
        float burst = stored * (1f + potency / 100f) * context.DamageMultiplier * level;
        // The skill's own (small) hit lands too, so an enemy with stacks is also struck.
        target.ApplyResolvedDamage(burst, _caster);
        ReportHit(body, burst);
        Color colour = fire ? new Color(1f, 0.5f, 0.15f) : new Color(0.85f, 0.1f, 0.1f);
        AreaBurst.Spawn(GetTree().CurrentScene, body.GlobalPosition, 26f + Mathf.Min(40f, stacks * 2f), 0f, colour, hit, harmless: true);

        // Stacks kept back (tree): a fraction of the stacks is re-applied, so the next
        // detonation does not start from zero.
        for (int i = 0; i < Mathf.Min(context.StacksKept, stacks); i++)
        {
            float each = stored / stacks;
            if (fire)
            {
                ailments.ApplyIgnite(each, _caster);
            }
            else
            {
                ailments.AddBleed(each, Ailments.BleedDuration, _caster);
            }
        }

        if (context.DetonationSplash <= 0f)
        {
            return;
        }

        var splash = new HitProfile
        {
            Damage = burst * context.DetonationSplash,
            Type = fire ? DamageType.Fire : DamageType.Physical,
            Source = _caster,
            Faction = _stats.Faction,
            Tags = context.Tags | SkillTags.Area,
            CanInflictAilments = true,
            IgniteChance = fire ? 1f : 0f,
            BleedChance = fire ? 0f : 1f,
            Skill = hit.Skill,
        };
        AreaBurst.SpawnOnKill(GetTree().CurrentScene, body.GlobalPosition, 80f * context.AreaMultiplier, colour, splash);
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
