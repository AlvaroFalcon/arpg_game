#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Net;

namespace ArpgGame.UI;

/// <summary>
/// Co-op from the main menu — docs/design/menus.md -> "Co-op" and
/// docs/design/multiplayer.md -> "Session and lobby". One screen, one state at
/// a time:
/// <list type="bullet">
/// <item><b>Choose</b>: two cards, Host a game / Join a friend. Joining needs
/// both the host's address (finds their PC) and the join code (lets you in);
/// pasting the host's invite into the address field fills both. Mistakes are
/// shown next to the field, Enter submits.</item>
/// <item><b>Connecting</b>: a spinner, the step (reaching the host, checking the
/// code, joining), the elapsed time and Cancel. Gives up after
/// <see cref="NetSession.ConnectTimeout"/>.</item>
/// <item><b>Failed</b>: what went wrong (unreachable, wrong code, full, already
/// started, version) and what to check, with Try again.</item>
/// <item><b>Lobby</b>: the host's invite (address and code, big, with Copy and
/// Copy invite, the router state) and four party slots, free ones marked.</item>
/// </list>
/// </summary>
public partial class CoopPanel : PanelContainer
{
    public enum View
    {
        Unavailable,
        NoCharacter,
        Choose,
        Connecting,
        Failed,
        Lobby,
    }

    public enum PanelPurpose
    {
        /// <summary>The main menu's "With friends": host or join, then the lobby leads to the hideout.</summary>
        Menu,

        /// <summary>The hideout's pause menu: open this hideout to friends, see the party; closing never leaves it.</summary>
        Invite,
    }

    /// <summary>Set before adding.</summary>
    public PanelPurpose Purpose { get; set; }

    /// <summary>Invite: runs before hosting (the hideout saves the player, so the lobby gets what they wear).</summary>
    public Action? BeforeHost { get; set; }

    /// <summary>Invite, a guest's "Leave the party".</summary>
    public event Action? LeaveParty;

    public event Action? Back;

    /// <summary>"Create a character" from the no-character state.</summary>
    public event Action? CreateCharacter;

    /// <summary>"Change" next to "Playing as" (the main menu offers it; set <see cref="OfferChange"/>).</summary>
    public event Action? ChangeCharacter;

    /// <summary>Set before adding: show [Change] next to the character line.</summary>
    public bool OfferChange { get; set; }

    /// <summary>Set before adding: host on this port / join this address right away
    /// (scripts/coop_local.sh passes them on the command line).</summary>
    public int? AutoHostPort { get; set; }
    public string? AutoJoin { get; set; }

    /// <summary>Set before adding: the join code to host with / join with (scripts).</summary>
    public string? AutoCode { get; set; }

    /// <summary>Set before adding: don't try UPnP (scripts/coop_local.sh, one PC).</summary>
    public bool NoUpnp { get; set; }

    /// <summary>Host only: start by itself once this many players are in (scripts, captures).</summary>
    public int AutoStartAt { get; set; }

    /// <summary>Set before adding: the session to drive (tests bind a second one;
    /// the game uses the autoload).</summary>
    public NetSession? Session { get; set; }

    public View Current { get; private set; } = View.Unavailable;

    /// <summary>Inline errors under the join fields ("" when fine).</summary>
    public string AddressError => _addressError?.Text ?? "";
    public string CodeError => _codeError?.Text ?? "";
    public string PortError => _portError?.Text ?? "";

    /// <summary>The failure card's title ("Wrong join code", "Couldn't reach the host"…).</summary>
    public string FailureTitle { get; private set; } = "";

    /// <summary>One line per party slot as shown ("Kaelen · Level 24 Samurai · Host · You", "Free — waiting for a friend").</summary>
    public IReadOnlyList<string> SlotTexts => _slotTexts;

    /// <summary>What the host's "Copy invite" puts on the clipboard.</summary>
    public string InviteText { get; private set; } = "";

    /// <summary>The line under the host's address saying what kind of address it is (tests).</summary>
    public string AddressKind { get; private set; } = "";

    /// <summary>Whether the host lobby offers "Find my public IP" (tests).</summary>
    public bool PublicIpOffered { get; private set; }

    // Kept across openings of the screen (a retry shouldn't make you type again).
    private static string _lastAddress = "";
    private static string _lastCode = "";

    private VBoxContainer _body = null!;
    private VBoxContainer _view = null!;
    private Label _title = null!;
    private Label _who = null!;
    private Button _change = null!;
    private Label _notice = null!;
    private Button _back = null!;
    private string _signature = "";

    /// <summary>The host's public IP, looked up on request when the router didn't
    /// open the port (UPnP can't tell then); "" until asked.</summary>
    private string _publicIp = "";
    private string _publicIpState = "";
    private HttpRequest? _ipLookup;

    /// <summary>Where "Find my public IP" asks. Only contacted when the host presses
    /// the button; it sees nothing but the request (docs/design/multiplayer.md -> "Security").</summary>
    public const string PublicIpService = "https://api.ipify.org";
    private bool _attempted;
    private bool _lastWasHost;
    private readonly List<string> _slotTexts = new();

    private LineEdit? _port;
    private LineEdit? _address;
    private LineEdit? _code;
    private Label? _portError;
    private Label? _addressError;
    private Label? _codeError;
    private Label? _pasted;
    private Label? _elapsed;
    private readonly List<(Label Label, JoinPhase Phase)> _steps = new();

    private NetSession? _session;

    private bool HasCharacter => Profile.Get(this) is { HasCharacters: true };

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(1000f, 0f);
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, UiTheme.PanelBorder, 2, 8, 18));
        _body = UiTheme.VBox(this, 10);
        _title = UiTheme.Label(Purpose == PanelPurpose.Invite ? "Invite friends" : "Play with friends", 32, UiTheme.Gold, HorizontalAlignment.Center);
        _body.AddChild(_title);
        HBoxContainer whoRow = UiTheme.HBox(_body, 10);
        whoRow.Alignment = BoxContainer.AlignmentMode.Center;
        _who = UiTheme.Label("", 16, UiTheme.TextDim, HorizontalAlignment.Center);
        whoRow.AddChild(_who);
        _change = UiTheme.Button("Change", () => ChangeCharacter?.Invoke(), 14, 90f);
        _change.CustomMinimumSize = new Vector2(90f, 30f);
        whoRow.AddChild(_change);
        // Short feedback (copied, host errors) right under the title, where the eye is.
        _notice = UiTheme.Label("", 16, UiTheme.Experience, HorizontalAlignment.Center);
        _notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _notice.Visible = false;
        _body.AddChild(_notice);
        _view = UiTheme.VBox(_body, 10);

        HBoxContainer footer = UiTheme.HBox(_body, 12);
        footer.Alignment = BoxContainer.AlignmentMode.Center;
        _back = UiTheme.Button("Back", GoBack, 18, 160f);
        footer.AddChild(_back);

        _session = Session ?? NetSession.Get(this);
        if (_session is not null)
        {
            _session.LobbyChanged += Refresh;
        }

        Profile? profile = Profile.Get(this);
        _who.Text = profile is { HasCharacters: true }
            ? $"Playing as {profile.Active.Name}, level {profile.Active.Level} {profile.Active.AscendancyDef?.Name ?? profile.Active.Class.Name}"
            : "";
        _who.Visible = _who.Text.Length > 0;
        _change.Visible = _who.Visible && OfferChange;

        Refresh();
        if (HasCharacter && AutoHostPort is { } port)
        {
            if (_port is not null)
            {
                _port.Text = port.ToString();
            }

            PressHost();
        }
        else if (HasCharacter && AutoJoin is { } address)
        {
            SetJoinFields(address, AutoCode ?? "");
            PressJoin();
        }
    }

    public override void _ExitTree()
    {
        if (_session is not null)
        {
            _session.LobbyChanged -= Refresh;
        }
    }

    public override void _Process(double delta)
    {
        if (_session is null)
        {
            return;
        }

        if (Current == View.Connecting)
        {
            UpdateConnecting();
        }

        // The session can end without a lobby event reaching us first (timeouts).
        if (Signature() != _signature)
        {
            Refresh();
        }
    }

    public override void _Input(InputEvent e)
    {
        // Invite: the overlay owns Esc (it closes the overlay; nothing here ends the party).
        if (Purpose == PanelPurpose.Invite || !IsVisibleInTree() || !e.IsActionPressed("ui_cancel"))
        {
            return;
        }

        switch (Current)
        {
            case View.Connecting:
                PressCancel();
                break;
            case View.Failed:
                ShowForm();
                break;
            case View.Lobby:
                // Leaving a lobby ends it for everyone: only the Leave button does that.
                return;
            default:
                GoBack();
                break;
        }

        GetViewport().SetInputAsHandled();
    }

    // --- actions (public for tests and the capture tool) -------------------------

    public void SetJoinFields(string address, string code)
    {
        _lastAddress = address;
        _lastCode = code;
        if (_address is not null)
        {
            _address.Text = address;
        }

        if (_code is not null)
        {
            _code.Text = code;
        }
    }

    public void SetHostPort(int port)
    {
        if (_port is not null)
        {
            _port.Text = port.ToString();
        }
    }

    public void PressHost()
    {
        EnsureForm();
        if (_session is null || Profile.Get(this) is not { HasCharacters: true } profile)
        {
            return;
        }

        int port = _port?.Text.Trim().ToInt() ?? NetTransport.DefaultPort;
        if (_port is not null && (!_port.Text.Trim().IsValidInt() || port is <= 0 or > 65535))
        {
            SetError(_port, _portError, "The port is a number from 1 to 65535 (7777 unless you changed it on purpose).");
            return;
        }

        _attempted = true;
        _lastWasHost = true;
        BeforeHost?.Invoke();
        if (_session.Host(port, profile.Active, AutoCode, openRouterPort: !NoUpnp) != Error.Ok)
        {
            SetError(_port, _portError, _session.Status);
            return;
        }

        if (Purpose == PanelPurpose.Invite)
        {
            // Already home: the party's phase is the hideout from the start.
            _session.EnterHideoutInPlace();
        }

        Refresh();
    }

    public void PressJoin()
    {
        EnsureForm();
        if (_session is null || Profile.Get(this) is not { HasCharacters: true } profile || _address is null || _code is null)
        {
            return;
        }

        string addressText = _address.Text.Trim();
        string codeText = _code.Text.Trim();
        _lastAddress = addressText;
        _lastCode = codeText;
        string? addressWrong = addressText.Length == 0
            ? "Type the host's address. They see it in their lobby (Copy invite)."
            : !NetTransport.TryParseAddress(addressText, out _, out _)
                ? "That isn't an address. It looks like 203.0.113.7:7777 — four numbers with dots, then : and the port."
                : null;
        string normalised = JoinCode.Normalize(codeText);
        string? codeWrong = codeText.Length == 0
            ? "Type the join code from the host's lobby — 6 letters or digits, like K7Q-29M."
            : !JoinCode.IsWellFormed(normalised)
                ? $"A join code is {JoinCode.Length} letters or digits, like K7Q-29M (there is no 0, O, 1, I or L)."
                : null;
        SetError(_address, _addressError, addressWrong);
        SetError(_code, _codeError, codeWrong);
        if (addressWrong is not null || codeWrong is not null)
        {
            (addressWrong is not null ? _address : _code).GrabFocus();
            return;
        }

        NetTransport.TryParseAddress(addressText, out string host, out int port);
        _attempted = true;
        _lastWasHost = false;
        if (_session.Join(host, port, profile.Active, codeText) != Error.Ok)
        {
            SetError(_address, _addressError, _session.Status);
            return;
        }

        Refresh();
    }

    public void PressCancel()
    {
        _attempted = false;
        _session?.Leave();
        Refresh();
    }

    /// <summary>The Back button (tests).</summary>
    public void PressBack() => GoBack();

    private void GoBack()
    {
        if (Purpose != PanelPurpose.Invite)
        {
            _session?.Leave();
        }

        Back?.Invoke();
    }

    /// <summary>Host/Join pressed from another state (a retry, scripts): the form first.</summary>
    private void EnsureForm()
    {
        if (Current is View.Failed)
        {
            ShowForm();
        }
    }

    /// <summary>From the failure card back to the form (fields kept).</summary>
    private void ShowForm()
    {
        _attempted = false;
        _signature = "";
        Refresh();
    }

    private void Retry()
    {
        if (_lastWasHost)
        {
            PressHost();
        }
        else
        {
            PressJoin();
        }
    }

    // --- state --------------------------------------------------------------------

    private View Decide()
    {
        if (_session is null)
        {
            return View.Unavailable;
        }

        if (!HasCharacter)
        {
            return View.NoCharacter;
        }

        if (_session.IsHost || (_session.IsClient && _session.Phase == JoinPhase.InLobby))
        {
            return View.Lobby;
        }

        if (_session.IsClient)
        {
            return View.Connecting;
        }

        return _attempted && _session.LastEnd != SessionEnd.None ? View.Failed : View.Choose;
    }

    /// <summary>Everything the current view shows; a change rebuilds it.</summary>
    private string Signature()
    {
        View view = Decide();
        if (_session is null)
        {
            return view.ToString();
        }

        string lobby = view == View.Lobby
            ? string.Join("|", _session.Members.Select(m => $"{m.PeerId}:{m.Name}:{m.Level}:{m.ClassName}:{_session.CanEditOf(m.PeerId)}"))
              + $"#{_session.Code}#{_session.Ports.State}#{_session.Ports.Message}#{_session.Ports.ExternalAddress}#{_session.SecurityLog.Count}#{_session.InRun}#{_publicIp}#{_publicIpState}"
            : "";
        string connecting = view == View.Connecting ? _session.Phase.ToString() : "";
        string failed = view == View.Failed ? $"{_session.LastEnd}:{_session.Status}" : "";
        return $"{view}#{lobby}#{connecting}#{failed}";
    }

    private void Refresh()
    {
        string signature = Signature();
        if (signature == _signature)
        {
            return;
        }

        _signature = signature;
        // Keep what was typed when the form is rebuilt.
        if (_address is not null && IsInstanceValid(_address))
        {
            _lastAddress = _address.Text;
        }

        if (_code is not null && IsInstanceValid(_code))
        {
            _lastCode = _code.Text;
        }

        foreach (Node child in _view.GetChildren())
        {
            _view.RemoveChild(child);
            child.QueueFree();
        }

        _port = _address = _code = null;
        _portError = _addressError = _codeError = _pasted = null;
        _elapsed = null;
        _steps.Clear();
        _slotTexts.Clear();
        Current = Decide();
        _back.Visible = Current is not View.Lobby and not View.Connecting;
        if (Purpose == PanelPurpose.Invite)
        {
            _title.Text = Current == View.Lobby ? "Your party" : "Invite friends";
            _back.Text = "Close";
            _back.Visible = Current is not View.Connecting;
        }

        // A different character only before hosting or joining (the lobby has this one).
        _change.Visible = _who.Visible && OfferChange && Current is View.Choose or View.Failed;
        switch (Current)
        {
            case View.Unavailable:
                _view.AddChild(Wrapped("Co-op is unavailable (no session service).", 18, UiTheme.Ember, 900f));
                break;
            case View.NoCharacter:
                BuildNoCharacter();
                break;
            case View.Choose when Purpose == PanelPurpose.Invite:
                BuildInviteChoose();
                break;
            case View.Choose:
                BuildChoose();
                break;
            case View.Connecting:
                BuildConnecting();
                break;
            case View.Failed:
                BuildFailed();
                break;
            case View.Lobby:
                BuildLobby();
                break;
        }

        if (Current == View.Lobby && AutoStartAt > 0 && _session is { IsHost: true, InRun: false } s && s.Members.Count >= AutoStartAt)
        {
            CallDeferred(MethodName.AutoStart);
        }
    }

    private void AutoStart()
    {
        if (_session is { IsHost: true, InRun: false })
        {
            _session.EnterHideout();
        }
    }

    // --- views --------------------------------------------------------------------

    private void BuildNoCharacter()
    {
        VBoxContainer box = UiTheme.VBox(_view, 12);
        box.AddChild(UiTheme.Label("You need a character first", 24, UiTheme.Text, HorizontalAlignment.Center));
        box.AddChild(Wrapped("Everyone plays their own saved character in co-op, and keeps what they find. Create one (it takes a few seconds), then come back here to host or join.", 16, UiTheme.TextDim, 900f, HorizontalAlignment.Center));
        Button create = UiTheme.PrimaryButton("Create a character", () => CreateCharacter?.Invoke(), 20, 320f);
        create.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        box.AddChild(create);
        create.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void BuildChoose()
    {
        HBoxContainer cards = UiTheme.HBox(_view, 16);
        cards.Alignment = BoxContainer.AlignmentMode.Center;

        // Host
        VBoxContainer host = Card(cards, "Host a game");
        host.AddChild(Wrapped($"Open a lobby on this PC for up to {PartyRules.MaxPlayers} players. You get an address and a join code to send your friends.", 15, UiTheme.TextDim, 400f));
        host.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        HBoxContainer portRow = UiTheme.HBox(host, 8);
        portRow.AddChild(UiTheme.Label("Port", 15, UiTheme.TextDim));
        _port = UiTheme.Field("7777", 100f, 16);
        _port.Text = NetTransport.DefaultPort.ToString();
        _port.MaxLength = 5;
        _port.TextSubmitted += _ => PressHost();
        _port.TextChanged += _ => SetError(_port, _portError, null);
        portRow.AddChild(_port);
        portRow.AddChild(UiTheme.Label("UDP · leave 7777 unless you know why", 13, UiTheme.TextDim));
        _portError = ErrorLabel(host);
        Button hostButton = UiTheme.PrimaryButton("Host lobby", PressHost, 20, 400f);
        host.AddChild(hostButton);

        // Join
        VBoxContainer join = Card(cards, "Join a friend");
        join.AddChild(Wrapped("You need both from the host's lobby: the address finds their PC, the code lets you in.", 15, UiTheme.TextDim, 400f));
        join.AddChild(UiTheme.Label("Host's address", 16, UiTheme.Text));
        _address = UiTheme.Field("e.g. 203.0.113.7:7777 — or paste the invite", 400f);
        _address.Text = _lastAddress;
        join.AddChild(_address);
        _addressError = ErrorLabel(join);
        _pasted = UiTheme.Label("", 13, UiTheme.Experience);
        _pasted.Visible = false;
        join.AddChild(_pasted);
        join.AddChild(UiTheme.Label("Join code", 16, UiTheme.Text));
        _code = UiTheme.Field("K7Q-29M", 200f, 20);
        _code.MaxLength = 12;
        _code.Text = _lastCode;
        join.AddChild(_code);
        _codeError = ErrorLabel(join);
        Button joinButton = UiTheme.PrimaryButton("Join", PressJoin, 20, 400f);
        join.AddChild(joinButton);

        _address.TextChanged += OnAddressTyped;
        _address.TextSubmitted += _ =>
        {
            if (_code!.Text.Trim().Length == 0)
            {
                _code.GrabFocus();
            }
            else
            {
                PressJoin();
            }
        };
        _code.TextChanged += _ => SetError(_code, _codeError, null);
        _code.TextSubmitted += _ => PressJoin();

        _view.AddChild(Wrapped(
            $"Same PC: 127.0.0.1:{NetTransport.DefaultPort}.  Same Wi-Fi: the host's 192.168… address.  Over the internet: the address the host's lobby shows " +
            "(their router opens the port by itself when it can), or a VPN both of you run — Tailscale, Radmin VPN, ZeroTier.",
            14, UiTheme.TextDim, 960f, HorizontalAlignment.Center));
        (_lastAddress.Length > 0 ? joinButton : hostButton).CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>Invite, not hosting yet: one card, to open this hideout to friends.</summary>
    private void BuildInviteChoose()
    {
        HBoxContainer cards = UiTheme.HBox(_view, 16);
        cards.Alignment = BoxContainer.AlignmentMode.Center;
        VBoxContainer host = Card(cards, "Open your hideout to friends", 620f);
        host.AddChild(Wrapped($"Up to {PartyRules.MaxPlayers - 1} friends can join you here, each with their own character, wallet and loot. "
            + "You get an address and a join code to send them; they join from the main menu: Play → With friends → Join a friend.", 15, UiTheme.TextDim, 580f));
        HBoxContainer portRow = UiTheme.HBox(host, 8);
        portRow.AddChild(UiTheme.Label("Port", 15, UiTheme.TextDim));
        _port = UiTheme.Field("7777", 100f, 16);
        _port.Text = NetTransport.DefaultPort.ToString();
        _port.MaxLength = 5;
        _port.TextSubmitted += _ => PressHost();
        _port.TextChanged += _ => SetError(_port, _portError, null);
        portRow.AddChild(_port);
        portRow.AddChild(UiTheme.Label("UDP · your router opens it by itself when it can", 13, UiTheme.TextDim));
        _portError = ErrorLabel(host);
        Button hostButton = UiTheme.PrimaryButton("Open my hideout to friends", PressHost, 20, 580f);
        host.AddChild(hostButton);
        hostButton.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>A pasted invite fills both fields.</summary>
    private void OnAddressTyped(string text)
    {
        SetError(_address!, _addressError, null);
        if (NetTransport.TryParseInvite(text, out string address, out string code) && address != text.Trim())
        {
            _address!.Text = address;
            _address.CaretColumn = address.Length;
            _code!.Text = code;
            SetError(_code, _codeError, null);
            _pasted!.Text = "Invite pasted — address and join code filled in.";
            _pasted.Visible = true;
        }
    }

    private void BuildConnecting()
    {
        NetSession s = _session!;
        VBoxContainer box = UiTheme.VBox(_view, 12);
        HBoxContainer head = UiTheme.HBox(box, 14);
        head.Alignment = BoxContainer.AlignmentMode.Center;
        head.AddChild(new Spinner(34f));
        head.AddChild(UiTheme.Label($"Joining {s.JoinTarget}", 26, UiTheme.Text));

        VBoxContainer steps = UiTheme.VBox(box, 6);
        steps.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        foreach ((JoinPhase phase, string text) in new[]
        {
            (JoinPhase.Connecting, "1.  Reaching the host's PC"),
            (JoinPhase.CheckingCode, "2.  Checking the join code"),
            (JoinPhase.Joining, "3.  Joining the party"),
        })
        {
            Label l = UiTheme.Label(text, 18, UiTheme.TextDim);
            steps.AddChild(l);
            _steps.Add((l, phase));
        }

        _elapsed = UiTheme.Label("", 15, UiTheme.TextDim, HorizontalAlignment.Center);
        _elapsed.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _elapsed.CustomMinimumSize = new Vector2(900f, 0f);
        box.AddChild(_elapsed);
        Button cancel = UiTheme.Button("Cancel", PressCancel, 18, 200f);
        cancel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        box.AddChild(cancel);
        cancel.CallDeferred(Control.MethodName.GrabFocus);
        UpdateConnecting();
    }

    /// <summary>Each frame while connecting: which step, how long, when it gives up.</summary>
    private void UpdateConnecting()
    {
        if (_session is null || _elapsed is null || !IsInstanceValid(_elapsed))
        {
            return;
        }

        JoinPhase phase = _session.Phase;
        foreach ((Label label, JoinPhase step) in _steps)
        {
            bool done = step < phase;
            bool current = step == phase;
            label.AddThemeColorOverride("font_color", done ? UiTheme.Experience : current ? UiTheme.Gold : UiTheme.TextDim);
            string baseText = label.Text.Split("   ")[0];
            label.Text = done ? $"{baseText}   — done" : baseText;
        }

        double total = NetSession.Clock - _session.JoinStarted;
        double inPhase = NetSession.Clock - _session.PhaseSince;
        _elapsed.Text = phase == JoinPhase.Connecting
            ? inPhase < 4.0
                ? $"{total:0} s"
                : $"{total:0} s — no answer yet. Gives up after {NetSession.ConnectTimeout:0} s."
            : $"{total:0} s — the host answered.";
    }

    private void BuildFailed()
    {
        NetSession s = _session!;
        (string title, string[] checks) = Explain(s.LastEnd, s.JoinTarget);
        FailureTitle = title;
        VBoxContainer box = UiTheme.VBox(_view, 10);
        box.AddChild(UiTheme.Label(title, 28, UiTheme.Life, HorizontalAlignment.Center));
        // The session's own words, minus a repeat of the title.
        string detail = s.Status.StartsWith(title, StringComparison.OrdinalIgnoreCase) ? s.Status[title.Length..].TrimStart('.', ' ', '—') : s.Status;
        if (detail.Length > 0)
        {
            box.AddChild(Wrapped(detail, 17, UiTheme.Text, 900f, HorizontalAlignment.Center));
        }
        if (checks.Length > 0)
        {
            var list = new PanelContainer();
            list.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.07f, 0.06f, 0.1f), UiTheme.PanelBorder, 1, 6, 14));
            box.AddChild(list);
            VBoxContainer lines = UiTheme.VBox(list, 6);
            lines.AddChild(UiTheme.Label("What to check", 16, UiTheme.Gold));
            foreach (string check in checks)
            {
                lines.AddChild(Wrapped($"•  {check}", 15, UiTheme.Text, 880f));
            }
        }

        HBoxContainer buttons = UiTheme.HBox(box, 12);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        Button retry = UiTheme.PrimaryButton("Try again", Retry, 18, 220f);
        buttons.AddChild(retry);
        buttons.AddChild(UiTheme.Button(_lastWasHost ? "Back to co-op" : "Change address or code", ShowForm, 18, 280f));
        retry.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>The failure card for each way a session ends: a title in the
    /// player's words and what to check next.</summary>
    public static (string Title, string[] Checks) Explain(SessionEnd why, string target) => why switch
    {
        SessionEnd.Unreachable => ("Couldn't reach the host", new[]
        {
            $"The address is exactly the one in the host's lobby, port included ({(target.Length > 0 ? target : "ip:7777")}).",
            "The host is still in their lobby (the lobby closes if they press Back).",
            "The host's lobby says whether their router opened the port. If it says it didn't, the host forwards UDP 7777 to their PC in the router's settings — or you both install a VPN like Tailscale or Radmin VPN and the host sends their VPN address instead.",
            "The host's firewall lets the game in (Windows asks the first time you host: allow it).",
            "Same PC: 127.0.0.1:7777.  Same Wi-Fi: the host's 192.168… address, not the internet one.",
        }),
        SessionEnd.WrongCode => ("Wrong join code", new[]
        {
            "The host's PC answered, so the address is right — only the code didn't match.",
            "Ask for the code in their lobby now: it changes with every new lobby and with New code.",
            "Codes never contain 0, O, 1, I or L. Case and the dash don't matter.",
            "After 5 wrong codes in a minute the host ignores your address for a minute.",
        }),
        SessionEnd.PartyFull => ("The party is full", new[]
        {
            $"A party is at most {PartyRules.MaxPlayers} players. Ask someone to leave the lobby, then try again.",
        }),
        SessionEnd.RunStarted => ("The run has already started", new[]
        {
            "Nobody can join a run in progress. When it ends, the host opens a new lobby and you join that one (with its new code).",
        }),
        SessionEnd.VersionMismatch => ("Different game versions", new[]
        {
            "Everyone needs the same version. Update the game from the launcher (both of you), then try again.",
        }),
        SessionEnd.CharacterRefused => ("Your character was refused", new[]
        {
            "The host checks every character for broken data. Pick another character in Play and try again.",
        }),
        SessionEnd.NotAccepted => ("The host turned the connection away", new[]
        {
            "If you typed wrong codes several times, wait a minute: the host ignores an address for a minute after 5 wrong codes.",
            "Check the code with the host, then try again.",
        }),
        SessionEnd.HostClosed => ("The host closed the lobby", new[]
        {
            "Ask the host to open a new lobby and send the new invite: the code changes every time.",
        }),
        SessionEnd.HostLeftRun => ("The host left", Array.Empty<string>()),
        _ => ("Something went wrong", Array.Empty<string>()),
    };

    private void BuildLobby()
    {
        NetSession s = _session!;
        HBoxContainer columns = UiTheme.HBox(_view, 18);
        columns.Alignment = BoxContainer.AlignmentMode.Center;

        if (s.IsHost)
        {
            BuildInvite(columns, s);
        }

        // Party slots.
        VBoxContainer party = Card(columns, $"Party  {s.Members.Count}/{PartyRules.MaxPlayers}", s.IsHost ? 440f : 640f);
        for (int i = 0; i < PartyRules.MaxPlayers; i++)
        {
            party.AddChild(Slot(i < s.Members.Count ? s.Members[i] : null, s));
        }

        foreach (string line in s.IsHost ? s.SecurityLog.TakeLast(2) : Enumerable.Empty<string>())
        {
            party.AddChild(Wrapped(line, 12, UiTheme.TextDim, 400f));
        }

        HBoxContainer buttons = UiTheme.HBox(_view, 12);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        if (Purpose == PanelPurpose.Invite)
        {
            BuildInviteButtons(buttons, s);
            return;
        }

        if (s.IsHost)
        {
            int n = s.Members.Count;
            Button start = UiTheme.PrimaryButton(n > 1 ? $"Go to the hideout — {n} players" : "Go to the hideout", () => _session?.EnterHideout(), 20, 360f);
            UiTheme.InstantTooltip(start, "Everyone goes to your hideout. Friends can still join from there, and anyone can open a rift for the party.");
            buttons.AddChild(start);
            start.CallDeferred(Control.MethodName.GrabFocus);
        }
        else
        {
            string hostName = s.Members.FirstOrDefault(m => m.PeerId == 1)?.Name ?? "the host";
            buttons.AddChild(new Spinner(24f));
            buttons.AddChild(UiTheme.Label($"In the party — waiting for {hostName} to open the hideout.", 18, UiTheme.Text));
        }

        buttons.AddChild(UiTheme.Button(s.IsHost ? "Close lobby" : "Leave", () =>
        {
            _attempted = false;
            _session?.Leave();
            Refresh();
        }, 18, 180f));
    }

    /// <summary>Invite, in a party: hosting stops only on purpose; closing keeps everything going.</summary>
    private void BuildInviteButtons(HBoxContainer buttons, NetSession s)
    {
        if (s.IsHost)
        {
            buttons.AddChild(UiTheme.Label(s.Members.Count > 1 ? "Your friends are in your town." : "Friends who join appear here in your town.", 16, UiTheme.TextDim));
            Button stop = UiTheme.Button("Stop hosting", () =>
            {
                _attempted = false;
                _session?.Leave();
                Refresh();
            }, 18, 200f);
            UiTheme.InstantTooltip(stop, "Everyone else goes back to their main menu. You stay here, playing alone.");
            buttons.AddChild(stop);
        }
        else
        {
            buttons.AddChild(UiTheme.Button("Leave the party", () => LeaveParty?.Invoke(), 18, 220f));
        }
    }

    /// <summary>Invite, host: the "Allow editing" toggle of a friend's slot (tests).</summary>
    public void SetAllowEditing(long peer, bool allowed)
    {
        if (Purpose == PanelPurpose.Invite)
        {
            _session?.SetCanEdit(peer, allowed);
        }
    }

    /// <summary>Invite: the host's "Stop hosting" (tests).</summary>
    public void PressStopHosting()
    {
        if (Purpose == PanelPurpose.Invite && _session is { IsHost: true })
        {
            _attempted = false;
            _session.Leave();
            Refresh();
        }
    }

    private void BuildInvite(HBoxContainer columns, NetSession s)
    {
        VBoxContainer invite = Card(columns, "Invite friends", 480f);
        invite.AddChild(UiTheme.Label("Send them both — or Copy invite, which holds both:", 14, UiTheme.TextDim));

        int port = s.Port;
        string[] local = PortMapper.LocalAddresses();
        (string address, string kind, Color kindColour) = PrimaryAddress(s, local, port);
        string code = JoinCode.Format(s.Code);
        InviteText = NetTransport.Invite(address, s.Code);
        AddressKind = kind;

        Value(invite, "Address", address, 24, UiTheme.Text, ("Copy", () => Copy(address, "Address copied.")));
        invite.AddChild(Wrapped(kind, 13, kindColour, 440f));
        Value(invite, "Join code", code, 34, UiTheme.Gold, ("Copy", () => Copy(code, "Join code copied.")), ("New code", () => _session?.NewCode()));

        Button copyInvite = UiTheme.PrimaryButton("Copy invite (address + code)", () => Copy(InviteText, "Invite copied — paste it to your friends; their address field fills both."), 18, 440f);
        invite.AddChild(copyInvite);

        PortMapper ports = s.Ports;
        (string routerText, Color routerColour) = ports.State switch
        {
            PortMapper.MapState.Working => (ports.Message, UiTheme.TextDim),
            PortMapper.MapState.Open => ($"Internet: ready. {ports.Message}", UiTheme.Experience),
            PortMapper.MapState.Failed => ($"Internet: not reachable yet. {ports.Message}", UiTheme.Ember),
            _ => ("Router port not opened (UPnP off for this lobby): same PC, same network or VPN only.", UiTheme.TextDim),
        };
        invite.AddChild(Wrapped(routerText, 13, routerColour, 440f));

        // Without UPnP nobody here knows the public address: offer to look it up
        // (on request only), for hosts who forward the port by hand.
        bool needsPublic = ports.State is not (PortMapper.MapState.Open or PortMapper.MapState.Working) && !local.Any(IsVpn);
        PublicIpOffered = needsPublic && _publicIp.Length == 0;
        if (PublicIpOffered)
        {
            HBoxContainer row = UiTheme.HBox(invite, 10);
            row.AddChild(UiTheme.Button(_publicIpState == "looking" ? "Looking up…" : "Find my public IP", LookUpPublicIp, 15, 200f));
            row.AddChild(Wrapped(_publicIpState is "" or "looking"
                ? "For friends on the internet once the port is forwarded (asks api.ipify.org)."
                : _publicIpState, 12, _publicIpState is "" or "looking" ? UiTheme.TextDim : UiTheme.Ember, 230f));
        }

        IEnumerable<string> others = local.Select(a => $"{a}:{port}").Where(a => a != address).Take(3);
        foreach (string other in others)
        {
            string label = $"{other}  ({Kind(other)})";
            Value(invite, "", label, 14, UiTheme.Text, ("Copy invite", () => Copy(NetTransport.Invite(other, s.Code), $"Invite with {other} copied.")));
        }
    }

    /// <summary>
    /// The address for the invite, best first: the router's public one when UPnP
    /// opened the port; a VPN address; the public IP the host looked up (works
    /// once the port is forwarded); else this PC on the local network — which is
    /// labelled as such, since it is useless to friends elsewhere (owner report:
    /// the LAN address read like the address to send).
    /// </summary>
    private (string Address, string Kind, Color Colour) PrimaryAddress(NetSession s, string[] local, int port)
    {
        if (s.Ports.State == PortMapper.MapState.Open && s.Ports.ExternalAddress.Length > 0)
        {
            return ($"{s.Ports.ExternalAddress}:{port}", "Internet address — works for friends anywhere.", UiTheme.Experience);
        }

        string? vpn = local.FirstOrDefault(IsVpn);
        if (vpn is not null)
        {
            return ($"{vpn}:{port}", "VPN address — friends need the same VPN running (Tailscale, Radmin VPN, ZeroTier).", UiTheme.TextDim);
        }

        string lan = local.Length > 0 ? local[0] : "this PC";
        if (_publicIp.Length > 0)
        {
            return ($"{_publicIp}:{port}", $"Your public IP. Friends on the internet reach it only once UDP {port} is forwarded in your router to this PC ({lan}).", UiTheme.Ember);
        }

        if (local.Length > 0)
        {
            return ($"{lan}:{port}", "Local network only — friends on the same Wi-Fi. This is not your internet address: for friends elsewhere, see below.", UiTheme.Ember);
        }

        return ($"127.0.0.1:{port}", "This PC only.", UiTheme.TextDim);
    }

    /// <summary>Asks <see cref="PublicIpService"/> for this connection's public IP (host's button).</summary>
    private void LookUpPublicIp()
    {
        if (_publicIpState == "looking")
        {
            return;
        }

        _ipLookup?.QueueFree();
        _ipLookup = new HttpRequest { Timeout = 8.0 };
        AddChild(_ipLookup);
        _ipLookup.RequestCompleted += (result, code, _, body) =>
        {
            string text = System.Text.Encoding.ASCII.GetString(body).Trim();
            bool ok = result == (long)HttpRequest.Result.Success && code == 200
                && System.Net.IPAddress.TryParse(text, out System.Net.IPAddress? ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
            _publicIp = ok ? text : "";
            _publicIpState = ok ? "" : "Couldn't look it up — search \"what is my IP\" in a browser.";
            Refresh();
        };
        _publicIpState = _ipLookup.Request(PublicIpService) == Error.Ok ? "looking" : "Couldn't look it up — search \"what is my IP\" in a browser.";
        Refresh();
    }

    private static bool IsVpn(string address) => address.StartsWith("100.") || address.StartsWith("26.") || address.StartsWith("25.");

    private static string Kind(string address) =>
        IsVpn(address) ? "VPN" : address.StartsWith("192.168.") ? "same Wi-Fi / LAN" : "local network or VPN";

    private Control Slot(LobbyMember? member, NetSession s)
    {
        var box = new PanelContainer { CustomMinimumSize = new Vector2(0f, 64f) };
        bool you = member is not null && member.PeerId == s.LocalPeerId;
        box.AddThemeStyleboxOverride("panel", member is null
            ? UiTheme.Box(new Color(0.07f, 0.06f, 0.1f, 0.6f), new Color(0.25f, 0.22f, 0.32f), 1, 6, 10)
            : UiTheme.Box(new Color(0.13f, 0.11f, 0.18f), you ? UiTheme.Gold : UiTheme.PanelBorder, 2, 6, 10));
        HBoxContainer row = UiTheme.HBox(box, 12);
        if (member is null)
        {
            var empty = new Control { CustomMinimumSize = new Vector2(48f, 48f) };
            row.AddChild(empty);
            VBoxContainer text = UiTheme.VBox(row, 0);
            text.AddChild(UiTheme.Label("Free — waiting for a friend", 17, UiTheme.TextDim));
            if (s.IsHost)
            {
                text.AddChild(UiTheme.Label("Send them the invite", 13, new Color(0.5f, 0.48f, 0.55f)));
            }

            _slotTexts.Add("Free — waiting for a friend");
            return box;
        }

        row.AddChild(UiTheme.Portrait(SpriteFor(member.ClassName), 48f));
        VBoxContainer info = UiTheme.VBox(row, 0);
        info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        info.AddChild(UiTheme.Label(member.Name, 19, you ? UiTheme.Gold : UiTheme.Text));
        info.AddChild(UiTheme.Label($"Level {member.Level} {member.ClassName}", 14, UiTheme.TextDim));
        var tags = new List<string>();
        if (member.PeerId == 1)
        {
            tags.Add("Host");
        }

        if (you)
        {
            tags.Add("You");
        }

        foreach (string tag in tags)
        {
            PanelContainer cap = UiTheme.KeyCap(tag, 13);
            cap.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(cap);
        }

        if (Purpose == PanelPurpose.Invite && s.IsHost && member.PeerId != 1)
        {
            // D10: per friend, this session only, revocable at any time.
            long peer = member.PeerId;
            var allow = new CheckButton { Text = "Allow editing", ButtonPressed = s.CanEditOf(peer), FocusMode = FocusModeEnum.None, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            allow.AddThemeFontSizeOverride("font_size", 14);
            allow.Toggled += on => _session?.SetCanEdit(peer, on);
            UiTheme.InstantTooltip(allow, $"Let {member.Name} rearrange your town (H): one editor at a time, with your storage. For this party only.");
            row.AddChild(allow);
            if (s.CanEditOf(peer))
            {
                tags.Add("Can edit");
            }
        }

        _slotTexts.Add($"{member.Name} · Level {member.Level} {member.ClassName}{string.Concat(tags.Select(t => $" · {t}"))}");
        return box;
    }

    private static string SpriteFor(string className)
    {
        foreach (AscendancyDefinition a in ClassCatalog.Ascendancies)
        {
            if (a.Name == className)
            {
                return a.Sprite;
            }
        }

        foreach (ClassDefinition c in ClassCatalog.Classes)
        {
            if (c.Name == className)
            {
                return c.Sprite;
            }
        }

        return "player";
    }

    // --- helpers --------------------------------------------------------------------

    private void Copy(string text, string notice)
    {
        DisplayServer.ClipboardSet(text);
        Notice(notice, UiTheme.Experience);
    }

    private void Notice(string text, Color colour)
    {
        _notice.Text = text;
        _notice.AddThemeColorOverride("font_color", colour);
        _notice.Visible = text.Length > 0;
        GetTree().CreateTimer(4.0, processAlways: true).Timeout += () =>
        {
            if (IsInstanceValid(_notice) && _notice.Text == text)
            {
                _notice.Visible = false;
            }
        };
    }

    private static void SetError(LineEdit? field, Label? label, string? error)
    {
        if (field is not null)
        {
            UiTheme.FieldError(field, error is not null);
        }

        if (label is not null)
        {
            label.Text = error ?? "";
            label.Visible = error is not null;
        }
    }

    private static Label ErrorLabel(Node parent)
    {
        Label l = UiTheme.Label("", 14, UiTheme.Ember);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(400f, 0f);
        l.Visible = false;
        parent.AddChild(l);
        return l;
    }

    private static VBoxContainer Card(Node parent, string title, float width = 460f)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(width, 0f) };
        card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.08f, 0.07f, 0.12f), UiTheme.PanelBorder, 1, 8, 16));
        parent.AddChild(card);
        VBoxContainer v = UiTheme.VBox(card, 8);
        v.AddChild(UiTheme.Label(title, 22, UiTheme.Gold));
        return v;
    }

    private static void Value(Node parent, string caption, string value, int size, Color colour, params (string Label, Action Action)[] buttons)
    {
        HBoxContainer row = UiTheme.HBox(parent, 10);
        if (caption.Length > 0)
        {
            Label c = UiTheme.Label(caption, 14, UiTheme.TextDim);
            c.CustomMinimumSize = new Vector2(78f, 0f);
            row.AddChild(c);
        }

        Label v = UiTheme.Label(value, size, colour);
        v.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        v.ClipText = true;
        row.AddChild(v);
        foreach ((string label, Action action) in buttons)
        {
            Button b = UiTheme.Button(label, action, 14, label.Length > 6 ? 110f : 70f);
            b.CustomMinimumSize = new Vector2(b.CustomMinimumSize.X, 34f);
            b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(b);
        }
    }

    private static Label Wrapped(string text, int size, Color colour, float width, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        Label l = UiTheme.Label(text, size, colour, align);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(width, 0f);
        return l;
    }
}
