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

/// <summary>Shared helpers for both ends of the co-op test.</summary>
public abstract partial class CoopTestDriver : Node
{
    protected abstract string LogPath { get; }

    protected abstract string Tag { get; }

    private readonly List<string> _lines = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        // Headless runs are otherwise uncapped: --quit-after counts frames, and a
        // real game runs at a frame rate, not as fast as the CPU allows.
        Engine.MaxFps = 60;
        DirAccess.MakeDirRecursiveAbsolute("user://test_results");
        _ = Main();
    }

    protected abstract Task Main();

    protected void Log(string line)
    {
        string text = $"[{Tag}] {line}";
        GD.Print(text);
        _lines.Add(text);
        using FileAccess? f = FileAccess.Open(LogPath, FileAccess.ModeFlags.Write);
        f?.StoreString(string.Join("\n", _lines) + "\n");
    }

    protected async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    protected async Task Seconds(float seconds)
    {
        ulong until = Time.GetTicksMsec() + (ulong)(seconds * 1000f);
        while (Time.GetTicksMsec() < until)
        {
            await Frames(1);
        }
    }

    protected async Task<bool> WaitFor(Func<bool> condition, float timeout)
    {
        ulong until = Time.GetTicksMsec() + (ulong)(timeout * 1000f);
        while (Time.GetTicksMsec() < until)
        {
            if (condition())
            {
                return true;
            }

            await Frames(1);
        }

        return condition();
    }

    protected Profile? SetUpProfile(string name, string classId)
    {
        Profile? profile = Profile.Get(this);
        if (profile is null)
        {
            return null;
        }

        // Never touch the real save: an in-memory test character only.
        profile.Persist = false;
        profile.CreateTestCharacter(name, classId);
        return profile;
    }

    protected RunController? Run => GetTree().CurrentScene as RunController;

    protected static Dictionary<string, string> Fields(string message)
    {
        var map = new Dictionary<string, string>();
        foreach (string part in message.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = part.Split('=', 2);
            if (kv.Length == 2)
            {
                map[kv[0]] = kv[1];
            }
        }

        return map;
    }

    protected static float F(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out string? v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : float.NaN;

    protected static string Num(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
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

        // --- into the run --------------------------------------------------------------
        session.StartRun();
        bool created = await WaitFor(() => Run?.NetHost is not null, 30f);
        if (created)
        {
            Run!.NetHost!.TestMessage += OnTestMessage;
        }

        bool loaded = created && await WaitFor(() => Run?.NetHost is { } h && h.ReadyPeers == 2 && Run.State.ClockRunning, 60f);
        Check("RUN-STARTS-WHEN-ALL-LOADED", loaded, $"ready peers={Run?.NetHost?.ReadyPeers}, clock running={Run?.State.ClockRunning}");
        if (!loaded)
        {
            return;
        }

        RunController run = Run!;
        RiftHost host = run.NetHost!;

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
        RunPlayer alphaRp = run.State.PlayerByPeer(alpha.PeerId)!;
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

        // --- a level-up chosen on a client -----------------------------------------------
        int levelBefore = alphaRp.Level;
        int ranksBefore = alphaRp.Build!.PassiveRanks.Values.Sum() + alphaRp.Build.Skills.Sum(s => s.Level + s.Attached.Count) + alphaRp.Build.Keystones.Count + alphaRp.Build.Skills.Count();
        run.State.AddExperience(alphaRp, RunState.ExperienceToNext(alphaRp.Level) + 0.1f);
        bool warded = await WaitFor(() => alphaRp.Player.Warded, 5f);
        Check("LEVEL-UP-WARDS-THE-CHOOSER", warded && alphaRp.PendingLevelUps == 1 && alphaRp.Level == levelBefore + 1,
            $"warded={warded}, level {levelBefore}→{alphaRp.Level}, pending={alphaRp.PendingLevelUps}");
        string? picked = await Ask(alpha.PeerId, "pick");
        bool consumed = await WaitFor(() => alphaRp.PendingLevelUps == 0 && !alphaRp.Player.Warded, 5f);
        int ranksAfter = alphaRp.Build.PassiveRanks.Values.Sum() + alphaRp.Build.Skills.Sum(s => s.Level + s.Attached.Count) + alphaRp.Build.Keystones.Count + alphaRp.Build.Skills.Count();
        Check("LEVEL-UP-PICK-APPLIED-ON-HOST", consumed && ranksAfter > ranksBefore, $"client: {picked ?? "no answer"}; pending={alphaRp.PendingLevelUps}, build {ranksBefore}→{ranksAfter}");
        string? hostBuilds = string.Join(" ", alphaRp.Build.History);
        string? buildAnswer = await Ask(alpha.PeerId, "history");
        Check("LEVEL-UP-SAME-BUILD-BOTH-SIDES", buildAnswer is not null && buildAnswer.EndsWith(hostBuilds), $"host: '{hostBuilds}' client: '{buildAnswer}'");

        // --- a client leaves mid-run ---------------------------------------------------------
        string? leaving = await Ask(alpha.PeerId, "leave");
        bool gone = await WaitFor(() => host.Peers == 1 && run.State.Players.Count == 2 && run.State.PlayerByPeer(alpha.PeerId) is null, 10f);
        Check("CLIENT-LEAVE-HANDLED-ON-HOST", gone && !run.State.Ended && session.Members.Count == 2,
            $"{leaving}; peers={host.Peers}, run players={run.State.Players.Count}, lobby={session.Members.Count}, run ended={run.State.Ended}");
        bool alphaEnded = await WaitFor(() => FileContains(ClientLog("alpha"), "LEFT-PARTY-RESULTS"), 10f);
        Check("CLIENT-LEAVE-HANDLED-ON-CLIENT", alphaEnded, ReadFile(ClientLog("alpha")).Split('\n').LastOrDefault(l => l.Contains("LEFT-PARTY")) ?? "no line");

        // Beta still gets traffic after alpha left.
        string? still = await Ask(beta.PeerId, "check_players");
        Check("PARTY-CONTINUES-AFTER-LEAVE", F(Fields(still ?? ""), "players") == 2f, still ?? "no answer");

        // --- the host leaves ---------------------------------------------------------------
        session.Leave();
        bool betaHandled = await WaitFor(() => FileContains(ClientLog("beta"), "HOST-LEFT"), 15f);
        string betaLine = ReadFile(ClientLog("beta")).Split('\n').LastOrDefault(l => l.Contains("HOST-LEFT")) ?? "no line";
        Check("HOST-LEAVE-HANDLED-ON-CLIENT", betaHandled && betaLine.Contains("PASS"), betaLine);

        await RateLimitCheck(session, profile);
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
        Check("UPNP-CLOSE-DURING-DISCOVERY", mapper3.State == PortMapper.MapState.Off && calls3 == $"discover, map 7777 {PortMapper.LeaseSeconds}, map 7777 0, unmap 7777",
            $"state={mapper3.State}; router calls: {calls3} (permanent-only router: retried with no lease, then removed)");

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

    private static string ReadFile(string path)
    {
        using FileAccess? f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return f?.GetAsText() ?? "";
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

                return $"picked open_before={open} open_after={run.LevelUp.IsOpen} level={run.State.Level}";
            }

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

    public MapResult Map(int port, string description, int leaseSeconds)
    {
        Calls.Enqueue($"map {port} {leaseSeconds}");
        return PermanentOnly && leaseSeconds != 0 ? MapResult.OnlyPermanent : MapResult.Ok;
    }

    public void Unmap(int port) => Calls.Enqueue($"unmap {port}");
}
