#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using PathToExile.Combat;

namespace PathToExile.Stats;

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

        Life = new ResourcePool(Get(StatId.MaxLife), Get(StatId.LifeRegen), Get(StatId.MaxLife).Value);
        Mana = HasManaPool
            ? new ResourcePool(Get(StatId.MaxMana), Get(StatId.ManaRegen), Get(StatId.MaxMana).Value)
            : null;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // ClampToMax before Regen: if a modifier changed Max since last tick,
        // fix Current's clamp first so regen never has to reason about a stale
        // ceiling. See ResourcePool.ClampToMax's doc comment.
        Life?.ClampToMax();
        Life?.Regen(dt);

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
        StatId.ManaRegen => 5f,
        StatId.Armour => 0f,
        StatId.FireResistance => 0f,
        StatId.ColdResistance => 0f,
        StatId.LightningResistance => 0f,
        StatId.FirePenetration => 0f,
        StatId.ColdPenetration => 0f,
        StatId.LightningPenetration => 0f,
        StatId.CritChance => 0.05f,
        StatId.CritMultiplier => 1.5f,
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
    /// a bare property read. The implementation still ignores
    /// <see cref="ModifierQuery.Tags"/> and returns every modifier on the named
    /// stat — this pass activates <see cref="StatModifier.Condition"/> as a real,
    /// constructible field (mitigation reads Fire/Cold/Lightning-typed damage) but
    /// deliberately does not wire tag-filtering into this method, since nothing in
    /// this pass's scope constructs a conditional modifier yet. See
    /// <see cref="ModifierQuery"/>'s doc comment.
    /// </summary>
    public IReadOnlyList<StatModifier> GetModifiers(ModifierQuery query)
    {
        return Get(query.Stat).Modifiers;
    }
}
