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
/// (player-only, `MovementSpeed` only) and was generalised in the combat pass —
/// see docs/design/stats.md's "PlayerStats needs to become entity-agnostic" note.
///
/// Four of the five combat stats are "modifier carrier with no meaningful base of
/// its own" (Damage, AttackSpeed, CastSpeed, CooldownRecovery) — the actor doesn't
/// own a real base for any of them; a skill supplies the base (damage) or the
/// stat is a pure multiplier around 1.0 (the three speed/recovery stats). See
/// docs/design/skills.md -> "How skills interact with the stat system".
///
/// One property per stat, not a generic registry — see docs/design/stats.md's open
/// question on this; still not urgent at five stats.
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
    public float BaseMovementSpeed = 130f;

    public Stat MovementSpeed { get; private set; } = null!;

    [Export]
    public float BaseMaxLife = 50f;

    /// <summary>Every actor has a life pool — anything that can die needs one.</summary>
    public ResourcePool? Life { get; private set; }

    [Export]
    public bool HasManaPool;

    [Export]
    public float BaseMaxMana = 50f;

    [Export]
    public float BaseManaRegenPerSecond = 5f;

    /// <summary>
    /// Null when <see cref="HasManaPool"/> is false — most enemies won't have one.
    /// First slice ships exactly one pool (Mana) on the player; see
    /// docs/design/skills.md -> "Why this moved into the first slice".
    /// </summary>
    public ResourcePool? Mana { get; private set; }

    // Modifier carriers with no meaningful base of their own. BaseValue is unused
    // for Damage (the skill supplies the base at cast time) and is 1.0 for the
    // three multiplier stats so that "no bonuses at all" is a no-op.
    public Stat Damage { get; private set; } = null!;
    public Stat AttackSpeed { get; private set; } = null!;
    public Stat CastSpeed { get; private set; } = null!;
    public Stat CooldownRecovery { get; private set; } = null!;

    public override void _Ready()
    {
        MovementSpeed = new Stat(BaseMovementSpeed);
        Life = new ResourcePool(BaseMaxLife, BaseMaxLife, 0f);
        Mana = HasManaPool ? new ResourcePool(BaseMaxMana, BaseMaxMana, BaseManaRegenPerSecond) : null;
        Damage = new Stat(0f);
        AttackSpeed = new Stat(1f);
        CastSpeed = new Stat(1f);
        CooldownRecovery = new Stat(1f);
    }

    public override void _PhysicsProcess(double delta)
    {
        Mana?.Regen((float)delta);
    }

    /// <summary>Looks up the resource pool for a cost/resource type. Null means
    /// this actor simply doesn't have that resource (see the class doc above).</summary>
    public ResourcePool? GetPool(ResourceType type) => type switch
    {
        ResourceType.Life => Life,
        ResourceType.Mana => Mana,
        ResourceType.Energy => null, // no actor allocates an Energy pool yet
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private Stat? GetStat(StatId id) => id switch
    {
        StatId.MovementSpeed => MovementSpeed,
        StatId.Damage => Damage,
        StatId.AttackSpeed => AttackSpeed,
        StatId.CastSpeed => CastSpeed,
        StatId.CooldownRecovery => CooldownRecovery,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, null),
    };

    /// <summary>
    /// Seam 3 (docs/design/skills.md): modifier lookup by query object rather than
    /// a bare property read. The first-slice implementation ignores
    /// <see cref="ModifierQuery.Tags"/> entirely and returns every modifier on the
    /// named stat — see <see cref="ModifierQuery"/>'s doc comment for why that's
    /// fine for now.
    /// </summary>
    public IReadOnlyList<StatModifier> GetModifiers(ModifierQuery query)
    {
        return GetStat(query.Stat)?.Modifiers ?? Array.Empty<StatModifier>();
    }
}
