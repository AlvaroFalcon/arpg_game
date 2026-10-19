#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Skills;
using ArpgGame.Stats;

namespace ArpgGame;

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

    /// <summary>How input feeds the skill user — docs/design/menus.md -> "Controls".</summary>
    public enum CastMode
    {
        /// <summary>The old sandbox: hold a slot key (or mouse button) to repeat.</summary>
        Hold,

        /// <summary>Realities and the hideout: LMB, RMB, 1, 2, 3, 4 each fire their
        /// own slot, aimed at the cursor.</summary>
        Bound,

        /// <summary>The Survival Rift: every equipped skill fires on its own,
        /// auto-aimed (the survivor genre's rule; there is no mana there either).</summary>
        Survival,
    }

    [Export]
    public CastMode Mode = CastMode.Hold;

    /// <summary>Legacy switch kept for callers/tests: true means <see cref="CastMode.Survival"/>.</summary>
    public bool RunMode
    {
        get => Mode == CastMode.Survival;
        set => Mode = value ? CastMode.Survival : CastMode.Hold;
    }

    /// <summary>Tools only (autoplay bot, screenshot capture): cast everything
    /// automatically at the nearest enemy whatever the mode, since they have no
    /// mouse or keyboard.</summary>
    public bool ForceAutoCast { get; set; }

    /// <summary>False suspends all casting (level-up screen, results, death).</summary>
    public bool InputEnabled { get; set; } = true;

    private static readonly SkillSlotId[] AllSlots = { SkillSlotId.Lmb, SkillSlotId.Rmb, SkillSlotId.Slot1, SkillSlotId.Slot2, SkillSlotId.Slot3, SkillSlotId.Slot4 };

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

        if (Mode == CastMode.Survival || ForceAutoCast)
        {
            SurvivalCast();
            return;
        }

        // Co-op host: a remote player's copy casts from that player's buttons and cursor
        // (Net.PlayerCommand), never from this machine's keyboard and mouse.
        Net.PlayerCommand? command = (_caster as Player)?.Command;
        if (command is null && _caster is Player { IsLocal: false })
        {
            return;
        }

        Vector2 aimPoint = command?.Aim ?? _caster.GetGlobalMousePosition();
        Vector2 aimDirection = _caster.GlobalPosition.DirectionTo(aimPoint);

        for (int i = 0; i < Bindings.Length; i++)
        {
            (SkillSlotId slot, string action) = Bindings[i];
            bool pressed = command is not null ? (command.HeldSlots & (1 << i)) != 0 : PollBinding(action);
            if (!pressed)
            {
                continue;
            }

            // Hold-to-repeat (docs/design/skills.md -> "Input behaviour"): called
            // every physics frame the button is held. SkillUser's own per-skill
            // use-time/cooldown gate decides whether a cast actually happens.
            // Bound mode is the same loop with a target point (ground-targeted
            // skills land under the cursor); each button only ever fires its own slot.
            if (Mode == CastMode.Bound)
            {
                _skillUser.TryCast(slot, aimDirection, aimPoint);
            }
            else
            {
                _skillUser.TryCast(slot, aimDirection);
            }
        }
    }

    private bool PollBinding(string action) =>
        _mouseGates.TryGetValue(action, out MouseActionGate? gate) ? gate.Poll() : Input.IsActionPressed(action);

    /// <summary>The skill slots in the bit order of <see cref="Net.PlayerCommand.HeldSlots"/>.</summary>
    public static IEnumerable<SkillSlotId> BoundSlots
    {
        get
        {
            foreach ((SkillSlotId slot, _) in Bindings)
            {
                yield return slot;
            }
        }
    }

    /// <summary>Co-op client in a reality: the skill buttons held now, one bit per
    /// <see cref="BoundSlots"/> entry (sent to the host, which casts).</summary>
    public byte HeldMask()
    {
        int mask = 0;
        for (int i = 0; i < Bindings.Length; i++)
        {
            if (PollBinding(Bindings[i].Action))
            {
                mask |= 1 << i;
            }
        }

        return (byte)mask;
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

    private readonly Dictionary<SkillSlotId, float> _autoTimers = new();
    private readonly Dictionary<SkillSlotId, float> _autoIntervals = new();

    /// <summary>
    /// Seconds between two automatic casts of a skill in the Survival Rift
    /// (docs/design/run.md -> "Cast timers"): slower, heavier skills wait
    /// longer; cast/attack speed and the Quickening card (<see cref="StatId.AutoCastSpeed"/>)
    /// shorten it. Before this every skill fired as fast as its use time allowed,
    /// so a build was "everything, all the time".
    /// </summary>
    public float AutoCastInterval(SkillInstance skill)
    {
        SkillDefinition d = skill.Definition;
        float baseInterval = Mathf.Max(0.5f, d.BaseUseTime * 1.5f + d.Cooldown);
        var stats = _caster.GetNodeOrNull<ActorStats>("Stats");
        float speed = stats is null ? 1f
            : stats.Get(StatId.AutoCastSpeed).Value
              * (d.Tags.HasFlag(SkillTags.Spell) ? stats.CastSpeed.Value : stats.Get(StatId.AttackSpeed).Value);
        return baseInterval / Mathf.Max(0.2f, speed);
    }

    /// <summary>How far a slot is from its next automatic cast (1 = just fired, 0 = ready).</summary>
    public float AutoCastFraction(SkillSlotId slot) =>
        _remoteFractions.TryGetValue(slot, out float remote) ? remote
        : _autoTimers.TryGetValue(slot, out float left) && _autoIntervals.TryGetValue(slot, out float total) && total > 0f
            ? Mathf.Clamp(left / total, 0f, 1f)
            : 0f;

    private readonly Dictionary<SkillSlotId, float> _remoteFractions = new();

    /// <summary>Co-op client: the host runs the casting, so the HUD's cast timers
    /// come from snapshots (one value per slot, in <see cref="AllSlots"/> order).</summary>
    public void SetRemoteFractions(System.ReadOnlySpan<byte> fractions)
    {
        for (int i = 0; i < AllSlots.Length && i < fractions.Length; i++)
        {
            _remoteFractions[AllSlots[i]] = fractions[i] / 255f;
        }
    }

    /// <summary>Host: this player's cast timers, one byte per slot (snapshot).</summary>
    public void WriteFractions(System.Span<byte> into)
    {
        for (int i = 0; i < AllSlots.Length && i < into.Length; i++)
        {
            into[i] = (byte)Mathf.RoundToInt(AutoCastFraction(AllSlots[i]) * 255f);
        }
    }

    public static int SlotCount => AllSlots.Length;

    private void SurvivalCast()
    {
        // The target is only needed when a skill fires: looking it up every frame
        // (a scan of every monster, per player) was the host's biggest cost in co-op.
        Vector2 target = Vector2.Zero;
        Vector2 aim = Vector2.Zero;
        bool aimed = false;

        float dt = (float)GetPhysicsProcessDeltaTime();
        int index = 0;
        foreach (SkillSlotId slot in AllSlots)
        {
            if (_skillUser.GetSkill(slot) is not { } skill)
            {
                continue;
            }

            float interval = AutoCastInterval(skill);
            _autoIntervals[slot] = interval;
            if (!_autoTimers.TryGetValue(slot, out float left))
            {
                // Staggered start: skills take turns instead of firing together.
                left = interval * (0.25f + 0.2f * index);
            }

            index++;
            left -= dt;
            if (left <= 0f && !aimed)
            {
                aimed = true;
                target = AutoAimTarget()?.GlobalPosition ?? (_caster is Player p ? p.AimPoint : _caster.GetGlobalMousePosition());
                aim = _caster.GlobalPosition.DirectionTo(target);
                if (aim == Vector2.Zero)
                {
                    aim = Vector2.Right;
                }
            }

            if (left <= 0f && _skillUser.TryCast(slot, aim, target) is null)
            {
                left = interval;
            }

            _autoTimers[slot] = Mathf.Max(left, 0f);
        }
    }
}
