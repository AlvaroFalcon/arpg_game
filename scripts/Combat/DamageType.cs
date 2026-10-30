namespace ArpgGame.Combat;

/// <summary>
/// The four damage types this game supports — docs/design/stats.md -> "Damage
/// types". <see cref="Physical"/> is the untyped default and carries no
/// corresponding <see cref="SkillTags"/> flag; Fire/Cold/Lightning each have a
/// matching tag (see <see cref="DamageTypeResolution"/>). Chaos is deliberately
/// excluded — see the design doc for why (no Energy Shield in this project, so
/// Chaos would be a fifth colour with no mechanical identity).
/// </summary>
public enum DamageType
{
    Physical,
    Fire,
    Cold,
    Lightning,

    /// <summary>The Witch's curses (2026-10-30). Unresisted: nothing in the game
    /// mitigates it but "damage taken" multipliers.</summary>
    Shadow,

    /// <summary>The Paladin's light (2026-10-30). Unresisted, like Shadow.</summary>
    Holy,
}

/// <summary>
/// Derives a hit's <see cref="DamageType"/> from the casting skill's tags.
///
/// Not specified explicitly by docs/design/stats.md's second pass — the doc adds
/// damage types and a payload breakdown but doesn't say where, mechanically, a
/// Fireball's Fire typing actually comes from. This resolves the gap the same
/// way docs/design/skills.md resolves attack/cast speed (see "Attack speed and
/// cast speed (tag-derived)"): rather than add a redundant <c>DamageType</c>
/// field to <c>SkillDefinition</c> that could silently disagree with the skill's
/// own Fire/Cold/Lightning tag, the tag *is* the source of truth for typing the
/// damage it deals. A skill tagged with none of the three is Physical, which by
/// design needs no tag at all.
///
/// Priority order (Fire, then Cold, then Lightning) only matters for a skill
/// carrying more than one elemental tag at once, which no shipped skill does —
/// arbitrary but deterministic, the same posture docs/design/skills.md takes for
/// the "Attack wins" fallback when a skill is mistagged both Attack and Spell.
/// </summary>
public static class DamageTypeResolution
{
    public static DamageType Resolve(SkillTags tags)
    {
        if ((tags & SkillTags.Fire) != 0)
        {
            return DamageType.Fire;
        }

        if ((tags & SkillTags.Cold) != 0)
        {
            return DamageType.Cold;
        }

        if ((tags & SkillTags.Lightning) != 0)
        {
            return DamageType.Lightning;
        }

        if ((tags & SkillTags.Shadow) != 0)
        {
            return DamageType.Shadow;
        }

        if ((tags & SkillTags.Holy) != 0)
        {
            return DamageType.Holy;
        }

        return DamageType.Physical;
    }
}
