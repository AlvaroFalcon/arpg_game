#nullable enable
using Godot;
using PathToExile.Combat;
using PathToExile.Skills;
using PathToExile.Stats;

namespace PathToExile.Tests;

/// <summary>
/// Headless functional smoke test for the M1 combat slice. Run with:
///   dotnet build PathToExile.csproj
///   ./scripts/run_headless.sh res://scenes/tests/SkillsSmokeTest.tscn --quit-after 200
///
/// Two testing strategies are used deliberately, mirroring the split already
/// established by MovementSmokeTest:
///
/// - The input-binding sanity check (frames 5-8) drives the real
///   skill_slot_1 action through Player -> PlayerSkillInput -> SkillUser, the
///   same path a real player uses.
/// - Everything else calls <see cref="SkillUser.TryCast"/> directly. This is
///   the same rationale MovementSmokeTest uses for setting the NavigationAgent's
///   target directly instead of pressing move_to: the headless mouse sits at
///   (0,0), so an aim direction derived from it depends on viewport size rather
///   than being deterministic. TryCast is still the real, production entry
///   point — this only bypasses the mouse-to-direction step, not the executor.
/// </summary>
public partial class SkillsSmokeTest : Node2D
{
    private Player _player = null!;
    private SkillUser _skillUser = null!;
    private ActorStats _stats = null!;
    private Health _dummyHealth = null!;

    private SkillInstance _bindingTestSkill = null!;
    private SkillInstance _cooldownTestSkill = null!;

    private bool _dummyDied;
    private int _frame;
    private bool _failed;

    public override void _Ready()
    {
        _player = GetNode<Player>("Player");
        _skillUser = _player.GetNode<SkillUser>("SkillUser");
        _stats = _player.GetNode<ActorStats>("Stats");
        _dummyHealth = GetNode<Health>("Dummy/Health");
        _dummyHealth.Died += () => _dummyDied = true;

        GD.Print("[SKILLTEST] Ready.");

        // Same lesson the movement test's own comment records: Input.ActionPress
        // works on an action with zero bound events, so existence AND bindings
        // both need checking, or a real key doing nothing would pass silently.
        foreach (string action in new[]
                 {
                     "skill_slot_1", "skill_slot_2", "skill_slot_3", "skill_slot_4",
                     "skill_slot_lmb", "skill_slot_rmb", "force_move",
                 })
        {
            if (!InputMap.HasAction(action))
            {
                GD.Print($"[SKILLTEST] BINDING {action}: MISSING FROM INPUTMAP");
                _failed = true;
                continue;
            }

            if (InputMap.ActionGetEvents(action).Count == 0)
            {
                GD.Print($"[SKILLTEST] BINDING {action}: FAIL — action exists but has ZERO bound events");
                _failed = true;
            }
        }

        // A free, ~instant test skill purely to exercise the real input pipeline
        // without waiting on use-time/cooldown/cost.
        var bindingTestDefinition = new ProjectileSkillDefinition
        {
            DisplayName = "BindingTestBolt",
            BaseDamage = 1f,
            BaseUseTime = 0.05f,
            Tags = SkillTags.Spell | SkillTags.Projectile,
            Speed = 600f,
            Range = 500f,
            SpawnOffset = 20f,
        };
        _bindingTestSkill = new SkillInstance(bindingTestDefinition);
        _skillUser.EquipSkill(SkillSlotId.Slot1, _bindingTestSkill);

        // A second test skill with a real Cooldown and a tiny use time, so the
        // cooldown lever can be tested distinctly from the use-time lever (see
        // docs/design/skills.md -> "Cooldown and use time are independent levers").
        var cooldownTestDefinition = new ProjectileSkillDefinition
        {
            DisplayName = "CooldownTestBolt",
            BaseDamage = 1f,
            BaseUseTime = 0.05f,
            Cooldown = 1.0f,
            MaxCharges = 1,
            Tags = SkillTags.Spell | SkillTags.Projectile,
            Speed = 600f,
            Range = 500f,
            SpawnOffset = 20f,
        };
        _cooldownTestSkill = new SkillInstance(cooldownTestDefinition);
        _skillUser.EquipSkill(SkillSlotId.Slot2, _cooldownTestSkill);
    }

    private void Check(string name, bool passed, string detail)
    {
        GD.Print($"[SKILLTEST] {name}: {(passed ? "PASS" : "FAIL")} — {detail}");
        if (!passed)
        {
            _failed = true;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;

        // Frames 5-8: real-input binding sanity check.
        if (_frame == 5)
        {
            Input.ActionPress("skill_slot_1");
        }

        if (_frame == 6)
        {
            Input.ActionRelease("skill_slot_1");
        }

        if (_frame == 8)
        {
            Check("INPUT-DRIVES-CAST", _bindingTestSkill.UseTimeRemaining > 0f,
                $"UseTimeRemaining={_bindingTestSkill.UseTimeRemaining:0.000} after holding skill_slot_1 for one frame");
        }

        // Frame 20: cast Fireball at the dummy (direct call — see class doc).
        // This is also the shot the PROJECTILE-KILLS-DUMMY check below waits on.
        if (_frame == 20)
        {
            float manaBefore = _stats.Mana!.Current;
            CastFailure? failure = _skillUser.TryCast(SkillSlotId.Rmb, Vector2.Right);
            float manaAfter = _stats.Mana!.Current;
            Check("COST-PAID", failure is null && manaAfter < manaBefore,
                $"mana {manaBefore:0.0} -> {manaAfter:0.0}, failure={failure}");
        }

        // Frame 55: past the frame-20 Fireball's use-time gate (0.4s @ 60fps =
        // 24 frames), so the slot is ready again. Drain mana below its flat cost
        // and confirm the cast is blocked atomically (mana untouched).
        if (_frame == 55)
        {
            _stats.Mana!.Current = 3f;
            float manaBefore = _stats.Mana.Current;
            CastFailure? failure = _skillUser.TryCast(SkillSlotId.Rmb, Vector2.Right);
            Check("INSUFFICIENT-MANA-BLOCKS",
                failure is { Kind: CastFailureKind.InsufficientResource, Resource: ResourceType.Mana }
                && _stats.Mana.Current == manaBefore,
                $"failure={failure}, mana {manaBefore:0.0} -> {_stats.Mana.Current:0.0}");
            _stats.Mana.Current = _stats.Mana.Max;
        }

        // Frames 40/45/110: the cooldown lever, tested distinctly from use time.
        if (_frame == 40)
        {
            CastFailure? first = _skillUser.TryCast(SkillSlotId.Slot2, Vector2.Up);
            Check("COOLDOWN-FIRST-CAST-SUCCEEDS", first is null, $"result={first}");
        }

        if (_frame == 45) // ~0.08s later: use time (0.05s) has elapsed, cooldown (1s) has not
        {
            CastFailure? second = _skillUser.TryCast(SkillSlotId.Slot2, Vector2.Up);
            Check("COOLDOWN-GATES-REPEAT-CAST", second is { Kind: CastFailureKind.StillRecovering },
                $"result={second}");
        }

        if (_frame == 110) // ~1.08s after the first cast: cooldown should have recovered
        {
            CastFailure? third = _skillUser.TryCast(SkillSlotId.Slot2, Vector2.Up);
            Check("COOLDOWN-RECOVERS", third is null, $"result={third}");
        }

        // Frame 160: the frame-20 Fireball (600px/s, ~250px to the dummy) has had
        // ~1.9s to arrive and one-shot the 30-life dummy with 35 damage.
        if (_frame == 160)
        {
            Check("PROJECTILE-KILLS-DUMMY", _dummyDied, $"Health.Died fired={_dummyDied}");
        }

        if (_frame >= 170)
        {
            GD.Print(_failed ? "[SKILLTEST] === RESULT: FAIL ===" : "[SKILLTEST] === RESULT: PASS ===");
            GetTree().Quit();
        }
    }
}
