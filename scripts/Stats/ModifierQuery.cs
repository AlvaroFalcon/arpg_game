using ArpgGame.Combat;

namespace ArpgGame.Stats;

/// <summary>
/// Seam 3 from docs/design/skills.md: modifier lookup takes a query object, not a
/// bare property read (<c>caster.Stats.GetModifiers(query)</c>, never
/// <c>caster.Stats.DamageMods</c> directly). The query carries at minimum which
/// stat is being asked about, plus the casting skill's tags for future
/// tag-conditional modifiers.
///
/// The first-slice implementation of <see cref="ActorStats.GetModifiers"/> ignores
/// <see cref="Tags"/> entirely and returns every modifier on the named stat — this
/// struct exists so that adding a filter later is a change inside that one method,
/// not a change to every call site that ever reads a modifier.
/// </summary>
public readonly struct ModifierQuery
{
    public StatId Stat { get; }
    public SkillTags Tags { get; }

    public ModifierQuery(StatId stat, SkillTags tags = SkillTags.None)
    {
        Stat = stat;
        Tags = tags;
    }
}
