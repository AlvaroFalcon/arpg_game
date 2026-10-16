#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Run;

namespace ArpgGame.Tools;

/// <summary>
/// Single-player Survival Rift load bench — docs/design/performance.md -> "Co-op".
/// Offline (no session): the Rift jumps to a late wave, fills for a while, is
/// measured, then a Cataclysm kills everything. The player stands still and
/// auto-casts at monsters that cannot die (so the count sits at the cap); level-ups take the first card. Run with --fixed-fps 60 so every
/// frame is exactly one simulation step, unthrottled: a frame's wall time is its cost.
///   "$GODOT_BIN" --headless --path . --fixed-fps 60 res://scenes/tools/RiftBench.tscn -- [--run-time=350] [--fill=1800] [--frames=1200]
/// Lines: stdout and user://bench/rift_bench.txt, ending in RESULT: PASS.
/// </summary>
public partial class RiftBench : Node
{
    private readonly List<string> _lines = new();

    public override void _Ready()
    {
        if (GetTree().CurrentScene == this)
        {
            // The scene is replaced by the Rift: the driver lives on the root.
            GetTree().Root.CallDeferred(Node.MethodName.AddChild, new RiftBench { Name = "RiftBenchDriver" });
            return;
        }

        ProcessMode = ProcessModeEnum.Always;
        _ = Bench();
    }

    private void Log(string line)
    {
        GD.Print(line);
        _lines.Add(line);
        DirAccess.MakeDirRecursiveAbsolute("user://bench");
        using FileAccess? f = FileAccess.Open("user://bench/rift_bench.txt", FileAccess.ModeFlags.Write);
        f?.StoreString(string.Join("\n", _lines) + "\n");
    }

    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private static string Stats(List<double> v)
    {
        if (v.Count == 0)
        {
            return "n/a";
        }

        var s = v.OrderBy(x => x).ToList();
        return string.Create(CultureInfo.InvariantCulture,
            $"avg {v.Average():0.00} ms, p95 {s[(int)(s.Count * 0.95)]:0.00}, p99 {s[(int)(s.Count * 0.99)]:0.00}, max {s[^1]:0.0}");
    }

    private async Task Bench()
    {
        var args = new Dictionary<string, string>();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string[] kv = arg.TrimStart('-').Split('=', 2);
            args[kv[0]] = kv.Length == 2 ? kv[1] : "";
        }

        float startAt = args.GetValueOrDefault("run-time", "350").ToFloat();
        int fill = args.GetValueOrDefault("fill", "1800").ToInt();
        int frames = args.GetValueOrDefault("frames", "1200").ToInt();
        GD.Seed(12345);
        RunLaunchOptions.Reset();
        RunLaunchOptions.StartSkill = "spark";
        GetTree().ChangeSceneToFile("res://scenes/run/Run.tscn");
        for (int i = 0; i < 90; i++)
        {
            await Frame();
        }

        if (GetTree().CurrentScene is not RunController run)
        {
            Log("BENCH ERROR rift scene failed to load");
            GetTree().Quit();
            return;
        }

        run.State.SkipTo(startAt);
        run.Director.SyncTimers();
        run.Player.Health!.IgnoreDamage = true;
        Log($"BENCH rift solo, clock {startAt:0}s, filling {fill} frames");
        for (int i = 0; i < fill; i++)
        {
            Keep(run);
            await Frame();
        }

        var wall = new List<double>();
        var enemies = new List<double>();
        long alloc0 = GC.GetTotalAllocatedBytes(false);
        int gc0 = GC.CollectionCount(0);
        ulong last = Time.GetTicksUsec();
        for (int i = 0; i < frames; i++)
        {
            Keep(run);
            await Frame();
            ulong now = Time.GetTicksUsec();
            wall.Add((now - last) / 1000.0);
            last = now;
            if (i % 60 == 0)
            {
                enemies.Add(GetTree().GetNodesInGroup(CombatQuery.EnemyGroup).Count);
            }
        }

        long alloc = GC.GetTotalAllocatedBytes(false) - alloc0;
        Log(string.Create(CultureInfo.InvariantCulture,
            $"BENCH steady {frames} frames: {enemies.Average():0} monsters (max {enemies.Max():0}), nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0}"));
        Log($"BENCH frame wall: {Stats(wall)}");
        Log(string.Create(CultureInfo.InvariantCulture, $"BENCH managed alloc {alloc / 1024.0 / frames:0.0} KB/frame, gen0 GCs {GC.CollectionCount(0) - gc0}"));

        foreach (Node node in GetTree().GetNodesInGroup(CombatQuery.EnemyGroup))
        {
            if (node is Enemies.Enemy { Health: { } h })
            {
                h.IgnoreDamage = false;
            }
        }

        var spike = new List<double>();
        last = Time.GetTicksUsec();
        int killed = run.State.Nuke(run.Player);
        for (int i = 0; i < 120; i++)
        {
            Keep(run, false);
            await Frame();
            ulong now = Time.GetTicksUsec();
            spike.Add((now - last) / 1000.0);
            last = now;
        }

        Log($"BENCH cataclysm {killed} killed: next 120 frames {Stats(spike)}; first two {spike[0]:0.0} + {spike[1]:0.0} ms");
        Log("RESULT: PASS");
        GetTree().Quit();
    }

    private int _keepFrame;

    /// <summary>Monsters can't die (the count stays at the cap, so runs compare);
    /// the player still auto-aims and casts. Level-ups take the first card.</summary>
    private void Keep(RunController run, bool immortal = true)
    {
        if (immortal && _keepFrame++ % 10 == 0)
        {
            foreach (Node node in GetTree().GetNodesInGroup(CombatQuery.EnemyGroup))
            {
                if (node is Enemies.Enemy { Health: { } h })
                {
                    h.IgnoreDamage = true;
                }
            }
        }

        if (run.LevelUp.IsOpen)
        {
            run.LevelUp.ChooseIndex(0);
        }

        if (GetTree().Paused)
        {
            GetTree().Paused = false;
        }
    }
}
