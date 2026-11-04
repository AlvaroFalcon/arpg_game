#nullable enable
using Godot;

namespace ArpgGame.Skills;

/// <summary>
/// The Totem archetype (owner's brief, 2026-11-01): places a totem at the cursor that
/// casts the skill <b>socketed</b> into the totem gem (its own gem, with its own level and
/// tree) at the nearest enemy every <see cref="CastInterval"/> seconds, for
/// <see cref="Duration"/> seconds. The totem's tree boosts whatever it holds: elemental
/// damage, forks, cast speed, duration, more totems. Docs: docs/design/skills.md -> "Totems".
/// </summary>
[GlobalClass]
public partial class TotemSkillDefinition : SkillDefinition
{
    [Export]
    public float Duration = 8f;

    /// <summary>The shortest wait between two casts of the socketed skill. The real wait is the
    /// longest of this, the socketed skill's use time and its cooldown (owner, 2026-11-04).</summary>
    [Export]
    public float CastInterval = 0.9f;

    /// <summary>The totem's life, as a share of its owner's maximum life (owner, 2026-11-04:
    /// totems have life and monsters can target them).</summary>
    [Export]
    public float LifeFraction = 0.3f;

    /// <summary>The least life a totem gets, whatever its owner's.</summary>
    [Export]
    public float MinLife = 60f;

    /// <summary>Totems up at once; placing one more removes the oldest.</summary>
    [Export]
    public int MaxTotems = 1;

    /// <summary>How far the totem looks for enemies.</summary>
    [Export]
    public float Reach = 420f;

    /// <summary>Furthest from the caster a totem can be placed.</summary>
    [Export]
    public float MaxRange = 260f;

    /// <summary>The socketed skill deals this much of its damage when a totem casts it.</summary>
    [Export]
    public float DamageEffectiveness = 0.7f;

    [Export]
    public Color Tint = new(0.95f, 0.75f, 0.45f);
}
