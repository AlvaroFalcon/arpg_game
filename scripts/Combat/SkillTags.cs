using System;

namespace PathToExile.Combat;

/// <summary>
/// Declarative labels for filtering — "what modifiers may grab this skill" — as
/// opposed to a skill's *archetype*, which decides which executor runs it. See
/// docs/design/skills.md -> "Skill identity: archetype vs tags".
///
/// Represented as a [Flags] enum rather than strings: fast, typo-proof, editor
/// checkboxes. Trade-off (accepted): capped at 64 values, and adding one costs a
/// recompile. For a solo project that's the better trade; the doc calls out that
/// getting this list wrong is cheap to fix (it's just re-tagging .tres files).
///
/// `Attack` and `Spell` are *structural* — docs/design/skills.md ->
/// "Attack speed and cast speed (tag-derived)" derives which speed stat scales a
/// skill's use time directly from these two tags, so re-tagging a skill between
/// them changes its behaviour, not just its filtering. Every other tag here is
/// purely descriptive. `Triggered` is a per-cast provenance tag: it is stamped onto
/// a cast context by whatever issued a triggered cast, and must never be authored
/// directly on a SkillDefinition (see "Sharpening 2" in the design doc).
/// </summary>
[Flags]
public enum SkillTags
{
    None = 0,
    Attack = 1 << 0,
    Spell = 1 << 1,
    Projectile = 1 << 2,
    Melee = 1 << 3,
    Area = 1 << 4,
    Duration = 1 << 5,
    Movement = 1 << 6,
    Channelled = 1 << 7,
    Minion = 1 << 8,
    Fire = 1 << 9,
    Cold = 1 << 10,

    /// <summary>
    /// Per-cast provenance only — see the doc note above. Never set on a
    /// SkillDefinition's authored Tags field.
    /// </summary>
    Triggered = 1 << 11,
}
