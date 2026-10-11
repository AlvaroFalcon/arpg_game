#nullable enable
using Godot;
using Godot.Collections;
using PathToExile.Combat;

namespace PathToExile.Skills;

/// <summary>
/// Immutable authored data for a skill — layer 1 of the three-layer split in
/// docs/design/skills.md -> "The three layers". Contains only things true about
/// the skill regardless of who casts it. A definition must never reference an
/// input action, the player node, the mouse, a specific collision layer, or a
/// specific faction — the moment it does, it stops being reusable between the
/// player and an enemy.
///
/// Archetype (which executor runs the skill) is expressed as the concrete
/// subclass of this base (e.g. <see cref="ProjectileSkillDefinition"/>), not as a
/// field — see "Skill identity: archetype vs tags". <see cref="Tags"/> lives here
/// on the base because tags are common to every archetype.
///
/// Saved as a `.tres` Resource: authored once, referenced by player loadouts and
/// enemy loadouts alike.
/// </summary>
[GlobalClass]
public abstract partial class SkillDefinition : Resource
{
    [Export]
    public string DisplayName = "";

    /// <summary>
    /// The skill's own base damage. The caster contributes only the modifier
    /// stack applied to this base at cast time — see docs/design/skills.md ->
    /// "How skills interact with the stat system". Meaningless for a definition
    /// with no damage component, but every archetype gets it on the base class so
    /// the formula in <see cref="Skills.SkillUser"/> doesn't need to know the
    /// archetype.
    /// </summary>
    [Export]
    public float BaseDamage;

    /// <summary>
    /// Minimum gap between two consecutive uses of this skill, before
    /// AttackSpeed/CastSpeed scaling. See docs/design/skills.md -> "Rate levers".
    /// </summary>
    [Export]
    public float BaseUseTime = 0.5f;

    /// <summary>
    /// Descriptive + structural labels (see <see cref="SkillTags"/>). Which speed
    /// stat scales this skill's use time is *derived* from whether this contains
    /// `Attack` or `Spell` — there is deliberately no separate `SpeedStat` field.
    /// </summary>
    [Export]
    public SkillTags Tags = SkillTags.None;

    /// <summary>
    /// A list, never a single scalar — see <see cref="CostEntry"/>'s doc comment.
    /// </summary>
    [Export]
    public Array<CostEntry> Costs = new();

    /// <summary>Seconds. 0 = no cooldown. Independent of <see cref="BaseUseTime"/>;
    /// see docs/design/skills.md -> "Cooldown and use time are independent levers".</summary>
    [Export]
    public float Cooldown;

    /// <summary>
    /// Charges are not implemented as a player-facing mechanic in the first slice,
    /// but the field exists now because it changes the *shape* of the runtime
    /// cooldown state on every <see cref="SkillInstance"/> — see
    /// docs/design/skills.md -> "Charges — out of scope, but the state shape is
    /// decided". With MaxCharges = 1, behaviour is identical to a plain cooldown.
    /// </summary>
    [Export]
    public int MaxCharges = 1;

    [Export]
    public MovementDuringUse MovementDuringUse = MovementDuringUse.Free;

    /// <summary>
    /// Reserved per docs/design/skills.md -> "What is explicitly simplified away":
    /// there is no equipment yet, so every skill is spell-like (its own base).
    /// This flag is the placeholder for "derives base from weapon damage" so that
    /// feature doesn't require a schema change later. Not read anywhere yet.
    /// </summary>
    [Export]
    public bool DerivesBaseFromWeapon;

    /// <summary>Card/skill-bar text. Purely presentational.</summary>
    [Export(PropertyHint.MultilineText)]
    public string Description = "";

    /// <summary>Skill-bar / level-up card icon (docs/design/progression.md).
    /// Presentation data, like <see cref="DisplayName"/>: knowing what a skill
    /// looks like is still true regardless of who casts it.</summary>
    [Export]
    public Texture2D? Icon;

    /// <summary>Animated visual for whatever this skill spawns (a projectile, an
    /// orbiting blade). Null keeps the archetype's placeholder polygon.</summary>
    [Export]
    public SpriteFrames? Frames;
}
