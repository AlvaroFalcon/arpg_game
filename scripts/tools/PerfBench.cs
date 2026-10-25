#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Enemies;
using ArpgGame.Run;
using ArpgGame.World;

namespace ArpgGame.Tools;

/// <summary>
/// Frame-budget bench — docs/design/performance.md -> "Frame budget pass". Loads the
/// Survival Rift (late wave, immortal monsters at the cap) or a reality (fixed seed,
/// packs asleep, then every pack woken onto the player), turns the <see cref="Perf"/>
/// probes on and measures frame wall time, physics-script time per step, allocations,
/// GCs (each logged with its pause), spikes over 50 ms, physics and render monitors and
/// every probe. Run it with --fixed-fps 60 (one step per frame, unthrottled). Use
/// scripts/perf_bench.sh rather than calling it by hand.
///   -- --mode=rift|reality [--run-time=350] [--fill=1800] [--frames=1200] [--tier=10]
///      [--layout=id] [--seed=7] [--tag=] [--forcevoxel] [--shots=dir] [--lineup]
/// Lines: stdout and user://bench/perf_&lt;mode&gt;&lt;tag&gt;.txt, one "PERF SUMMARY" line per
/// phase, ending in RESULT: PASS.
/// </summary>
public partial class PerfBench : Node
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
    private readonly List<string> _lines = new();
    private readonly Dictionary<string, string> _args = new();
    private string _out = "user://bench/perf_bench.txt";
    private long _lastGcIndex = -1;
    private int _keepFrame;

    public override void _Ready()
    {
        if (GetTree().CurrentScene == this)
        {
            // The scene is replaced by the Rift or the reality: the driver lives on the root.
            GetTree().Root.CallDeferred(Node.MethodName.AddChild, new PerfBench { Name = "PerfBenchDriver" });
            return;
        }

        ProcessMode = ProcessModeEnum.Always;
        _ = Bench();
    }

    public override void _ExitTree() => Perf.Enabled = false;

    private void Log(string line)
    {
        GD.Print(line);
        _lines.Add(line);
        DirAccess.MakeDirRecursiveAbsolute("user://bench");
        using FileAccess? f = FileAccess.Open(_out, FileAccess.ModeFlags.Write);
        f?.StoreString(string.Join("\n", _lines) + "\n");
    }

    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
        {
            await Frame();
        }
    }

    private string Arg(string key, string fallback) => _args.TryGetValue(key, out string? v) ? v : fallback;

    private static (double Avg, double P95, double P99, double Max) Stats(List<double> v)
    {
        if (v.Count == 0)
        {
            return (0, 0, 0, 0);
        }

        var s = new List<double>(v);
        s.Sort();
        return (v.Average(), s[(int)(s.Count * 0.95)], s[(int)(s.Count * 0.99)], s[^1]);
    }

    private static string Describe(List<double> v)
    {
        (double avg, double p95, double p99, double max) = Stats(v);
        return string.Create(Ci, $"avg {avg:0.00}, p95 {p95:0.00}, p99 {p99:0.00}, max {max:0.0}");
    }

    private async Task Bench()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string[] kv = arg.TrimStart('-').Split('=', 2);
            _args[kv[0]] = kv.Length == 2 ? kv[1] : "";
        }

        string mode = Arg("mode", "rift");
        _out = $"user://bench/perf_{mode}{Arg("tag", "")}.txt";
        int fill = Arg("fill", "1800").ToInt();
        int frames = Arg("frames", "1200").ToInt();
        View3D.HdView.ForceInHeadless = _args.ContainsKey("forcevoxel");
        GD.Seed(12345);
        Log($"PERF mode={mode} args=[{string.Join(" ", OS.GetCmdlineUserArgs())}] display={DisplayServer.GetName()} adapter={RenderingServer.GetVideoAdapterName()} voxel={View3D.HdView.Enabled(this)}");

        RunController? run = null;
        RealityController? reality = null;
        if (mode == "rift")
        {
            RunLaunchOptions.Reset();
            RunLaunchOptions.StartSkill = "spark";
            GetTree().ChangeSceneToFile("res://scenes/run/Run.tscn");
            await Frames(90);
            run = GetTree().CurrentScene as RunController;
            if (run is null)
            {
                Fail("the Rift failed to load");
                return;
            }

            run.Player.Health!.IgnoreDamage = true;
            if (_args.TryGetValue("shots", out string? riftShots))
            {
                await RiftShots(run, riftShots);
            }

            run.State.SkipTo(Arg("run-time", "350").ToFloat());
            run.Director.SyncTimers();
        }
        else
        {
            if (Core.Profile.Get(this) is { } profile)
            {
                profile.Persist = false;
                profile.CreateCharacter("Bench", "warrior");
            }

            RealityLaunch.Tier = Arg("tier", "10").ToInt();
            RealityLaunch.LayoutId = _args.TryGetValue("layout", out string? layout) && layout.Length > 0 ? layout : null;
            RealityLaunch.Seed = (ulong)Arg("seed", "7").ToInt();
            RealityLaunch.Modifiers = new List<string>();
            RealityLaunch.Tutorial = false;
            if (_args.ContainsKey("viascreen"))
            {
                // The real path (the Mirror, the hideout portal): through the loading screen.
                UI.LoadingScreen.Go(GetTree(), "res://scenes/reality/Reality.tscn", "PerfBench", "");
            }
            else
            {
                GetTree().ChangeSceneToFile("res://scenes/reality/Reality.tscn");
            }

            await Frames(120);
            reality = GetTree().CurrentScene as RealityController;
            if (reality is null)
            {
                Fail("the reality failed to load");
                return;
            }

            reality.Player.Health!.IgnoreDamage = true;
            GetTree().Paused = false;
            Log($"PERF reality {reality.Layout.Name} tier {reality.Tier}, monsters {GetTree().GetNodesInGroup(CombatQuery.EnemyGroup).Count}");
            if (_args.ContainsKey("viascreen"))
            {
                Log(string.Create(Ci, $"PERF entered through the loading screen, heap now {GC.GetTotalMemory(false) / 1048576.0:0.0} MB"));
            }

            if (_args.TryGetValue("shots", out string? realityShots))
            {
                await RealityShots(reality, realityShots);
            }
        }

        Log(string.Create(Ci, $"PERF terrain build {View3D.VoxelStage.TerrainBuildMs:0.0} ms (all BuildTerrain calls so far)"));
        Perf.Enabled = true;
        for (int i = 0; i < fill; i++)
        {
            Keep(run);
            await Frame();
        }

        if (frames > 0)
        {
            await Measure("steady", frames, run);
            if (reality is not null)
            {
                // Every pack wakes and floods toward the player through the flow field.
                foreach (Node n in GetTree().GetNodesInGroup(CombatQuery.EnemyGroup))
                {
                    if (n is Enemy { IsBoss: false } e)
                    {
                        e.Wake();
                    }
                }

                for (int i = 0; i < Math.Min(600, Math.Max(fill, 60)); i++)
                {
                    Keep(null);
                    await Frame();
                }

                await Measure("awake", frames, null);
            }
        }

        Perf.Enabled = false;
        Log("RESULT: PASS");
        GetTree().Quit();
    }

    private void Fail(string why)
    {
        Log($"PERF ERROR {why}");
        Log("RESULT: FAIL");
        GetTree().Quit();
    }

    private async Task Measure(string label, int frames, RunController? run)
    {
        StepProbe probe = StepProbe.Install(this);
        probe.Recording = true;
        var wall = new List<double>(frames);
        var draws = new List<double>(frames);
        var objs = new List<double>(frames);
        var prims = new List<double>(frames);
        var monsters = new List<double>();
        long alloc0 = GC.GetTotalAllocatedBytes(false);
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        _lastGcIndex = GC.GetGCMemoryInfo(GCKind.Any).Index;
        Perf.Reset();
        ulong last = Time.GetTicksUsec();
        for (int i = 0; i < frames; i++)
        {
            Keep(run);
            await Frame();
            ulong now = Time.GetTicksUsec();
            double ms = (now - last) / 1000.0;
            wall.Add(ms);
            GCMemoryInfo g = GC.GetGCMemoryInfo(GCKind.Any);
            if (g.Index != _lastGcIndex)
            {
                _lastGcIndex = g.Index;
                Log(string.Create(Ci, $"PERF GC {label} #{g.Index} gen{g.Generation} concurrent={g.Concurrent} compacted={g.Compacted} pauses {g.PauseDurations[0].TotalMilliseconds:0.0}+{g.PauseDurations[1].TotalMilliseconds:0.0} ms, heap {g.HeapSizeBytes / 1048576.0:0.0} MB, promoted {g.PromotedBytes / 1048576.0:0.0} MB, frame {i}"));
            }

            if (ms > 50.0 && DisplayServer.GetName() == "headless")
            {
                Log(string.Create(Ci, $"PERF SPIKE {label} frame {i}: {ms:0.0} ms, scripts {(probe.ScriptMs.Count > 0 ? probe.ScriptMs[^1] : 0):0.0} ms, GCs {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}"));
            }

            last = now;
            draws.Add(RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame));
            objs.Add(RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame));
            prims.Add(RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame));
            if (i % 30 == 0)
            {
                monsters.Add(GetTree().GetNodesInGroup(CombatQuery.EnemyGroup).Count);
            }
        }

        probe.Recording = false;
        long alloc = GC.GetTotalAllocatedBytes(false) - alloc0;
        int dgc0 = GC.CollectionCount(0) - gc0, dgc1 = GC.CollectionCount(1) - gc1, dgc2 = GC.CollectionCount(2) - gc2;
        double active = Performance.GetMonitor(Performance.Monitor.Physics2DActiveObjects);
        double pairs = Performance.GetMonitor(Performance.Monitor.Physics2DCollisionPairs);
        Log(string.Create(Ci, $"PERF {label} {frames} frames: monsters {monsters.Average():0} (max {monsters.Max():0}), nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0}, objects {Performance.GetMonitor(Performance.Monitor.ObjectCount):0}, orphan {Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount):0}"));
        Log($"PERF {label} frame wall ms: {Describe(wall)}");
        Log($"PERF {label} physics scripts ms/step: {Describe(probe.ScriptMs)}  (steps {probe.ScriptMs.Count})");
        Log(string.Create(Ci, $"PERF {label} physics2d active {active:0}, pairs {pairs:0}, islands {Performance.GetMonitor(Performance.Monitor.Physics2DIslandCount):0}"));
        Log(string.Create(Ci, $"PERF {label} render: draw calls {draws.Average():0} (max {draws.Max():0}), objects {objs.Average():0}, primitives {prims.Average():0}, video mem {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0:0.0} MB, tex {Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / 1048576.0:0.0} MB, buf {Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed) / 1048576.0:0.0} MB"));
        Log(string.Create(Ci, $"PERF {label} managed alloc {alloc / 1024.0 / frames:0.0} KB/frame, GCs gen0 {dgc0} gen1 {dgc1} gen2 {dgc2}, heap {GC.GetTotalMemory(false) / 1048576.0:0.0} MB"));
        var parts = new List<string>();
        var summary = new List<string>();
        for (int k = 0; k < (int)Perf.K.Count; k++)
        {
            if (Perf.Calls[k] == 0)
            {
                continue;
            }

            var key = (Perf.K)k;
            double perFrame = Perf.Ms(key) / frames;
            double each = Perf.Ms(key) * 1000.0 / Perf.Calls[k];
            double kb = Perf.Bytes[k] / 1024.0 / frames;
            parts.Add(string.Create(Ci, $"{key} {perFrame:0.000} ms/frame ({Perf.Calls[k] / (double)frames:0} calls, {each:0.00} us each, {kb:0.0} KB/frame)"));
            summary.Add(string.Create(Ci, $"{key}_ms={perFrame:0.000} {key}_us={each:0.00} {key}_kb={kb:0.0}"));
        }

        Log($"PERF {label} probes: " + string.Join("; ", parts));
        (double avg, double p95, double p99, double max) = Stats(wall);
        (double sAvg, _, _, _) = Stats(probe.ScriptMs);
        Log(string.Create(Ci, $"PERF SUMMARY phase={label} monsters={monsters.Average():0} avg={avg:0.00} p95={p95:0.00} p99={p99:0.00} max={max:0.0} scripts={sAvg:0.00} allocKB={alloc / 1024.0 / frames:0.0} gc0={dgc0} gc1={dgc1} gc2={dgc2} pairs={pairs:0} active={active:0} draws={draws.Average():0} objects={objs.Average():0} prims={prims.Average():0} ") + string.Join(" ", summary));
        GetTree().Root.GetNodeOrNull("StepProbeBegin")?.QueueFree();
        probe.QueueFree();
        await Frame();
    }

    /// <summary>Monsters in the Rift can't die (the count sits at the cap, so runs compare);
    /// the player still auto-casts. Level-ups take the first card.</summary>
    private void Keep(RunController? run)
    {
        if (run is not null && _keepFrame++ % 10 == 0)
        {
            foreach (Node node in GetTree().GetNodesInGroup(CombatQuery.EnemyGroup))
            {
                if (node is Enemy { Health: { } h })
                {
                    h.IgnoreDamage = true;
                }
            }
        }

        if (run is not null && run.LevelUp.IsOpen)
        {
            run.LevelUp.ChooseIndex(0);
        }

        if (GetTree().Paused)
        {
            GetTree().Paused = false;
        }
    }

    // --- the fixed screenshot set (docs/design/performance.md -> "Visual comparison") ------

    private async Task Snap(string dir, string file)
    {
        if (DisplayServer.GetName() == "headless")
        {
            Log($"PERF SHOT skipped {file} (headless)");
            return;
        }

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        string folder = dir.StartsWith("res://") || dir.StartsWith("user://") ? ProjectSettings.GlobalizePath(dir) : dir;
        DirAccess.MakeDirRecursiveAbsolute(folder);
        Error err = image.SavePng($"{folder}/{file}");
        Log($"PERF SHOT {(err == Error.Ok ? "saved" : "FAILED " + err)} {folder}/{file}");
    }

    private async Task MoveTo(Player player, Vector2 at)
    {
        player.GlobalPosition = at;
        player.Velocity = Vector2.Zero;
        player.GetNodeOrNull<Camera2D>("Camera2D")?.ResetSmoothing();
        await Frames(45);
    }

    /// <summary>Monsters frozen in place and hidden, so the shots are the same on every run
    /// (pack rarity and counts are rolled per run).</summary>
    private void HoldMonsters(bool hold)
    {
        foreach (Node n in GetTree().GetNodesInGroup(CombatQuery.EnemyGroup))
        {
            if (n is Enemy e && e.GetParent()?.Name != "PerfLineup")
            {
                e.ProcessMode = hold ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
                e.Visible = !hold;
            }
        }
    }

    private async Task RiftShots(RunController run, string dir)
    {
        run.Director.Spawning = false;
        foreach (Node n in GetTree().GetNodesInGroup(CombatQuery.EnemyGroup))
        {
            n.QueueFree();
        }

        await MoveTo(run.Player, run.Arena.Center);
        await Snap(dir, "rift-centre.png");
        await MoveTo(run.Player, new Vector2(run.Arena.Center.X, run.Arena.Bounds.End.Y - 120f));
        await Snap(dir, "rift-edge.png");
        if (_args.ContainsKey("lineup"))
        {
            Node2D lineup = Lineup(run.Arena.Center);
            run.AddChild(lineup);
            await MoveTo(run.Player, run.Arena.Center + new Vector2(0f, 100f));
            await Snap(dir, "rift-lineup.png");
            lineup.QueueFree();
            await Frame();
        }

        run.Player.GlobalPosition = run.Arena.Center;
        run.Director.Spawning = true;
    }

    /// <summary>Every enemy scene and every player look, dormant, in rows 48 px apart.</summary>
    private static Node2D Lineup(Vector2 centre)
    {
        var root = new Node2D { Name = "PerfLineup" };
        string[] enemies = { "Slime", "Bat", "Imp", "Skeleton", "Caster", "Siren", "Thrall", "Brute" };
        string[] bosses = { "EmberColossus", "Nereth", "BoneHerald" };
        string[] looks = { "player", "warrior", "rogue", "sage", "barbarian", "paladin", "assassin", "ninja", "samurai", "warlock", "ascendant" };
        for (int i = 0; i < enemies.Length; i++)
        {
            AddDormant(root, $"res://scenes/enemies/{enemies[i]}.tscn", centre + new Vector2((i - (enemies.Length - 1) / 2f) * 48f, -50f));
        }

        for (int i = 0; i < bosses.Length; i++)
        {
            AddDormant(root, $"res://scenes/enemies/{bosses[i]}.tscn", centre + new Vector2((i - 1) * 130f, -140f));
        }

        for (int i = 0; i < looks.Length; i++)
        {
            if (GD.Load<PackedScene>($"res://assets/sprites/player/{looks[i]}.tscn")?.Instantiate() is not AnimatedSprite2D sprite)
            {
                continue;
            }

            var holder = new Node2D { Position = centre + new Vector2((i - (looks.Length - 1) / 2f) * 48f, 40f) };
            sprite.Position = new Vector2(0f, -6f);
            holder.AddChild(sprite);
            root.AddChild(holder);
        }

        return root;
    }

    private static void AddDormant(Node2D root, string path, Vector2 at)
    {
        if (GD.Load<PackedScene>(path)?.Instantiate() is not Enemy enemy)
        {
            return;
        }

        enemy.Dormant = true;
        enemy.ProcessMode = ProcessModeEnum.Disabled;
        enemy.Position = at;
        root.AddChild(enemy);
    }

    private async Task RealityShots(RealityController reality, string dir)
    {
        HoldMonsters(true);
        RasterLayout r = reality.Raster;
        Player player = reality.Player;
        await MoveTo(player, player.GlobalPosition);
        await Snap(dir, "reality-entrance.png");

        // On the first floor cell (nearest the entrance) with a wall run of 3 cells just
        // south of it: the wall stands between the camera and the player (the dither).
        bool Wall(Vector2I c) => c.X >= 0 && c.Y >= 0 && c.X < r.Width && c.Y < r.Height && r.Cells[c.X, c.Y] is CellKind.Void or CellKind.Pillar;
        if (Nearest(r, r.Entrance, c => r.Walkable(c) && Wall(c + new Vector2I(-1, 1)) && Wall(c + new Vector2I(0, 1)) && Wall(c + new Vector2I(1, 1))) is { } behind)
        {
            await MoveTo(player, RasterLayout.World(behind));
            await Snap(dir, "reality-behind-wall.png");
        }

        if (Nearest(r, r.Entrance, c => r.Cells[c.X, c.Y] == CellKind.Hazard && r.Walkable(c + new Vector2I(0, 2))) is { } liquid)
        {
            await MoveTo(player, RasterLayout.World(liquid + new Vector2I(0, 2)));
            await Snap(dir, "reality-liquid.png");
        }

        if (r.Gate.Count > 0)
        {
            Vector2I g = r.Gate[r.Gate.Count / 2];
            Vector2 away = ((Vector2)(g - r.BossCell)).Normalized();
            await MoveTo(player, RasterLayout.World(g) + away * 120f);
            await Snap(dir, "reality-arena.png");
        }

        var camera = player.GetNodeOrNull<Camera2D>("Camera2D");
        Vector2 zoom = camera?.Zoom ?? Vector2.One;
        if (camera is not null)
        {
            camera.Zoom = zoom * 0.5f;
        }

        await MoveTo(player, RasterLayout.World(new Vector2I(r.Width / 2, r.Height / 2)));
        await Snap(dir, "reality-overview.png");
        if (camera is not null)
        {
            camera.Zoom = zoom;
        }

        await MoveTo(player, RasterLayout.World(r.Entrance));
        HoldMonsters(false);
    }

    private static Vector2I? Nearest(RasterLayout r, Vector2I from, Func<Vector2I, bool> ok)
    {
        Vector2I? best = null;
        int bestD = int.MaxValue;
        for (int y = 0; y < r.Height; y++)
        {
            for (int x = 0; x < r.Width; x++)
            {
                var c = new Vector2I(x, y);
                int d = (c - from).LengthSquared();
                if (d < bestD && ok(c))
                {
                    bestD = d;
                    best = c;
                }
            }
        }

        return best;
    }
}
