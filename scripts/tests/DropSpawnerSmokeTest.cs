#nullable enable
using System.Collections.Generic;
using Godot;
using PathToExile.Combat;
using PathToExile.Items;

namespace PathToExile.Tests;

/// <summary>
/// Headless functional smoke test for <see cref="DropSpawner"/> — the death hook
/// that makes target dummies drop gold in the playable scene. Run with:
///   dotnet build PathToExile.csproj
///   ./scripts/run_headless.sh res://scenes/tests/DropSpawnerSmokeTest.tscn --quit-after 120
///
/// The kill goes through <see cref="Health.TakeDamage"/> with a real
/// <see cref="DamagePayload"/> rather than by emitting Died directly, so the test
/// exercises the same signal path a Fireball does. The most important assertion is
/// SPAWNED-DROP-OUTLIVES-KILLER: <see cref="TargetDummy.OnDied"/> calls QueueFree on
/// itself, so a drop parented to the dying entity would be deleted along with it.
/// That is the specific bug this test exists to catch.
/// </summary>
public partial class DropSpawnerSmokeTest : Node2D
{
    private TargetDummy _dummy = null!;
    private int _frame;
    private bool _failed;
    private bool _killed;

    public override void _Ready()
    {
        _dummy = GetNode<TargetDummy>("TargetDummy");
        GD.Print("[DROPTEST] Ready.");

        Check(
            "NO-DROP-BEFORE-DEATH",
            FindDrops().Count == 0,
            $"scene starts with no CurrencyDrop (found {FindDrops().Count})");
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;

        if (_frame == 2 && !_killed)
        {
            _killed = true;
            var health = _dummy.GetNode<Health>("Health");
            health.TakeDamage(new DamagePayload(
                9999f, DamageType.Physical, null, Faction.Player, SkillTags.Spell, DamageDeliveryKind.Hit));
            Check("DUMMY-DIES", health.IsDead, "Health.IsDead after a lethal payload");
            return;
        }

        // One full frame later: QueueFree on the dummy has been processed, so a
        // drop that survives here genuinely outlived its killer.
        if (_frame != 6)
        {
            return;
        }

        List<CurrencyDrop> drops = FindDrops();

        Check("EXACTLY-ONE-DROP", drops.Count == 1, $"found {drops.Count} CurrencyDrop(s), expected 1");
        if (drops.Count != 1)
        {
            Finish();
            return;
        }

        CurrencyDrop drop = drops[0];

        Check(
            "SPAWNED-DROP-OUTLIVES-KILLER",
            IsInstanceValid(drop) && drop.IsInsideTree() && !IsInstanceValid(_dummy),
            $"dummy freed={!IsInstanceValid(_dummy)}, drop still in tree={drop.IsInsideTree()}");

        Check(
            "DROP-IS-GOLD",
            drop.Currency is not null && drop.Currency.Id == "gold",
            $"currency id='{drop.Currency?.Id}'");

        Check(
            "AMOUNT-IN-AUTHORED-RANGE",
            drop.Amount >= 5 && drop.Amount <= 15,
            $"amount={drop.Amount}, expected the TargetDummy.tscn range 5..15");

        ItemDescriptor d = drop.Descriptor;
        Check(
            "DESCRIPTOR-IS-FINAL-NOT-PLACEHOLDER",
            d.ItemClass == ItemClass.Currency
                && d.CurrencyId == "gold"
                && d.Amount == drop.Amount
                && d.DisplayName == $"{drop.Amount} Gold",
            $"class={d.ItemClass}, currencyId='{d.CurrencyId}', amount={d.Amount}, name='{d.DisplayName}'");

        Check(
            "SCATTERED-NEAR-KILL-SITE",
            drop.GlobalPosition.DistanceTo(new Vector2(400f, 300f)) <= 26f,
            $"drop at {drop.GlobalPosition}, dummy died at (400, 300), scatter radius 18px/axis");

        Finish();
    }

    private List<CurrencyDrop> FindDrops()
    {
        var found = new List<CurrencyDrop>();
        foreach (Node child in GetChildren())
        {
            if (child is CurrencyDrop drop)
            {
                found.Add(drop);
            }
        }

        return found;
    }

    private void Check(string name, bool passed, string detail)
    {
        if (!passed)
        {
            _failed = true;
        }

        GD.Print($"[DROPTEST] {name}: {(passed ? "PASS" : "FAIL")} — {detail}");
    }

    private void Finish()
    {
        GD.Print($"[DROPTEST] === RESULT: {(_failed ? "FAIL" : "PASS")} ===");
        GetTree().Quit(_failed ? 1 : 0);
    }
}
