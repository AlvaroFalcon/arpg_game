#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Core;
using ArpgGame.Net;
using ArpgGame.UI;

namespace ArpgGame.Tests;

/// <summary>
/// Headless test of the co-op screen — docs/design/menus.md -> "Co-op".
///   ./scripts/run_all_tests.sh CoopUiSmokeTest
///
/// Drives real <see cref="CoopPanel"/>s: the autoload session hosts, and the
/// friends are extra in-process sessions on their own SceneMultiplayer branch
/// (the pattern of <see cref="CoopTestHost"/>'s intruders), each with its own
/// panel. Asserts what a player sees: inline errors for empty or malformed
/// fields, an invite that pastes back into address + code, four lobby slots
/// with the free ones marked, a wrong code and a full party named as such, a
/// dead address failing within <see cref="NetSession.ConnectTimeout"/>, Cancel,
/// and the no-character state. Judged by its RESULT line (also mirrored to
/// user://test_results/CoopUiSmokeTest.txt).
/// </summary>
public partial class CoopUiSmokeTest : Node
{
    public override void _Ready() =>
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new CoopUiTestDriver { Name = "CoopUiTestDriver" });
}

public partial class CoopUiTestDriver : CoopTestDriver
{
    protected override string LogPath => "user://test_results/CoopUiSmokeTest.txt";

    protected override string Tag => "coop-ui";

    private bool _failed;
    private Control _screen = null!;
    private int _branches;

    private void Check(string name, bool ok, string detail)
    {
        Log($"{name}: {(ok ? "PASS" : "FAIL")} — {detail}");
        _failed |= !ok;
    }

    protected override async Task Main()
    {
        try
        {
            await Body();
        }
        catch (Exception e)
        {
            Check("NO-EXCEPTION", false, e.ToString());
        }

        Log($"=== RESULT: {(_failed ? "FAIL" : "PASS")} ===");
        GetTree().Quit(_failed ? 1 : 0);
    }

    private async Task Body()
    {
        Profile? profile = Profile.Get(this);
        NetSession? session = NetSession.Get(this);
        if (profile is null || session is null)
        {
            Check("AUTOLOADS", false, "Profile or NetSession missing");
            return;
        }

        // Never touch the real save.
        profile.Persist = false;
        while (profile.Characters.Count > 0)
        {
            profile.DeleteCharacter(0);
        }

        var layer = new CanvasLayer();
        AddChild(layer);
        _screen = UiTheme.FullRect(layer);

        // --- no character: a clear way forward instead of disabled buttons -----------
        CoopPanel none = await Open(null);
        Check("NO-CHARACTER-STATE", none.Current == CoopPanel.View.NoCharacter, $"view={none.Current}");
        Close(none);

        profile.CreateTestCharacter("Host", "warrior");

        // --- inline validation --------------------------------------------------------
        CoopPanel form = await Open(null);
        Check("OPENS-ON-CHOICE", form.Current == CoopPanel.View.Choose, $"view={form.Current}");
        form.SetJoinFields("", "");
        form.PressJoin();
        await Frames(2);
        Check("EMPTY-FIELDS-INLINE", form.AddressError.Length > 0 && form.CodeError.Length > 0 && !session.Active && form.Current == CoopPanel.View.Choose,
            $"address='{form.AddressError}' code='{form.CodeError}' active={session.Active}");
        form.SetJoinFields("203.0.113.7:7777", "K7Q-2");
        form.PressJoin();
        await Frames(2);
        Check("BAD-CODE-INLINE", form.AddressError.Length == 0 && form.CodeError.Contains("6 letters") && !session.Active,
            $"address='{form.AddressError}' code='{form.CodeError}'");
        form.SetJoinFields("203.0.113.7:99999", "K7Q-29M");
        form.PressJoin();
        await Frames(2);
        Check("BAD-ADDRESS-INLINE", form.AddressError.Length > 0 && form.CodeError.Length == 0 && !session.Active,
            $"address='{form.AddressError}' code='{form.CodeError}'");

        // --- the invite round trip ------------------------------------------------------
        string invite = NetTransport.Invite("203.0.113.7:7777", "K7Q29M");
        bool parsed = NetTransport.TryParseInvite(invite, out string ia, out string ic);
        bool chat = NetTransport.TryParseInvite("join me! 100.64.1.2:7777, code k7q 29m", out string ca, out string cc);
        bool bare = NetTransport.TryParseInvite("203.0.113.7:7777", out _, out _);
        Check("INVITE-PASTES-BACK", parsed && ia == "203.0.113.7:7777" && ic == "K7Q-29M" && chat && ca == "100.64.1.2:7777" && cc == "K7Q-29M" && !bare,
            $"'{invite}' → {parsed} {ia} {ic}; chat → {chat} {ca} {cc}; bare → {bare}");

        // --- the host's lobby: four slots, three free -----------------------------------------
        form.SetHostPort(0);
        form.PressHost();
        await Frames(2);
        Check("BAD-PORT-INLINE", form.PortError.Length > 0 && !session.Active, $"port='{form.PortError}'");
        form.NoUpnp = true;
        form.AutoCode = JoinCode.TestCode;
        int port = 0;
        for (int attempt = 0; attempt < 5 && !session.IsHost; attempt++)
        {
            port = 7790 + (int)(GD.Randi() % 400);
            form.SetHostPort(port);
            form.PressHost();
        }

        await Frames(3);
        Check("HOST-LOBBY-FOUR-SLOTS", form.Current == CoopPanel.View.Lobby && form.SlotTexts.Count == 4
                && form.SlotTexts.Count(t => t.StartsWith("Free")) == 3 && form.SlotTexts[0].Contains("Host") && form.SlotTexts[0].Contains("You"),
            $"view={form.Current} port={port} slots=[{string.Join(" | ", form.SlotTexts)}]");
        Check("HOST-INVITE-HAS-CODE", form.InviteText.EndsWith(JoinCode.Format(JoinCode.TestCode)) && form.InviteText.Contains($":{port}"), $"invite='{form.InviteText}'");
        // Owner report: with the router port not opened, the lobby put the LAN address
        // up as "the" address. It must say it's local only and offer the public IP lookup.
        bool vpnHere = PortMapper.LocalAddresses().Any(a => a.StartsWith("100.") || a.StartsWith("26.") || a.StartsWith("25."));
        Check("HOST-LAN-ADDRESS-LABELLED", vpnHere || ((form.AddressKind.StartsWith("Local network only") || form.AddressKind == "This PC only.") && form.PublicIpOffered),
            $"kind='{form.AddressKind}' offered public IP lookup={form.PublicIpOffered} (VPN on this machine={vpnHere})");

        // --- a friend joins through their own panel --------------------------------------------
        (NetSession f1, CoopPanel p1) = await Friend();
        p1.SetJoinFields($"127.0.0.1:{port}", "test-42");
        p1.PressJoin();
        Check("JOIN-SHOWS-CONNECTING", p1.Current == CoopPanel.View.Connecting && f1.Phase == JoinPhase.Connecting, $"view={p1.Current} phase={f1.Phase}");
        bool joined = await WaitFor(() => p1.Current == CoopPanel.View.Lobby, 8f);
        await Frames(3);
        Check("FRIEND-SEES-LOBBY", joined && p1.SlotTexts.Count == 4 && p1.SlotTexts.Count(t => t.StartsWith("Free")) == 2,
            $"view={p1.Current} slots=[{string.Join(" | ", p1.SlotTexts)}]");
        Check("HOST-SEES-TWO-FREE", form.SlotTexts.Count(t => t.StartsWith("Free")) == 2 && form.SlotTexts.Any(t => t.Contains("You")),
            $"slots=[{string.Join(" | ", form.SlotTexts)}]");

        // --- a wrong code is called a wrong code ------------------------------------------------
        (NetSession wrong, CoopPanel pw) = await Friend();
        pw.SetJoinFields($"127.0.0.1:{port}", "ZZZ-ZZZ");
        pw.PressJoin();
        bool failed = await WaitFor(() => pw.Current == CoopPanel.View.Failed, 10f);
        Check("WRONG-CODE-SURFACES", failed && pw.FailureTitle == "Wrong join code" && wrong.LastEnd == SessionEnd.WrongCode,
            $"view={pw.Current} title='{pw.FailureTitle}' end={wrong.LastEnd} status='{wrong.Status}'");
        await Drop(wrong, pw);

        // --- a full party ---------------------------------------------------------------------------
        (NetSession f2, CoopPanel p2) = await Friend();
        (NetSession f3, CoopPanel p3) = await Friend();
        foreach (CoopPanel p in new[] { p2, p3 })
        {
            p.SetJoinFields($"127.0.0.1:{port}", JoinCode.TestCode);
            p.PressJoin();
        }

        bool four = await WaitFor(() => session.Members.Count == 4, 8f);
        await Frames(3);
        Check("HOST-LOBBY-FULL", four && form.SlotTexts.Count(t => t.StartsWith("Free")) == 0, $"members={session.Members.Count} slots=[{string.Join(" | ", form.SlotTexts)}]");
        (NetSession fifth, CoopPanel p5) = await Friend();
        p5.SetJoinFields($"127.0.0.1:{port}", JoinCode.TestCode);
        p5.PressJoin();
        bool refused = await WaitFor(() => p5.Current == CoopPanel.View.Failed, 10f);
        Check("FULL-PARTY-SURFACES", refused && fifth.LastEnd == SessionEnd.PartyFull && p5.FailureTitle == "The party is full",
            $"view={p5.Current} title='{p5.FailureTitle}' end={fifth.LastEnd} status='{fifth.Status}'");
        await Drop(fifth, p5);

        // The run-started refusal shares the same path (the host's reason, classified).
        Check("RUN-STARTED-CLASSIFIED", JoinRefusal.Classify(JoinRefusal.RunStarted) == SessionEnd.RunStarted
                && CoopPanel.Explain(SessionEnd.RunStarted, "").Title == "The run has already started"
                && JoinRefusal.Classify(JoinRefusal.Version) == SessionEnd.VersionMismatch,
            "reasons map to their cards");

        // --- the host closes: the friend is told -------------------------------------------------------
        foreach ((NetSession s, CoopPanel p) in new[] { (f2, p2), (f3, p3) })
        {
            await Drop(s, p);
        }

        Close(form);
        session.Leave();
        bool told = await WaitFor(() => p1.Current == CoopPanel.View.Failed, 10f);
        Check("HOST-CLOSE-SURFACES", told && f1.LastEnd == SessionEnd.HostClosed && p1.FailureTitle == "The host closed the lobby",
            $"view={p1.Current} end={f1.LastEnd} title='{p1.FailureTitle}'");
        await Drop(f1, p1);

        // --- nobody at the address: a failure within the timeout -------------------------------------------
        CoopPanel dead = await Open(null);
        int deadPort = port + 1;
        dead.SetJoinFields($"127.0.0.1:{deadPort}", JoinCode.TestCode);
        ulong started = Time.GetTicksMsec();
        dead.PressJoin();
        await Seconds(1f);
        Check("DEAD-ADDRESS-CONNECTING", dead.Current == CoopPanel.View.Connecting && session.Phase == JoinPhase.Connecting, $"view={dead.Current} phase={session.Phase}");
        bool gaveUp = await WaitFor(() => dead.Current == CoopPanel.View.Failed, (float)NetSession.ConnectTimeout + 4f);
        double took = (Time.GetTicksMsec() - started) / 1000.0;
        Check("DEAD-ADDRESS-FAILS-IN-TIME", gaveUp && session.LastEnd == SessionEnd.Unreachable && dead.FailureTitle == "Couldn't reach the host"
                && took >= NetSession.ConnectTimeout - 3.0 && took <= NetSession.ConnectTimeout + 3.0,
            $"after {took:0.0} s: view={dead.Current} end={session.LastEnd} title='{dead.FailureTitle}'");

        // --- Cancel while connecting --------------------------------------------------------------------
        dead.PressJoin();
        await Seconds(0.5f);
        bool connecting = dead.Current == CoopPanel.View.Connecting;
        dead.PressCancel();
        await Frames(2);
        Check("CANCEL-STOPS", connecting && !session.Active && dead.Current == CoopPanel.View.Choose, $"was connecting={connecting} active={session.Active} view={dead.Current}");
        Close(dead);

        await MenuChecks(profile, session);
    }

    private string Focused() => GetViewport().GuiGetFocusOwner() is Button b ? b.Text : GetViewport().GuiGetFocusOwner()?.Name ?? "nothing";

    /// <summary>
    /// The title screen's Play (docs/design/menus.md -> "Main menu"): Solo / With friends,
    /// the character list for each, a new character's introduction question, focus coming
    /// back to the button that opened each screen, and the notice a lost party leaves.
    /// </summary>
    private async Task MenuChecks(Profile profile, NetSession session)
    {
        while (profile.Characters.Count > 0)
        {
            profile.DeleteCharacter(0);
        }

        profile.CreateTestCharacter("Veteran", "warrior");
        Characters.CharacterData rookie = profile.CreateCharacter("Rookie", "rogue");
        int veteran = 0;
        int fresh = 1;

        // A party that ended out in the world leaves its reason for the next main menu, once.
        session.MenuNotice = "Ana closed the party.";
        MainMenu first = await NewMenu();
        MainMenu second = await NewMenu();
        Check("MENU-NOTICE", first.NoticeText == "Ana closed the party." && session.MenuNotice is null && second.NoticeText is null,
            $"first menu '{first.NoticeText}', session cleared={session.MenuNotice is null}, second menu '{second.NoticeText}'");
        first.QueueFree();
        await Frames(2);
        MainMenu menu = second;

        menu.OpenPlayChooser();
        await Frames(3);
        bool chooser = menu.ChooserOpen && Focused() == "Solo";
        menu.PressSolo();
        await Frames(3);
        CharacterSelect? solo = menu.OpenCharacterSelect;
        bool soloList = solo is { Purpose: CharacterSelect.PlayPurpose.Solo, TitleText: "Choose your hero" } && !menu.ChooserOpen;
        solo?.PressBack();
        await Frames(3);
        string afterSolo = Focused();
        menu.PressWithFriends();
        await Frames(3);
        CharacterSelect? coop = menu.OpenCharacterSelect;
        bool coopList = coop is { Purpose: CharacterSelect.PlayPurpose.Coop, TitleText: "Choose who plays with friends" };
        coop?.Play(veteran);
        await Frames(3);
        CoopPanel? panel = menu.OpenCoopPanel;
        Check("MENU-PLAY-CHOOSER", chooser && soloList && afterSolo == "Solo" && coopList && panel is { Current: CoopPanel.View.Choose } && profile.ActiveIndex == veteran,
            $"chooser open={menu.ChooserOpen} first focus Solo={chooser}; Solo → '{solo?.TitleText}' ({solo?.Purpose}), back focuses '{afterSolo}'; With friends → '{coop?.TitleText}' → pick → co-op screen {panel?.Current}");

        panel?.PressBack();
        await Frames(3);
        string afterCoop = Focused();
        menu.BackToMenu();
        await Frames(3);
        string afterChooser = Focused();
        Check("MENU-FOCUS", afterCoop == "With friends" && afterChooser == "Start",
            $"Back from the co-op screen focuses '{afterCoop}', Back from the chooser focuses '{afterChooser}'");

        menu.OpenPlayChooser();
        menu.PressWithFriends();
        await Frames(3);
        CharacterSelect? pick = menu.OpenCharacterSelect;
        pick?.Play(fresh);
        await Frames(3);
        bool asked = pick is { TutorialPromptShowing: true } && pick.TitleText == "Rookie hasn't played the introduction" && menu.OpenCoopPanel is null;
        pick?.SkipTutorial();
        await Frames(3);
        Check("MENU-TUTORIAL-PROMPT", asked && rookie.TutorialDone && menu.OpenCoopPanel is not null,
            $"prompt shown={asked} ('{pick?.TitleText}'), skipped → tutorial done={rookie.TutorialDone}, co-op screen open={menu.OpenCoopPanel is not null}");
        menu.QueueFree();
        await Frames(2);
    }

    private async Task<MainMenu> NewMenu()
    {
        MainMenu menu = GD.Load<PackedScene>(Scenes.MainMenu).Instantiate<MainMenu>();
        AddChild(menu);
        await Frames(3);
        return menu;
    }

    private async Task<CoopPanel> Open(NetSession? session)
    {
        var center = UiTheme.Center(_screen);
        var panel = new CoopPanel { Session = session };
        center.AddChild(panel);
        await Frames(2);
        return panel;
    }

    private void Close(CoopPanel panel)
    {
        Node center = panel.GetParent();
        center.QueueFree();
    }

    /// <summary>A friend: a session on its own multiplayer branch, with its own panel.</summary>
    private async Task<(NetSession, CoopPanel)> Friend()
    {
        var holder = new Node { Name = $"Friend{++_branches}" };
        GetTree().Root.AddChild(holder);
        GetTree().SetMultiplayer(new SceneMultiplayer(), holder.GetPath());
        var friend = new NetSession { Name = "NetSession" };
        holder.AddChild(friend);
        await Frames(1);
        CoopPanel panel = await Open(friend);
        return (friend, panel);
    }

    private async Task Drop(NetSession friend, CoopPanel panel)
    {
        Close(panel);
        Node holder = friend.GetParent();
        friend.Leave();
        GetTree().SetMultiplayer(null, holder.GetPath());
        holder.QueueFree();
        await Frames(2);
    }
}
