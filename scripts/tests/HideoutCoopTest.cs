#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Net;
using ArpgGame.UI;
using ArpgGame.World;

namespace ArpgGame.Tests;

/// <summary>
/// Headless test of the party session in the hideout — docs/design/multiplayer.md
/// -> "Testing". Run with
///   ./scripts/run_all_tests.sh HideoutCoopTest
///
/// This process hosts and starts two more Godot processes on this same scene as
/// guests (<c>-- --coop-role=client</c>). Each guest plays a scratch profile that
/// really saves (<c>Persist = true</c>, its own file), so a guest overwriting its
/// own town with the host's would show on disk. The host asks the guests
/// questions over the party channel (<see cref="NetSession.TestHooks"/>) and
/// judges everything; the RESULT line is mirrored to
/// user://test_results/HideoutCoopTest.txt (the hideout uses the NavigationServer,
/// so the process may hang at shutdown after passing).
/// </summary>
public partial class HideoutCoopTest : Node
{
    public override void _Ready()
    {
        NetSession.TestHooks = true;
        Node driver = CoopTestDriverArgs.Role == "client"
            ? new HideoutCoopGuest { Name = "HideoutCoopDriver" }
            : new HideoutCoopHost { Name = "HideoutCoopDriver" };
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, driver);
    }
}

internal static class CoopTestDriverArgs
{
    public static string Role => Arg("coop-role") ?? "host";

    public static string? Arg(string name)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string[] kv = arg.TrimStart('-').Split('=', 2);
            if (kv[0] == name)
            {
                return kv.Length == 2 ? kv[1] : "";
            }
        }

        return null;
    }
}

/// <summary>The host end of the hideout test: starts the guests and judges.</summary>
public partial class HideoutCoopHost : CoopTestDriver
{
    protected override string LogPath => "user://test_results/HideoutCoopTest.txt";

    protected override string Tag => "HIDEOUT";

    private bool _failed;
    private readonly List<int> _pids = new();
    private readonly Dictionary<(long, int), string> _answers = new();
    private int _questions;
    private int _port;

    private void Check(string name, bool ok, string detail)
    {
        Log($"{name}: {(ok ? "PASS" : "FAIL")} — {detail}");
        if (!ok)
        {
            _failed = true;
        }
    }

    public static string GuestLog(string name) => $"user://test_results/HideoutCoopTest_{name}.txt";

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

    private void StartGuest(string name)
    {
        if (FileAccess.FileExists(GuestLog(name)))
        {
            DirAccess.RemoveAbsolute(GuestLog(name));
        }

        _pids.Add(StartProcess("res://scenes/tests/HideoutCoopTest.tscn", 20000, "--coop-role=client", $"--coop-port={_port}", $"--coop-name={name}"));
    }

    private async Task<string> Ask(long peer, string question, float timeout = 8f)
    {
        NetSession session = NetSession.Get(this)!;
        int id = ++_questions;
        session.SendTestAsk(peer, id, question);
        string? answer = null;
        await WaitFor(() => _answers.Remove((peer, id), out answer), timeout);
        return answer ?? "";
    }

    private Hideout? Town => GetTree().CurrentScene as Hideout;

    private static string Line(Dictionary<string, string> f) => string.Join(" ", f.Select(kv => $"{kv.Key}={kv.Value}"));

    private async Task Scenario()
    {
        NetSession? session = NetSession.Get(this);
        Profile? profile = SetUpProfile("HostHero", "warrior");
        if (session is null || profile is null)
        {
            Check("AUTOLOADS", false, "NetSession or Profile autoload missing");
            return;
        }

        ProposalUnitChecks();
        profile.Hideout = new HideoutData();
        HideoutCatalog.EnsureDefaults(profile.Hideout);
        profile.Hideout.GuideSeen = true;
        session.TestAnswered += (peer, id, text) => _answers[(peer, id)] = text;

        (Error hosted, _port) = await HostOnFreePort(session, profile.Active);
        Check("HOST-STARTS", hosted == Error.Ok && session.IsHost, $"port {_port}: {hosted}");
        if (hosted != Error.Ok)
        {
            return;
        }

        StartGuest("ana");
        StartGuest("bo");
        bool lobby = await WaitFor(() => session.Members.Count == 3, 60f);
        Check("LOBBY-THREE", lobby, string.Join(", ", session.Members.Select(m => $"{m.Name} ({m.PeerId})")));
        if (!lobby)
        {
            return;
        }

        LobbyMember ana = session.Members.First(m => m.Name == "ana");
        LobbyMember bo = session.Members.First(m => m.Name == "bo");
        await Seconds(0.5f);

        // --- everyone into the host's hideout --------------------------------------------------
        session.EnterHideout();
        bool home = await WaitFor(() => Town is not null && session.Members.All(m => session.PlaceOf(m.PeerId) == MemberPlace.Hideout), 40f);
        Dictionary<string, string> anaScene = Fields(await Ask(ana.PeerId, "scene"));
        Dictionary<string, string> boScene = Fields(await Ask(bo.PeerId, "scene"));
        Check("HIDEOUT-ENTER", home && session.PartyPhase == SessionPhase.Hideout && anaScene.GetValueOrDefault("scene") == "Hideout" && boScene.GetValueOrDefault("scene") == "Hideout"
                && anaScene.GetValueOrDefault("guest") == "1" && anaScene.GetValueOrDefault("phase") == "Hideout",
            $"host in hideout={Town is not null}, phase={session.PartyPhase}, places={string.Join(",", session.Members.Select(m => session.PlaceOf(m.PeerId)))}; ana: {Line(anaScene)}; bo: {Line(boScene)}");
        if (Town is not { } town)
        {
            return;
        }

        // --- everyone sees everyone -----------------------------------------------------------------
        bool hostSees = await WaitFor(() => town.Party.Puppets.Count == 2, 8f);
        await Seconds(0.5f);
        Dictionary<string, string> anaSees = Fields(await Ask(ana.PeerId, "puppets want=2"));
        Dictionary<string, string> boSees = Fields(await Ask(bo.PeerId, "puppets want=2"));
        Check("HIDEOUT-SEES-PARTY", hostSees && F(anaSees, "puppets") == 2f && F(boSees, "puppets") == 2f,
            $"host puppets={town.Party.Puppets.Count}; ana: {Line(anaSees)}; bo: {Line(boSees)}");

        // --- presence: ana walks; the host and bo see her there -------------------------------------------
        long bytes0 = session.PresenceBytesSent;
        ulong t0 = Time.GetTicksMsec();
        Dictionary<string, string> walked = Fields(await Ask(ana.PeerId, "walk"));
        Vector2 anaAt = new(F(walked, "x"), F(walked, "y"));
        bool hostFollows = await WaitFor(() => town.Party.Puppets.TryGetValue(ana.PeerId, out Player? p) && p.GlobalPosition.DistanceTo(anaAt) < 24f, 1.5f);
        float hostGap = town.Party.Puppets.TryGetValue(ana.PeerId, out Player? hp) ? hp.GlobalPosition.DistanceTo(anaAt) : -1f;
        Dictionary<string, string> boView = Fields(await Ask(bo.PeerId, $"puppet peer={ana.PeerId} x={Num(anaAt.X)} y={Num(anaAt.Y)}"));
        float boGap = new Vector2(F(boView, "x"), F(boView, "y")).DistanceTo(anaAt);
        Check("HIDEOUT-PRESENCE", F(walked, "moved") > 40f && hostFollows && boGap < 24f,
            $"ana walked {F(walked, "moved"):0} px to {Num(anaAt.X)},{Num(anaAt.Y)}; host's puppet {hostGap:0.0} px off, bo's {boGap:0.0} px off");

        // --- the guests show the host's town, not their own -----------------------------------------------
        string hostLayout = LayoutKey(town.Data);
        Dictionary<string, string> anaLayout = Fields(await Ask(ana.PeerId, "layout"));
        Check("HIDEOUT-HOST-LAYOUT", anaLayout.GetValueOrDefault("shown") == Hash(hostLayout) && anaLayout.GetValueOrDefault("own") != Hash(hostLayout),
            $"host {Hash(hostLayout)}; ana shows {anaLayout.GetValueOrDefault("shown")}, ana's own town {anaLayout.GetValueOrDefault("own")} ({town.Data.Props.Count} props)");

        // --- H on a guest opens nothing -------------------------------------------------------------------------
        Dictionary<string, string> edit = Fields(await Ask(ana.PeerId, "edit"));
        Check("HIDEOUT-GUEST-NO-EDIT", edit.GetValueOrDefault("editor") == "0" && edit.GetValueOrDefault("toast") == "1", Line(edit));

        // --- Odran on a guest: the guest's own wallet --------------------------------------------------------
        long hostGold = town.Player.Wallet.GetBalance(Profile.GoldId);
        Dictionary<string, string> bought = Fields(await Ask(ana.PeerId, "buy_gem"));
        await Seconds(0.3f);
        Check("HIDEOUT-NPC-LOCAL", bought.GetValueOrDefault("ok") == "1" && F(bought, "after") < F(bought, "before") && town.Player.Wallet.GetBalance(Profile.GoldId) == hostGold,
            $"ana: {Line(bought)}; host gold {hostGold} → {town.Player.Wallet.GetBalance(Profile.GoldId)}");

        // --- a panel open on a guest: nothing pauses, the others keep moving -----------------------------------
        Dictionary<string, string> modal = Fields(await Ask(bo.PeerId, "open_panel"));
        Vector2 hostFrom = town.Player.GlobalPosition;
        town.Player.Command = new PlayerCommand { Move = Vector2.Left };
        await Seconds(0.5f);
        town.Player.Command = null;
        await Seconds(0.6f);
        Vector2 hostTo = town.Player.GlobalPosition;
        Dictionary<string, string> boSeesHost = Fields(await Ask(bo.PeerId, $"puppet peer=1 x={Num(hostTo.X)} y={Num(hostTo.Y)}"));
        float hostGapOnBo = new Vector2(F(boSeesHost, "x"), F(boSeesHost, "y")).DistanceTo(hostTo);
        Dictionary<string, string> closed = Fields(await Ask(bo.PeerId, "close_panel"));
        Check("HIDEOUT-NO-PAUSE", modal.GetValueOrDefault("open") == "1" && modal.GetValueOrDefault("paused") == "0" && hostFrom.DistanceTo(hostTo) > 40f && hostGapOnBo < 24f
                && boSeesHost.GetValueOrDefault("paused") == "0" && closed.GetValueOrDefault("held") == "1",
            $"bo: {Line(modal)}; host walked {hostFrom.DistanceTo(hostTo):0} px, bo's puppet of the host {hostGapOnBo:0.0} px off (paused={boSeesHost.GetValueOrDefault("paused")}); controls held while open: {closed.GetValueOrDefault("held")}");

        // --- presence bandwidth --------------------------------------------------------------------------------
        double seconds = (Time.GetTicksMsec() - t0) / 1000.0;
        double perClient = (session.PresenceBytesSent - bytes0) / seconds / 2.0;
        Dictionary<string, string> anaCount = Fields(await Ask(ana.PeerId, "puppets"));
        Dictionary<string, string> boCount = Fields(await Ask(bo.PeerId, "puppets"));
        float guestsSent = F(anaCount, "sent") + F(boCount, "sent");
        float guestsGot = F(anaCount, "presence_in") + F(boCount, "presence_in");
        Check("HIDEOUT-PRESENCE-DELIVERY", session.PresenceReceived >= 0.9f * guestsSent && guestsGot >= 0.9f * session.PresenceSent && guestsSent > 20f,
            $"host got {session.PresenceReceived} of the {guestsSent} the guests sent; the guests got {guestsGot} of the host's {session.PresenceSent} (reliable: ENet's throttle drops unreliable ones)");
        Check("HIDEOUT-PRESENCE-BYTES", perClient > 0 && perClient < 1024, $"{perClient:0} B/s of presence per guest (payload) over {seconds:0.0} s");

        // --- a guest saving never writes the host's town into its own -------------------------------------------
        Dictionary<string, string> saved = Fields(await Ask(ana.PeerId, "save"));
        Check("HIDEOUT-GUEST-SAVE-SAFE", saved.GetValueOrDefault("own_kept") == "1" && saved.GetValueOrDefault("gem_kept") == "1", Line(saved));

        await AfterHideoutChecks(session, town, ana, bo);
        town = Town ?? town;

        // --- the host leaves: guests to the main menu with a notice --------------------------------------------
        string[] guests = session.Members.Count > 3 ? new[] { "ana", "bo", "cy" } : new[] { "ana", "bo" };
        session.Leave();
        bool told = await WaitFor(() => guests.All(g => ReadFile(GuestLog(g)).Contains("HOST-LEFT")), 10f);
        string[] lines = guests.Select(g => ReadFile(GuestLog(g)).Split('\n').LastOrDefault(l => l.Contains("HOST-LEFT")) ?? $"{g}: no line").ToArray();
        Check("HIDEOUT-HOST-LEFT", told && lines.All(l => l.Contains("PASS")), string.Join(" | ", lines));

        await InviteChecks(session);
    }

    /// <summary>
    /// "Invite friends" from the hideout's pause menu (docs/design/multiplayer.md -> "Invite friends"):
    /// the host, alone in its hideout, opens it to friends without changing scene; a friend joins
    /// and lands next to it; Stop hosting leaves the host alone in the same town and the friend at
    /// the main menu with a notice.
    /// </summary>
    private async Task InviteChecks(NetSession session)
    {
        if (Town is not { } town)
        {
            Check("INVITE-FROM-HIDEOUT", false, "the host isn't in its hideout after the party closed");
            return;
        }

        town.Pause.Open();
        string solo = string.Join(",", town.Pause.ExtraTexts) + " / " + town.Pause.LeaveButtonText;
        bool pressed = town.Pause.PressExtra("Invite friends");
        await Frames(3);
        PartyOverlay overlay = town.PartyOverlay;
        overlay.Panel.NoUpnp = true;
        overlay.Panel.AutoCode = JoinCode.TestCode;
        bool offline = overlay.IsOpen && overlay.Panel.Current == CoopPanel.View.Choose && !session.Active;
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        for (int attempt = 0; attempt < 5 && !session.IsHost; attempt++)
        {
            _port = 7790 + rng.RandiRange(0, 400);
            overlay.Panel.SetHostPort(_port);
            overlay.Panel.PressHost();
        }

        await Frames(3);
        bool hosting = session.IsHost && session.PartyPhase == SessionPhase.Hideout && Town == town && overlay.Panel.Current == CoopPanel.View.Lobby && town.Pause.NoPause;
        StartGuest("dee");
        bool deeHome = await WaitFor(() => session.Members.FirstOrDefault(m => m.Name == "dee") is { } d && session.PlaceOf(d.PeerId) == MemberPlace.Hideout, 40f);
        LobbyMember? dee = session.Members.FirstOrDefault(m => m.Name == "dee");
        bool seen = await WaitFor(() => town.Party.Puppets.Count == 1, 5f);
        Dictionary<string, string> deeSees = dee is null ? new() : Fields(await Ask(dee.PeerId, "puppets want=1"));
        overlay.Close();
        town.Pause.Open();
        string hostEntries = string.Join(",", town.Pause.ExtraTexts) + " / " + town.Pause.LeaveButtonText;
        town.Pause.Close();
        Dictionary<string, string> deeEntries = dee is null ? new() : Fields(await Ask(dee.PeerId, "pause"));
        Check("INVITE-FROM-HIDEOUT", pressed && offline && hosting && deeHome && seen && F(deeSees, "puppets") == 1f && session.Active,
            $"solo menu '{solo}'; overlay offline={offline} → hosting in place={hosting} (port {_port}); dee home={deeHome}, host sees {town.Party.Puppets.Count}, dee: {Line(deeSees)}");
        Check("PAUSE-ENTRIES", solo == "Invite friends,Edit hideout / Character select" && hostEntries == "Party & invite,Edit hideout / Close the party"
                && deeEntries.GetValueOrDefault("entries") == "Party" && deeEntries.GetValueOrDefault("leave") == "Leave_the_party",
            $"solo '{solo}'; hosting '{hostEntries}'; guest: {Line(deeEntries)}");

        // --- Stop hosting: the host stays, alone, in the same town; the friend goes to the main menu ---------
        town.OpenPartyOverlay();
        overlay.Panel.PressStopHosting();
        bool told = await WaitFor(() => ReadFile(GuestLog("dee")).Contains("HOST-LEFT"), 10f);
        await Frames(5);
        string deeLine = ReadFile(GuestLog("dee")).Split('\n').LastOrDefault(l => l.Contains("HOST-LEFT")) ?? "no line";
        overlay.Close();
        Check("STOP-HOSTING", !session.Active && Town == town && town.Party.Puppets.Count == 0 && !town.Pause.NoPause && overlay.Panel.Current == CoopPanel.View.Choose
                && town.Player.ControlsEnabled && told && deeLine.Contains("PASS"),
            $"host active={session.Active}, same hideout={Town == town}, puppets={town.Party.Puppets.Count}, pause pauses again={!town.Pause.NoPause}, overlay view={overlay.Panel.Current}, controls={town.Player.ControlsEnabled}; {deeLine}");
    }

    /// <summary>
    /// Proposals (docs/design/multiplayer.md -> "Proposals"): a guest proposes, the host
    /// declines (cancelled for all), then accepts with one guest staying (the rift runs for
    /// two; the other waits in the hideout and a late joiner lands next to them), the run
    /// ends and everyone is home again in the same session, and an unanswered host times out.
    /// </summary>
    private async Task AfterHideoutChecks(NetSession session, Hideout town, LobbyMember ana, LobbyMember bo)
    {
        await EditChecks(session, town, ana, bo);

        // --- ana proposes; the host and bo are asked --------------------------------------------
        Dictionary<string, string> proposed = Fields(await Ask(ana.PeerId, "propose"));
        bool hostAsked = await WaitFor(() => town.Party.Popup.Current == RiftProposalPopup.Mode.Asked, 5f);
        Dictionary<string, string> boPopup = Fields(await Ask(bo.PeerId, "popup"));
        Check("PROPOSAL-POPUP", hostAsked && town.Party.Popup.TitleText == "ana is opening the Survival Rift" && town.Party.Popup.NoText == "Decline (cancels it for everyone)"
                && boPopup.GetValueOrDefault("mode") == "Asked" && boPopup.GetValueOrDefault("title") == "ana_is_opening_the_Survival_Rift" && boPopup.GetValueOrDefault("no") == "Stay_in_the_hideout"
                && proposed.GetValueOrDefault("mode") == "Waiting" && F(boPopup, "secs") is > 20f and <= 30f,
            $"host: {town.Party.Popup.Current} '{town.Party.Popup.TitleText}' / '{town.Party.Popup.NoText}'; bo: {Line(boPopup)}; ana: {Line(proposed)}");

        // --- the host declines: cancelled for everyone, nobody moves ---------------------------------
        town.Party.Popup.PressNo();
        bool closed = await WaitFor(() => session.OpenProposal is null, 3f);
        await Seconds(0.3f);
        Dictionary<string, string> anaAfter = Fields(await Ask(ana.PeerId, "popup"));
        Dictionary<string, string> boAfter = Fields(await Ask(bo.PeerId, "popup"));
        Check("PROPOSAL-HOST-DECLINE", closed && anaAfter.GetValueOrDefault("mode") == "Hidden" && boAfter.GetValueOrDefault("mode") == "Hidden"
                && anaAfter.GetValueOrDefault("toast", "").Contains("the_host_declined") && boAfter.GetValueOrDefault("toast", "").Contains("the_host_declined")
                && session.Members.All(m => session.PlaceOf(m.PeerId) == MemberPlace.Hideout) && Town == town,
            $"closed={closed}; ana: {Line(anaAfter)}; bo: {Line(boAfter)}; places={string.Join(",", session.Members.Select(m => session.PlaceOf(m.PeerId)))}");

        // --- again: the host joins, bo stays — the rift runs for two -----------------------------------
        await Seconds(PartyRules.ProposalCooldownSeconds + 0.3f);
        await Ask(ana.PeerId, "propose");
        await WaitFor(() => town.Party.Popup.Current == RiftProposalPopup.Mode.Asked, 5f);
        await Ask(bo.PeerId, "answer accept=0");
        await Seconds(0.3f);
        town.Party.Popup.PressYes();
        bool inRun = await WaitFor(() => Run?.NetHost is { } h && h.Peers == 1 && session.PlaceOf(ana.PeerId) == MemberPlace.Run, 30f);
        Dictionary<string, string> anaRun = Fields(await Ask(ana.PeerId, "scene"));
        Check("PROPOSAL-START", inRun && Run!.State.PartySize == 2 && Run.NetHost!.Peers == 1 && anaRun.GetValueOrDefault("scene") == "Run" && session.HostRun?.Participants.Count == 2,
            $"host in the run={inRun}, party size={Run?.State.PartySize}, links={Run?.NetHost?.Peers}, participants={session.HostRun?.Participants.Count}; ana: {Line(anaRun)}");

        Dictionary<string, string> waiting = Fields(await Ask(bo.PeerId, "waiting"));
        Check("PROPOSAL-DECLINER-WAITS", waiting.GetValueOrDefault("scene") == "Hideout" && waiting.GetValueOrDefault("line", "").Contains("Survival_Rift") && waiting.GetValueOrDefault("clientside") == "0"
                && waiting.GetValueOrDefault("runlink") == "0" && waiting.GetValueOrDefault("inrun") == "0" && F(waiting, "puppets") == 0f,
            Line(waiting));

        // --- someone new joins while the rift runs: they land in the hideout with bo -------------------------
        StartGuest("cy");
        bool cyHome = await WaitFor(() => session.Members.FirstOrDefault(m => m.Name == "cy") is { } c && session.PlaceOf(c.PeerId) == MemberPlace.Hideout, 40f);
        LobbyMember? cy = session.Members.FirstOrDefault(m => m.Name == "cy");
        await Seconds(1f);
        Dictionary<string, string> cySees = cy is null ? new() : Fields(await Ask(cy.PeerId, "puppets want=1"));
        Dictionary<string, string> boSeesCy = Fields(await Ask(bo.PeerId, "puppets want=1"));
        Check("LATE-JOIN-DURING-RUN", cyHome && F(cySees, "puppets") == 1f && F(boSeesCy, "puppets") == 1f,
            $"cy home={cyHome}; cy: {Line(cySees)}; bo: {Line(boSeesCy)}");

        // --- the host ends the run; both go home from the results: same session, everyone together ---------------
        Run!.State.EndRun(false);
        await WaitFor(() => Run?.Results.Showing == true, 5f);
        Dictionary<string, string> anaHome = Fields(await Ask(ana.PeerId, "results_home", 12f));
        Run?.Results.OnHideout?.Invoke();
        bool back = await WaitFor(() => Town is { } t && t != town && session.PartyPhase == SessionPhase.Hideout
            && session.Members.All(m => session.PlaceOf(m.PeerId) == MemberPlace.Hideout), 30f);
        bool together = back && await WaitFor(() => Town!.Party.Puppets.Count == 3, 8f);
        Check("RUN-RETURN", back && together && session.Active && session.IsHost && session.Members.Count == 4 && session.HostRun is null && !session.InRun,
            $"home={back}, host sees {Town?.Party.Puppets.Count} puppets, phase={session.PartyPhase}, members={session.Members.Count}, still hosting={session.IsHost}; ana: {Line(anaHome)}");
        if (!back)
        {
            return;
        }

        // --- the host doesn't answer: cancelled when the time runs out ------------------------------------------
        Hideout home = Town!;
        NetSession.ProposalSecondsOverride = 3f;
        await Ask(bo.PeerId, "propose");
        bool asked = await WaitFor(() => home.Party.Popup.Current == RiftProposalPopup.Mode.Asked, 5f);
        bool timedOut = await WaitFor(() => session.OpenProposal is null, 6f);
        await Seconds(0.3f);
        Dictionary<string, string> boTimeout = Fields(await Ask(bo.PeerId, "popup"));
        NetSession.ProposalSecondsOverride = null;
        Check("PROPOSAL-TIMEOUT", asked && timedOut && boTimeout.GetValueOrDefault("toast", "").Contains("didn't_answer") && Town == home,
            $"asked={asked}, closed after the timeout={timedOut}; bo: {Line(boTimeout)}");
    }

    /// <summary>A spot inside the town where a prop may stand, near the spawn.</summary>
    public static Vector2 OpenSpot(int dx, int dy)
    {
        for (int r = 0; r < 12; r++)
        {
            Vector2 at = HideoutTown.World(HideoutTown.SpawnCell + new Vector2I(dx + r, dy));
            if (HideoutLayoutRules.CanStand(at))
            {
                return at;
            }
        }

        return HideoutTown.World(HideoutTown.SpawnCell);
    }

    /// <summary>The edit checks' rules without a network (docs/design/hideout.md -> "Edit permission").</summary>
    private void EditRuleChecks()
    {
        static HideoutData Town()
        {
            var d = new HideoutData();
            HideoutCatalog.EnsureDefaults(d);
            d.Stock["barrel"] = 1;
            d.Stock[HideoutCatalog.FloorsForSale.First().Id] = 10;
            return d;
        }

        static HideoutData Edit(Action<HideoutData> change)
        {
            HideoutData d = Town();
            change(d);
            return d;
        }

        HideoutData before = Town();
        string floor = HideoutCatalog.FloorsForSale.First().Id;
        string cell = HideoutData.Key(HideoutTown.SpawnCell);
        Vector2 spot = OpenSpot(2, 2);
        var results = new (string Name, bool Ok, string? Why)[]
        {
            ("move", true, HideoutLayoutRules.ValidateEdit(before, Edit(d => d.Find("d2")!.Position = spot))),
            ("place from storage", true, HideoutLayoutRules.ValidateEdit(before, Edit(d => { d.Stock.Remove("barrel"); d.Props.Add(new PlacedProp { Uid = "n1", Kind = "barrel", Position = spot }); }))),
            ("pave from storage", true, HideoutLayoutRules.ValidateEdit(before, Edit(d => { d.Stock[floor] = 9; d.Floors[cell] = floor; }))),
            ("decoration from nowhere", false, HideoutLayoutRules.ValidateEdit(before, Edit(d => d.Props.Add(new PlacedProp { Uid = "n1", Kind = "barrel", Position = spot })))),
            ("tiles from nowhere", false, HideoutLayoutRules.ValidateEdit(before, Edit(d => d.Floors[cell] = floor))),
            ("station removed", false, HideoutLayoutRules.ValidateEdit(before, Edit(d => d.Props.RemoveAll(p => p.Uid == "mirror")))),
            ("into a wall", false, HideoutLayoutRules.ValidateEdit(before, Edit(d => d.Find("d2")!.Position = new Vector2(16f, 16f)))),
            ("kind swapped", false, HideoutLayoutRules.ValidateEdit(before, Edit(d => { PlacedProp p = d.Find("d1")!; p.Kind = p.Kind == "barrel" ? "crate" : "barrel"; }))),
        };
        bool ok = results.All(r => (r.Why is null) == r.Ok);
        Check("EDIT-RULES", ok, string.Join("; ", results.Select(r => $"{r.Name}: {(r.Why is null ? "kept" : $"refused ({r.Why})")}")));
    }

    /// <summary>
    /// Edit permission (D10): ana may edit once the host allows it; while she edits the host
    /// can't; her change lands in the host's town (and bo's view) but never in her own save; a
    /// forged edit is refused without kicking her; taking the right back closes her editor.
    /// </summary>
    private async Task EditChecks(NetSession session, Hideout town, LobbyMember ana, LobbyMember bo)
    {
        EditRuleChecks();
        Profile profile = Profile.Get(this)!;
        town.PartyOverlay.Panel.SetAllowEditing(ana.PeerId, true);
        await Seconds(0.3f);
        Dictionary<string, string> allowed = Fields(await Ask(ana.PeerId, "can_edit"));
        Dictionary<string, string> boAllowed = Fields(await Ask(bo.PeerId, "can_edit"));
        Check("EDIT-ALLOWED", allowed.GetValueOrDefault("ok") == "1" && allowed.GetValueOrDefault("entries", "").Contains("Edit_hideout") && boAllowed.GetValueOrDefault("ok") == "0",
            $"ana: {Line(allowed)}; bo (not allowed): {Line(boAllowed)}");

        Dictionary<string, string> opened = Fields(await Ask(ana.PeerId, "edit_open"));
        town.OpenEditor();
        await Frames(3);
        bool hostRefused = !town.Editing && town.Hud.ToastText.Contains("ana is editing");
        Check("EDIT-LOCK", opened.GetValueOrDefault("editing") == "1" && session.EditHolder == ana.PeerId && hostRefused,
            $"ana: {Line(opened)}; lock holder={session.EditHolder}; host's own editor refused={hostRefused} ('{town.Hud.ToastText}')");

        Vector2 target = OpenSpot(4, 3);
        Dictionary<string, string> moved = Fields(await Ask(ana.PeerId, $"edit_move uid=d2 x={Num(target.X)} y={Num(target.Y)}"));
        bool landed = await WaitFor(() => town.Data.Find("d2") is { } p && p.Position.DistanceTo(target) < 1f, 3f);
        await Seconds(0.5f);
        Dictionary<string, string> boLayout = Fields(await Ask(bo.PeerId, "layout"));
        Dictionary<string, string> anaOwn = Fields(await Ask(ana.PeerId, "own_d2"));
        Check("EDIT-GRANT", landed && ReferenceEquals(profile.Hideout, town.Data) && boLayout.GetValueOrDefault("shown") == Hash(LayoutKey(town.Data))
                && anaOwn.GetValueOrDefault("own_default") == "1" && session.EditHolder == 0 && moved.GetValueOrDefault("editing") == "0",
            $"d2 on the host at {town.Data.Find("d2")?.Position} (target {target}) = {landed}; host profile holds it={ReferenceEquals(profile.Hideout, town.Data)}; bo shows the host's town={boLayout.GetValueOrDefault("shown") == Hash(LayoutKey(town.Data))}; ana's own save untouched: {Line(anaOwn)}; lock {session.EditHolder}");

        string layoutBefore = LayoutKey(town.Data);
        int refusedBefore = session.RefusedEdits;
        Dictionary<string, string> forged = Fields(await Ask(ana.PeerId, "edit_forge"));
        bool refused = await WaitFor(() => session.RefusedEdits > refusedBefore, 3f);
        Check("EDIT-INVALID", refused && LayoutKey(town.Data) == layoutBefore && session.Member(ana.PeerId) is not null && session.EditHolder == 0
                && forged.GetValueOrDefault("toast", "").Contains("weren't_kept") && forged.GetValueOrDefault("editing") == "0",
            $"refused={refused} ('{session.LastEditRefusal}'), host layout unchanged={LayoutKey(town.Data) == layoutBefore}, ana still in the party={session.Member(ana.PeerId) is not null}; ana: {Line(forged)}");

        Dictionary<string, string> again = Fields(await Ask(ana.PeerId, "edit_open"));
        town.PartyOverlay.Panel.SetAllowEditing(ana.PeerId, false);
        Dictionary<string, string> revoked = Fields(await Ask(ana.PeerId, "editing"));
        Check("EDIT-REVOKE", again.GetValueOrDefault("editing") == "1" && revoked.GetValueOrDefault("editing") == "0" && revoked.GetValueOrDefault("toast", "").Contains("took_back")
                && session.EditHolder == 0 && !session.CanEditOf(ana.PeerId) && LayoutKey(town.Data) == layoutBefore,
            $"editing before={again.GetValueOrDefault("editing")}; after the revoke: {Line(revoked)}; lock {session.EditHolder}");
    }

    /// <summary>The proposal's rules without a network (docs/design/multiplayer.md -> "Proposals").</summary>
    private void ProposalUnitChecks()
    {
        bool Home(long _) => true;
        RiftSpec rift = RiftSpec.SurvivalRift();
        var lines = new List<string>();
        bool ok = true;

        var decline = new RiftProposal(1, 7, rift, new long[] { 1, 7, 8 }, 0, 30);
        decline.Answer(1, false);
        ok &= decline.Tick(1, Home, "A") == RiftProposal.Result.Cancelled && decline.Reason == "the host declined";
        lines.Add($"host declines → {decline.State} '{decline.Reason}'");

        var silent = new RiftProposal(2, 7, rift, new long[] { 1, 7, 8 }, 0, 30);
        silent.Answer(8, true);
        ok &= silent.Tick(29, Home, "A") == RiftProposal.Result.Open && silent.Tick(30, Home, "A") == RiftProposal.Result.Cancelled && silent.Reason == "the host didn't answer";
        lines.Add($"host silent → {silent.State} '{silent.Reason}'");

        var early = new RiftProposal(3, 7, rift, new long[] { 1, 7, 8, 9 }, 0, 30);
        early.Answer(1, true);
        early.Answer(8, false);
        bool openWithOnePending = early.Tick(5, Home, "A") == RiftProposal.Result.Open;
        early.Answer(9, true);
        ok &= openWithOnePending && early.Tick(6, Home, "A") == RiftProposal.Result.Started && string.Join(",", early.Participants) == "1,7,9";
        lines.Add($"all answered → {early.State} with {string.Join(",", early.Participants)}");

        var lapse = new RiftProposal(4, 7, rift, new long[] { 1, 7, 8 }, 0, 30);
        lapse.Answer(1, true);
        ok &= lapse.Tick(30, Home, "A") == RiftProposal.Result.Started && string.Join(",", lapse.Participants) == "1,7";
        lines.Add($"no answer = stay → {string.Join(",", lapse.Participants)}");

        var gone = new RiftProposal(5, 7, rift, new long[] { 1, 7 }, 0, 30);
        ok &= gone.Tick(1, p => p != 7, "Ana") == RiftProposal.Result.Cancelled && gone.Reason == "Ana left";
        lines.Add($"proposer gone → '{gone.Reason}'");

        var twice = new RiftProposal(6, 1, rift, new long[] { 1, 8 }, 0, 30);
        ok &= !twice.Answer(1, false) && twice.Answer(8, true) && !twice.Answer(8, false) && !twice.Answer(42, true) && twice.Tick(1, Home, "Host") == RiftProposal.Result.Started;
        lines.Add($"host proposing = host in; repeats and strangers ignored → {twice.State}");

        var spec = new RiftSpec { Kind = RiftKind.Reality, Tier = 25 };
        var bad = new RiftSpec { Kind = RiftKind.Reality, Tier = 5, Modifiers = new List<string> { "hardened", "hardened" } };
        var junk = new RiftSpec { Kind = (RiftKind)9 };
        ok &= !spec.Sanitize() && !bad.Sanitize() && !junk.Sanitize() && RiftSpec.Reality(20, new[] { "swift" }).Sanitize();
        lines.Add("specs: tier 25, duplicate modifiers and an unknown kind refused");
        Check("PROPOSAL-UNIT", ok, string.Join("; ", lines));
    }

    public static string LayoutKey(HideoutData data) =>
        string.Join(";", data.Props.OrderBy(p => p.Uid, StringComparer.Ordinal).Select(p => $"{p.Uid}:{p.Kind}:{Mathf.RoundToInt(p.X)}:{Mathf.RoundToInt(p.Y)}"))
        + "|" + string.Join(";", data.Floors.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}={f.Value}"));

    public static string Hash(string text)
    {
        ulong h = 1469598103934665603UL;
        foreach (char c in text)
        {
            h = (h ^ c) * 1099511628211UL;
        }

        return h.ToString("x16", CultureInfo.InvariantCulture);
    }
}

/// <summary>A guest end: a scratch profile that saves, joins, answers the host.</summary>
public partial class HideoutCoopGuest : CoopTestDriver
{
    private string PlayerName => CoopTestDriverArgs.Arg("coop-name") ?? "guest";

    protected override string LogPath => HideoutCoopHost.GuestLog(PlayerName);

    protected override string Tag => $"GUEST-{PlayerName.ToUpperInvariant()}";

    /// <summary>Where this guest's own town has decoration d1 (moved from the default before joining).</summary>
    private static readonly Vector2 OwnMark = new(208f, 208f);

    private readonly Queue<(int Id, string Question)> _questions = new();
    private Profile _profile = null!;
    private string _boughtGem = "";

    private Hideout? Town => GetTree().CurrentScene as Hideout;

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
        profile.FilePath = $"user://test_guest_{PlayerName}.json";
        if (FileAccess.FileExists(profile.FilePath))
        {
            DirAccess.RemoveAbsolute(profile.FilePath);
        }

        profile.Persist = true;
        profile.CreateTestCharacter(PlayerName, PlayerName == "ana" ? "rogue" : "arcanist");
        profile.Hideout = new HideoutData();
        HideoutCatalog.EnsureDefaults(profile.Hideout);
        profile.Hideout.GuideSeen = true;
        profile.Hideout.Find("d1")!.Position = OwnMark;
        profile.Save();

        session.TestAsked += (id, q) => _questions.Enqueue((id, q));
        session.Ended += reason => _ = OnEnded(reason);
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

        // Kept alive until the host kills the process (the host-left check reads this log).
        while (true)
        {
            await Frames(30);
        }
    }

    private static string PopupLine(Hideout town)
    {
        RiftProposalPopup p = town.Party.Popup;
        return $"mode={p.Current} title={p.TitleText.Replace(' ', '_')} no={p.NoText.Replace(' ', '_')} secs={p.SecondsShown} toast={town.Hud.ToastText.Replace(' ', '_')}";
    }

    private async Task OnEnded(string reason)
    {
        bool menu = await WaitFor(() => GetTree().CurrentScene is MainMenu, 8f);
        await Frames(5);
        string notice = (GetTree().CurrentScene as MainMenu)?.NoticeText ?? "";
        Log($"HOST-LEFT: {(menu && notice.Length > 0 ? "PASS" : "FAIL")} — reason '{reason}', at the main menu={menu}, notice '{notice}'");
    }

    private async Task<string> Answer(NetSession session, string question)
    {
        Dictionary<string, string> f = Fields(question);
        Hideout? town = Town;
        switch (question.Split(' ')[0])
        {
            case "scene":
                return $"scene={GetTree().CurrentScene?.Name} phase={session.PartyPhase} guest={(town?.IsGuest == true ? 1 : 0)}";
            case "puppets":
                // A guest that just loaded draws its first frames slowly: give it a moment to catch up.
                if (f.TryGetValue("want", out string? want))
                {
                    await WaitFor(() => town?.Party.Puppets.Count == want.ToInt(), 6f);
                }

                return $"puppets={town?.Party.Puppets.Count ?? -1} players={GetTree().GetNodesInGroup(Combat.CombatQuery.PlayerGroup).Count} presence_in={session.PresenceReceived} sent={session.PresenceSent} place={session.LocalPlace} fps={Engine.GetFramesPerSecond()} error={town?.Party.LastError.Replace(' ', '_')}";
            case "walk":
            {
                Player p = town!.Player;
                Vector2 from = p.GlobalPosition;
                p.Command = new PlayerCommand { Move = Vector2.Right };
                await Seconds(0.6f);
                p.Command = null;
                await Seconds(0.2f);
                return $"x={Num(p.GlobalPosition.X)} y={Num(p.GlobalPosition.Y)} moved={Num(from.DistanceTo(p.GlobalPosition))} place={session.LocalPlace}";
            }
            case "puppet":
            {
                long peer = long.Parse(f.GetValueOrDefault("peer", "0"), CultureInfo.InvariantCulture);
                if (f.ContainsKey("x"))
                {
                    var near = new Vector2(F(f, "x"), F(f, "y"));
                    await WaitFor(() => town!.Party.Puppets.TryGetValue(peer, out Player? p) && p.GlobalPosition.DistanceTo(near) < 24f, 1.5f);
                }

                bool has = town!.Party.Puppets.TryGetValue(peer, out Player? puppet);
                return has ? $"x={Num(puppet!.GlobalPosition.X)} y={Num(puppet.GlobalPosition.Y)} paused={(GetTree().Paused ? 1 : 0)}" : $"none=1 paused={(GetTree().Paused ? 1 : 0)}";
            }
            case "layout":
                return $"shown={HideoutCoopHost.Hash(HideoutCoopHost.LayoutKey(town!.Data))} own={HideoutCoopHost.Hash(HideoutCoopHost.LayoutKey(_profile.Hideout))}";
            case "edit":
            {
                town!.OpenEditor();
                await Frames(3);
                return $"editor={(town.Editing || town.GetNodeOrNull("HideoutEditor") is not null ? 1 : 0)} toast={(town.Hud.ToastText.Contains("can edit") ? 1 : 0)}";
            }
            case "buy_gem":
            {
                MerchantPanel shop = town!.GetNode<MerchantPanel>("MerchantPanel");
                shop.Open();
                shop.ShowTab(MerchantTab.SkillGems);
                long before = town.Player.Wallet.GetBalance(Profile.GoldId);
                GemOffer? offer = GemShop.Offers.FirstOrDefault(o => GemShop.CannotBuy(_profile.Active, town.Player.Wallet, o) is null);
                bool ok = false;
                if (offer is not null)
                {
                    shop.Gems.Select(offer);
                    ok = shop.Gems.Buy();
                    _boughtGem = offer.Id;
                }

                long after = town.Player.Wallet.GetBalance(Profile.GoldId);
                shop.Close();
                return $"ok={(ok ? 1 : 0)} before={before} after={after} gem={offer?.Id ?? "none"}";
            }
            case "open_panel":
            {
                StashPanel stash = town!.GetNode<StashPanel>("StashPanel");
                stash.Open();
                await Frames(3);
                return $"open={(stash.IsOpen ? 1 : 0)} paused={(GetTree().Paused ? 1 : 0)}";
            }
            case "close_panel":
            {
                bool held = !town!.Player.ControlsEnabled;
                town.GetNode<StashPanel>("StashPanel").Close();
                await Frames(3);
                return $"held={(held ? 1 : 0)} released={(town.Player.ControlsEnabled ? 1 : 0)}";
            }
            case "save":
            {
                town!.SaveLayout();
                await Frames(2);
                using FileAccess? file = FileAccess.Open(_profile.FilePath, FileAccess.ModeFlags.Read);
                string json = file?.GetAsText() ?? "";
                using JsonDocument doc = JsonDocument.Parse(json.Length > 0 ? json : "{}");
                bool ownKept = false;
                if (doc.RootElement.TryGetProperty("Hideout", out JsonElement h) && h.TryGetProperty("Props", out JsonElement props))
                {
                    foreach (JsonElement p in props.EnumerateArray())
                    {
                        if (p.GetProperty("Uid").GetString() == "d1")
                        {
                            ownKept = Mathf.IsEqualApprox(p.GetProperty("X").GetSingle(), OwnMark.X) && Mathf.IsEqualApprox(p.GetProperty("Y").GetSingle(), OwnMark.Y);
                        }
                    }
                }

                bool shownDiffers = town.Data.Find("d1") is { } shown && shown.Position.DistanceTo(OwnMark) > 1f;
                bool gemKept = _boughtGem.Length > 0 && json.Contains(_boughtGem);
                return $"own_kept={(ownKept && shownDiffers ? 1 : 0)} gem_kept={(gemKept ? 1 : 0)} file_bytes={json.Length}";
            }
            case "pause":
            {
                town!.Pause.Open();
                string entries = string.Join(",", town.Pause.ExtraTexts);
                string leave = town.Pause.LeaveButtonText;
                town.Pause.Close();
                return $"entries={entries.Replace(' ', '_')} leave={leave.Replace(' ', '_')}";
            }
            case "can_edit":
            {
                town!.Pause.Open();
                string entries = string.Join(",", town.Pause.ExtraTexts);
                town.Pause.Close();
                return $"ok={(town.EditBlockedReason is null ? 1 : 0)} entries={entries.Replace(' ', '_')}";
            }
            case "edit_open":
                town!.OpenEditor();
                await WaitFor(() => town.Editing, 4f);
                return $"editing={(town.Editing ? 1 : 0)} toast={town.Hud.ToastText.Replace(' ', '_')}";
            case "edit_move":
            {
                bool was = town!.Editing;
                town.MoveProp(f.GetValueOrDefault("uid", ""), new Vector2(F(f, "x"), F(f, "y")));
                town.Editor?.Close();
                await Frames(3);
                return $"was={(was ? 1 : 0)} editing={(town.Editing ? 1 : 0)}";
            }
            case "edit_forge":
            {
                town!.OpenEditor();
                await WaitFor(() => town.Editing, 4f);
                // A modified client: a decoration that was never bought, sent without the editor.
                var forged = new HideoutData { Initialized = true };
                forged.Props.AddRange(town.Data.Props.Select(p => new PlacedProp { Uid = p.Uid, Kind = p.Kind, Position = p.Position }));
                foreach ((string key, string style) in town.Data.Floors)
                {
                    forged.Floors[key] = style;
                }

                foreach ((string id, int n) in town.Data.Stock)
                {
                    forged.Stock[id] = n;
                }

                forged.Props.Add(new PlacedProp { Uid = "forged1", Kind = "statue", Position = HideoutCoopHost.OpenSpot(1, 4) });
                session.SubmitLayout(forged);
                await WaitFor(() => !town.Editing, 4f);
                await Frames(3);
                return $"editing={(town.Editing ? 1 : 0)} toast={town.Hud.ToastText.Replace(' ', '_')}";
            }
            case "editing":
                await WaitFor(() => !town!.Editing, 3f);
                await Frames(2);
                return $"editing={(town!.Editing ? 1 : 0)} toast={town.Hud.ToastText.Replace(' ', '_')}";
            case "own_d2":
            {
                var defaults = new HideoutData();
                HideoutCatalog.EnsureDefaults(defaults);
                Vector2 home = defaults.Find("d2")!.Position;
                using FileAccess? file = FileAccess.Open(_profile.FilePath, FileAccess.ModeFlags.Read);
                using JsonDocument doc = JsonDocument.Parse(file?.GetAsText() is { Length: > 0 } text ? text : "{}");
                Vector2 own = new(float.NaN, float.NaN);
                if (doc.RootElement.TryGetProperty("Hideout", out JsonElement h) && h.TryGetProperty("Props", out JsonElement props))
                {
                    foreach (JsonElement p in props.EnumerateArray().Where(p => p.GetProperty("Uid").GetString() == "d2"))
                    {
                        own = new Vector2(p.GetProperty("X").GetSingle(), p.GetProperty("Y").GetSingle());
                    }
                }

                return $"own_default={(own.DistanceTo(home) < 0.5f ? 1 : 0)} own={Num(own.X)},{Num(own.Y)} default={Num(home.X)},{Num(home.Y)}";
            }
            case "propose":
                town!.ProposeRift(RiftSpec.SurvivalRift());
                await WaitFor(() => session.OpenProposal is not null, 3f);
                return PopupLine(town);
            case "popup":
                return PopupLine(town!);
            case "answer":
                if (f.GetValueOrDefault("accept") == "1")
                {
                    town!.Party.Popup.PressYes();
                }
                else
                {
                    town!.Party.Popup.PressNo();
                }

                await Frames(2);
                return PopupLine(town);
            case "waiting":
                await Frames(10);
                return $"scene={GetTree().CurrentScene?.Name} line={town?.Hud.PartyLine.Replace(' ', '_')} clientside={(NetMode.ClientSide ? 1 : 0)} runlink={(session.Run is null ? 0 : 1)} "
                    + $"inrun={(session.InRun ? 1 : 0)} puppets={town?.Party.Puppets.Count ?? -1}";
            case "results_home":
            {
                bool ended = await WaitFor(() => Run?.State.Ended == true && Run.Results.Showing, 10f);
                Run?.Results.OnHideout?.Invoke();
                return $"ended={(ended ? 1 : 0)}";
            }
            default:
                return $"unknown={question.Split(' ')[0]}";
        }
    }
}
