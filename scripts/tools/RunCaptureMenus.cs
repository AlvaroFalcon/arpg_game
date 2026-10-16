#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Net;
using ArpgGame.UI;

namespace ArpgGame.Tools;

/// <summary>`--only=coop` and `--only=menus` for <see cref="RunCapture"/>
/// (docs/design/menus.md): the co-op screen's states and every menu reachable
/// from the title screen.</summary>
public partial class RunCapture
{
    /// <summary>`--only=coop`: the co-op screen — host/join choice, inline
    /// errors, the host lobby with free slots, a joined friend's lobby, a wrong
    /// code, connecting to nobody and that failure.</summary>
    private async Task CoopScript()
    {
        SetupDemoProfile();
        Profile profile = Profile.Get(this)!;
        NetSession session = NetSession.Get(this)!;
        NetSession.GatewayFactory = () => new Tests.FakeGateway();
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.MainMenu);
        await Wait(0.8f);
        if (GetTree().CurrentScene is not MainMenu menu)
        {
            GetTree().Quit();
            return;
        }

        CoopPanel panel = menu.OpenCoopForCapture();
        await Wait(0.4f);
        await Snap("coop-start.png");

        // Join with nothing typed: errors next to each field.
        panel.SetJoinFields("203.0.113.7", "K7Q-2");
        panel.PressJoin();
        await Wait(0.3f);
        await Snap("coop-join-errors.png");

        // A router that doesn't answer UPnP: the local address is labelled as local
        // only and the public IP lookup is offered.
        NetSession.GatewayFactory = () => new NoGateway();
        panel.PressHost();
        await WaitUntil(() => session.Ports.State == PortMapper.MapState.Failed, 4f);
        await Wait(0.3f);
        await Snap("coop-host-lobby-no-router.png");
        session.Leave();
        NetSession.GatewayFactory = () => new Tests.FakeGateway();
        panel = menu.ReopenCoopForCapture();
        await Wait(0.3f);

        // Host; a friend joins in-process: two filled slots, two free.
        panel.PressHost();
        await WaitUntil(() => session.Ports.State == PortMapper.MapState.Open, 4f);
        await Wait(0.3f);
        await Snap("coop-host-lobby-empty.png");
        NetSession friend = await CaptureIntruder();
        CharacterData nyx = profile.Characters.First(c => c.Name == "Nyx");

        // The friend's screen: a wrong code first, then the right one.
        int host = profile.ActiveIndex;
        profile.Select(profile.Characters.IndexOf(nyx));
        CoopPanel friendPanel = menu.ReopenCoopForCapture(friend);
        await Wait(0.3f);
        friendPanel.SetJoinFields($"127.0.0.1:{session.Port}", "ZZZ-ZZZ");
        friendPanel.PressJoin();
        await WaitUntil(() => friendPanel.Current == CoopPanel.View.Failed, 8f);
        await Wait(0.3f);
        await Snap("coop-wrong-code.png");
        friendPanel.SetJoinFields($"127.0.0.1:{session.Port}", JoinCode.Format(session.Code));
        friendPanel.PressJoin();
        await WaitUntil(() => friend.Phase == JoinPhase.InLobby, 6f);
        await Wait(0.4f);
        await Snap("coop-client-lobby.png");

        profile.Select(host);
        panel = menu.ReopenCoopForCapture();
        await Wait(0.4f);
        await Snap("coop-host-lobby.png");
        friend.Leave();
        await Wait(0.3f);
        session.Leave();
        await Wait(0.2f);

        // Connecting to nobody, then the failure.
        CoopPanel join = menu.ReopenCoopForCapture();
        await Wait(0.3f);
        join.SetJoinFields("127.0.0.1:7798", "TEST42");
        join.PressJoin();
        // Real seconds: under Xvfb frames are slow enough that timers lag behind the clock.
        ulong until = Time.GetTicksMsec() + 5500;
        await WaitUntil(() => Time.GetTicksMsec() >= until, 30f);
        await Snap("coop-connecting.png");
        await WaitUntil(() => join.Current == CoopPanel.View.Failed, 40f);
        await Wait(0.4f);
        await Snap("coop-failed.png");

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }

    /// <summary>`--only=menus`: every menu reachable from the title screen, first
    /// as a brand-new player (no characters), then with demo characters.</summary>
    private async Task MenusScript()
    {
        SetupDemoProfile();
        Profile profile = Profile.Get(this)!;
        while (profile.Characters.Count > 0)
        {
            profile.DeleteCharacter(0);
        }

        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.MainMenu);
        await Wait(0.8f);
        await Snap("menus-main-new.png");
        if (GetTree().CurrentScene is MainMenu fresh)
        {
            fresh.OpenCoopForCapture();
            await Wait(0.4f);
            await Snap("menus-coop-no-character.png");
            GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.MainMenu);
            await Wait(0.6f);
        }

        if (GetTree().CurrentScene is MainMenu fresh2)
        {
            fresh2.OpenCharactersForCapture();
            await Wait(0.4f);
            await Snap("menus-characters-empty.png");
        }

        SetupDemoProfile();
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.MainMenu);
        await Wait(0.8f);
        await Snap("menus-main.png");
        if (GetTree().CurrentScene is MainMenu menu)
        {
            menu.OpenCharactersForCapture();
            await Wait(0.4f);
            await Snap("menus-characters.png");
            if (FindChild<CharacterSelect>(menu) is { } select)
            {
                select.ShowCreate();
                await Wait(0.3f);
                await Snap("menus-create.png");
            }
        }

        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.MainMenu);
        await Wait(0.6f);
        if (GetTree().CurrentScene is MainMenu menu2)
        {
            menu2.OpenSettingsForCapture();
            await Wait(0.4f);
            if (FindTabs(menu2) is { } tabs)
            {
                foreach ((int index, string name) in new[] { (0, "audio"), (1, "display"), (2, "gameplay") })
                {
                    tabs.CurrentTab = index;
                    await Wait(0.3f);
                    await Snap($"menus-settings-{name}.png");
                }
            }
        }

        ArpgGame.Run.RunLaunchOptions.Reset();
        _autoPick = true;
        await Launch();
        await Wait(1.5f);
        Run()?.Pause.Open();
        await Wait(0.4f);
        await Snap("menus-pause.png");
        Run()?.Pause.Close();
        await Wait(0.3f);
        Run()?.State.EndRun(false);
        await Wait(1.5f);
        await Snap("menus-results.png");

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }

    /// <summary>A second session in this process on its own multiplayer branch
    /// (the pattern of CoopSmokeTest's intruders).</summary>
    private async Task<NetSession> CaptureIntruder()
    {
        var holder = new Node { Name = "CaptureFriend" };
        GetTree().Root.AddChild(holder);
        GetTree().SetMultiplayer(new SceneMultiplayer(), holder.GetPath());
        var friend = new NetSession { Name = "NetSession" };
        holder.AddChild(friend);
        await Wait(0.05f);
        return friend;
    }

    private async Task WaitUntil(System.Func<bool> condition, float timeout)
    {
        float waited = 0f;
        while (!condition() && waited < timeout)
        {
            await Wait(0.1f);
            waited += 0.1f;
        }
    }
}
