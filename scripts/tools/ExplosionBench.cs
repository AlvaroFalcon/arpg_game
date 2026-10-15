#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Core;
using ArpgGame.Enemies;
using ArpgGame.Run;
using ArpgGame.World;

namespace ArpgGame.Tools;

/// <summary>
/// Mass-death benchmark — docs/design/performance.md. Loads a reality (or the
/// Survival Rift), spawns <c>--count</c> monsters in a tight cluster on the player
/// and kills them in one frame, then records the wall time of every frame of the
/// next <c>--seconds</c>. Run it with <c>--fixed-fps 60</c> so each frame is
/// exactly one simulation step and frames are not throttled: the wall time of a
/// frame is then what it cost.
///
/// <c>-- --mode=reality|rift --kind=volatile|chain|exploder|plain --count=60
/// [--enemy=Slime] [--seconds=2.5] [--seed=7] [--warmup=2] [--tag=label] [--shot=/tmp/x]</c>
/// <list type="bullet">
/// <item>plain — N ordinary monsters die at once.</item>
/// <item>volatile — N monsters with the "volatile" mod (the reality modifier):
/// N delayed blasts land together one second later.</item>
/// <item>chain — the player has Chain Reaction; every monster is left on 1 life
/// and the centre one is killed: the explosions cascade outwards.</item>
/// <item>exploder — N volatile imps spawned on the player prime together and
/// detonate on the same frame.</item>
/// </list>
/// The player ignores damage. Lines go to stdout and to
/// <c>user://bench/explosion.txt</c> (stdout is block-buffered headless).
/// </summary>
public partial class ExplosionBench : Node
{
    private string _mode = "reality";
    private string _kind = "volatile";
    private int _count = 60;
    private string _enemy = "";
    private float _seconds = 2.5f;
    private ulong _seed = 7;
    private string _tag = "";
    private int _warmup = 2;
    private string _shot = "";

    private bool _recording;
    private ulong _last;
    private readonly List<float> _frames = new();
    private readonly List<(float Process, float Physics, int Nodes)> _engine = new();
    private int _peakNodes;
    private int _peakDraws;
    private int _peakObjects;
    private int _kills;
    private readonly List<float> _perKill = new();

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string[] kv = arg.TrimStart('-').Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            switch (kv[0])
            {
                case "mode": _mode = kv[1]; break;
                case "kind": _kind = kv[1]; break;
                case "count": _count = kv[1].ToInt(); break;
                case "enemy": _enemy = kv[1]; break;
                case "seconds": _seconds = kv[1].ToFloat(); break;
                case "seed": _seed = (ulong)kv[1].ToInt(); break;
                case "tag": _tag = kv[1]; break;
                case "warmup": _warmup = kv[1].ToInt(); break;
                case "shot": _shot = kv[1]; break;
            }
        }

        if (_enemy.Length == 0)
        {
            _enemy = _kind == "exploder" ? "Imp" : "Slime";
        }

        ProcessMode = ProcessModeEnum.Always;
        Engine.MaxFps = 0;
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        }


        CallDeferred(nameof(Detach));
    }

    private async Task Guarded()
    {
        try
        {
            await Bench();
        }
        catch (System.Exception e)
        {
            Log($"BENCH ERROR {e}");
            GetTree().Quit();
        }
    }

    private void Detach()
    {
        Node root = GetTree().Root;
        GetParent().RemoveChild(this);
        root.AddChild(this);
        _ = Guarded();
    }

    private void Log(string line)
    {
        GD.Print(line);
        DirAccess.MakeDirRecursiveAbsolute("user://bench");
        const string path = "user://bench/explosion.txt";
        using FileAccess? f = FileAccess.FileExists(path) ? FileAccess.Open(path, FileAccess.ModeFlags.ReadWrite) : FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (f is null)
        {
            return;
        }

        f.SeekEnd();
        f.StoreLine(line);
        f.Flush();
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task Bench()
    {
        Player? player;
        if (_mode == "rift")
        {
            RunLaunchOptions.Reset();
            RunLaunchOptions.StartSkill = "spark";
            GetTree().ChangeSceneToFile(Scenes.Run);
            await Frames(90);
            if (GetTree().CurrentScene is not RunController run)
            {
                Log("BENCH ERROR rift scene failed to load");
                GetTree().Quit();
                return;
            }

            run.Director.Spawning = false;
            player = run.Player;
            if (_kind == "chain")
            {
                run.Build.ChainReaction = true;
            }
        }
        else
        {
            if (Profile.Get(this) is { } profile)
            {
                profile.Persist = false;
                profile.CreateCharacter("Bench", "warrior");
            }

            RealityLaunch.Tier = 1;
            RealityLaunch.LayoutId = null;
            RealityLaunch.Seed = _seed;
            RealityLaunch.Modifiers = new List<string>();
            RealityLaunch.Tutorial = false;
            GetTree().ChangeSceneToFile(Scenes.Reality);
            await Frames(90);
            if (GetTree().CurrentScene is not RealityController reality)
            {
                Log("BENCH ERROR reality scene failed to load");
                GetTree().Quit();
                return;
            }

            player = reality.Player;
            if (_kind == "chain" && Characters.CharacterBuild.Of(player) is { } build)
            {
                build.ChainReaction = true;
            }
        }

        player.Health!.IgnoreDamage = true;
        // The player stands still and casts nothing: only the bench kills monsters.
        player.ProcessMode = ProcessModeEnum.Disabled;
        Log($"BENCH loaded at {Time.GetTicksMsec() / 1000f:0.0}s");
        GetTree().Paused = false;

        // Clear the stage: nothing but the bench's monsters.
        foreach (Node n in GetTree().GetNodesInGroup(CombatQuery.EnemyGroup))
        {
            n.QueueFree();
        }

        await Frames(30);

        // Warm-up: a few deaths of the same kind first, so the measured burst runs
        // warm (JIT, first-load of drop tables and sounds) as it would mid-reality.
        for (int round = 0; round < (_kind == "free" ? 0 : _warmup); round++)
        {
            foreach (Enemy e in Spawn(player))
            {
                Kill(e, player);
            }

            await Frames(150);
        }


        // Baseline: an idle second with the cluster alive.
        List<Enemy> cluster = Spawn(player);
        if (_kind != "exploder")
        {
            await Frames(60);
            _frames.Clear();
            _recording = true;
            await Frames(60);
            _recording = false;
        }

        float[] idle = _frames.ToArray();
        _frames.Clear();

        int gc0 = System.GC.CollectionCount(0), gc1 = System.GC.CollectionCount(1), gc2 = System.GC.CollectionCount(2);
        long alloc0 = System.GC.GetTotalAllocatedBytes();
        var sw = Stopwatch.StartNew();
        int nodesBefore = GetTree().GetNodeCount();
        _peakNodes = nodesBefore;
        int drawsBefore = (int)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        int objectsBefore = (int)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
        _peakDraws = drawsBefore;
        _peakObjects = objectsBefore;
        _kills = 0;
        Enemy.AnyKilled += OnAnyKilled;
        _sw = sw;
        Health.AnyHitResolved += OnAnyHit;
        _recording = true;
        _last = Time.GetTicksUsec();
        if (_kind == "chain")
        {
            // Set again: a level-up during the warm-up rebuilds the character.
            if (Characters.CharacterBuild.Of(player) is { } cb)
            {
                cb.ChainReaction = true;
            }

            foreach (Enemy e in cluster)
            {
                if (e.Stats.Life is { } life)
                {
                    life.Current = 1f;
                }
            }

            Kill(cluster.OrderBy(e => e.GlobalPosition.DistanceSquaredTo(Centre(player))).First(), player);
        }
        else if (_kind == "free")
        {
            // Control: the same monsters removed without dying (node teardown only).
            foreach (Enemy e in cluster)
            {
                e.QueueFree();
            }
        }
        else if (_kind != "exploder")
        {
            foreach (Enemy e in cluster)
            {
                double t0 = sw.Elapsed.TotalMilliseconds;
                _tHit = _tKilled = t0;
                Kill(e, player);
                double t3 = sw.Elapsed.TotalMilliseconds;
                _perKill.Add((float)(t3 - t0));
                _split[0] += _tHit - t0;
                _split[1] += _tKilled - _tHit;
                _split[2] += t3 - _tKilled;
            }
        }

        double killMs = sw.Elapsed.TotalMilliseconds;
        if (_shot.Length > 0 && DisplayServer.GetName() != "headless")
        {
            // A look at the crumble mid-air (windowed runs only), then the blasts landing.
            await Frames(6);
            GetViewport().GetTexture().GetImage().SavePng($"{_shot}-crumble.png");
            await Frames(56);
            GetViewport().GetTexture().GetImage().SavePng($"{_shot}-blasts.png");
            await Frames(Mathf.RoundToInt(_seconds * 60f) - 62);
        }
        else
        {
            await Frames(Mathf.RoundToInt(_seconds * 60f));
        }
        _recording = false;
        Enemy.AnyKilled -= OnAnyKilled;
        Health.AnyHitResolved -= OnAnyHit;

        float[] after = _frames.ToArray();
        var eng = _engine.Skip(_engine.Count - after.Length).ToList();
        string gc = $"gc0={System.GC.CollectionCount(0) - gc0} gc1={System.GC.CollectionCount(1) - gc1} gc2={System.GC.CollectionCount(2) - gc2} allocMB={(System.GC.GetTotalAllocatedBytes() - alloc0) / 1048576.0:0.0}";
        string label = _tag.Length > 0 ? $" tag={_tag}" : "";
        bool view = View3D.HdView.Enabled(this);
        Log($"BENCH mode={_mode} kind={_kind} enemy={_enemy} count={_count} view={(view ? "voxel" : "none")}{label} killCallMs={killMs:0.0} kills={_kills} nodes={nodesBefore}->peak {_peakNodes} draws={drawsBefore}->peak {_peakDraws} objects={objectsBefore}->peak {_peakObjects} {gc}");
        if (_perKill.Count > 0)
        {
            Log($"BENCH   per-kill ms: first={_perKill[0]:0.00} {Summary(_perKill.Skip(1).ToArray())}");
            Log($"BENCH   split ms (all kills): hit+damageNumber={_split[0]:0.0} died->killedHandlers={_split[1]:0.0} afterKilled(burst,puff,free)={_split[2]:0.0}");
        }

        Log($"BENCH   idle  {Summary(idle)}");
        Log($"BENCH   after {Summary(after)}");
        Log($"BENCH   killFrame(#0+#1)={(after.Length > 1 ? after[0] + after[1] : 0):0.0}ms");
        Log($"BENCH   worst frames: {string.Join(" ", after.Select((ms, i) => (ms, i)).OrderByDescending(p => p.ms).Take(6).Select(p => $"#{p.i}:{p.ms:0.0}"))}");
        Log("BENCH   engine (wall/process/physics ms, nodes) first frames: " + string.Join(" | ", eng.Take(4).Select((e, i) => $"#{i} {after[i]:0.0}/{e.Process:0.0}/{e.Physics:0.0} n{e.Nodes}")));
        Log("RESULT: PASS");
        GetTree().Quit();
    }

    private static string Summary(float[] frames)
    {
        if (frames.Length == 0)
        {
            return "n=0";
        }

        float[] s = frames.OrderBy(f => f).ToArray();
        float P(float q) => s[Mathf.Clamp((int)(s.Length * q), 0, s.Length - 1)];
        float over33 = frames.Count(f => f > 33.3f);
        return $"n={s.Length} avg={frames.Average():0.00} p50={P(0.5f):0.00} p95={P(0.95f):0.00} p99={P(0.99f):0.00} max={s[^1]:0.00} >33ms={over33} sum={frames.Sum():0}";
    }

    private void OnAnyKilled(Enemy e)
    {
        _kills++;
        _tKilled = _sw?.Elapsed.TotalMilliseconds ?? 0;
    }

    private void OnAnyHit(Health h, DamagePayload p, float taken) => _tHit = _sw?.Elapsed.TotalMilliseconds ?? 0;

    private Stopwatch? _sw;
    private double _tHit;
    private double _tKilled;
    private readonly double[] _split = new double[3];

    /// <summary>On the player in a reality (walls everywhere else); off to the side in
    /// the open Rift arena so the player does not collect the experience and level up
    /// (the level-up screen pauses the game).</summary>
    private Vector2 Centre(Player player) => player.GlobalPosition + (_mode == "rift" ? new Vector2(420f, 0f) : Vector2.Zero);

    private List<Enemy> Spawn(Player player)
    {
        var scene = GD.Load<PackedScene>($"res://scenes/enemies/{_enemy}.tscn");
        var list = new List<Enemy>();
        Node parent = GetTree().CurrentScene;
        Vector2 centre = Centre(player);
        for (int i = 0; i < _count; i++)
        {
            var enemy = scene.Instantiate<Enemy>();
            parent.AddChild(enemy);
            // Sunflower spiral: a dense disc, ~14 px apart.
            enemy.GlobalPosition = centre + Vector2.Right.Rotated(i * 2.39996f) * 14f * Mathf.Sqrt(i + 0.5f);
            if (_kind == "volatile")
            {
                enemy.ApplyRarity(MonsterRarity.Magic, new[] { MonsterRarities.Mods.First(m => m.Id == "volatile") });
            }

            if (parent is RealityController reality)
            {
                reality.AdoptMonster(enemy);
            }
            else if (parent is RunController run)
            {
                run.State.RegisterEnemy(enemy);
            }

            if (_kind != "exploder")
            {
                // Hold still so the idle baseline is the same every run.
                enemy.Dormant = true;
                enemy.AggroRadius = 0f;
            }

            list.Add(enemy);
        }

        return list;
    }

    private static void Kill(Enemy e, Player player)
    {
        if (!IsInstanceValid(e) || e.Health.IsDead)
        {
            return;
        }

        e.Health.TakeDamage(new DamagePayload(1e9f, DamageType.Physical, player, Faction.Player, SkillTags.None, DamageDeliveryKind.Hit));
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        if (_recording && _last != 0)
        {
            GetTree().Paused = false;
            _frames.Add((now - _last) / 1000f);
            _engine.Add(((float)Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000f, (float)Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000f, (int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)));
            _peakNodes = Mathf.Max(_peakNodes, GetTree().GetNodeCount());
            _peakDraws = Mathf.Max(_peakDraws, (int)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame));
            _peakObjects = Mathf.Max(_peakObjects, (int)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame));
        }

        _last = now;
    }
}
