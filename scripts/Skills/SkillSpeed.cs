using PathToExile.Combat;

namespace PathToExile.Skills;

public enum SpeedStatKind
{
    Attack,
    Spell,
    None,
}

/// <summary>
/// One small pure function, called by the skill user when it computes a skill's
/// use timer. Deliberately not in any executor — see docs/design/skills.md ->
/// "Attack speed and cast speed (tag-derived)": "There is no SpeedStat field on
/// the definition." Which stat scales a skill is derived from its Attack/Spell
/// tags so the tag a player reads and the stat that actually applies can never
/// silently disagree.
/// </summary>
public static class SkillSpeed
{
    /// <summary>
    /// Attack -> AttackSpeed, else Spell -> CastSpeed, else no scaling. Tagged
    /// both is an authoring error (a genuine hybrid should be a third stat, not an
    /// overlap) — the deterministic runtime fallback if a bad asset ships anyway
    /// is "Attack wins", per the design doc.
    /// </summary>
    public static SpeedStatKind ResolveSpeedStat(SkillTags tags)
    {
        bool attack = (tags & SkillTags.Attack) != 0;
        bool spell = (tags & SkillTags.Spell) != 0;

        if (attack)
        {
            return SpeedStatKind.Attack;
        }

        if (spell)
        {
            return SpeedStatKind.Spell;
        }

        return SpeedStatKind.None;
    }
}
