#nullable enable
namespace ArpgGame.Skills;

/// <summary>
/// The equipped-skill slots — Q, E, R, T (Slot1-4), LMB, RMB and Space — per docs/design/skills.md
/// -> "Six slots, uniform semantics". A plain enum rather than a bare int index so
/// call sites read as slot names; <see cref="Skills.SkillUser.SlotCount"/> is the
/// actual count, kept as a separate concern from this naming so a 7th slot is a
/// config change, not a schema change.
/// </summary>
public enum SkillSlotId
{
    Slot1,
    Slot2,
    Slot3,
    Slot4,
    Lmb,
    Rmb,

    /// <summary>Space: a movement skill (Blink, Leap Slam), or the dodge roll when empty —
    /// docs/design/skills.md -> "The Space slot". Never auto-cast in the Survival Rift.</summary>
    Space,
}

/// <summary>
/// The sentinel for "this slot isn't a skill at all, it's a built-in command."
/// See docs/design/skills.md -> "Should Move just *be* a skill?" — Move is kept
/// out of the skill pipeline for now (it's continuous state, not a discrete timed
/// cast), but a loadout slot still needs to represent "Move lives here" so LMB can
/// default to it.
/// </summary>
public enum BuiltInAction
{
    None,
    Move,
}

/// <summary>
/// One loadout slot: a discriminated union of "holds a <see cref="SkillInstance"/>"
/// or "holds a <see cref="BuiltInAction"/> sentinel". Never both at once in
/// practice — <see cref="IsEmpty"/> only reads true when neither is set.
/// </summary>
public sealed class LoadoutSlot
{
    public SkillInstance? Skill { get; set; }

    public BuiltInAction BuiltIn { get; set; } = BuiltInAction.None;

    public bool IsEmpty => Skill is null && BuiltIn == BuiltInAction.None;
}
