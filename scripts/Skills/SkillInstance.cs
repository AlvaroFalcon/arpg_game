using System.Collections.Generic;

namespace ArpgGame.Skills;

/// <summary>
/// The per-owner, mutable wrapper around a <see cref="SkillDefinition"/> that
/// actually lives in a loadout slot — never a bare definition reference. See
/// docs/design/skills.md -> "The three things that genuinely are blocking":
///
/// - <see cref="Definition"/> — the immutable authored data.
/// - <see cref="Attached"/> — empty in the first slice, but every future
///   per-skill upgrade shape (artifact, tree node, bound aspect) lives here.
/// - The charge-shaped cooldown state — not a bare `float cooldownRemaining`,
///   because charges change the *shape* of this state, not just its value. See
///   "Charges — out of scope, but the state shape is decided".
///
/// <see cref="UseTimeRemaining"/> is a fourth, plain-float field. It is
/// deliberately *not* part of the three schema-critical fields above: use time
/// never grows charges (only cooldown recovery does), so there is no shape
/// question for it to get wrong.
/// </summary>
public sealed class SkillInstance
{
    public SkillDefinition Definition { get; }

    public List<ISkillUpgrade> Attached { get; } = new();

    /// <summary>How many uses are currently banked. With MaxCharges = 1 this
    /// behaves exactly like a plain "is it off cooldown" flag.</summary>
    public int ChargesAvailable { get; set; }

    /// <summary>Time remaining until the next charge becomes available. Only
    /// meaningful while <see cref="ChargesAvailable"/> is below
    /// <see cref="SkillDefinition.MaxCharges"/>.</summary>
    public float RechargeTimer { get; set; }

    /// <summary>Time remaining before this skill's use-time gate reopens. See
    /// docs/design/skills.md -> "Cooldown and use time are independent levers".</summary>
    public float UseTimeRemaining { get; set; }

    /// <summary>
    /// docs/design/progression.md -> "Skill levels": picking the same skill
    /// again on level-up raises this. Each level past 1 adds
    /// <see cref="DamagePerLevel"/> more damage. Per-instance, not per-definition,
    /// so an enemy casting the same .tres is unaffected.
    /// </summary>
    public int Level { get; set; } = 1;

    public const float DamagePerLevel = 0.12f;

    /// <summary>Each level past 1 also costs this much more mana (docs/design/skills.md ->
    /// "Mana and cooldowns, 2026-10-30"): a level-20 gem costs about 40% more than a level-1 one.</summary>
    public const float CostPerLevel = 0.02f;

    public float LevelDamageMultiplier => 1f + DamagePerLevel * (Level - 1);

    public float LevelCostMultiplier => 1f + CostPerLevel * (Level - 1);

    /// <summary>The skill's upgrade-tree nodes as cast transforms (docs/design/skills.md ->
    /// "Skill upgrade trees"), applied with the supports on every cast.</summary>
    public List<CastModifier> TreeModifiers { get; } = new();

    public SkillInstance(SkillDefinition definition)
    {
        Definition = definition;
        ChargesAvailable = definition.MaxCharges;
        RechargeTimer = 0f;
        UseTimeRemaining = 0f;
    }
}
