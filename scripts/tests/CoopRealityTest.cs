#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Enemies;
using ArpgGame.Net;
using ArpgGame.Run;
using ArpgGame.World;

namespace ArpgGame.Tests;

/// <summary>
/// Headless test of co-op Altered Realities — docs/design/multiplayer.md -> "Testing".
/// Run with
///   ./scripts/run_all_tests.sh CoopRealityTest
///
/// This process hosts and starts one more Godot process on this same scene as a
/// guest (<c>-- --coop-role=client</c>) with a scratch save that really writes. Both
/// go to the host's hideout; the host proposes a tier-3 reality with a pinned seed,
/// the guest joins, and the host judges everything, asking the guest over the party
/// channel (<see cref="NetSession.TestHooks"/>). The log is mirrored to
/// user://test_results/CoopRealityTest.txt (realities use the NavigationServer, so
/// the process may hang at shutdown after passing).
/// </summary>
public partial class CoopRealityTest : Node
{
    public const ulong Seed = 424242;

    public override void _Ready()
    {
        NetSession.TestHooks = true;
        Node driver = CoopTestDriverArgs.Role == "client"
            ? new CoopRealityGuest { Name = "CoopRealityDriver" }
            : new CoopRealityHost { Name = "CoopRealityDriver" };
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, driver);
    }

    /// <summary>A stable hash of a cavern (FNV-1a over its ASCII dump).</summary>
    public static string CavernHash(RasterLayout raster)
    {
        ulong h = 1469598103934665603UL;
        foreach (char ch in raster.Dump())
        {
            h = (h ^ ch) * 1099511628211UL;
        }

        return h.ToString("x16");
    }
}

/// <summary>The host end: starts the guest and judges.</summary>
public partial class CoopRealityHost : CoopTestDriver
{
    protected override string LogPath => "user://test_results/CoopRealityTest.txt";

    protected override string Tag => "REALITY";

    private bool _failed;
    private readonly List<int> _pids = new();
    private readonly Dictionary<(long, int), string> _answers = new();
    private int _questions;

    private void Check(string name, bool ok, string detail)
    {
        Log($"{name}: {(ok ? "PASS" : "FAIL")} — {detail}");
        if (!ok)
        {
            _failed = true;
        }
    }

    public static string GuestLog(string name) => $"user://test_results/CoopRealityTest_{name}.txt";

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

    private async Task<string> Ask(long peer, string question, float timeout = 10f)
    {
        NetSession session = NetSession.Get(this)!;
        int id = ++_questions;
        session.SendTestAsk(peer, id, question);
        string? answer = null;
        await WaitFor(() => _answers.Remove((peer, id), out answer), timeout);
        return answer ?? "";
    }

    private static string Line(Dictionary<string, string> f) => string.Join(" ", f.Select(kv => $"{kv.Key}={kv.Value}"));

    private RealityController? Reality => GetTree().CurrentScene as RealityController;

    private Hideout? Town => GetTree().CurrentScene as Hideout;

    /// <summary>Character experience, from the build (a level-up resets it: tests compare levels too).</summary>
    public static float Xp(Player p) => CharacterBuild.Of(p) is { } b ? b.Data.Experience : -1f;

    public static int Lvl(Player p) => CharacterBuild.Of(p)?.Data.Level ?? -1;

    /// <summary>A deliberate death (the test keeps players out of harm's way otherwise).</summary>
    private static void Kill(Player p)
    {
        p.Health!.IgnoreDamage = false;
        p.Health.ApplyResolvedDamage(1e9f, null);
        p.Health.IgnoreDamage = true;
    }

    private void PureChecks()
    {
        // The multi-target flow field: a 9x1 corridor, players at both ends.
        var grid = new bool[9, 1];
        for (int x = 0; x < 9; x++)
        {
            grid[x, 0] = true;
        }

        var field = new FlowField(grid, 10f, Vector2.Zero);
        field.Update(new List<Vector2> { new(5f, 5f), new(85f, 5f) });
        bool towardLeft = field.Direction(new Vector2(25f, 5f)).X < 0f;
        bool towardRight = field.Direction(new Vector2(65f, 5f)).X > 0f;
        int middle = field.DistanceAt(new Vector2(45f, 5f));
        field.Update(new Vector2(5f, 5f));
        int single = field.DistanceAt(new Vector2(85f, 5f));
        Check("FLOWFIELD-MULTI", towardLeft && towardRight && middle == 4 && single == 8,
            $"left half walks left={towardLeft}, right half right={towardRight}, middle distance {middle} (4), one target again: far end {single} (8)");

        Check("PARTY-RULES-REALITY", PartyRules.RealityPackSize(1) == 1f && Mathf.IsEqualApprox(PartyRules.RealityPackSize(2), 1.2f) && Mathf.IsEqualApprox(PartyRules.RealityPackSize(4), 1.6f)
                && PartyRules.RealityPortals(3, 1) == RealityTiers.Portals(3) && PartyRules.RealityPortals(3, 2) == RealityTiers.Portals(3) + 1 && PartyRules.CoopRealities,
            $"packs ×{PartyRules.RealityPackSize(1)}/{PartyRules.RealityPackSize(2)}/{PartyRules.RealityPackSize(4)}, portals {PartyRules.RealityPortals(3, 1)}/{PartyRules.RealityPortals(3, 2)}");
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

        PureChecks();
        profile.Hideout = new HideoutData();
        HideoutCatalog.EnsureDefaults(profile.Hideout);
        profile.Hideout.GuideSeen = true;
        profile.Active.Level = 20; // below the cap, so kills show as experience
        profile.Active.AddShards(3); // the proposer's shard: spent when the reality starts
        session.TestAnswered += (peer, id, text) => _answers[(peer, id)] = text;

        (Error hosted, int port) = await HostOnFreePort(session, profile.Active);
        Check("HOST-STARTS", hosted == Error.Ok && session.IsHost, $"port {port}: {hosted}");
        if (hosted != Error.Ok)
        {
            return;
        }

        if (FileAccess.FileExists(GuestLog("ana")))
        {
            DirAccess.RemoveAbsolute(GuestLog("ana"));
        }

        _pids.Add(StartProcess("res://scenes/tests/CoopRealityTest.tscn", 30000, "--coop-role=client", $"--coop-port={port}", "--coop-name=ana"));
        bool lobby = await WaitFor(() => session.Members.Count == 2, 60f);
        Check("LOBBY", lobby, string.Join(", ", session.Members.Select(m => $"{m.Name} ({m.PeerId})")));
        if (!lobby)
        {
            return;
        }

        LobbyMember ana = session.Members.First(m => m.Name == "ana");
        session.EnterHideout();
        bool home = await WaitFor(() => Town is not null && session.Members.All(m => session.PlaceOf(m.PeerId) == MemberPlace.Hideout), 40f);
        Check("HIDEOUT", home, $"places={string.Join(",", session.Members.Select(m => session.PlaceOf(m.PeerId)))}");
        if (!home)
        {
            return;
        }

        // --- the host proposes a tier-3 reality from the Mirror; ana joins -------------------------
        await Seconds(0.5f);
        RiftSpec spec = RiftSpec.Reality(3);
        spec.Seed = CoopRealityTest.Seed;
        session.Propose(spec);
        Dictionary<string, string> joined = Fields(await Ask(ana.PeerId, "join"));
        bool inside = await WaitFor(() => Reality is { NetHost: { } h } && h.ReadyPeers == 1, 40f);
        Check("REALITY-PROPOSAL-START", inside && joined.GetValueOrDefault("asked", "").Contains("Tier_3") && session.HostRun?.Spec.Kind == RiftKind.Reality,
            $"host in the reality with ana loaded={inside}; ana: {Line(joined)}");
        if (Reality is not { } reality)
        {
            return;
        }

        foreach (RunPlayer rp in reality.Party.Players)
        {
            rp.Player!.Health!.IgnoreDamage = true; // tests drive every death themselves
        }

        Player host = reality.Player;
        int hostShards = profile.Active.ShardCount(3);
        Player? anaCopy = reality.Party.ByPeer(ana.PeerId)?.Player;
        await Seconds(1.5f);
        Dictionary<string, string> view = Fields(await Ask(ana.PeerId, "view"));
        string hostHash = CoopRealityTest.CavernHash(reality.Raster);
        Check("REALITY-SAME-CAVERN", view.GetValueOrDefault("cavern") == hostHash && reality.Seed == CoopRealityTest.Seed && reality.Raster.Width * RasterLayout.CellPx < 16000 && reality.Raster.Height * RasterLayout.CellPx < 16000,
            $"host {hostHash} ({reality.Layout.Id}, {reality.Raster.Width}x{reality.Raster.Height} cells), ana {view.GetValueOrDefault("cavern")}; seed {reality.Seed}");
        Check("REALITY-PLAYERS", reality.Party.Players.Count == 2 && anaCopy is not null && F(view, "players") == 2f && reality.PartySize == 2,
            $"host party {reality.Party.Players.Count}, ana sees {F(view, "players")}");
        int hostEnemies = GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup).Count;
        Check("REALITY-MONSTERS-ON-CLIENT", reality.MonstersTotal >= 20 && Math.Abs(F(view, "enemies") - hostEnemies) <= 2 && F(view, "total") == reality.MonstersTotal,
            $"host {hostEnemies} monsters alive of {reality.MonstersTotal}; ana {F(view, "enemies")} replicas, total {F(view, "total")}");
        Check("REALITY-RULES", reality.PortalsLeft == PartyRules.RealityPortals(3, 2) && F(view, "portals") == reality.PortalsLeft && reality.Boss is not null && reality.Boss.Stats.Life!.Max > 0f
                && view.GetValueOrDefault("clientside") == "1" && view.GetValueOrDefault("boss") == "1",
            $"pool {reality.PortalsLeft} (ana {F(view, "portals")}), boss life {reality.Boss?.Stats.Life?.Max:0}; ana: {Line(view)}");

        // --- ana walks and casts: the host's copy follows and casts from her buttons -------------------
        Vector2 anaFrom = anaCopy!.GlobalPosition;
        int casts = 0;
        anaCopy.SkillUser.CastStarted += _ => casts++;
        Dictionary<string, string> walked = Fields(await Ask(ana.PeerId, "walk"));
        float moved = anaCopy.GlobalPosition.DistanceTo(anaFrom);
        Check("REALITY-CLIENT-INPUT", moved > 30f && new Vector2(F(walked, "x"), F(walked, "y")).DistanceTo(anaCopy.GlobalPosition) < 40f && casts > 0,
            $"host's copy moved {moved:0} px, {anaCopy.GlobalPosition.DistanceTo(new Vector2(F(walked, "x"), F(walked, "y"))):0} px from ana's own; casts from her held buttons: {casts}; ana: {Line(walked)}");

        // --- a kill: both get the experience; drops are instanced -------------------------------------
        Dictionary<string, string> before = Fields(await Ask(ana.PeerId, "stats"));
        float hostXp = Xp(host);
        int hostLevel = Lvl(host);
        Enemy? victim = GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup).OfType<Enemy>().FirstOrDefault(e => !e.IsBoss && !e.Health.IsDead);
        victim?.Health.ApplyResolvedDamage(1e9f, host);
        await Seconds(1.5f);
        int hiddenOnHost = GetTree().GetNodesInGroup(RunState.PickupGroup).OfType<Pickup>().Count(p => p.OwnerPlayer == anaCopy && p.Visible);
        reality.ForceDropForTest(anaCopy);
        bool shardSent = await WaitFor(() => GetTree().GetNodesInGroup(RunState.PickupGroup).OfType<Pickup>().All(p => p.Kind != PickupKind.Shard || p.OwnerPlayer != anaCopy), 6f);
        await Seconds(1.2f);
        Dictionary<string, string> after = Fields(await Ask(ana.PeerId, "stats"));
        Check("REALITY-XP-SHARED", victim is not null && (F(after, "xp") > F(before, "xp") || F(after, "level") > F(before, "level")) && (Xp(host) > hostXp || Lvl(host) > hostLevel),
            $"ana xp {F(before, "xp")} → {F(after, "xp")} (level {F(before, "level")} → {F(after, "level")}), host xp {hostXp} → {Xp(host)} (level {hostLevel} → {Lvl(host)}), victim level {victim?.MonsterLevel}");
        Check("REALITY-KILL-INSTANCED", shardSent && F(after, "shards") == F(before, "shards") + 1 && profile.Active.ShardCount(3) == hostShards && hiddenOnHost == 0 && F(after, "items") > F(before, "items"),
            $"ana tier-3 shards {F(before, "shards")} → {F(after, "shards")}, host's {hostShards} → {profile.Active.ShardCount(3)}; ana's drops visible on the host: {hiddenOnHost}; ana's items on her floor {F(before, "items")} → {F(after, "items")}");

        // --- nothing pauses: the host opens its pause menu, ana keeps getting snapshots -------------------
        reality.Pause.Open();
        await Frames(2);
        bool paused = GetTree().Paused;
        float snaps0 = F(Fields(await Ask(ana.PeerId, "view")), "snapshots");
        await Seconds(0.6f);
        float snaps1 = F(Fields(await Ask(ana.PeerId, "view")), "snapshots");
        reality.Pause.Close();
        Check("REALITY-NO-PAUSE", !paused && snaps1 > snaps0 + 5, $"host tree paused={paused}; ana's snapshots {snaps0} → {snaps1}");

        // --- a death costs the party a portal; ana comes back at the entrance -------------------------------
        int pool = reality.PortalsLeft;
        Kill(anaCopy);
        bool respawned = await WaitFor(() => anaCopy.Health is { IsDead: false }, 4f);
        await Seconds(0.6f); // the teleport is replayed at its moment on ana's machine
        Dictionary<string, string> fell = Fields(await Ask(ana.PeerId, "view"));
        Check("REALITY-DEATH-POOL", respawned && reality.PortalsLeft == pool - 1 && F(fell, "portals") == pool - 1 && F(fell, "teleports") >= 1f,
            $"pool {pool} → {reality.PortalsLeft} (ana {F(fell, "portals")}); ana back up={respawned}, teleports {F(fell, "teleports")}");

        // --- a broken or hostile host: a teleport into the void is ignored by the client ------------------
        reality.NetHost!.Teleport(ana.PeerId, new Vector2(-4000f, -4000f));
        await Seconds(0.6f);
        Dictionary<string, string> forged = Fields(await Ask(ana.PeerId, "view"));
        Check("REALITY-HOSTILE-TELEPORT", F(forged, "teleports") == F(fell, "teleports") && forged.GetValueOrDefault("cavern") == hostHash,
            $"teleports {F(fell, "teleports")} → {F(forged, "teleports")} (a spot outside the cavern is refused)");

        // --- the host walks into the arena: ana is pulled in, the gate closes on both -----------------------------
        host.GlobalPosition = RasterLayout.World(reality.Raster.BossCell) + new Vector2(0f, 60f);
        bool fight = await WaitFor(() => reality.BossFightActive, 3f);
        await Seconds(0.8f);
        bool anaInside = reality.Raster.BossRegion.Contains(reality.Field.CellOf(anaCopy.GlobalPosition));
        Dictionary<string, string> pulled = Fields(await Ask(ana.PeerId, "view"));
        Check("REALITY-BOSS-PULL", fight && anaInside && pulled.GetValueOrDefault("fight") == "1" && pulled.GetValueOrDefault("inside") == "1" && pulled.GetValueOrDefault("gate") == "1",
            $"fight={fight}; ana's copy inside={anaInside}; ana: {Line(pulled)}");

        // --- the boss falls: ana's clear is recorded on her machine, the exit portal shows there -------------------
        int hostCleared = profile.Active.RealitiesCleared;
        reality.Boss!.Health.ApplyResolvedDamage(1e12f, host);
        bool done = await WaitFor(() => reality.Completed && reality.ExitPortal is not null, 3f);
        await Seconds(1f);
        Dictionary<string, string> cleared = Fields(await Ask(ana.PeerId, "stats"));
        Check("REALITY-CLEARED", done && F(cleared, "cleared") == F(before, "cleared") + 1 && cleared.GetValueOrDefault("portal") == "1" && profile.Active.RealitiesCleared == hostCleared + 1
                && reality.Party.Players.Count == 2 && !reality.BossFightActive,
            $"host clears {hostCleared} → {profile.Active.RealitiesCleared}; ana {F(before, "cleared")} → {F(cleared, "cleared")}, exit portal on ana={cleared.GetValueOrDefault("portal")}");

        // --- ana goes home alone: still in the party, the host keeps playing ---------------------------------------
        Dictionary<string, string> left = Fields(await Ask(ana.PeerId, "leave"));
        bool alone = await WaitFor(() => reality.Party.Players.Count == 1 && session.PlaceOf(ana.PeerId) == MemberPlace.Hideout, 15f);
        Check("REALITY-CLIENT-EXIT", alone && left.GetValueOrDefault("scene") == "Hideout" && left.GetValueOrDefault("active") == "1" && Reality == reality && session.Members.Count == 2
                && F(left, "cleared_saved") == F(cleared, "cleared"),
            $"host party {reality.Party.Players.Count}, ana's place {session.PlaceOf(ana.PeerId)}; ana: {Line(left)}");

        // --- the host closes it: home, phase Hideout ---------------------------------------------------------------------
        reality.Pause.OnLeave?.Invoke();
        bool hostHome = await WaitFor(() => Town is not null && session.PartyPhase == SessionPhase.Hideout && session.PlaceOf(1) == MemberPlace.Hideout, 20f);
        Check("REALITY-HOST-CLOSE", hostHome && session.Active && session.HostRun is null && !session.InRun, $"home={hostHome}, phase={session.PartyPhase}, run={session.HostRun?.Id}");
        if (!hostHome)
        {
            return;
        }

        // --- a second reality collapses on the last portal: both back home ----------------------------------------------
        await Seconds(PartyRules.ProposalCooldownSeconds + 0.5f);
        RiftSpec again = RiftSpec.Reality(1);
        again.Seed = CoopRealityTest.Seed + 1;
        session.Propose(again);
        await Ask(ana.PeerId, "join");
        bool second = await WaitFor(() => Reality is { NetHost: { } h } && h.ReadyPeers == 1, 40f);
        if (Reality is { } r2 && second)
        {
            foreach (RunPlayer rp in r2.Party.Players)
            {
                rp.Player!.Health!.IgnoreDamage = true;
            }

            r2.SetPortalsForTest(1);
            if (r2.Party.ByPeer(ana.PeerId)?.Player is { } anaAgain)
            {
                Kill(anaAgain);
            }
        }

        bool collapsedHome = await WaitFor(() => Town is not null && session.Members.All(m => session.PlaceOf(m.PeerId) == MemberPlace.Hideout) && session.PartyPhase == SessionPhase.Hideout, 25f);
        Dictionary<string, string> anaBack = Fields(await Ask(ana.PeerId, "scene"));
        Check("REALITY-COLLAPSE", second && collapsedHome && anaBack.GetValueOrDefault("scene") == "Hideout" && anaBack.GetValueOrDefault("active") == "1",
            $"second reality={second}, both home={collapsedHome}; ana: {Line(anaBack)}");

    }
}

/// <summary>The guest end: answers the host's questions about what this machine sees.</summary>
public partial class CoopRealityGuest : CoopTestDriver
{
    private string PlayerName => CoopTestDriverArgs.Arg("coop-name") ?? "guest";

    protected override string LogPath => CoopRealityHost.GuestLog(PlayerName);

    protected override string Tag => $"GUEST-{PlayerName.ToUpperInvariant()}";

    private readonly Queue<(int Id, string Question)> _questions = new();
    private Profile _profile = null!;

    private RealityController? Reality => GetTree().CurrentScene as RealityController;

    protected override async Task Main()
    {
        await Frames(2);
        NetSession? session = NetSession.Get(this);
        Profile? profile = Profile.Get(this);
        int port = (CoopTestDriverArgs.Arg("coop-port") ?? "0").ToInt();
        if (session is null || profile is null)
        {
            Log("no autoloads");
            return;
        }

        // A scratch save that really writes (never user://profile.json).
        _profile = profile;
        profile.FilePath = $"user://test_guest_reality_{PlayerName}.json";
        if (FileAccess.FileExists(profile.FilePath))
        {
            DirAccess.RemoveAbsolute(profile.FilePath);
        }

        profile.Persist = true;
        profile.CreateTestCharacter(PlayerName, "rogue");
        profile.Active.Level = 20;
        profile.Hideout = new HideoutData();
        HideoutCatalog.EnsureDefaults(profile.Hideout);
        profile.Hideout.GuideSeen = true;
        profile.Save();

        session.TestAsked += (id, q) => _questions.Enqueue((id, q));
        session.Join("127.0.0.1", port, profile.Active, JoinCode.TestCode);
        bool joined = await WaitFor(() => session.Joined, 60f);
        Log($"joined={joined}");

        while (session.Active || _questions.Count > 0)
        {
            if (_questions.Count == 0)
            {
                await Frames(1);
                continue;
            }

            (int id, string question) = _questions.Dequeue();
            Log($"asked: {question}");
            string answer;
            try
            {
                answer = await Answer(session, question);
            }
            catch (Exception e)
            {
                answer = $"error={e.GetType().Name} message={e.Message.Replace(' ', '_')}";
            }

            Log($"answer: {answer}");
            session.SendTestAnswer(id, answer);
        }

        while (true)
        {
            await Frames(30);
        }
    }

    private async Task<string> Answer(NetSession session, string question)
    {
        switch (question.Split(' ')[0])
        {
            case "scene":
                return $"scene={GetTree().CurrentScene?.Name} active={(session.Active ? 1 : 0)} place={session.LocalPlace}";
            case "join":
            {
                bool asked = await WaitFor(() => session.OpenProposal is not null, 8f);
                string title = session.OpenProposal?.Spec.Describe().Replace(' ', '_') ?? "";
                session.AnswerProposal(true);
                bool loaded = await WaitFor(() => Reality is { NetClient: { HeardFromHost: true } }, 30f);
                return $"asked={(asked ? title : "no")} loaded={(loaded ? 1 : 0)}";
            }

            case "view":
            {
                if (Reality is not { } r)
                {
                    return "scene=" + GetTree().CurrentScene?.Name;
                }

                bool gate = r.Raster.Gate.Count > 0 && !r.Field.IsWalkable(r.Raster.Gate[0]);
                bool inside = r.Raster.BossRegion.Contains(r.Field.CellOf(r.Player.GlobalPosition));
                return $"cavern={CoopRealityTest.CavernHash(r.Raster)} players={r.Party.Players.Count} enemies={r.NetClient?.EnemyCount ?? -1} total={r.MonstersTotal} portals={r.PortalsLeft} "
                    + $"clientside={(NetMode.ClientSide ? 1 : 0)} boss={(r.Boss is not null ? 1 : 0)} snapshots={r.NetClient?.Snapshots ?? 0} teleports={r.NetClient?.Teleports ?? 0} "
                    + $"fight={(r.BossFightActive ? 1 : 0)} inside={(inside ? 1 : 0)} gate={(gate ? 1 : 0)}";
            }

            case "walk":
            {
                RealityController r = Reality!;
                CoopClient.TestMove = Vector2.Right;
                RealityClient.TestHeld = 0x3F;
                await Seconds(0.8f);
                CoopClient.TestMove = null;
                RealityClient.TestHeld = null;
                await Seconds(0.4f);
                return $"x={Num(r.Player.GlobalPosition.X)} y={Num(r.Player.GlobalPosition.Y)}";
            }

            case "stats":
            {
                CharacterData c = _profile.Active;
                float xp = Reality is { } rr ? CoopRealityHost.Xp(rr.Player) : c.Experience;
                int level = Reality is { } rl ? CoopRealityHost.Lvl(rl.Player) : c.Level;
                int items = GetTree().CurrentScene?.GetChildren().OfType<Items.EquipmentItem>().Count() ?? 0;
                return $"xp={xp.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} level={level} shards={c.ShardCount(3)} cleared={c.RealitiesCleared} items={items} portal={(Reality?.ExitPortal is not null ? 1 : 0)}";
            }

            case "leave":
            {
                Reality!.Pause.OnLeave?.Invoke();
                bool home = await WaitFor(() => GetTree().CurrentScene is Hideout && session.LocalPlace == MemberPlace.Hideout, 15f);
                // The clear is in this machine's own save file.
                _profile.Save();
                using var doc = System.Text.Json.JsonDocument.Parse(ReadFile(_profile.FilePath));
                int active = doc.RootElement.GetProperty("Active").GetInt32();
                int saved = doc.RootElement.GetProperty("Characters")[active].GetProperty("RealitiesCleared").GetInt32();
                return $"scene={GetTree().CurrentScene?.Name} home={(home ? 1 : 0)} active={(session.Active ? 1 : 0)} cleared_saved={saved}";
            }
        }

        return "unknown=1";
    }
}
