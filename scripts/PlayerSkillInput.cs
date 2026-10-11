#nullable enable
using System.Collections.Generic;
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

    /// <summary>
    /// Same fix as <see cref="Player"/>'s own <c>_lmbGate</c>, and the same
    /// reason: <c>skill_slot_lmb</c>/<c>skill_slot_rmb</c> are mouse-bound
    /// (project.godot), so a raw <c>Input.IsActionPressed</c> poll cannot tell a
    /// world click from one the inventory UI already consumed — see
    /// <see cref="MouseActionGate"/>'s class doc. Right-click-to-equip in
    /// <see cref="UI.InventoryGridControl"/>/<see cref="UI.EquipmentStripControl"/>
    /// would otherwise also fire whatever skill is bound to RMB in the world
    /// underneath, the same class of bug docs/design/items.md -&gt; "UI must
    /// consume its own input" describes for movement. Slot hotkeys 1-4 are
    /// keyboard-bound and keep polling directly — nothing here grabs keyboard
    /// focus, so there is no GUI-consumption gap for them to fall into.
    /// </summary>
    private readonly Dictionary<string, MouseActionGate> _mouseGates = new()
    {
        ["skill_slot_lmb"] = new MouseActionGate("skill_slot_lmb"),
        ["skill_slot_rmb"] = new MouseActionGate("skill_slot_rmb"),
    };

    private SkillUser _skillUser = null!;
    private Node2D _caster = null!;

    /// <summary>
    /// docs/design/progression.md -> "Controls": in a run, slots 1-4 hold the
    /// build's skills and fire on their own (<see cref="Core.GameSettings.AutoCast"/>)
    /// or while LMB is held; aim follows the cursor or the nearest enemy
    /// (<see cref="Core.GameSettings.AutoAim"/>). Off (the hideout, the old
    /// sandbox), the original hold-a-slot-key behaviour is untouched.
    /// </summary>
    [Export]
    public bool RunMode;

    /// <summary>False suspends all casting (level-up screen, results, death).</summary>
    public bool InputEnabled { get; set; } = true;

    private static readonly SkillSlotId[] RunSlots = { SkillSlotId.Slot1, SkillSlotId.Slot2, SkillSlotId.Slot3, SkillSlotId.Slot4 };

    public override void _Ready()
    {
        _caster = GetParent<Node2D>();
        _skillUser = _caster.GetNode<SkillUser>("SkillUser");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        foreach (MouseActionGate gate in _mouseGates.Values)
        {
            gate.HandleUnhandledInput(@event);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!InputEnabled || (_caster.GetNodeOrNull<Combat.Health>("Health")?.IsDead ?? false))
        {
            return;
        }

        if (RunMode)
        {
            RunModeCast();
            return;
        }

        Vector2 aimDirection = _caster.GlobalPosition.DirectionTo(_caster.GetGlobalMousePosition());

        foreach ((SkillSlotId slot, string action) in Bindings)
        {
            bool pressed = _mouseGates.TryGetValue(action, out MouseActionGate? gate)
                ? gate.Poll()
                : Input.IsActionPressed(action);
            if (!pressed)
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

    /// <summary>
    /// Auto-aim target: the nearest enemy, except that a boss in range wins
    /// unless something is already on top of the player (docs/design/run.md
    /// -> "Controls"). Without the boss preference, auto-aim spends a whole boss
    /// fight shooting the adds between you and it.
    /// </summary>
    private Node2D? AutoAimTarget()
    {
        Vector2 from = _caster.GlobalPosition;
        Node2D? nearest = Combat.CombatQuery.NearestHostile(GetTree(), from, Combat.Faction.Player, 650f, requireSight: true);
        if (nearest is null || nearest.GlobalPosition.DistanceTo(from) < 110f)
        {
            return nearest;
        }

        foreach (Node node in GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup))
        {
            if (node is Enemies.Enemy { IsBoss: true, Dormant: false } boss && !boss.Health.IsDead && boss.GlobalPosition.DistanceTo(from) < 700f
                && (World.FlowField.Current is not { } field || field.HasLineOfSight(from, boss.GlobalPosition)))
            {
                return boss;
            }
        }

        return nearest;
    }

    private void RunModeCast()
    {
        Core.GameSettings? settings = Core.GameSettings.Get(this);
        bool autoCast = settings?.AutoCast ?? true;
        bool autoAim = settings?.AutoAim ?? false;

        Vector2 mouse = _caster.GetGlobalMousePosition();
        Vector2 target = mouse;
        if (autoAim && AutoAimTarget() is { } aimed)
        {
            target = aimed.GlobalPosition;
        }

        Vector2 aim = _caster.GlobalPosition.DirectionTo(target);
        if (aim == Vector2.Zero)
        {
            aim = Vector2.Right;
        }

        bool lmbHeld = _mouseGates["skill_slot_lmb"].Poll();
        for (int i = 0; i < RunSlots.Length; i++)
        {
            bool wants = autoCast || lmbHeld || Input.IsActionPressed(Bindings[i].Action);
            if (wants && _skillUser.GetSkill(RunSlots[i]) is not null)
            {
                _skillUser.TryCast(RunSlots[i], aim, target);
            }
        }
    }
}
