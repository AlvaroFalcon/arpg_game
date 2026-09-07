using Godot;
using PathToExile.Skills;

namespace PathToExile;

/// <summary>
/// The "what feeds the skill user" component for a player character. Deliberately
/// separate from <see cref="Skills.SkillUser"/> itself, which knows nothing about
/// input actions, the mouse, or WASD — per docs/design/skills.md -> "The three
/// layers": "Player and enemy both have one; the only difference is what feeds
/// it (input vs AI)." An AI-driven caster later gets its own feeder component
/// that calls <see cref="SkillUser.TryCast"/> directly, with no changes to
/// SkillUser at all.
///
/// Also deliberately separate from <see cref="Player"/> (the movement
/// controller), so that component doesn't grow loadout/input-mapping logic of
/// its own — see the input-rebinding spec in docs/design/skills.md -> "The
/// right-click conflict".
/// </summary>
[GlobalClass]
public partial class PlayerSkillInput : Node
{
    private static readonly (SkillSlotId Slot, string Action)[] Bindings =
    {
        (SkillSlotId.Slot1, "skill_slot_1"),
        (SkillSlotId.Slot2, "skill_slot_2"),
        (SkillSlotId.Slot3, "skill_slot_3"),
        (SkillSlotId.Slot4, "skill_slot_4"),
        (SkillSlotId.Lmb, "skill_slot_lmb"),
        (SkillSlotId.Rmb, "skill_slot_rmb"),
    };

    private SkillUser _skillUser = null!;
    private Node2D _caster = null!;

    public override void _Ready()
    {
        _caster = GetParent<Node2D>();
        _skillUser = _caster.GetNode<SkillUser>("SkillUser");
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector2 aimDirection = _caster.GlobalPosition.DirectionTo(_caster.GetGlobalMousePosition());

        foreach ((SkillSlotId slot, string action) in Bindings)
        {
            if (!Input.IsActionPressed(action))
            {
                continue;
            }

            // Hold-to-repeat (docs/design/skills.md -> "Input behaviour"): called
            // every physics frame the button is held. SkillUser's own per-skill
            // use-time/cooldown gate decides whether a cast actually happens.
            // The failure reason is intentionally discarded — nothing in the
            // first slice consumes it (no UI/sound/flash exists yet).
            _skillUser.TryCast(slot, aimDirection);
        }
    }
}
