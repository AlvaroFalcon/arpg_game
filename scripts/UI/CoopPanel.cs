#nullable enable
using System;
using Godot;
using ArpgGame.Core;
using ArpgGame.Net;

namespace ArpgGame.UI;

/// <summary>
/// Co-op from the main menu — docs/design/multiplayer.md -> "Session and lobby".
/// Host (a port, 7777 by default) or Join (address:port), then the lobby: up
/// to four players with name, class and level; the host presses Start, anyone
/// can Leave. Everyone plays their own saved character (the active one).
/// </summary>
public partial class CoopPanel : PanelContainer
{
    public event Action? Back;

    /// <summary>Set before adding: host on this port / join this address right away
    /// (scripts/coop_local.sh passes them on the command line).</summary>
    public int? AutoHostPort { get; set; }
    public string? AutoJoin { get; set; }

    /// <summary>Host only: start by itself once this many players are in (scripts, captures).</summary>
    public int AutoStartAt { get; set; }

    private VBoxContainer _body = null!;
    private LineEdit _port = null!;
    private LineEdit _address = null!;
    private Label _status = null!;
    private VBoxContainer _members = null!;
    private Button _start = null!;
    private Button _leave = null!;
    private Control _connect = null!;
    private NetSession? _session;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(760f, 0f);
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, UiTheme.PanelBorder, 2, 8, 18));
        _body = UiTheme.VBox(this, 10);
        _body.AddChild(UiTheme.Label("Co-op — Survival Rift", 32, UiTheme.Gold, HorizontalAlignment.Center));

        Profile? profile = Profile.Get(this);
        bool hasCharacter = profile is { HasCharacters: true };
        string who = hasCharacter
            ? $"Playing as {profile!.Active.Name}, level {profile.Active.Level} {profile.Active.AscendancyDef?.Name ?? profile.Active.Class.Name} (change it in Play)"
            : "Create a character first (Play → New character).";
        _body.AddChild(UiTheme.Label(who, 16, UiTheme.TextDim, HorizontalAlignment.Center));

        var connect = new VBoxContainer();
        connect.AddThemeConstantOverride("separation", 8);
        _body.AddChild(connect);
        _connect = connect;

        HBoxContainer hostRow = UiTheme.HBox(connect, 10);
        hostRow.AddChild(UiTheme.Label("Port", 18, UiTheme.Text));
        _port = new LineEdit { Text = NetTransport.DefaultPort.ToString(), CustomMinimumSize = new Vector2(120f, 0f) };
        hostRow.AddChild(_port);
        Button host = UiTheme.Button("Host co-op", Host, 18, 220f);
        host.Disabled = !hasCharacter;
        hostRow.AddChild(host);

        HBoxContainer joinRow = UiTheme.HBox(connect, 10);
        joinRow.AddChild(UiTheme.Label("Address", 18, UiTheme.Text));
        _address = new LineEdit { Text = $"127.0.0.1:{NetTransport.DefaultPort}", CustomMinimumSize = new Vector2(260f, 0f), PlaceholderText = "ip:port" };
        joinRow.AddChild(_address);
        Button join = UiTheme.Button("Join", Join, 18, 160f);
        join.Disabled = !hasCharacter;
        joinRow.AddChild(join);

        Label help = UiTheme.Label(
            "Same PC or LAN: 127.0.0.1 / the host's local IP. Over the internet: the host's Tailscale, Radmin VPN or ZeroTier address, " +
            $"or the host forwards UDP port {NetTransport.DefaultPort} on their router and shares their public IP.",
            14, UiTheme.TextDim);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        help.CustomMinimumSize = new Vector2(700f, 0f);
        connect.AddChild(help);

        _status = UiTheme.Label("", 16, UiTheme.Ember, HorizontalAlignment.Center);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new Vector2(700f, 0f);
        _body.AddChild(_status);

        _body.AddChild(UiTheme.Label("Party", 20, UiTheme.Gold));
        _members = UiTheme.VBox(_body, 4);

        HBoxContainer buttons = UiTheme.HBox(_body, 12);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        _start = UiTheme.Button("Start", () => _session?.StartRun(), 20, 200f);
        buttons.AddChild(_start);
        _leave = UiTheme.Button("Leave", () => { _session?.Leave(); Refresh(); }, 20, 160f);
        buttons.AddChild(_leave);
        buttons.AddChild(UiTheme.Button("Back", () =>
        {
            _session?.Leave();
            Back?.Invoke();
        }, 20, 160f));

        _session = NetSession.Get(this);
        if (_session is not null)
        {
            _session.LobbyChanged += Refresh;
        }

        Refresh();
        if (hasCharacter && AutoHostPort is { } port)
        {
            _port.Text = port.ToString();
            Host();
        }
        else if (hasCharacter && AutoJoin is { } address)
        {
            _address.Text = address;
            Join();
        }
    }

    public override void _ExitTree()
    {
        if (_session is not null)
        {
            _session.LobbyChanged -= Refresh;
        }
    }

    private void Host()
    {
        if (_session is null || Profile.Get(this) is not { } profile)
        {
            return;
        }

        int port = _port.Text.ToInt();
        if (port is <= 0 or > 65535)
        {
            _status.Text = "That port is not a number between 1 and 65535.";
            return;
        }

        _session.Host(port, profile.Active);
        Refresh();
    }

    private void Join()
    {
        if (_session is null || Profile.Get(this) is not { } profile)
        {
            return;
        }

        if (!NetTransport.TryParseAddress(_address.Text, out string host, out int port))
        {
            _status.Text = "Write the host's address as ip:port, e.g. 100.64.0.7:7777.";
            return;
        }

        _session.Join(host, port, profile.Active);
        Refresh();
    }

    private void Refresh()
    {
        if (_session is null)
        {
            _status.Text = "Co-op is unavailable (no session service).";
            return;
        }

        _status.Text = _session.Status;
        foreach (Node child in _members.GetChildren())
        {
            _members.RemoveChild(child);
            child.QueueFree();
        }

        for (int i = 0; i < _session.Members.Count; i++)
        {
            LobbyMember m = _session.Members[i];
            string role = m.PeerId == 1 ? "  (host)" : "";
            string you = m.PeerId == _session.LocalPeerId ? "  — you" : "";
            _members.AddChild(UiTheme.Label($"{i + 1}.  {m.Name}  ·  level {m.Level} {m.ClassName}{role}{you}", 18, m.PeerId == _session.LocalPeerId ? UiTheme.Gold : UiTheme.Text));
        }

        if (_session.Members.Count == 0)
        {
            _members.AddChild(UiTheme.Label(_session.Active ? "Waiting for the lobby…" : "Not in a party.", 16, UiTheme.TextDim));
        }

        _connect.Visible = !_session.Active;
        _start.Visible = _session.IsHost;
        _start.Text = _session.Members.Count > 1 ? $"Start ({_session.Members.Count} players)" : "Start (alone)";
        _leave.Visible = _session.Active;
        if (AutoStartAt > 0 && _session.IsHost && !_session.InRun && _session.Members.Count >= AutoStartAt)
        {
            CallDeferred(MethodName.AutoStart);
        }
    }

    private void AutoStart()
    {
        if (_session is { IsHost: true, InRun: false })
        {
            _session.StartRun();
        }
    }
}
