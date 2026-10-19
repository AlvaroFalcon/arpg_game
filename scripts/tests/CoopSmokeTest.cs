#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Core;
using ArpgGame.Enemies;
using ArpgGame.Net;
using ArpgGame.Run;
using ArpgGame.UI;

namespace ArpgGame.Tests;

/// <summary>
/// Headless co-op test — docs/design/multiplayer.md -> "Testing". Run with
///   ./scripts/run_all_tests.sh CoopSmokeTest
///
/// This process is the host. It starts two more Godot processes running this
/// same scene as clients (<c>-- --coop-role=client</c>) that join on
/// 127.0.0.1, then drives a real Survival Rift over ENet and asserts on what
/// each side sees: the lobby, players on every machine, a host monster
/// appearing on the clients, client input moving its player on the host, a
/// kill seen on the clients, per-player loot, a level-up picked on a client,
/// a client leaving mid-run and the host leaving. Security (docs/design/
/// multiplayer.md -> "Security"): join code, UPnP state machine with a fake
/// router and with none, size and rate limits, character validation, failed-
/// attempt blocking — the attacks come from extra in-process sessions on their
/// own SceneMultiplayer branch (<see cref="CoopTestHost.NewIntruder"/>). Clients answer the host's
/// questions over the run's own event channel; the host judges and prints the
/// RESULT line (also mirrored to user://test_results/CoopSmokeTest.txt).
///
/// The driver lives on the tree root so it survives the scene changes into
/// the run, exactly like the real game's flow (lobby → Run.tscn).
/// </summary>
public partial class CoopSmokeTest : Node
{
    public override void _Ready()
    {
        string role = "host";
        int port = 0;
        string name = "";
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string[] kv = arg.TrimStart('-').Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            switch (kv[0])
            {
                case "coop-role":
                    role = kv[1];
                    break;
                case "coop-port":
                    port = kv[1].ToInt();
                    break;
                case "coop-name":
                    name = kv[1];
                    break;
            }
        }

        Node driver = role == "client"
            ? new CoopTestClient { Port = port, PlayerName = name, Name = "CoopTestDriver" }
            : new CoopTestHost { Name = "CoopTestDriver" };
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, driver);
    }
}

/// <summary>The host end: starts the clients and judges everything.</summary>
public partial class CoopTestHost : CoopTestDriver
{
    protected override string LogPath => "user://test_results/CoopSmokeTest.txt";

    protected override string Tag => "COOP";

    private bool _failed;
    private readonly List<int> _pids = new();
    private readonly Dictionary<long, Queue<string>> _inbox = new();
    private int _port;
    private int _lobbyReports;

    private void Check(string name, bool ok, string detail)
    {
        Log($"{name}: {(ok ? "PASS" : "FAIL")} — {detail}");
        if (!ok)
        {
            _failed = true;
        }
    }

    private static string ClientLog(string name) => $"user://test_results/CoopSmokeTest_{name}.txt";

    protected override async Task Main()
    {
        await Frames(2);
        Log("Ready.");
        try
        {
            await Scenario();
        }
        catch (Exception e)
        {
            Check("NO-EXCEPTIONS", false, e.ToString());
        }

        foreach (int pid in _pids)
        {
            OS.Kill(pid);
        }

        Log($"=== RESULT: {(_failed ? "FAIL" : "PASS")} ===");
        GetTree().Quit(_failed ? 1 : 0);
    }

    private int StartClient(string name)
    {
        foreach (string f in new[] { ClientLog(name) })
        {
            if (FileAccess.FileExists(f))
            {
                DirAccess.RemoveAbsolute(f);
            }
        }

        string[] args =
        {
            "--headless", "--path", ProjectSettings.GlobalizePath("res://"),
            "res://scenes/tests/CoopSmokeTest.tscn", "--quit-after", "20000",
            "--", "--coop-role=client", $"--coop-port={_port}", $"--coop-name={name}",
        };
        int pid = OS.CreateProcess(OS.GetExecutablePath(), args);
        _pids.Add(pid);
        return pid;
    }

    private async Task<string?> Ask(long peer, string question, float timeout = 8f)
    {
        _inbox.TryAdd(peer, new Queue<string>());
        _inbox[peer].Clear();
        Run!.NetHost!.SendTest(peer, question);
        string? answer = null;
        await WaitFor(() => _inbox[peer].TryDequeue(out answer), timeout);
        return answer;
    }

    private async Task Scenario()
    {
        NetSession? session = NetSession.Get(this);
        Profile? profile = SetUpProfile("HostHero", "warrior");
        if (session is null || profile is null)
        {
            Check("AUTOLOADS", false, "NetSession or Profile autoload missing");
            return;
        }

        await SecurityUnitChecks(profile);

        // --- lobby ---------------------------------------------------------------------
        // No router in the cloud: hosting must still work and say why the port isn't open.
        NetSession.GatewayFactory = () => new NoGateway();
        Error hosted = await HostOnFreePort(session, profile);
        Check("HOST-STARTS", hosted == Error.Ok && session.IsHost && session.Members.Count == 1, $"port {_port}: {hosted}, members={session.Members.Count}");
        if (hosted != Error.Ok)
        {
            return;
        }

        bool settled = await WaitFor(() => session.Ports.State != PortMapper.MapState.Working, 10f);
        Check("UPNP-NO-GATEWAY-FALLBACK", settled && session.Ports.State == PortMapper.MapState.Failed
                && session.Ports.Message.Contains($"forward UDP {_port} manually") && session.Ports.Message.Contains("Tailscale") && session.IsHost,
            $"state={session.Ports.State}, message='{session.Ports.Message}', still hosting={session.IsHost}");

        await IntruderChecks(session, profile);

        StartClient("alpha");
        StartClient("beta");
        bool lobby = await WaitFor(() => session.Members.Count == 3, 60f);
        Check("LOBBY-HOST-LISTS-EVERYONE", lobby, string.Join(", ", session.Members.Select(m => $"{m.Name} (peer {m.PeerId}, Lv {m.Level} {m.ClassName})")));
        if (!lobby)
        {
            return;
        }

        LobbyMember alpha = session.Members.First(m => m.Name == "alpha");
        LobbyMember beta = session.Members.First(m => m.Name == "beta");
        // Give the clients a moment to receive the final lobby before the start.
        await Seconds(0.5f);

        // --- into the run: the start pick, for everyone, before anything acts ----------
        session.StartRun();
        bool created = await WaitFor(() => Run?.NetHost is not null, 30f);
        if (created)
        {
            Run!.NetHost!.TestMessage += OnTestMessage;
        }

        bool startOpen = created && await WaitFor(() => Run?.NetHost is { } h && h.ReadyPeers == 2 && h.RoundOpen, 60f);
        Check("RUN-WAITS-FOR-ALL-LOADED", startOpen, $"ready peers={Run?.NetHost?.ReadyPeers}, start round open={Run?.NetHost?.RoundOpen}");
        if (!startOpen)
        {
            return;
        }

        RunController run = Run!;
        RiftHost host = run.NetHost!;
        int enemiesAtStart = GetTree().GetNodesInGroup(CombatQuery.EnemyGroup).Count;
        Check("START-PICK-HOLDS-THE-RUN", host.RoundInfo.Start && GetTree().Paused && !run.State.ClockRunning && !run.Director.Spawning
                && run.State.Elapsed == 0f && enemiesAtStart == 0 && run.LevelUp.PartyRound == host.RoundInfo.Id && run.LevelUp.Offers.Count > 0,
            $"start round={host.RoundInfo.Start}, host paused={GetTree().Paused}, clock running={run.State.ClockRunning}, director spawning={run.Director.Spawning}, "
            + $"elapsed={run.State.Elapsed}, monsters={enemiesAtStart}, host cards={run.LevelUp.Offers.Count}");
        foreach (LobbyMember m in new[] { alpha, beta })
        {
            Dictionary<string, string> f = Fields(await Ask(m.PeerId, "round") ?? "");
            Check($"START-PICK-ON-CLIENT-{m.Name.ToUpperInvariant()}", F(f, "open") == 1f && F(f, "start") == 1f && F(f, "paused") == 1f && F(f, "cards") >= 1f
                    && f.GetValueOrDefault("count") == "0/3" && F(f, "secs") is >= 6f and <= 10f,
                string.Join(" ", f.Select(kv => $"{kv.Key}={kv.Value}")));
        }

        run.LevelUp.ChooseIndex(0);
        Dictionary<string, string> afterHost = Fields(await Ask(beta.PeerId, "round") ?? "");
        Check("PICK-COUNTER-REPLICATES", host.RoundInfo.Picked == 1 && afterHost.GetValueOrDefault("count") == "1/3",
            $"host picked → host counts {host.RoundInfo.Picked}/{host.RoundInfo.Total}, beta shows '{afterHost.GetValueOrDefault("count")}'");
        await Ask(alpha.PeerId, "pick");
        bool twoPicked = await WaitFor(() => host.RoundInfo.Picked == 2, 5f);
        await Seconds(0.5f);
        Check("START-PICK-WAITS-FOR-EVERYONE", twoPicked && host.RoundOpen && GetTree().Paused && !run.State.ClockRunning,
            $"2 of 3 chose: round open={host.RoundOpen}, paused={GetTree().Paused}, clock running={run.State.ClockRunning}");
        await Ask(beta.PeerId, "pick");
        bool begun = await WaitFor(() => !host.RoundOpen && run.State.ClockRunning && !GetTree().Paused, 5f);
        RunPlayer alphaRp = run.State.PlayerByPeer(alpha.PeerId)!;
        RunPlayer betaRp = run.State.PlayerByPeer(beta.PeerId)!;
        Check("START-PICK-DONE-RUN-BEGINS", begun && run.Build.History.Count == 1 && alphaRp.Build!.History.Count == 1 && betaRp.Build!.History.Count == 1,
            $"run began={begun} after {host.LastRoundSeconds:0.0} s; picks on the host: host '{string.Join(",", run.Build.History)}', alpha '{string.Join(",", alphaRp.Build!.History)}', beta '{string.Join(",", betaRp.Build!.History)}'");
        await BuildsMatch("START-PICK-SAME-BUILD", run, alpha, beta);

        // A quiet arena: no director, nobody casting, nobody dying by accident.
        run.Director.Spawning = false;
        foreach (Node n in GetTree().GetNodesInGroup(CombatQuery.EnemyGroup))
        {
            n.QueueFree();
        }

        foreach (RunPlayer rp in run.State.Players)
        {
            rp.Player!.GetNode<PlayerSkillInput>("PlayerSkillInput").InputEnabled = false;
            rp.Player.Health!.IgnoreDamage = true;
        }

        await Seconds(0.5f);

        // --- players on both sides -----------------------------------------------------
        Check("PLAYERS-ON-HOST", run.State.Players.Count == 3 && GetTree().GetNodesInGroup(CombatQuery.PlayerGroup).Count == 3,
            $"run players={run.State.Players.Count}, player nodes={GetTree().GetNodesInGroup(CombatQuery.PlayerGroup).Count}");
        foreach (LobbyMember m in new[] { alpha, beta })
        {
            string? answer = await Ask(m.PeerId, "check_players");
            Dictionary<string, string> f = Fields(answer ?? "");
            Check($"PLAYERS-ON-CLIENT-{m.Name.ToUpperInvariant()}", F(f, "players") == 3f && F(f, "local") == m.PeerId && F(f, "puppets") == 2f,
                answer ?? "no answer");
        }

        await WaitFor(() => _lobbyReports == 2, 5f);
        Check("LOBBY-REPORTED-BY-BOTH-CLIENTS", _lobbyReports == 2, $"{_lobbyReports} of 2 clients reported the lobby");

        // --- a host monster appears on the clients ---------------------------------------
        Player hostPlayer = run.Player;
        Vector2 at = run.Arena.Clamp(hostPlayer.GlobalPosition + new Vector2(0f, -800f), 60f);
        Enemy slime = run.Director.SpawnAt(run.Director.Slime!, at);
        slime.Dormant = true;
        slime.Health.IgnoreDamage = true;
        await Seconds(0.6f);
        foreach (LobbyMember m in new[] { alpha, beta })
        {
            string? answer = await Ask(m.PeerId, $"check_enemy x={Num(at.X)} y={Num(at.Y)}");
            Dictionary<string, string> f = Fields(answer ?? "");
            Check($"ENEMY-REPLICATED-{m.Name.ToUpperInvariant()}", F(f, "count") == 1f && F(f, "dist") < 6f, answer ?? "no answer");
        }

        // --- client input moves its player on the host -----------------------------------
        Vector2 before = alphaRp.Player!.GlobalPosition;
        string? moved = await Ask(alpha.PeerId, "move_right");
        await Seconds(0.4f);
        float dx = alphaRp.Player.GlobalPosition.X - before.X;
        float dy = Mathf.Abs(alphaRp.Player.GlobalPosition.Y - before.Y);
        Dictionary<string, string> mf = Fields(moved ?? "");
        Check("CLIENT-INPUT-MOVES-HOST-COPY", dx > 50f && dy < 20f, $"alpha on the host moved dx={Num(dx)} dy={Num(dy)}; client says {moved ?? "nothing"}");
        await Seconds(0.5f);
        string? where = await Ask(alpha.PeerId, "where");
        Dictionary<string, string> wf = Fields(where ?? "");
        float drift = new Vector2(F(wf, "x"), F(wf, "y")).DistanceTo(alphaRp.Player.GlobalPosition);
        Check("PREDICTION-AGREES-WITH-HOST", drift < 12f, $"client {Num(F(wf, "x"))},{Num(F(wf, "y"))} vs host {Num(alphaRp.Player.GlobalPosition.X)},{Num(alphaRp.Player.GlobalPosition.Y)} (corrections {F(mf, "corrections")})");

        // --- the host kills it; per-player loot ------------------------------------------
        slime.Health.IgnoreDamage = false;
        slime.Health.ApplyResolvedDamage(float.MaxValue / 4f, hostPlayer);
        await Seconds(0.6f);
        int hostOwn = GetTree().GetNodesInGroup(RunState.PickupGroup).OfType<Pickup>().Count(p => p.OwnerPlayer == hostPlayer && p.Visible);
        int hiddenRemote = GetTree().GetNodesInGroup(RunState.PickupGroup).OfType<Pickup>().Count(p => p.OwnerPlayer is { IsLocal: false } && !p.Visible);
        int visibleRemote = GetTree().GetNodesInGroup(RunState.PickupGroup).OfType<Pickup>().Count(p => p.OwnerPlayer is { IsLocal: false } && p.Visible);
        Check("LOOT-INSTANCED-ON-HOST", hostOwn >= 1 && hiddenRemote >= 2 && visibleRemote == 0,
            $"host's own visible drops={hostOwn}, remote drops hidden={hiddenRemote}, remote drops visible={visibleRemote}");
        foreach (LobbyMember m in new[] { alpha, beta })
        {
            int expected = host.PickupsOwnedBy(m.PeerId);
            string? answer = await Ask(m.PeerId, $"check_kill expected={expected}");
            Dictionary<string, string> f = Fields(answer ?? "");
            Check($"KILL-SEEN-{m.Name.ToUpperInvariant()}", F(f, "enemies") == 0f, answer ?? "no answer");
            Check($"OWN-LOOT-ONLY-{m.Name.ToUpperInvariant()}", expected >= 1 && F(f, "pickups") == expected && F(f, "cosmetic") == 1f,
                $"host dropped {expected} for {m.Name} (and {hostOwn} for itself); client shows {F(f, "pickups")}");
        }

        await LevelUpChecks(run, host, alpha, beta);
        await CashOutStayChecks(run, host, alpha, beta);
        await CashOutLeaveModeChecks(run, beta);

        // --- a client leaves mid-pick: the round closes without them ------------------------
        float missing = run.State.ExperienceNeeded(run.State.PartyLevel) - run.State.PartyExperience;
        run.State.AddExperience(betaRp, missing + 0.1f);
        bool leaveRound = await WaitFor(() => host.RoundOpen, 3f);
        run.LevelUp.ChooseIndex(0);
        await Ask(beta.PeerId, "pick");
        bool othersChose = await WaitFor(() => host.RoundInfo.Picked == 2, 5f);
        bool stillHeld = host.RoundOpen && GetTree().Paused;
        string? leaving = await Ask(alpha.PeerId, "leave");
        bool gone = await WaitFor(() => host.Peers == 1 && run.State.Players.Count == 2 && run.State.PlayerByPeer(alpha.PeerId) is null, 10f);
        bool resumed = await WaitFor(() => !host.RoundOpen && !GetTree().Paused, 5f);
        Check("CLIENT-LEAVE-MID-PICK-RESUMES", leaveRound && othersChose && stillHeld && gone && resumed && !run.State.Ended,
            $"round open={leaveRound}, host+beta chose={othersChose}, held while alpha chose nothing={stillHeld}; alpha gone={gone} → resumed={resumed}, run ended={run.State.Ended}");
        Check("CLIENT-LEAVE-HANDLED-ON-HOST", gone && !run.State.Ended && session.Members.Count == 2,
            $"{leaving}; peers={host.Peers}, run players={run.State.Players.Count}, lobby={session.Members.Count}, run ended={run.State.Ended}");
        bool alphaEnded = await WaitFor(() => FileContains(ClientLog("alpha"), "LEFT-PARTY-RESULTS"), 10f);
        Check("CLIENT-LEAVE-HANDLED-ON-CLIENT", alphaEnded, ReadFile(ClientLog("alpha")).Split('\n').LastOrDefault(l => l.Contains("LEFT-PARTY")) ?? "no line");

        // Beta still gets traffic after alpha left, and isn't held by a round.
        string? still = await Ask(beta.PeerId, "check_players");
        Dictionary<string, string> betaRound = Fields(await Ask(beta.PeerId, "round") ?? "");
        Check("PARTY-CONTINUES-AFTER-LEAVE", F(Fields(still ?? ""), "players") == 2f && F(betaRound, "paused") == 0f && F(betaRound, "open") == 0f,
            $"{still ?? "no answer"}; beta round: open={betaRound.GetValueOrDefault("open")} paused={betaRound.GetValueOrDefault("paused")}");

        // --- the party goes home: the host leaves, beta never answers (a timeout counts as leave) ---
        await CashOutLeaveChecks(run, host, beta);

        // --- the host leaves ---------------------------------------------------------------
        session.Leave();
        bool betaHandled = await WaitFor(() => FileContains(ClientLog("beta"), "HOST-LEFT"), 15f);
        string betaLine = ReadFile(ClientLog("beta")).Split('\n').LastOrDefault(l => l.Contains("HOST-LEFT")) ?? "no line";
        Check("HOST-LEAVE-HANDLED-ON-CLIENT", betaHandled && betaLine.Contains("PASS"), betaLine);

        await RateLimitCheck(session, profile);
    }

    /// <summary>The host's copy of each client's build has the same history as the client's own.</summary>
    private async Task BuildsMatch(string name, RunController run, params LobbyMember[] members)
    {
        foreach (LobbyMember m in members)
        {
            if (run.State.PlayerByPeer(m.PeerId) is not { Build: { } build })
            {
                continue;
            }

            string hostSide = string.Join(" ", build.History);
            string? clientSide = await Ask(m.PeerId, "history");
            Check($"{name}-{m.Name.ToUpperInvariant()}", clientSide is not null && clientSide == hostSide, $"host: '{hostSide}' client: '{clientSide}'");
        }
    }

    /// <summary>
    /// docs/design/multiplayer.md -> "Level-ups": one party bar; a level pauses the
    /// whole game on every machine (monsters, telegraphs, the clock); everyone picks;
    /// the counter replicates; the host's timer takes the first card after 10 s; a
    /// burst of levels is rounds back to back; bad picks from a client are refused.
    /// </summary>
    private async Task LevelUpChecks(RunController run, RiftHost host, LobbyMember alpha, LobbyMember beta)
    {
        RunPlayer alphaRp = run.State.PlayerByPeer(alpha.PeerId)!;
        RunPlayer betaRp = run.State.PlayerByPeer(beta.PeerId)!;
        Player hostPlayer = run.Player;
        int level = run.State.PartyLevel;
        float need = run.State.ExperienceNeeded(level);
        Check("PARTY-XP-CURVE", Mathf.IsEqualApprox(need, RunState.ExperienceToNext(level) * PartyRules.PartyExperience(3)) && PartyRules.PartyExperience(1) == 1f,
            $"party of 3 needs {need:0.0} for level {level + 1} (solo {RunState.ExperienceToNext(level):0.0})");

        // Something that would act: an awake slime walking at the host, and a telegraph on it.
        // Far enough that it is still walking when the round opens, and after it.
        Vector2 slimeAt = run.Arena.Clamp(hostPlayer.GlobalPosition + new Vector2(0f, -520f), 60f);
        Enemy slime = run.Director.SpawnAt(run.Director.Slime!, slimeAt);
        slime.Health.IgnoreDamage = true;
        await Seconds(0.4f);

        // Half the bar from alpha's gems, the rest from beta's: one level for everyone.
        float missing = need - run.State.PartyExperience;
        run.State.AddExperience(alphaRp, missing * 0.5f);
        bool earlyRound = host.RoundOpen;
        run.State.AddExperience(betaRp, missing * 0.5f + 0.1f);
        bool open = await WaitFor(() => host.RoundOpen, 3f);
        Check("SHARED-XP-ONE-LEVEL-FOR-ALL", !earlyRound && open && run.State.PartyLevel == level + 1 && run.State.Players.All(rp => rp.Level == level + 1 && rp.PendingLevelUps == 1),
            $"half the bar opened no round={!earlyRound}; party level {level}→{run.State.PartyLevel}; players: {string.Join(", ", run.State.Players.Select(rp => $"Lv {rp.Level} pending {rp.PendingLevelUps}"))}");

        var hit = new HitProfile { Damage = 40f, Type = DamageType.Physical, Faction = Faction.Enemy, Source = slime };
        AreaBurst burst = AreaBurst.Spawn(run, hostPlayer.GlobalPosition, 60f, 0.4f, Colors.Red, hit);
        hostPlayer.Health!.IgnoreDamage = false;
        Vector2 slime0 = slime.GlobalPosition;
        float life0 = hostPlayer.Stats.Life!.Current;
        float clock0 = run.State.Elapsed;
        await Seconds(1.5f);
        Check("PAUSE-FREEZES-HOST", GetTree().Paused && slime.GlobalPosition.DistanceTo(slime0) < 0.01f && hostPlayer.Stats.Life.Current == life0
                && run.State.Elapsed == clock0 && IsInstanceValid(burst) && !burst.IsQueuedForDeletion(),
            $"1.5 s into the round: paused={GetTree().Paused}, slime moved {slime.GlobalPosition.DistanceTo(slime0):0.00} px, host life {life0:0}→{hostPlayer.Stats.Life.Current:0}, "
            + $"clock {clock0:0.00}→{run.State.Elapsed:0.00}, 0.4 s telegraph still pending={IsInstanceValid(burst) && !burst.IsQueuedForDeletion()}");
        foreach (LobbyMember m in new[] { alpha, beta })
        {
            Dictionary<string, string> f = Fields(await Ask(m.PeerId, "frozen") ?? "");
            Check($"PAUSE-ON-CLIENT-{m.Name.ToUpperInvariant()}", F(f, "open") == 1f && F(f, "start") == 0f && F(f, "paused") == 1f && F(f, "level") == level + 1
                    && F(f, "monster_moved") < 0.5f && F(f, "clock_moved") < 0.01f && f.GetValueOrDefault("count") == "0/3",
                string.Join(" ", f.Select(kv => $"{kv.Key}={kv.Value}")));
        }

        // The host chooses, then alpha: still held for beta.
        run.LevelUp.ChooseIndex(0);
        await Ask(alpha.PeerId, "pick");
        bool two = await WaitFor(() => host.RoundInfo.Picked == 2, 5f);
        Dictionary<string, string> betaView = Fields(await Ask(beta.PeerId, "round") ?? "");
        Check("GAME-WAITS-FOR-EVERYONE", two && host.RoundOpen && GetTree().Paused && betaView.GetValueOrDefault("count") == "2/3" && F(betaView, "chosen") == 0f,
            $"{host.RoundInfo.Picked} of 3 chose (last refusal: {host.LastRefusal}): round open={host.RoundOpen}, paused={GetTree().Paused}; beta shows '{betaView.GetValueOrDefault("count")}', beta chosen={betaView.GetValueOrDefault("chosen")}");

        // Beta never chooses: the countdown goes red from 5, ticks, and the host takes beta's first card.
        int betaHistory = betaRp.Build!.History.Count;
        int hostTicksBefore = run.LevelUp.TicksPlayed;
        await WaitFor(() => run.LevelUp.PartySecondsShown is > 0 and <= 4 || !host.RoundOpen, 12f);
        bool hostRed = run.LevelUp.PartyClockUrgent;
        Dictionary<string, string> red = Fields(await Ask(beta.PeerId, "round") ?? "");
        bool closed = await WaitFor(() => !host.RoundOpen, 12f);
        // Resuming grants everyone a moment of invulnerability (owner decision 2026-10-17).
        bool graced = run.State.Players.All(rp => rp.Player?.Health is { InvulnerableTime: > 0f });
        Check("RESUME-GRACE", closed && graced,
            $"after the round closed every player invulnerable={graced} ({string.Join(", ", run.State.Players.Select(rp => $"{rp.PeerId}:{rp.Player?.Health?.InvulnerableTime:0.00}s"))})");
        Check("COUNTDOWN-RED-FROM-5", hostRed && F(red, "urgent") == 1f && F(red, "secs") <= 5f,
            $"host red={hostRed}; beta: secs={red.GetValueOrDefault("secs")} urgent={red.GetValueOrDefault("urgent")}");
        // Ticks only for a player still choosing (owner decision 2026-10-17): beta hears
        // the countdown, the host — who already chose — doesn't.
        Check("COUNTDOWN-TICKS", F(red, "ticks") >= 4f && run.LevelUp.TicksPlayed == hostTicksBefore,
            $"beta (still choosing) heard {red.GetValueOrDefault("ticks")} ticks; the host (chosen) heard {run.LevelUp.TicksPlayed - hostTicksBefore} this round");
        Check("TIMEOUT-AUTO-PICKS", closed && host.LastRoundSeconds is >= 9.5f and <= 11.5f && betaRp.Build.History.Count == betaHistory + 1 && run.State.Players.All(rp => rp.PendingLevelUps == 0),
            $"round closed={closed} after {host.LastRoundSeconds:0.00} s (timeout {PartyRules.LevelUpTimeout} s); beta's build on the host {betaHistory}→{betaRp.Build.History.Count} ('{betaRp.Build.History.LastOrDefault()}')");
        await BuildsMatch("TIMEOUT-SAME-BUILD", run, beta);

        // Resumed: the clock runs, the slime walks, the telegraph lands.
        float clock1 = run.State.Elapsed;
        Vector2 slime1 = slime.GlobalPosition;
        await Seconds(0.7f);
        Check("RESUME-UNFREEZES", !GetTree().Paused && run.State.Elapsed > clock1 + 0.3f && slime.GlobalPosition.DistanceTo(slime1) > 2f && (!IsInstanceValid(burst) || burst.IsQueuedForDeletion()),
            $"paused={GetTree().Paused}, clock {clock1:0.00}→{run.State.Elapsed:0.00}, slime moved {slime.GlobalPosition.DistanceTo(slime1):0.0} px, telegraph landed={!IsInstanceValid(burst) || burst.IsQueuedForDeletion()}, host life {life0:0}→{hostPlayer.Stats.Life.Current:0}");
        Dictionary<string, string> betaAfter = Fields(await Ask(beta.PeerId, "round") ?? "");
        Check("RESUME-ON-CLIENT", F(betaAfter, "open") == 0f && F(betaAfter, "paused") == 0f, string.Join(" ", betaAfter.Select(kv => $"{kv.Key}={kv.Value}")));

        // Downed in co-op: back up on their own after 10 s at half life (owner decision
        // 2026-10-17), the run going on meanwhile. The host is moved away from the
        // others so nobody revives it sooner.
        await Seconds(1.2f); // past the resume grace
        Vector2 others = run.State.Players.Where(rp => !rp.IsLocal && rp.Player is not null).Select(rp => rp.Player!.GlobalPosition).Aggregate(Vector2.Zero, (a, b) => a + b) / Mathf.Max(1, run.State.Players.Count - 1);
        hostPlayer.GlobalPosition = run.Arena.Clamp(others + (hostPlayer.GlobalPosition - others).Normalized() * 500f + new Vector2(1f, 0f), 60f);
        hostPlayer.Health!.ApplyResolvedDamage(float.MaxValue / 4f, null);
        bool fell = hostPlayer.Health.IsDead && !run.State.Ended;
        await Seconds(5f);
        bool stillDown = hostPlayer.Health.IsDead && !run.State.Ended;
        float ring = hostPlayer.ReviveProgress;
        bool up = await WaitFor(() => !hostPlayer.Health.IsDead, 7f);
        float lifeShare = hostPlayer.Stats.Life.Current / Mathf.Max(1f, hostPlayer.Stats.Life.Max);
        Check("DOWNED-RESPAWNS-IN-10S", fell && stillDown && ring is > 0.35f and < 0.75f && up && lifeShare is > 0.4f and <= 0.6f && !run.State.Ended,
            $"fell={fell}, still down at 5 s={stillDown} (ring {ring:0.00}), back up={up} at {lifeShare:P0} life, run ended={run.State.Ended}");
        hostPlayer.Health.IgnoreDamage = true;
        slime.QueueFree();

        // A burst of two levels: two rounds back to back, never unpaused between them.
        level = run.State.PartyLevel;
        float twoLevels = run.State.ExperienceNeeded(level) + run.State.ExperienceNeeded(level + 1) - run.State.PartyExperience + 0.1f;
        run.State.AddExperience(alphaRp, twoLevels);
        bool burstOpen = await WaitFor(() => host.RoundOpen, 3f);
        int firstId = host.RoundInfo.Id;
        int alphaBuild = alphaRp.Build!.History.Count;
        int refused = host.RefusedPicks;
        string? bad = await Ask(alpha.PeerId, "bad_picks");
        await Seconds(0.5f);
        Check("BAD-PICKS-REFUSED", host.RefusedPicks >= refused + 5 && host.RoundInfo.Picked == 0 && alphaRp.Build.History.Count == alphaBuild,
            $"{bad}; host refused {host.RefusedPicks - refused} messages (last: {host.LastRefusal}), chosen={host.RoundInfo.Picked}, alpha's build {alphaBuild}→{alphaRp.Build.History.Count}");
        bool pausedThroughout = true;
        run.LevelUp.ChooseIndex(0);
        await Ask(alpha.PeerId, "pick");
        await Ask(beta.PeerId, "pick");
        bool second = await WaitFor(() => { pausedThroughout &= GetTree().Paused || !host.RoundOpen; return host.RoundOpen && host.RoundInfo.Id != firstId; }, 5f);
        pausedThroughout &= GetTree().Paused;
        int secondId = host.RoundInfo.Id;
        Dictionary<string, string> alphaSecond = Fields(await Ask(alpha.PeerId, "round") ?? "");
        run.LevelUp.ChooseIndex(0);
        await Ask(alpha.PeerId, "pick");
        await Ask(beta.PeerId, "pick");
        bool done = await WaitFor(() => !host.RoundOpen && !GetTree().Paused, 5f);
        Check("LEVEL-BURST-ROUNDS-BACK-TO-BACK", burstOpen && second && pausedThroughout && done && run.State.PartyLevel == level + 2 && run.State.Players.All(rp => rp.PendingLevelUps == 0)
                && F(alphaSecond, "round") == secondId && F(alphaSecond, "chosen") == 0f && F(alphaSecond, "paused") == 1f,
            $"rounds {firstId}→{secondId}, paused between={pausedThroughout}, resumed={done}, level {level}→{run.State.PartyLevel}, alpha saw round {alphaSecond.GetValueOrDefault("round")} (chosen={alphaSecond.GetValueOrDefault("chosen")})");
        await BuildsMatch("LEVEL-UP-SAME-BUILD-BOTH-SIDES", run, alpha, beta);
    }

    private async Task<Error> HostOnFreePort(NetSession session, Profile profile)
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        Error hosted = Error.Failed;
        for (int attempt = 0; attempt < 5 && hosted != Error.Ok; attempt++)
        {
            _port = 7790 + rng.RandiRange(0, 400);
            hosted = session.Host(_port, profile.Active, JoinCode.TestCode);
        }

        await Frames(1);
        return hosted;
    }

    // --- security -----------------------------------------------------------------------

    /// <summary>A second session in this process on its own SceneMultiplayer
    /// branch (/root/IntruderN/NetSession): to the host it is just another
    /// machine, so it can misbehave in ways the real client never would.</summary>
    private async Task<NetSession> NewIntruder()
    {
        var holder = new Node { Name = $"Intruder{++_intruders}" };
        GetTree().Root.AddChild(holder);
        GetTree().SetMultiplayer(new SceneMultiplayer(), holder.GetPath());
        var intruder = new NetSession { Name = "NetSession" };
        holder.AddChild(intruder);
        await Frames(1);
        return intruder;
    }

    private async Task DropIntruder(NetSession intruder)
    {
        Node holder = intruder.GetParent();
        intruder.Leave();
        GetTree().SetMultiplayer(null, holder.GetPath());
        holder.QueueFree();
        await Frames(2);
    }

    private int _intruders;

    private async Task IntruderChecks(NetSession session, Profile profile)
    {
        // Wrong code: refused with the reason, disconnected, never listed.
        NetSession wrong = await NewIntruder();
        int log = session.SecurityLog.Count;
        wrong.Join("127.0.0.1", _port, profile.Active, "ZZZ-ZZZ");
        bool refused = await WaitFor(() => !wrong.Active, 10f);
        Check("WRONG-CODE-REJECTED", refused && wrong.Status.Contains("Wrong join code") && session.Members.Count == 1
                && session.SecurityLog.Skip(log).Any(l => l.Contains("turned away") && l.Contains("Wrong join code")),
            $"intruder: active={wrong.Active}, '{wrong.Status}'; host lobby={session.Members.Count}; host log: {session.SecurityLog.LastOrDefault()}");
        await DropIntruder(wrong);

        // Right code: listed (the real clients below prove the full run with it).
        NetSession right = await NewIntruder();
        right.Join("127.0.0.1", _port, profile.Active, "test-42");
        bool listed = await WaitFor(() => session.Members.Count == 2 && right.Joined, 10f);
        Check("RIGHT-CODE-ACCEPTED", listed, $"lobby={session.Members.Count}, intruder joined={right.Joined}, '{right.Status}' (code typed as 'test-42')");

        // Flood: a joined peer sending a thousand messages in one frame is kicked.
        // (Reliable ones: ENet's throttle would drop part of an unreliable burst
        // on the sender's side, which is not what this checks.)
        if (listed)
        {
            log = session.SecurityLog.Count;
            byte[] batch = new byte[1];
            for (int i = 0; i < 1000; i++)
            {
                right.SendClientEvents(batch);
            }

            bool kicked = await WaitFor(() => session.Members.Count == 1 && !right.Active, 10f);
            Check("FLOOD-KICKED", kicked && session.SecurityLog.Skip(log).Any(l => l.Contains("kicked") && l.Contains("messages a second")),
                $"lobby={session.Members.Count}, intruder active={right.Active}; host log: {session.SecurityLog.LastOrDefault()}");
        }

        await DropIntruder(right);

        // Oversized hello: kicked before the JSON is read.
        NetSession big = await NewIntruder();
        log = session.SecurityLog.Count;
        big.HelloJsonOverride = new string('x', NetLimits.HelloChars + 1000);
        big.Join("127.0.0.1", _port, profile.Active, JoinCode.TestCode);
        bool bigGone = await WaitFor(() => !big.Active, 15f);
        Check("OVERSIZED-HELLO-REJECTED", bigGone && session.Members.Count == 1 && session.SecurityLog.Skip(log).Any(l => l.Contains("hello of")),
            $"intruder active={big.Active}, lobby={session.Members.Count}; host log: {session.SecurityLog.LastOrDefault()}");
        await DropIntruder(big);

        // A character out of range: refused with the reason.
        NetSession cheat = await NewIntruder();
        log = session.SecurityLog.Count;
        var character = System.Text.Json.Nodes.JsonNode.Parse(Profile.SerializeCharacter(profile.Active))!;
        character["Level"] = 999;
        LobbyMember member = NetSession.MemberFor(profile.Active, new Vector2(1280f, 720f));
        member.Character = character.ToJsonString();
        cheat.HelloJsonOverride = System.Text.Json.JsonSerializer.Serialize(member);
        cheat.Join("127.0.0.1", _port, profile.Active, JoinCode.TestCode);
        bool cheatGone = await WaitFor(() => !cheat.Active, 10f);
        Check("INVALID-CHARACTER-REJECTED", cheatGone && cheat.Status.Contains("refused") && session.Members.Count == 1
                && session.SecurityLog.Skip(log).Any(l => l.Contains("level 999")),
            $"intruder: active={cheat.Active}, '{cheat.Status}'; lobby={session.Members.Count}; host log: {session.SecurityLog.LastOrDefault()}");
        await DropIntruder(cheat);
    }

    /// <summary>After the real party is gone: five wrong codes from one address,
    /// then that address is ignored (no answer at all).</summary>
    private async Task RateLimitCheck(NetSession session, Profile profile)
    {
        Error hosted = await HostOnFreePort(session, profile);
        if (hosted != Error.Ok)
        {
            Check("RATE-LIMIT", false, $"could not host again: {hosted}");
            return;
        }

        var reasons = new List<string>();
        for (int i = 0; i < NetLimits.FailedAttempts + 1; i++)
        {
            NetSession attacker = await NewIntruder();
            attacker.Join("127.0.0.1", _port, profile.Active, "ZZZZZZ");
            await WaitFor(() => !attacker.Active, 12f);
            reasons.Add(attacker.Active ? "still connected" : attacker.Status);
            await DropIntruder(attacker);
        }

        bool lastIgnored = reasons[^1] != "still connected" && !reasons[^1].Contains("Wrong join code");
        bool blockedLogged = session.SecurityLog.Any(l => l.Contains("blocked for a minute"));
        bool ignoredLogged = session.SecurityLog.Any(l => l.StartsWith("ignored"));
        Check("FAILED-ATTEMPTS-RATE-LIMITED", reasons.Take(NetLimits.FailedAttempts).All(r => r.Contains("Wrong join code")) && lastIgnored && blockedLogged && ignoredLogged,
            $"answers: {string.Join(" | ", reasons.Select(r => r.Length > 40 ? r[..40] : r))}; blocked logged={blockedLogged}, ignored logged={ignoredLogged}");
        session.Leave();
        Check("UPNP-RELEASED-ON-LEAVE", session.Ports.State == PortMapper.MapState.Off, $"state={session.Ports.State}");
    }

    /// <summary>Logic checks that need no network: join codes, limits, the string
    /// writer, character validation, and the UPnP state machine with a fake router.</summary>
    private async Task SecurityUnitChecks(Profile profile)
    {
        string code = JoinCode.Generate();
        Check("JOIN-CODE-FORMAT", JoinCode.IsWellFormed(code) && JoinCode.Format(code).Length == 7 && JoinCode.Format(code)[3] == '-'
                && JoinCode.Matches(code, JoinCode.Normalize(JoinCode.Format(code).ToLowerInvariant())) && !JoinCode.Matches(code, "")
                && !JoinCode.IsWellFormed(JoinCode.Normalize("K0Q-29M")) && !JoinCode.IsWellFormed(JoinCode.Normalize("K7Q-29")),
            $"{code} shown as {JoinCode.Format(code)}; 'K0Q-29M' (has 0) rejected, 6 chars required");

        var limiter = new AttemptLimiter();
        bool blockedEarly = false;
        for (int i = 0; i < NetLimits.FailedAttempts - 1; i++)
        {
            blockedEarly |= limiter.Fail("1.2.3.4", 10 + i);
        }

        bool blockedAtLimit = limiter.Fail("1.2.3.4", 15);
        Check("ATTEMPT-LIMITER", !blockedEarly && blockedAtLimit && limiter.IsBlocked("1.2.3.4", 30) && !limiter.IsBlocked("5.6.7.8", 30)
                && !limiter.IsBlocked("1.2.3.4", 15 + NetLimits.BlockSeconds + 1),
            $"{NetLimits.FailedAttempts} failures block for {NetLimits.BlockSeconds} s; other addresses unaffected");

        var budget = new PeerBudget();
        int allowed = 0;
        for (int i = 0; i < 1000; i++)
        {
            allowed += budget.Spend(100.0) ? 1 : 0;
        }

        bool steady = true;
        var budget2 = new PeerBudget();
        for (int i = 0; i < 600; i++)
        {
            steady &= budget2.Spend(200.0 + i / 60.0); // ten seconds of 60 Hz input
        }

        Check("MESSAGE-BUDGET", allowed == (int)NetLimits.MessageBurst && steady, $"burst allowed {allowed} of 1000; 60/s for 10 s allowed={steady}");

        var w = new NetWriter(16);
        string huge = new string('a', ushort.MaxValue - 1) + "é" + "tail";
        w.Str(huge);
        var r = new NetReader(w.ToArray());
        string back = r.Str();
        Check("NETWRITER-LONG-STRING", w.Length <= 2 + ushort.MaxValue && back.Length == ushort.MaxValue - 1 && !r.More,
            $"wrote {w.Length} bytes for a {System.Text.Encoding.UTF8.GetByteCount(huge)}-byte string, read back {back.Length} chars (cut before the split 'é')");

        string valid = Profile.SerializeCharacter(profile.Active);
        string? okReason = Profile.ValidateCharacterJson(valid, out string vName, out int vLevel);
        var node = System.Text.Json.Nodes.JsonNode.Parse(valid)!;
        node["Level"] = 0;
        string? lowReason = Profile.ValidateCharacterJson(node.ToJsonString(), out _, out _);
        node["Level"] = 10;
        node["Name"] = new string('n', 500);
        string? nameReason = Profile.ValidateCharacterJson(node.ToJsonString(), out _, out _);
        string? junkReason = Profile.ValidateCharacterJson("{\"Gems\": 5}", out _, out _);
        Check("CHARACTER-VALIDATION", okReason is null && vName == profile.Active.Name && vLevel == profile.Active.Level && lowReason is not null && nameReason is not null && junkReason is not null,
            $"valid → {okReason ?? "ok"}; level 0 → {lowReason}; 500-char name → {nameReason}; junk → {junkReason}; character JSON {valid.Length} chars");

        // A character with quest progress still joins: the payload carries it and the host's check lets it through.
        profile.Active.Quests["odran_wares"] = new Quests.QuestProgress { Status = Quests.QuestStatus.Ready, Count = 1 };
        profile.Active.Quests["ascend"] = new Quests.QuestProgress { Status = Quests.QuestStatus.Active };
        string withQuests = Profile.SerializeCharacter(profile.Active);
        string? questReason = Profile.ValidateCharacterJson(withQuests, out _, out _);
        Characters.CharacterData? rejoined = Profile.DeserializeCharacter(withQuests);
        profile.Active.Quests.Clear();
        Check("CHARACTER-QUESTS-JOIN", questReason is null && withQuests.Contains("\"Quests\"") && rejoined?.Quests["odran_wares"].Status == Quests.QuestStatus.Ready,
            $"a character with two quests: payload carries them={withQuests.Contains("\"Quests\"")}, the host's check → {questReason ?? "ok"}, read back={rejoined?.Quests.Count} entries");

        // UPnP with a fake router: opens, renews, closes; CG-NAT refused; close mid-discovery leaves nothing.
        var good = new FakeGateway();
        var mapper = new PortMapper(() => good);
        mapper.Open(7777);
        await WaitFor(() => { mapper.Poll(Time.GetTicksMsec()); return mapper.State != PortMapper.MapState.Working; }, 5f);
        bool open = mapper.State == PortMapper.MapState.Open && mapper.ExternalAddress == "203.0.113.7";
        mapper.RenewSoon();
        mapper.Poll(Time.GetTicksMsec());
        mapper.CloseAndWait(2000);
        string calls = string.Join(", ", good.Calls);
        Check("UPNP-OPEN-RENEW-CLOSE", open && calls == $"discover, map 7777 {PortMapper.LeaseSeconds}, map 7777 {PortMapper.LeaseSeconds}, unmap 7777" && mapper.State == PortMapper.MapState.Off,
            $"open={open} ({mapper.Message}); router calls: {calls}");

        var cgnat = new FakeGateway { External = "100.72.4.9" };
        var mapper2 = new PortMapper(() => cgnat);
        mapper2.Open(7777);
        await WaitFor(() => { mapper2.Poll(Time.GetTicksMsec()); return mapper2.State != PortMapper.MapState.Working; }, 5f);
        Check("UPNP-CGNAT-FALLBACK", mapper2.State == PortMapper.MapState.Failed && mapper2.Message.Contains("CG-NAT") && mapper2.Message.Contains("forward UDP 7777 manually") && !cgnat.Calls.Any(c => c.StartsWith("map")),
            $"{mapper2.State}: {mapper2.Message}; router calls: {string.Join(", ", cgnat.Calls)}");
        mapper2.CloseAndWait(1000);

        var slow = new FakeGateway { DelayMs = 400, PermanentOnly = true };
        var mapper3 = new PortMapper(() => slow);
        mapper3.Open(7777);
        await Frames(2);
        mapper3.CloseAndWait(3000);
        for (int i = 0; i < 3; i++)
        {
            mapper3.Poll(Time.GetTicksMsec());
            await Frames(1);
        }

        string calls3 = string.Join(", ", slow.Calls);
        Check("UPNP-CLOSE-DURING-DISCOVERY", mapper3.State == PortMapper.MapState.Off && calls3 == "discover",
            $"state={mapper3.State}; router calls: {calls3} (closed while discovering: nothing mapped, so nothing to remove or wait for)");

        // Permanent-only router, no close: the lease is retried as 0 and the lobby says so.
        var permanent = new FakeGateway { PermanentOnly = true };
        var mapper4 = new PortMapper(() => permanent);
        mapper4.Open(7777);
        await WaitFor(() => { mapper4.Poll(Time.GetTicksMsec()); return mapper4.State != PortMapper.MapState.Working; }, 5f);
        bool permanentOpen = mapper4.State == PortMapper.MapState.Open && mapper4.Permanent;
        mapper4.CloseAndWait(2000);
        string calls4 = string.Join(", ", permanent.Calls);
        Check("UPNP-PERMANENT-ONLY", permanentOpen && calls4 == $"discover, map 7777 {PortMapper.LeaseSeconds}, map 7777 0, unmap 7777",
            $"open={permanentOpen}, permanent={mapper4.Permanent}; router calls: {calls4}");

        // A router that stops renewing: the lobby falls back to the manual-forwarding advice.
        var flaky = new FakeGateway();
        var mapper5 = new PortMapper(() => flaky);
        mapper5.Open(7777);
        await WaitFor(() => { mapper5.Poll(Time.GetTicksMsec()); return mapper5.State != PortMapper.MapState.Working; }, 5f);
        flaky.FailMaps = true;
        mapper5.RenewSoon();
        await WaitFor(() => { mapper5.Poll(Time.GetTicksMsec()); return mapper5.State != PortMapper.MapState.Open; }, 5f);
        Check("UPNP-RENEW-FAILURE", mapper5.State == PortMapper.MapState.Failed && mapper5.Message.Contains("forward UDP 7777 manually"),
            $"{mapper5.State}: {mapper5.Message}");
        mapper5.CloseAndWait(2000);

        Check("PUBLIC-ADDRESS-CHECK", PortMapper.IsPublicAddress("203.0.113.7") && !PortMapper.IsPublicAddress("100.64.0.1") && !PortMapper.IsPublicAddress("192.168.1.1")
                && !PortMapper.IsPublicAddress("10.0.0.1") && !PortMapper.IsPublicAddress("172.20.0.1") && !PortMapper.IsPublicAddress("") && PortMapper.IsPublicAddress("2001:db8::1") == true,
            "public vs private / CG-NAT / empty");
    }

    private void OnTestMessage(long peer, string message)
    {
        if (message.StartsWith("CHECK "))
        {
            // CHECK <name> <PASS|FAIL> <detail...>
            string[] parts = message.Split(' ', 4);
            if (parts.Length > 1 && parts[1].StartsWith("LOBBY-SEEN"))
            {
                _lobbyReports++;
            }

            Check(parts.Length > 1 ? parts[1] : "CLIENT", parts.Length > 2 && parts[2] == "PASS", parts.Length > 3 ? $"(peer {peer}) {parts[3]}" : $"peer {peer}");
            return;
        }

        _inbox.TryAdd(peer, new Queue<string>());
        _inbox[peer].Enqueue(message);
    }

    private static bool FileContains(string path, string text) => ReadFile(path).Contains(text);
}

/// <summary>A client end: joins, answers the host's questions, reports how it ended.</summary>
public partial class CoopTestClient : CoopTestDriver
{
    public int Port { get; set; }

    public string PlayerName { get; set; } = "client";

    protected override string LogPath => $"user://test_results/CoopSmokeTest_{PlayerName}.txt";

    protected override string Tag => $"COOP-{PlayerName.ToUpperInvariant()}";

    private readonly Queue<string> _questions = new();
    private bool _leaving;

    protected override async Task Main()
    {
        await Frames(2);
        NetSession? session = NetSession.Get(this);
        Profile? profile = SetUpProfile(PlayerName, PlayerName == "alpha" ? "rogue" : "arcanist");
        if (session is null || profile is null)
        {
            Log("no autoloads");
            return;
        }

        session.Ended += reason =>
        {
            if (!_leaving)
            {
                bool ended = Run?.State.Ended ?? false;
                Log($"HOST-LEFT: {(ended ? "PASS" : "FAIL")} — '{reason}', run ended={ended}, session active={session.Active}");
            }
        };
        session.Join("127.0.0.1", Port, profile.Active, JoinCode.TestCode);
        bool joined = await WaitFor(() => session.Joined && session.Members.Count == 3, 60f);
        Log($"lobby: joined={joined}, members={string.Join(", ", session.Members.Select(m => m.Name))}");
        bool inRun = await WaitFor(() => Run?.NetClient is not null, 60f);
        if (!inRun)
        {
            Log("never reached the run");
            return;
        }

        RunController run = Run!;
        run.NetClient!.TestMessage += message => _questions.Enqueue(message);
        await WaitFor(() => run.NetClient.HeardFromHost, 60f);
        run.NetClient.SendTest($"CHECK LOBBY-SEEN-BY-{PlayerName.ToUpperInvariant()} {(joined ? "PASS" : "FAIL")} members={session.Members.Count}");

        while (true)
        {
            if (_questions.Count == 0)
            {
                await Frames(1);
                continue;
            }

            string question = _questions.Dequeue();
            Log($"asked: {question}");
            string answer = await Answer(run, session, question);
            if (answer.Length > 0)
            {
                Log($"answer: {answer}");
                run.NetClient?.SendTest(answer);
            }

            if (_leaving)
            {
                await Seconds(0.3f);
                session.Leave();
                run.NetClient?.OnHostLost("You left the party.");
                await Seconds(0.5f);
                Log($"LEFT-PARTY-RESULTS: run ended={run.State.Ended}, session active={session.Active}");
                return;
            }
        }
    }

    private string RoundLine(RunController run)
    {
        LevelUpScreen lu = run.LevelUp;
        string count = lu.PartyRound != 0 ? lu.PartyCountText.Replace(" chosen", "").Replace(" ", "") : "-";
        return $"open={(lu.PartyRound != 0 ? 1 : 0)} round={lu.PartyRound} start={(lu.PartyRound != 0 && lu.PartyRoundIsStart ? 1 : 0)} "
            + $"paused={(GetTree().Paused ? 1 : 0)} chosen={(lu.PartyChosenThisRound ? 1 : 0)} count={count} secs={lu.PartySecondsShown} urgent={(lu.PartyClockUrgent ? 1 : 0)} ticks={lu.TicksPlayed} "
            + $"ticks={lu.TicksPlayed} level={run.State.Level} cards={lu.Offers.Count}";
    }

    private async Task<string> Answer(RunController run, NetSession session, string question)
    {
        Dictionary<string, string> f = Fields(question);
        RiftClient client = run.NetClient!;
        switch (question.Split(' ')[0])
        {
            case "check_players":
            {
                int players = GetTree().GetNodesInGroup(CombatQuery.PlayerGroup).Count;
                int puppets = GetTree().GetNodesInGroup(CombatQuery.PlayerGroup).OfType<Player>().Count(p => p.Puppet);
                return $"players={players} local={session.LocalPeerId} puppets={puppets} local_is_mine={run.Player.IsLocal && !run.Player.Puppet}";
            }

            case "check_enemy":
            {
                var target = new Vector2(F(f, "x"), F(f, "y"));
                float dist = client.Enemies.Select(e => e.GlobalPosition.DistanceTo(target)).DefaultIfEmpty(float.MaxValue).Min();
                bool replica = client.Enemies.All(e => e.IsReplica);
                return $"count={client.EnemyCount} dist={Num(dist)} replica={replica}";
            }

            case "move_right":
            {
                Vector2 before = run.Player.GlobalPosition;
                RiftClient.TestMove = Vector2.Right;
                await Seconds(1.0f);
                RiftClient.TestMove = null;
                float dx = run.Player.GlobalPosition.X - before.X;
                return $"moved local_dx={Num(dx)} corrections={client.Corrections}";
            }

            case "where":
                return $"x={Num(run.Player.GlobalPosition.X)} y={Num(run.Player.GlobalPosition.Y)}";
            case "check_kill":
            {
                bool cosmetic = GetTree().GetNodesInGroup(RunState.PickupGroup).OfType<Pickup>().All(p => p.Cosmetic);
                int pickups = GetTree().GetNodesInGroup(RunState.PickupGroup).Count;
                return $"enemies={client.EnemyCount} pickups={pickups} tracked={client.PickupCount} cosmetic={(cosmetic ? 1 : 0)} expected={f.GetValueOrDefault("expected")}";
            }

            case "pick":
            {
                bool open = run.LevelUp.IsOpen;
                if (open)
                {
                    run.LevelUp.ChooseIndex(0);
                }

                return $"picked open_before={open} chosen={(run.LevelUp.PartyChosenThisRound ? 1 : 0)} level={run.State.Level}";
            }

            case "round":
                return RoundLine(run);
            case "frozen":
            {
                // Over half a second of the round: does anything this machine draws move?
                Enemy? e = client.Enemies.FirstOrDefault(x => IsInstanceValid(x));
                Vector2 at = e?.GlobalPosition ?? Vector2.Zero;
                float clock = run.State.Elapsed;
                await Seconds(0.5f);
                float moved = e is not null && IsInstanceValid(e) ? e.GlobalPosition.DistanceTo(at) : -1f;
                return $"{RoundLine(run)} monster_moved={Num(moved)} clock_moved={Num(run.State.Elapsed - clock)}";
            }

            case "bad_picks":
            {
                // What a broken or modified client could send in a round: all refused.
                int round = run.LevelUp.PartyRound;
                string real = run.LevelUp.Offers.Count > 0 ? run.LevelUp.Offers[0].Key : "s:spark";
                client.SendPick(round + 100, real);            // wrong round
                client.SendPick(round, "k:not_offered_here");  // not on its row
                client.SendPick(round, new string('p', 300));  // malformed
                var w = new NetWriter(256);
                w.U8(RiftProtocol.ClientOffers);
                w.U16((ushort)round);
                w.U8(50);                                       // too many cards
                for (int i = 0; i < 50; i++)
                {
                    w.Str("p:might:2");
                }

                session.SendClientEvents(w.ToArray());
                w.Clear();
                w.U8(RiftProtocol.ClientOffers);
                w.U16((ushort)round);
                w.U8(1);
                w.Str("zz");                                    // not a card key
                session.SendClientEvents(w.ToArray());
                return $"sent 5 bad round messages for round {round}";
            }

            case "cashout":
            case "cashout_pick":
            case "cashout_bad":
            case "toast":
            case "leave_panel":
                return await CashOutAnswer(run, session, question);
            case "history":
                return string.Join(" ", run.Build.History);
            case "leave":
                _leaving = true;
                return "leaving";
        }

        return $"unknown question '{question}'";
    }
}

/// <summary>A router for the UPnP checks: records every call, can be slow,
/// CG-NATed (private external address) or permanent-lease-only.</summary>
public sealed class FakeGateway : IPortGateway
{
    public string External { get; init; } = "203.0.113.7";
    public int DelayMs { get; init; }
    public bool PermanentOnly { get; init; }
    public ConcurrentQueue<string> Calls { get; } = new();

    public string? Discover()
    {
        if (DelayMs > 0)
        {
            System.Threading.Thread.Sleep(DelayMs);
        }

        Calls.Enqueue("discover");
        return null;
    }

    public string ExternalAddress() => External;

    public bool FailMaps { get; set; }

    public MapResult Map(int port, string description, int leaseSeconds)
    {
        Calls.Enqueue($"map {port} {leaseSeconds}");
        if (FailMaps)
        {
            return MapResult.Failed;
        }

        return PermanentOnly && leaseSeconds != 0 ? MapResult.OnlyPermanent : MapResult.Ok;
    }

    public void Unmap(int port) => Calls.Enqueue($"unmap {port}");
}
