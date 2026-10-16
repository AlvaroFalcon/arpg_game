#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Net;
using ArpgGame.Run;
using ArpgGame.Tests;

namespace ArpgGame.Tools;

/// <summary>
/// Co-op load bench — docs/design/multiplayer.md -> "Performance". Headless:
///   ./scripts/run_headless.sh res://scenes/tools/CoopBench.tscn -- [--clients=3] [--run-time=350] [--measure=20]
/// The host starts N client processes on localhost (a full party of four by
/// default), jumps the Rift to a late wave, lets the arena fill, then measures
/// for a window and finishes with a Cataclysm (a mass kill) to catch the
/// spike. Host: wire bytes per client per second, physics step time, the
/// replication step's own cost, monsters alive. Clients: frame time and the
/// replica update cost. Every process shares this machine's CPU, so absolute
/// times are pessimistic; bandwidth is exact (ENet's own counters).
/// Results: stdout and user://bench/coop_bench.txt.
/// </summary>
public partial class CoopBench : Node
{
    public override void _Ready()
    {
        var args = new Dictionary<string, string>();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string[] kv = arg.TrimStart('-').Split('=', 2);
            args[kv[0]] = kv.Length == 2 ? kv[1] : "";
        }

        Node driver = args.GetValueOrDefault("coop-role") == "client"
            ? new CoopBenchClient { Port = args["coop-port"].ToInt(), PlayerName = args.GetValueOrDefault("coop-name", "c"), Measure = args.GetValueOrDefault("measure", "20").ToInt() }
            : new CoopBenchHost
            {
                Clients = args.GetValueOrDefault("clients", "3").ToInt(),
                StartAt = args.GetValueOrDefault("run-time", "350").ToFloat(),
                Measure = args.GetValueOrDefault("measure", "20").ToInt(),
            };
        driver.Name = "CoopBenchDriver";
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, driver);
    }

    public static string Stats(IReadOnlyList<double> values, string unit)
    {
        if (values.Count == 0)
        {
            return "n/a";
        }

        var sorted = values.OrderBy(v => v).ToList();
        double p95 = sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.95))];
        return string.Create(CultureInfo.InvariantCulture, $"avg {values.Average():0.0}{unit}, p95 {p95:0.0}{unit}, max {sorted[^1]:0.0}{unit}");
    }
}

public partial class CoopBenchHost : CoopTestDriver
{
    public int Clients { get; set; } = 3;
    public float StartAt { get; set; } = 350f;
    public int Measure { get; set; } = 20;

    protected override string LogPath => "user://bench/coop_bench.txt";

    protected override string Tag => "BENCH";

    private readonly List<int> _pids = new();

    protected override async Task Main()
    {
        DirAccess.MakeDirRecursiveAbsolute("user://bench");
        NetSession session = NetSession.Get(this)!;
        Core.Profile profile = SetUpProfile("Host", "arcanist")!;
        int port = 7700 + (int)(GD.Randi() % 80);
        session.Host(port, profile.Active);
        string[] classes = { "warrior", "rogue", "arcanist" };
        for (int i = 0; i < Clients; i++)
        {
            string[] a =
            {
                "--headless", "--path", ProjectSettings.GlobalizePath("res://"), "res://scenes/tools/CoopBench.tscn", "--quit-after", "100000",
                "--", "--coop-role=client", $"--coop-port={port}", $"--coop-name={classes[i % 3]}{i}", $"--measure={Measure}",
            };
            _pids.Add(OS.CreateProcess(OS.GetExecutablePath(), a));
        }

        await WaitFor(() => session.Members.Count == Clients + 1, 90f);
        Log($"lobby: {session.Members.Count} players");
        session.StartRun();
        await WaitFor(() => Run?.NetHost is { } h && h.ReadyPeers == Clients && Run.State.ClockRunning, 90f);
        RunController run = Run!;
        RiftHost host = run.NetHost!;
        run.State.SkipTo(StartAt);
        run.Director.SyncTimers();
        foreach (RunPlayer rp in run.State.Players)
        {
            rp.Player!.Health!.IgnoreDamage = true;
        }

        Log($"party of {run.State.PartySize}, clock at {StartAt:0}s — filling the arena for 30 s");
        await Seconds(30f);

        var physicsMs = new List<double>();
        var frameMs = new List<double>();
        var stepUs = new List<double>();
        var kbPerClient = new List<double>();
        var enemies = new List<double>();
        var pickups = new List<double>();
        var nodes = new List<double>();
        // TimePhysicsProcess/TimeProcess are the WORST step of the last second
        // (Godot updates them once a second), so they read as per-second maxima.
        // The probes time every physics step's scripts and every frame's wall time.
        var probe = StepProbe.Install(this);
        var wallMs = new List<double>();
        var stepsPerFrame = new List<double>();
        GcSample gc0 = GcSample.Now();
        NetTransport.PopWireBytes(session.Multiplayer.MultiplayerPeer);
        (long snap0, long ev0) = Payload(host, session);
        ulong lastFrame = Time.GetTicksUsec();
        ulong lastSteps = Engine.GetPhysicsFrames();
        probe.Recording = true;
        for (int s = 0; s < Measure; s++)
        {
            ulong until = Time.GetTicksMsec() + 1000;
            while (Time.GetTicksMsec() < until)
            {
                await Frames(1);
                ulong nowUs = Time.GetTicksUsec();
                wallMs.Add((nowUs - lastFrame) / 1000.0);
                lastFrame = nowUs;
                stepsPerFrame.Add(Engine.GetPhysicsFrames() - lastSteps);
                lastSteps = Engine.GetPhysicsFrames();
                physicsMs.Add(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0);
                frameMs.Add(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0);
                stepUs.Add(host.LastStepMicros);
            }

            (long sent, _) = NetTransport.PopWireBytes(session.Multiplayer.MultiplayerPeer);
            kbPerClient.Add(sent / 1024.0 / Clients);
            enemies.Add(GetTree().GetNodesInGroup(CombatQuery.EnemyGroup).Count);
            pickups.Add(GetTree().GetNodesInGroup(RunState.PickupGroup).Count);
            nodes.Add(Performance.GetMonitor(Performance.Monitor.ObjectNodeCount));
        }

        Log($"  pickups on the floor: avg {pickups.Average():0} (all players' instanced drops), nodes: avg {nodes.Average():0}");

        (long snap1, long ev1) = Payload(host, session);
        Log($"steady ({Measure} s, {enemies.Average():0} monsters alive, max {enemies.Max():0}):");
        Log($"  wire upload per client: {CoopBench.Stats(kbPerClient, " KB/s")}");
        Log(string.Create(CultureInfo.InvariantCulture, $"  payload per client: snapshots {(snap1 - snap0) / 1024.0 / Clients / Measure:0.0} KB/s, events {(ev1 - ev0) / 1024.0 / Clients / Measure:0.0} KB/s"));
        probe.Recording = false;
        Log($"  host physics step (monitor = worst step of each second): {CoopBench.Stats(physicsMs, " ms")}");
        Log($"  host process step (monitor = worst of each second): {CoopBench.Stats(frameMs, " ms")}");
        Log($"  host frame wall time: {CoopBench.Stats(wallMs, " ms")}; physics steps per frame avg {stepsPerFrame.Average():0.00}");
        Log($"  host physics scripts per step (probe): {CoopBench.Stats(probe.ScriptMs, " ms")}");
        Log($"  host replication (RiftHost) step: {CoopBench.Stats(stepUs.Select(u => u / 1000.0).ToList(), " ms")}");
        Log($"  host managed {GcSample.Now().Since(gc0, Measure)}");

        // The spike: everything on the host's screen dies at once.
        var spikePhys = new List<double>();
        var spikeStep = new List<double>();
        var spikeWall = new List<double>();
        probe.ScriptMs.Clear();
        probe.Recording = true;
        NetTransport.PopWireBytes(session.Multiplayer.MultiplayerPeer);
        ulong spikeLast = Time.GetTicksUsec();
        int killed = run.State.Nuke(run.Player);
        ulong spikeUntil = Time.GetTicksMsec() + 2000;
        while (Time.GetTicksMsec() < spikeUntil)
        {
            await Frames(1);
            ulong nowUs = Time.GetTicksUsec();
            spikeWall.Add((nowUs - spikeLast) / 1000.0);
            spikeLast = nowUs;
            spikePhys.Add(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0);
            spikeStep.Add(host.LastStepMicros / 1000.0);
        }

        (long spikeSent, _) = NetTransport.PopWireBytes(session.Multiplayer.MultiplayerPeer);
        Log($"Cataclysm: {killed} killed at once");
        Log(string.Create(CultureInfo.InvariantCulture, $"  next 2 s wire upload per client: {spikeSent / 1024.0 / Clients / 2.0:0.0} KB/s"));
        probe.Recording = false;
        Log($"  host physics step (monitor): {CoopBench.Stats(spikePhys, " ms")}; replication step: {CoopBench.Stats(spikeStep, " ms")}");
        Log($"  host frame wall time: {CoopBench.Stats(spikeWall, " ms")}; physics scripts per step: {CoopBench.Stats(probe.ScriptMs, " ms")}");

        await Seconds(4f);
        foreach (string f in DirAccess.GetFilesAt("user://bench").Where(f => f.StartsWith("coop_bench_client_")).OrderBy(f => f))
        {
            using FileAccess? file = FileAccess.Open($"user://bench/{f}", FileAccess.ModeFlags.Read);
            Log(file?.GetAsText().Trim() ?? "");
        }

        foreach (int pid in _pids)
        {
            OS.Kill(pid);
        }

        Log("=== RESULT: PASS ===");
        GetTree().Quit();
    }

    private static (long Snapshots, long Events) Payload(RiftHost host, NetSession session)
    {
        long snap = 0;
        long ev = 0;
        foreach (LobbyMember m in session.Members)
        {
            (long s, long e) = host.BytesTo(m.PeerId);
            snap += s;
            ev += e;
        }

        return (snap, ev);
    }
}

public partial class CoopBenchClient : CoopTestDriver
{
    public int Port { get; set; }
    public string PlayerName { get; set; } = "c";
    public int Measure { get; set; } = 20;

    protected override string LogPath => $"user://bench/coop_bench_client_{PlayerName}.txt";

    protected override string Tag => $"CLIENT {PlayerName}";

    protected override async Task Main()
    {
        NetSession session = NetSession.Get(this)!;
        Core.Profile profile = SetUpProfile(PlayerName, PlayerName.TrimEnd('0', '1', '2', '3'))!;
        await Frames(2);
        session.Join("127.0.0.1", Port, profile.Active);
        await WaitFor(() => Run?.NetClient is not null, 120f);
        RiftClient client = Run!.NetClient!;
        // Wander in a slow circle like a player kiting.
        float angle = PlayerName.GetHashCode() % 7;
        await Seconds(30f);
        var frames = new List<double>();
        var replicaUs = new List<double>();
        var received = new List<double>();
        NetTransport.PopWireBytes(session.Multiplayer.MultiplayerPeer);
        int snapshots = client.Snapshots;
        ulong end = Time.GetTicksMsec() + (ulong)(Measure * 1000 + 2500);
        ulong last = Time.GetTicksUsec();
        ulong nextSecond = Time.GetTicksMsec() + 1000;
        GcSample gc0 = GcSample.Now();
        while (Time.GetTicksMsec() < end && !Run!.State.Ended)
        {
            angle += 0.01f;
            RiftClient.TestMove = Vector2.Right.Rotated(angle) * 0.8f;
            await Frames(1);
            ulong now = Time.GetTicksUsec();
            frames.Add((now - last) / 1000.0);
            last = now;
            replicaUs.Add(client.LastProcessMicros);
            if (Time.GetTicksMsec() >= nextSecond)
            {
                nextSecond += 1000;
                (_, long got) = NetTransport.PopWireBytes(session.Multiplayer.MultiplayerPeer);
                received.Add(got / 1024.0);
            }
        }

        RiftClient.TestMove = null;
        Log($"client {PlayerName}: {client.EnemyCount} replicas, {client.Snapshots - snapshots} snapshot chunks, {client.Corrections} corrections");
        Log($"  frame time: {CoopBench.Stats(frames, " ms")}");
        Log($"  replica update (_Process): {CoopBench.Stats(replicaUs.Select(u => u / 1000.0).ToList(), " ms")}");
        Log($"  wire download: {CoopBench.Stats(received, " KB/s")}");
        Log($"  managed {GcSample.Now().Since(gc0, Measure + 2.5f)}");
    }
}

/// <summary>Times the scripts of every physics step: one node runs first, one last.</summary>
public partial class StepProbe : Node
{
    public bool Recording { get; set; }
    public readonly List<double> ScriptMs = new();
    private StepProbe? _end;
    private ulong _start;

    public static StepProbe Install(Node at)
    {
        var begin = new StepProbe { Name = "StepProbeBegin", ProcessPhysicsPriority = int.MinValue };
        var end = new StepProbe { Name = "StepProbeEnd", ProcessPhysicsPriority = int.MaxValue };
        begin._end = end;
        end._end = begin;
        at.GetTree().Root.AddChild(begin);
        at.GetTree().Root.AddChild(end);
        return end;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (ProcessPhysicsPriority == int.MinValue)
        {
            _start = Time.GetTicksUsec();
        }
        else if (Recording && _end is not null)
        {
            ScriptMs.Add((Time.GetTicksUsec() - _end._start) / 1000.0);
        }
    }
}

/// <summary>Managed allocation and GC counts between two moments.</summary>
public readonly record struct GcSample(long Allocated, int Gen0, int Gen1, int Gen2)
{
    public static GcSample Now() => new(GC.GetTotalAllocatedBytes(false), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

    public string Since(GcSample before, float seconds) => string.Create(CultureInfo.InvariantCulture,
        $"allocations: {(Allocated - before.Allocated) / 1024.0 / seconds:0} KB/s; GCs gen0 {Gen0 - before.Gen0}, gen1 {Gen1 - before.Gen1}, gen2 {Gen2 - before.Gen2}");
}
