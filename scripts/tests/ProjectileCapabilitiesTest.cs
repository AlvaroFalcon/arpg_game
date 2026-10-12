#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Skills;

namespace ArpgGame.Tests;

/// <summary>
/// Headless functional test for the two projectile capabilities wired up after
/// the M1 slice: Count/SpreadDegrees (fan at cast) and Forks (split on hit).
/// Run with:
///   dotnet build ArpgGame.csproj
///   ./scripts/run_headless.sh res://scenes/tests/ProjectileCapabilitiesTest.tscn --quit-after 400
///
/// Kept separate from SkillsSmokeTest rather than appended to it: that test is a
/// tightly frame-sequenced script covering costs/cooldowns, and interleaving a
/// second concern into its frame budget makes both harder to read and to fail
/// usefully.
///
/// Casts go through <see cref="SkillUser.TryCast"/> with an explicit aim vector
/// for the same reason SkillsSmokeTest does: the headless mouse sits at (0,0), so
/// a cursor-derived direction would depend on viewport size.
/// </summary>
public partial class ProjectileCapabilitiesTest : Node2D
{
    private SkillUser _skillUser = null!;
    private Health _dummyHealth = null!;
    private int _frame;
    private bool _failed;

    private int _forkChildCount;

    public override void _Ready()
    {
        var player = GetNode<Node2D>("Player");
        _skillUser = player.GetNode<SkillUser>("SkillUser");
        _dummyHealth = GetNode<Health>("Dummy/Health");
        GD.Print("[PROJTEST] Ready.");
    }

    private void Check(string name, bool ok, string detail)
    {
        if (!ok)
        {
            _failed = true;
        }

        GD.Print($"[PROJTEST] {name}: {(ok ? "PASS" : "FAIL")} — {detail}");
    }

    private List<Projectile> LiveProjectiles() =>
        GetTree().CurrentScene.GetChildren().OfType<Projectile>().Where(p => IsInstanceValid(p)).ToList();

    private void ClearProjectiles()
    {
        foreach (Projectile p in LiveProjectiles())
        {
            p.QueueFree();
        }
    }

    private static SkillInstance MakeSkill(int count, float spreadDegrees, int forks, float damage = 1f)
    {
        return new SkillInstance(new ProjectileSkillDefinition
        {
            DisplayName = "CapabilityTestBolt",
            BaseDamage = damage,
            BaseUseTime = 0.01f,
            Tags = SkillTags.Projectile | SkillTags.Spell,
            Speed = 400f,
            Range = 2000f,
            SpawnOffset = 20f,
            Count = count,
            SpreadDegrees = spreadDegrees,
            Forks = forks,
        });
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;

        // --- Count = 1 stays a single projectile (the pre-existing behaviour
        // must not regress now that the generator does real work). ---
        if (_frame == 5)
        {
            ClearProjectiles();
            _skillUser.EquipSkill(SkillSlotId.Slot1, MakeSkill(count: 1, spreadDegrees: 0f, forks: 0));
            _skillUser.TryCast(SkillSlotId.Slot1, Vector2.Right);
        }

        if (_frame == 6)
        {
            Check("COUNT-1-IS-SINGLE", LiveProjectiles().Count == 1,
                $"spawned {LiveProjectiles().Count} projectile(s), expected 1");
        }

        // --- Count = 5 across a 60 degree cone: five projectiles, outermost two
        // exactly on the cone edges, all distinct headings. ---
        if (_frame == 10)
        {
            ClearProjectiles();
            _skillUser.EquipSkill(SkillSlotId.Slot2, MakeSkill(count: 5, spreadDegrees: 60f, forks: 0));
            _skillUser.TryCast(SkillSlotId.Slot2, Vector2.Right);
        }

        if (_frame == 11)
        {
            List<Projectile> live = LiveProjectiles();
            List<float> angles = live.Select(p => Mathf.RadToDeg(p.Rotation)).OrderBy(a => a).ToList();
            bool countOk = live.Count == 5;
            bool edgesOk = countOk
                && Mathf.Abs(angles[0] - -30f) < 0.5f
                && Mathf.Abs(angles[4] - 30f) < 0.5f;
            bool distinct = angles.Distinct().Count() == angles.Count;

            Check("COUNT-5-FANS-CONE", countOk && edgesOk && distinct,
                $"n={live.Count}, angles=[{string.Join(", ", angles.Select(a => a.ToString("0.0")))}], expected 5 spanning -30..30");
        }

        // --- Spread >= 360 is the "around the caster" nova: the full circle is
        // divided by Count, so no two projectiles share a heading. ---
        if (_frame == 15)
        {
            ClearProjectiles();
            _skillUser.EquipSkill(SkillSlotId.Slot3, MakeSkill(count: 4, spreadDegrees: 360f, forks: 0));
            _skillUser.TryCast(SkillSlotId.Slot3, Vector2.Right);
        }

        if (_frame == 16)
        {
            List<Projectile> live = LiveProjectiles();
            List<float> angles = live.Select(p => Mathf.RadToDeg(p.Rotation)).OrderBy(a => a).ToList();
            // 4 projectiles over 360 degrees = 90 degrees apart: -180, -90, 0, 90.
            bool spacingOk = live.Count == 4
                && Mathf.Abs(angles[1] - angles[0] - 90f) < 0.5f
                && Mathf.Abs(angles[2] - angles[1] - 90f) < 0.5f
                && Mathf.Abs(angles[3] - angles[2] - 90f) < 0.5f;

            Check("SPREAD-360-IS-NOVA", spacingOk,
                $"n={live.Count}, angles=[{string.Join(", ", angles.Select(a => a.ToString("0.0")))}], expected 4 spaced 90 apart");
            ClearProjectiles();
        }

        // --- Fork: one projectile fired at a dummy becomes two after the hit.
        // Damage is kept below the dummy's life so the dummy survives to be a
        // stable target and the test measures forking, not killing. ---
        if (_frame == 25)
        {
            ClearProjectiles();
            _skillUser.EquipSkill(SkillSlotId.Slot4, MakeSkill(count: 1, spreadDegrees: 0f, forks: 1, damage: 1f));
            _skillUser.TryCast(SkillSlotId.Slot4, Vector2.Right);
        }

        // The projectile needs time to fly to the dummy, hit, and for the
        // deferred fork spawn to land — but this must be read BEFORE either
        // child reaches the ForkCatcher dummy (~frame 86).
        if (_frame == 65)
        {
            _forkChildCount = LiveProjectiles().Count;
            List<float> angles = LiveProjectiles().Select(p => Mathf.RadToDeg(p.Rotation)).OrderBy(a => a).ToList();
            bool anglesOk = _forkChildCount == 2
                && Mathf.Abs(angles[0] - -30f) < 0.5f
                && Mathf.Abs(angles[1] - 30f) < 0.5f;

            Check("FORK-SPAWNS-TWO-CHILDREN", anglesOk,
                $"n={_forkChildCount} after hit (dummy alive={!_dummyHealth.IsDead}), " +
                $"angles=[{string.Join(", ", angles.Select(a => a.ToString("0.0")))}], expected 2 at -30/+30");
        }

        // --- Fork budget is a generation cap, not per-hit: children inherit the
        // decremented counter, so Forks = 1 must never produce a third generation.
        // Without this, fork-on-hit is exponential.
        //
        // This check needs a target for a CHILD to hit — the "ForkCatcher" dummy
        // sits 200px along the -30 degree child's path. An earlier version of this
        // test simply counted projectiles in empty space and PASSED with the bug
        // deliberately reintroduced (children kept an undecremented counter),
        // because nothing the children could hit meant nothing could ever fork
        // twice. Correct behaviour: the child hits ForkCatcher and despawns,
        // leaving 1 (its sibling). Buggy: it forks again, leaving 3.
        if (_frame == 110)
        {
            int n = LiveProjectiles().Count;
            Check("FORK-DOES-NOT-CASCADE", n == 1,
                $"{n} projectile(s) alive after a fork child hit the ForkCatcher dummy; " +
                "expected 1 (the sibling). 3 means the child forked again, i.e. the counter isn't inherited decremented");
        }

        if (_frame >= 120)
        {
            GD.Print(_failed ? "[PROJTEST] === RESULT: FAIL ===" : "[PROJTEST] === RESULT: PASS ===");
            GetTree().Quit();
        }
    }
}
