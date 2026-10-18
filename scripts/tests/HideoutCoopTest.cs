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
        Dictionary<string, string> anaSees = Fields(await Ask(ana.PeerId, "puppets"));
        Dictionary<string, string> boSees = Fields(await Ask(bo.PeerId, "puppets"));
        Check("HIDEOUT-SEES-PARTY", hostSees && F(anaSees, "puppets") == 2f && F(boSees, "puppets") == 2f,
            $"host puppets={town.Party.Puppets.Count}; ana: {Line(anaSees)}; bo: {Line(boSees)}");

        // --- presence: ana walks; the host and bo see her there -------------------------------------------
        long bytes0 = session.PresenceBytesSent;
        ulong t0 = Time.GetTicksMsec();
        Dictionary<string, string> walked = Fields(await Ask(ana.PeerId, "walk"));
        Vector2 anaAt = new(F(walked, "x"), F(walked, "y"));
        bool hostFollows = await WaitFor(() => town.Party.Puppets.TryGetValue(ana.PeerId, out Player? p) && p.GlobalPosition.DistanceTo(anaAt) < 24f, 1.5f);
        float hostGap = town.Party.Puppets.TryGetValue(ana.PeerId, out Player? hp) ? hp.GlobalPosition.DistanceTo(anaAt) : -1f;
        Dictionary<string, string> boView = Fields(await Ask(bo.PeerId, $"puppet peer={ana.PeerId}"));
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
        Dictionary<string, string> boSeesHost = Fields(await Ask(bo.PeerId, "puppet peer=1"));
        float hostGapOnBo = new Vector2(F(boSeesHost, "x"), F(boSeesHost, "y")).DistanceTo(hostTo);
        Dictionary<string, string> closed = Fields(await Ask(bo.PeerId, "close_panel"));
        Check("HIDEOUT-NO-PAUSE", modal.GetValueOrDefault("open") == "1" && modal.GetValueOrDefault("paused") == "0" && hostFrom.DistanceTo(hostTo) > 40f && hostGapOnBo < 24f
                && boSeesHost.GetValueOrDefault("paused") == "0" && closed.GetValueOrDefault("held") == "1",
            $"bo: {Line(modal)}; host walked {hostFrom.DistanceTo(hostTo):0} px, bo's puppet of the host {hostGapOnBo:0.0} px off (paused={boSeesHost.GetValueOrDefault("paused")}); controls held while open: {closed.GetValueOrDefault("held")}");

        // --- presence bandwidth --------------------------------------------------------------------------------
        double seconds = (Time.GetTicksMsec() - t0) / 1000.0;
        double perClient = (session.PresenceBytesSent - bytes0) / seconds / 2.0;
        Check("HIDEOUT-PRESENCE-BYTES", perClient > 0 && perClient < 1024, $"{perClient:0} B/s of presence per guest (payload) over {seconds:0.0} s");

        // --- a guest saving never writes the host's town into its own -------------------------------------------
        Dictionary<string, string> saved = Fields(await Ask(ana.PeerId, "save"));
        Check("HIDEOUT-GUEST-SAVE-SAFE", saved.GetValueOrDefault("own_kept") == "1" && saved.GetValueOrDefault("gem_kept") == "1", Line(saved));

        await AfterHideoutChecks(session, town, ana, bo);

        // --- the host leaves: guests to the main menu with a notice --------------------------------------------
        session.Leave();
        bool told = await WaitFor(() => ReadFile(GuestLog("ana")).Contains("HOST-LEFT") && ReadFile(GuestLog("bo")).Contains("HOST-LEFT"), 10f);
        string anaLine = ReadFile(GuestLog("ana")).Split('\n').LastOrDefault(l => l.Contains("HOST-LEFT")) ?? "no line";
        string boLine = ReadFile(GuestLog("bo")).Split('\n').LastOrDefault(l => l.Contains("HOST-LEFT")) ?? "no line";
        Check("HIDEOUT-HOST-LEFT", told && anaLine.Contains("PASS") && boLine.Contains("PASS"), $"{anaLine} | {boLine}");
    }

    /// <summary>Later steps hang their checks here (proposals, invite, edit permission).</summary>
    private Task AfterHideoutChecks(NetSession session, Hideout town, LobbyMember ana, LobbyMember bo) => Task.CompletedTask;

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
                return $"puppets={town?.Party.Puppets.Count ?? -1} players={GetTree().GetNodesInGroup(Combat.CombatQuery.PlayerGroup).Count}";
            case "walk":
            {
                Player p = town!.Player;
                Vector2 from = p.GlobalPosition;
                p.Command = new PlayerCommand { Move = Vector2.Right };
                await Seconds(0.6f);
                p.Command = null;
                await Seconds(0.2f);
                return $"x={Num(p.GlobalPosition.X)} y={Num(p.GlobalPosition.Y)} moved={Num(from.DistanceTo(p.GlobalPosition))}";
            }
            case "puppet":
            {
                long peer = long.Parse(f.GetValueOrDefault("peer", "0"), CultureInfo.InvariantCulture);
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
            default:
                return $"unknown={question.Split(' ')[0]}";
        }
    }
}
