#nullable enable
using ArpgGame.Combat;

namespace ArpgGame.Skills;

/// <summary>Which skill goes where — docs/design/skills.md -> "Auras, totems and stacks".</summary>
public static class SkillRules
{
    /// <summary>Movement skills (Blink, Leap Slam) are the only ones the Space slot takes.</summary>
    public static bool FitsSpace(SkillDefinition d) => d is MovementSkillDefinition;

    /// <summary>A totem can hold any skill that hits — not an aura, a movement skill or another totem.</summary>
    public static bool FitsTotem(SkillDefinition d) => d is not (AuraSkillDefinition or MovementSkillDefinition or TotemSkillDefinition);

    /// <summary>Whether the Survival Rift offers it: its skills fire on their own, aimed at
    /// the nearest monster, so movement skills, auras and totems (which need a socket) stay out.</summary>
    public static bool InRift(SkillDefinition d) => FitsTotem(d) && !d.Tags.HasFlag(SkillTags.Movement);
}
