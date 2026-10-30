#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;

namespace ArpgGame.Stats;

/// <summary>
/// Holds a combat entity's live stats. Entity-agnostic by design: the skill
/// system's hard constraint is that one skill definition serves both players and
/// enemies (docs/design/skills.md -> "What it is"), which means enemies need the
/// same stat surface a player has. This type used to be `PlayerStats`
/// (player-only, `MovementSpeed` only), was generalised in the combat pass (see
/// docs/design/stats.md's resolved "PlayerStats needs to become entity-agnostic"
/// question), and became a keyed registry in the second pass — see below.
///
/// <b>The registry (second pass, docs/design/stats.md -> "The registry"):</b>
/// stats are stored in a <see cref="Dictionary{StatId, Stat}"/> and looked up
/// through <see cref="Get"/>. The named properties below (<c>MovementSpeed</c>,
/// <c>Armour</c>, ...) stay as thin one-line accessors over the registry, so
/// existing call sites like <c>_stats.MovementSpeed.Value</c> remain readable and
/// typo-proof while <see cref="GetModifiers"/> and <see cref="Get"/> can still
/// take a <see cref="StatId"/> as a plain value. Eighteen stats is the number
/// that forced this — see the design doc for the full argument.
/// </summary>
[GlobalClass]
public partial class ActorStats : Node
{
    /// <summary>
    /// This actor's allegiance. Defaults to Neutral rather than either combat side,
    /// so every scene must set it explicitly instead of silently inheriting a bias.
    /// </summary>
    [Export]
    public Faction Faction = Faction.Neutral;

    /// <summary>
    /// Docs/design/skills.md -> "Resources are per-actor, and enemies mostly don't
    /// have them": an explicit opt-out rather than an inferred one, so a missing
    /// resource pool can never be silently confused with "this actor is broke."
    /// Defaults false (players pay costs); an enemy scene should set this true
    /// explicitly in the Inspector rather than relying on a type-level default,
    /// since this single class now serves both sides.
    /// </summary>
    [Export]
    public bool IgnoresResourceCosts;

    [Export]
    public bool HasManaPool;

    /// <summary>
    /// Per-entity-archetype base stat values — docs/design/stats.md -> "Base
    /// values move to a StatBlock resource". Null (or a StatBlock missing an
    /// entry for a given <see cref="StatId"/>) falls back to
    /// <see cref="DefaultBase"/> for that stat.
    /// </summary>
    [Export]
    public StatBlock? Bases;

    private readonly Dictionary<StatId, Stat> _stats = new();

    /// <summary>Every actor has a life pool — anything that can die needs one.
    /// Kept nullable per docs/design/skills.md's note that a missing pool and an
    /// empty pool are different things, even though this is always populated in
    /// <see cref="_Ready"/> today.</summary>
    public ResourcePool? Life { get; private set; }

    /// <summary>
    /// Null when <see cref="HasManaPool"/> is false — most enemies won't have one.
    /// First slice ships exactly one pool (Mana) on the player; see
    /// docs/design/skills.md -> "Why this moved into the first slice".
    /// </summary>
    public ResourcePool? Mana { get; private set; }

    public override void _Ready()
    {
        foreach (StatId id in Enum.GetValues<StatId>())
        {
            _stats[id] = new Stat(DefaultBase(id));
        }

        if (Bases is not null)
        {
            foreach (StatBlockEntry entry in Bases.Entries)
            {
                Get(entry.Id).BaseValue = entry.BaseValue;
            }
        }

        // Before the pools are built, not after: the pools seed Current from
        // MaxLife/MaxMana's *value*, so deriving afterwards would leave every actor
        // spawning at partial life. And before anything else can observe the actor
        // at all — an actor whose attributes only take effect once it first equips
        // something has two different baselines depending on its history, which is
        // the bug this call closes. docs/design/affixes.md -> "Derivations need a
        // recompute hook".
        RecomputeAttributeDerivations();

        Life = new ResourcePool(Get(StatId.MaxLife), Get(StatId.LifeRegen), Get(StatId.MaxLife).Value);
        Mana = HasManaPool
            ? new ResourcePool(Get(StatId.MaxMana), Get(StatId.ManaRegen), Get(StatId.MaxMana).Value)
            : null;
    }

    private Health? _health;

    /// <summary>True while the actor is dead (a downed co-op player): no regeneration, and
    /// nothing that "refills" life (a character level, a Vitality card) brings it back.</summary>
    public bool OwnerDead => (_health ??= GetParent()?.GetNodeOrNull<Health>("Health")) is { IsDead: true };

    public override void _PhysicsProcess(double delta) => Tick(delta);

    /// <summary>One physics step of this component. A monster drives it from its own
    /// <c>_PhysicsProcess</c> (its ActorStats has physics processing turned off): one engine callback
    /// per monster instead of four (docs/design/performance.md -> "Frame budget pass", I5).</summary>
    public void Tick(double delta)
    {
        float dt = (float)delta;

        // ClampToMax before Regen: if a modifier changed Max since last tick,
        // fix Current's clamp first so regen never has to reason about a stale
        // ceiling. See ResourcePool.ClampToMax's doc comment.
        Life?.ClampToMax();
        // The dead don't regenerate: a downed co-op player (docs/design/multiplayer.md ->
        // "Deaths") whose life refilled while down looked as if they had got back up at
        // once, and the revive then dropped them to half (owner's report 2026-10-23).
        if (!OwnerDead)
        {
            Life?.Regen(dt);
        }

        Mana?.ClampToMax();
        Mana?.Regen(dt);
    }

    /// <summary>
    /// The default base value for a stat with no entry in <see cref="Bases"/>.
    /// docs/design/stats.md -> "The stat list after this pass" is the single
    /// source of truth for these eighteen numbers.
    /// </summary>
    public static float DefaultBase(StatId id) => id switch
    {
        StatId.MovementSpeed => 130f,
        StatId.Damage => 0f,
        StatId.AttackSpeed => 1f,
        StatId.CastSpeed => 1f,
        StatId.CooldownRecovery => 1f,
        StatId.MaxLife => 50f,
        StatId.LifeRegen => 0f,
        StatId.MaxMana => 50f,
        StatId.ManaRegen => 1.5f,
        StatId.Armour => 0f,
        StatId.FireResistance => 0f,
        StatId.ColdResistance => 0f,
        StatId.LightningResistance => 0f,
        StatId.FirePenetration => 0f,
        StatId.ColdPenetration => 0f,
        StatId.LightningPenetration => 0f,
        StatId.CritChance => 0.05f,
        StatId.CritMultiplier => 1.5f,

        // Third pass (docs/design/stats.md -> "The stat list after this pass").
        // Attributes base 10 each, per docs/design/affixes.md -> "Attributes".
        StatId.Strength => 10f,
        StatId.Dexterity => 10f,
        StatId.Intelligence => 10f,
        StatId.ManaCostMultiplier => 1f,
        StatId.ThornsDamage => 0f,

        // Fourth pass (docs/design/progression.md -> "Run stats"). The
        // multiplier-shaped ones are base 1.0 so "no bonuses" is a no-op, the
        // same convention ManaCostMultiplier already uses.
        StatId.DamageTaken => 1f,
        StatId.PickupRadius => 80f,
        StatId.ExperienceGain => 1f,
        StatId.AreaOfEffect => 1f,
        StatId.ProjectileSpeed => 1f,
        StatId.DodgeCharges => 2f,
        StatId.DodgeRecovery => 1f,
        StatId.ExtraProjectiles => 0f,
        StatId.IgniteChance => 0f,
        StatId.ShockChance => 0f,
        StatId.SkillDuration => 1f,
        StatId.LifeOnKill => 0f,
        StatId.ExtraChains => 0f,
        StatId.ExtraPierce => 0f,
        StatId.MaxStamina => 100f,
        StatId.StaminaRegen => 22f,
        StatId.AutoCastSpeed => 1f,
        StatId.LifeLeech => 0f,
        StatId.ManaOnKill => 0f,
        StatId.FreezeChance => 0f,
        StatId.ElementalDamageTaken => 1f,
        StatId.PhysicalDamageTaken => 1f,
        StatId.EnergyShield => 0f,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, null),
    };

    /// <summary>The registry lookup every named accessor below is a thin wrapper
    /// over, and the lookup <see cref="GetModifiers"/> uses.</summary>
    public Stat Get(StatId id) => _stats[id];

    public Stat MovementSpeed => Get(StatId.MovementSpeed);

    // Modifier carriers with no meaningful base of their own. BaseValue is unused
    // for Damage (the skill supplies the base at cast time) and is 1.0 for the
    // three multiplier stats so that "no bonuses at all" is a no-op.
    public Stat Damage => Get(StatId.Damage);
    public Stat AttackSpeed => Get(StatId.AttackSpeed);
    public Stat CastSpeed => Get(StatId.CastSpeed);
    public Stat CooldownRecovery => Get(StatId.CooldownRecovery);

    // Defensive layer — docs/design/stats.md -> "Mitigation".
    public Stat Armour => Get(StatId.Armour);
    public Stat FireResistance => Get(StatId.FireResistance);
    public Stat ColdResistance => Get(StatId.ColdResistance);
    public Stat LightningResistance => Get(StatId.LightningResistance);
    public Stat FirePenetration => Get(StatId.FirePenetration);
    public Stat ColdPenetration => Get(StatId.ColdPenetration);
    public Stat LightningPenetration => Get(StatId.LightningPenetration);

    // Critical strikes — docs/design/stats.md -> "Critical strikes".
    public Stat CritChance => Get(StatId.CritChance);
    public Stat CritMultiplier => Get(StatId.CritMultiplier);

    // Attributes — docs/design/stats.md's third pass -> "Attributes and derived
    // bonuses". Real registry stats (affixes roll flat Strength/Dexterity/
    // Intelligence directly), and each also grants a derived bonus elsewhere —
    // see RecomputeAttributeDerivations.
    public Stat Strength => Get(StatId.Strength);
    public Stat Dexterity => Get(StatId.Dexterity);
    public Stat Intelligence => Get(StatId.Intelligence);

    // docs/design/affixes.md -> "of Efficiency": a negative Increased modifier on
    // this base-1.0 multiplier, not a fourth ModifierType. Consumed by
    // Skills.SkillUser's mana-cost resolution.
    public Stat ManaCostMultiplier => Get(StatId.ManaCostMultiplier);

    // docs/design/affixes.md -> "Thorns needs a recursion guard". Consumed by
    // Health.TakeDamage.
    public Stat ThornsDamage => Get(StatId.ThornsDamage);

    /// <summary>Looks up the resource pool for a cost/resource type. Null means
    /// this actor simply doesn't have that resource (see the class doc above).</summary>
    public ResourcePool? GetPool(ResourceType type) => type switch
    {
        ResourceType.Life => Life,
        ResourceType.Mana => Mana,
        ResourceType.Energy => null, // no actor allocates an Energy pool yet
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>
    /// Seam 3 (docs/design/skills.md): modifier lookup by query object rather than
    /// a bare property read. <b>This is the pass that switches seam 4 on for the
    /// first time</b> (docs/design/affixes.md -> "Condition semantics"): every
    /// modifier on the named stat is now filtered through
    /// <see cref="StatModifier.AppliesTo"/> against <see cref="ModifierQuery.Tags"/>
    /// — <c>Condition</c> (all-of) and <c>ConditionAny</c> (any-of) both apply. A
    /// modifier with the default empty Condition/ConditionAny always passes,
    /// which is why every unconditional modifier authored before this pass (there
    /// were none yet) keeps working unchanged.
    /// </summary>
    public IReadOnlyList<StatModifier> GetModifiers(ModifierQuery query)
    {
        IReadOnlyList<StatModifier> all = Get(query.Stat).Modifiers;
        var matching = new List<StatModifier>(all.Count);
        foreach (StatModifier modifier in all)
        {
            if (modifier.AppliesTo(query.Tags))
            {
                matching.Add(modifier);
            }
        }

        return matching;
    }

    /// <summary>
    /// Shared <see cref="StatModifier.Source"/> tag for every modifier this method
    /// grants, so a recompute is "remove by source, re-add" through the existing
    /// <see cref="Stat.RemoveModifiersFromSource"/> — docs/design/stats.md's third
    /// pass -> "Derivations need a recompute hook": "no new mechanism."
    /// </summary>
    private static readonly object AttributeDerivationSource = new();

    /// <summary>
    /// Registry-wide sibling of <see cref="Stat.RemoveModifiersFromSource"/>: an
    /// equipment item can grant modifiers across many different stats (a Rare
    /// item might touch Damage, MaxLife, Armour and Strength all at once), and
    /// milestone 2b's equip system (docs/design/items.md -> "Equipment slots")
    /// needs to strip every one of them on unequip <b>without tracking which
    /// stats the item actually touched</b> — the whole point of tagging
    /// modifiers with the item instance as <see cref="StatModifier.Source"/> is
    /// that the caller never has to remember that. Iterating every registered
    /// stat is deliberately unconditional (a stat the item never touched is just
    /// a no-op <see cref="Stat.RemoveModifiersFromSource"/> call) rather than
    /// trying to be clever about which stats to visit.
    /// </summary>
    public void RemoveModifiersFromSource(object source)
    {
        foreach (Stat stat in _stats.Values)
        {
            stat.RemoveModifiersFromSource(source);
        }
    }

    /// <summary>
    /// Recomputes the derived bonuses attributes grant per
    /// docs/design/affixes.md -> "Attributes" / docs/design/stats.md's third pass
    /// -> "Attributes and derived bonuses": Strength +2 Maximum Life / +1 Armour
    /// per point, Intelligence +2 Maximum Mana / +0.1 Mana Regeneration per point,
    /// Dexterity +0.5% increased Critical Strike Chance per point.
    ///
    /// <b>Dexterity's crit-chance grant is explicitly temporary.</b> It belongs on
    /// Evasion/Accuracy once that mechanic exists (deferred — see
    /// docs/design/stats.md's Open Questions); until then, shipping Dexterity with
    /// no grant at all would make it a dead stat players learn to ignore, so it
    /// borrows crit chance. Nothing else should come to depend on Dexterity
    /// granting crit — when evasion lands, this one branch moves and nothing else
    /// needs to change.
    ///
    /// <see cref="Stat"/> has no change notification, so this cannot be a live
    /// formula — it must be called explicitly at "known moments" (equip, unequip,
    /// any event that changes an attribute stat). <b>Milestone 2b's
    /// <see cref="Items.Equipment"/> is now that caller</b> — it invokes this
    /// after applying (or removing) an item's own modifiers, per
    /// docs/design/affixes.md -> "Derivations need a recompute hook": "an item
    /// granting +20 Strength has to feed its derived life and armour in the same
    /// operation."
    ///
    /// <b>Called from <see cref="_Ready"/> as well as on equip/unequip.</b> An
    /// earlier pass deliberately skipped the _Ready call to keep already-calibrated
    /// test numbers stable, which was the wrong trade: it left base Strength
    /// granting nothing until an actor's first equip, and applying permanently
    /// afterwards, so two otherwise identical actors had different stats depending
    /// on whether either had ever equipped anything. The derived state is the
    /// correct state; the tests were updated instead.
    /// </summary>
    public void RecomputeAttributeDerivations()
    {
        Get(StatId.MaxLife).RemoveModifiersFromSource(AttributeDerivationSource);
        Get(StatId.Armour).RemoveModifiersFromSource(AttributeDerivationSource);
        Get(StatId.MaxMana).RemoveModifiersFromSource(AttributeDerivationSource);
        Get(StatId.ManaRegen).RemoveModifiersFromSource(AttributeDerivationSource);
        Get(StatId.CritChance).RemoveModifiersFromSource(AttributeDerivationSource);

        float strength = Strength.Value;
        float dexterity = Dexterity.Value;
        float intelligence = Intelligence.Value;

        Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.Flat, strength * 2f, AttributeDerivationSource));
        Get(StatId.Armour).AddModifier(new StatModifier(ModifierType.Flat, strength * 1f, AttributeDerivationSource));
        Get(StatId.MaxMana).AddModifier(new StatModifier(ModifierType.Flat, intelligence * 2f, AttributeDerivationSource));
        Get(StatId.ManaRegen).AddModifier(new StatModifier(ModifierType.Flat, intelligence * 0.05f, AttributeDerivationSource));
        Get(StatId.CritChance).AddModifier(new StatModifier(ModifierType.Increased, dexterity * 0.5f, AttributeDerivationSource));
    }
}
