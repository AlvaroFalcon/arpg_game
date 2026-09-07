using Godot;
using PathToExile.Skills;

namespace PathToExile.Tests;

/// <summary>
/// Headless functional smoke test for player movement. Run with:
///   dotnet build PathToExile.csproj
///   ./scripts/run_headless.sh "res://scenes/tests/MovementSmokeTest.tscn" --quit-after 600
/// Simulates input actions directly (Input.ActionPress/Release) rather than real
/// OS key/mouse events, so it validates Player.cs's movement logic independent of
/// window/editor state. Prints PASS/FAIL lines and quits itself.
/// </summary>
public partial class MovementSmokeTest : Node2D
{
    private static readonly Vector2 FarTarget = new(900f, 350f);

    private Player _player = null!;
    private NavigationAgent2D _agent = null!;
    private SkillUser _skillUser = null!;
    private int _frame;
    private Vector2 _posAtStart;
    private Vector2 _posAfterWasd;
    private Vector2 _posBeforePathing;
    private Vector2 _posAtWasdInterrupt;
    private bool _failed;

    public override void _Ready()
    {
        _player = GetNode<Player>("Player");
        _agent = _player.GetNode<NavigationAgent2D>("NavigationAgent2D");
        _skillUser = _player.GetNode<SkillUser>("SkillUser");
        GD.Print("[SMOKETEST] Ready. Player start position: " + _player.GlobalPosition);

        // Verify each action not only EXISTS but has real input events bound to it.
        // Input.ActionPress() works on an action with zero bindings, so the movement
        // test below can pass while real keyboard/mouse input does nothing.
        // "move_to" was deleted by the combat pass (docs/design/skills.md -> "The
        // right-click conflict — resolved"); click-to-move is now driven by the
        // skill-slot/force_move actions checked here instead — see
        // scripts/tests/SkillsSmokeTest.cs for behavioural coverage of those.
        foreach (string action in new[]
                 {
                     "move_up", "move_down", "move_left", "move_right",
                     "skill_slot_1", "skill_slot_2", "skill_slot_3", "skill_slot_4",
                     "skill_slot_lmb", "skill_slot_rmb", "force_move",
                 })
        {
            if (!InputMap.HasAction(action))
            {
                GD.Print($"[SMOKETEST] BINDING {action}: MISSING FROM INPUTMAP");
                _failed = true;
                continue;
            }

            Godot.Collections.Array<InputEvent> events = InputMap.ActionGetEvents(action);
            if (events.Count == 0)
            {
                GD.Print($"[SMOKETEST] BINDING {action}: FAIL — action exists but has ZERO bound events");
                _failed = true;
                continue;
            }

            foreach (InputEvent ev in events)
            {
                GD.Print($"[SMOKETEST] BINDING {action}: {ev.AsText()}  ({ev.GetType().Name})");
            }
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        GD.Print($"[SMOKETEST] {name}: {(passed ? "PASS" : "FAIL")} — {detail}");
        if (!passed)
        {
            _failed = true;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;

        // Frames 1-5: let the NavigationServer sync the map.
        if (_frame == 5)
        {
            _posAtStart = _player.GlobalPosition;
            Input.ActionPress("move_right");
        }

        // Frames 6-25: hold move_right, expect steady rightward motion.
        if (_frame == 25)
        {
            Input.ActionRelease("move_right");
            _posAfterWasd = _player.GlobalPosition;
            float dx = _posAfterWasd.X - _posAtStart.X;
            Check("WASD-RIGHT", dx > 5f, $"moved {dx:0.00}px right (130px/s * 20/60s = 43.3 expected)");
        }

        // Frames 26-35: no input at all, expect a dead stop.
        if (_frame == 35)
        {
            float idleDrift = _player.GlobalPosition.DistanceTo(_posAfterWasd);
            Check("IDLE-STOP", idleDrift < 2f, $"drifted {idleDrift:0.00}px with no input");

            // Simulate a right-click on a far-away point. The agent's target is set
            // directly rather than via Input + GetGlobalMousePosition(), because the
            // headless mouse sits at (0,0) and the camera follows the player, which
            // puts the resulting world target within TargetDesiredDistance — too close
            // to tell "walked there" from "never moved".
            _posBeforePathing = _player.GlobalPosition;
            _agent.TargetPosition = FarTarget;
        }

        // Frames 36-60: the agent should be walking toward FarTarget.
        if (_frame == 60)
        {
            float moved = _player.GlobalPosition.DistanceTo(_posBeforePathing);
            Check("CLICK-TO-MOVE", moved > 2f, $"moved {moved:0.00}px toward {FarTarget}");

            // Interrupt mid-path with WASD. This is the behaviour under test: WASD must
            // CANCEL the pending destination, not merely pause it.
            Input.ActionPress("move_up");
        }

        // Frames 61-70: WASD held, overriding the path.
        if (_frame == 70)
        {
            Input.ActionRelease("move_up");
            _posAtWasdInterrupt = _player.GlobalPosition;
        }

        // Frames 71-90: no input. The player must STAY PUT. If the path was only
        // paused rather than cancelled, it resumes toward FarTarget here (~43px over
        // 20 frames), which is what this asserts against.
        if (_frame == 90)
        {
            float resumed = _player.GlobalPosition.DistanceTo(_posAtWasdInterrupt);
            Check("WASD-CANCELS-PATH", resumed < 2f,
                $"moved {resumed:0.00}px after releasing WASD (should be ~0; >40 means the old destination resumed)");
        }

        // Frame 100: LMB still defaults to the built-in Move occupant (nothing has
        // equipped a skill over it yet) — pressing skill_slot_lmb should path-follow
        // exactly like the old move_to did. Reset to a known baseline first so a
        // "did it change at all" check is unambiguous.
        if (_frame == 100)
        {
            _agent.TargetPosition = _player.GlobalPosition;
            Input.ActionPress("skill_slot_lmb");
        }

        if (_frame == 101)
        {
            Vector2 expectedMouseWorld = _player.GetGlobalMousePosition();
            float error = _agent.TargetPosition.DistanceTo(expectedMouseWorld);
            // Small tolerance, not exact equality: the player (and therefore the
            // camera it carries) may have drifted a sub-pixel amount between the
            // frame Player.cs computed this and the frame this check re-computes
            // it, since the previous check left it mid-path.
            Check("LMB-DEFAULTS-TO-MOVE", error < 5f,
                $"TargetPosition {_agent.TargetPosition} vs mouse world {expectedMouseWorld} (error {error:0.00}px)");
            Input.ActionRelease("skill_slot_lmb");
        }

        // Frame 110: equip a real skill over the LMB slot (docs/design/skills.md ->
        // "The right-click conflict — resolved": binding a skill there replaces
        // click-to-move). Pressing skill_slot_lmb must now do nothing to movement.
        if (_frame == 110)
        {
            _skillUser.EquipSkill(SkillSlotId.Lmb, new SkillInstance(new ProjectileSkillDefinition()));
            _agent.TargetPosition = _player.GlobalPosition;
            Input.ActionPress("skill_slot_lmb");
        }

        if (_frame == 111)
        {
            float drift = _agent.TargetPosition.DistanceTo(_player.GlobalPosition);
            Check("SKILL-OVER-LMB-DISABLES-MOVE", drift < 0.01f,
                $"TargetPosition drifted {drift:0.00}px from baseline (should be exactly 0 — LMB no longer holds Move)");
            Input.ActionRelease("skill_slot_lmb");
        }

        // Frame 120: force_move is the escape hatch — it must still path-follow
        // even though LMB now holds a skill instead of Move.
        if (_frame == 120)
        {
            _agent.TargetPosition = _player.GlobalPosition;
            Input.ActionPress("force_move");
        }

        if (_frame == 121)
        {
            Vector2 expectedMouseWorld = _player.GetGlobalMousePosition();
            float error = _agent.TargetPosition.DistanceTo(expectedMouseWorld);
            Check("FORCE-MOVE-BYPASSES-SKILL-SLOT", error < 5f,
                $"TargetPosition {_agent.TargetPosition} vs mouse world {expectedMouseWorld} (error {error:0.00}px)");
            Input.ActionRelease("force_move");
        }

        if (_frame >= 130)
        {
            GD.Print(_failed ? "[SMOKETEST] === RESULT: FAIL ===" : "[SMOKETEST] === RESULT: PASS ===");
            GetTree().Quit();
        }
    }
}
