namespace ArpgGame.Skills;

/// <summary>
/// Marker for anything that can attach to a specific equipped
/// <see cref="SkillInstance"/> — a socketed artifact, a per-skill tree node, a
/// bound item aspect, a gem. Zero implementations exist yet; only the empty
/// <see cref="SkillInstance.Attached"/> list is needed for the first slice. See
/// docs/design/skills.md -> "The three things that genuinely are blocking".
/// </summary>
public interface ISkillUpgrade
{
}
