#nullable enable
using Godot;

namespace ArpgGame.Tests;

/// <summary>
/// Headless co-op test of the Survival Rift's 2026-11 loop in a real party — run with
///   ./scripts/run_all_tests.sh CoopRiftLoopTest
/// The host end of <see cref="CoopTestHost"/> with <see cref="CoopTestHost.RiftLoopOnly"/>: it starts
/// the same two client processes as CoopSmokeTest (they run CoopSmokeTest.tscn as clients), plays
/// the lobby and the start pick, then <c>RiftLoopCoopChecks</c> (CoopSmokeTest.RiftLoop.cs): tier
/// and Fear cards, Skip, Banish, chests and shrines across machines. Its own suite because
/// CoopSmokeTest already fills the runner's <c>--quit-after 6000</c> frames
/// (docs/design/survival-roadmap.md -> "Tests that guard the Rift").
/// </summary>
public partial class CoopRiftLoopTest : Node
{
    public override void _Ready()
    {
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new CoopTestHost { Name = "CoopTestDriver", RiftLoopOnly = true });
    }
}
