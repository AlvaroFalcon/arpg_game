#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using ArpgGame.Characters;

namespace ArpgGame.Net;

/// <summary>One party member as the lobby lists them.</summary>
public sealed class LobbyMember
{
    public long PeerId { get; set; }
    public string Name { get; set; } = "";
    public string ClassName { get; set; } = "";
    public int Level { get; set; }

    /// <summary>The whole character in the save-file format (their machine owns it;
    /// the others only use it to build their copy).</summary>
    public string Character { get; set; } = "";

    public float ViewWidth { get; set; } = 1280f;
    public float ViewHeight { get; set; } = 720f;

    public Vector2 ViewSize => new(ViewWidth, ViewHeight);
}

/// <summary>Where a joining client is (the co-op screen shows it with the elapsed time).</summary>
public enum JoinPhase
{
    None,

    /// <summary>Reaching the host's address (the transport handshake).</summary>
    Connecting,

    /// <summary>Reached: the host is checking the join code.</summary>
    CheckingCode,

    /// <summary>Code accepted: the character is on its way to the lobby.</summary>
    Joining,

    /// <summary>Listed in the lobby (or hosting it).</summary>
    InLobby,
}

/// <summary>Why the last session ended, so the co-op screen can say what to
/// check (docs/design/menus.md -> "Co-op").</summary>
public enum SessionEnd
{
    None,

    /// <summary>Nobody answered at that address within <see cref="NetSession.ConnectTimeout"/>.</summary>
    Unreachable,
    WrongCode,
    VersionMismatch,
    PartyFull,
    RunStarted,
    CharacterRefused,

    /// <summary>Dropped before the code was checked (an address blocked after
    /// wrong codes, or the host's game closing at that moment).</summary>
    NotAccepted,
    HostClosed,
    HostLeftRun,
}

/// <summary>The host's refusal reasons. The client reads them back to tell a
/// wrong code from a full party (both ends run the same build: the protocol
/// number is checked first).</summary>
public static class JoinRefusal
{
    public const string WrongCode = "Wrong join code. Ask the host for the code shown in their lobby.";
    public const string Version = "Your game version does not match the host's.";
    public const string Full = "The party is full.";
    public const string RunStarted = "The run has already started.";
    public const string NotClient = "That is not an ARPG co-op client.";
    public const string CharacterPrefix = "Your character";

    public static SessionEnd Classify(string reason) =>
        reason.StartsWith("Wrong join code") ? SessionEnd.WrongCode
        : reason == Version ? SessionEnd.VersionMismatch
        : reason == Full ? SessionEnd.PartyFull
        : reason == RunStarted ? SessionEnd.RunStarted
        : reason.StartsWith(CharacterPrefix) ? SessionEnd.CharacterRefused
        : SessionEnd.NotAccepted;
}

/// <summary>What a run scene implements to receive the session's run traffic.</summary>
public interface IRunLink
{
    void OnSnapshot(byte[] data);
    void OnHostEvents(byte[] data);
    void OnClientInput(long peer, byte[] data);
    void OnClientEvents(long peer, byte[] data);
    void OnPeerLeft(long peer);
    void OnHostLost(string reason);
}

/// <summary>
/// The co-op session — docs/design/multiplayer.md -> "Session and lobby". An
/// autoload (always at /root/NetSession on every machine, so its RPCs resolve
/// on every peer whatever scene is loaded). It owns the peer (made by
/// <see cref="NetTransport"/>), the lobby (up to <see cref="PartyRules.MaxPlayers"/>
/// members: name, class, level, character), starting the run, and every RPC:
/// lobby traffic plus four byte-array channels the run scene's
/// <see cref="RiftHost"/>/<see cref="RiftClient"/> speak through (<see cref="IRunLink"/>).
///
/// Offline (no session) nothing here runs and nothing in the game changes.
/// </summary>
public partial class NetSession : Node
{
    /// <summary>Bumped whenever the wire format changes: a mismatched build is turned away.
    /// 2: the join code travels in the engine's authentication step.
    /// 3: co-op level-up rounds (PartyRound, PickApplied, ClientOffers; ClientPick carries the round).
    /// 4: the cash-out round (PartyRound kind 3; ClientPick carries "leave" or "stay").
    /// 5: party session (hideout, proposals, presence, edit lock, co-op realities):
    /// RpcStartRun replaced by the StartRift party message, to the participants only.</summary>
    public const int Protocol = 5;

    /// <summary>First bytes of the authentication message ("ARPG").</summary>
    private static readonly byte[] AuthMagic = { 0x41, 0x52, 0x50, 0x47 };

    /// <summary>Makes the router gateway for <see cref="Ports"/>. Tests swap in a
    /// fake (UPnP can't be exercised in the cloud); the game uses the real one.</summary>
    public static Func<IPortGateway> GatewayFactory { get; set; } = () => new UpnpGateway();

    public enum SessionRole
    {
        Offline,
        Host,
        Client,
    }

    public SessionRole Role { get; private set; }

    public bool Active => Role != SessionRole.Offline;
    public bool IsHost => Role == SessionRole.Host;
    public bool IsClient => Role == SessionRole.Client;

    /// <summary>A client is connected and listed in the host's lobby.</summary>
    public bool Joined { get; private set; }

    /// <summary>This machine takes part in the party's running rift (the host: a rift runs).</summary>
    public bool InRun { get; private set; }

    public long LocalPeerId => Active ? _api.GetUniqueId() : 0;

    /// <summary>The lobby in join order (the host first).</summary>
    public List<LobbyMember> Members { get; } = new();

    public string Status { get; private set; } = "";

    /// <summary>Client: how far the join has got (host: <see cref="JoinPhase.InLobby"/>).</summary>
    public JoinPhase Phase { get; private set; }

    /// <summary>Seconds (engine clock) when <see cref="Phase"/> last changed.</summary>
    public double PhaseSince { get; private set; }

    /// <summary>Seconds (engine clock) when the current join started.</summary>
    public double JoinStarted { get; private set; }

    /// <summary>Client: "address:port" being joined.</summary>
    public string JoinTarget { get; private set; } = "";

    /// <summary>Why the last session ended (None while one is active, or after
    /// the player left on purpose).</summary>
    public SessionEnd LastEnd { get; private set; }

    /// <summary>Seconds a join may spend reaching the host before it gives up.
    /// ENet alone waits ~30 s with no answer; the transport is told to give up
    /// sooner too (<see cref="NetTransport.ShortenConnectTimeout"/>), and this is
    /// the session's own deadline whatever the transport does.</summary>
    public const double ConnectTimeout = 12.0;

    public static double Clock => Time.GetTicksMsec() / 1000.0;

    /// <summary>The lobby changed (someone joined, left, or the list arrived).</summary>
    public event Action? LobbyChanged;

    /// <summary>The session ended: the reason, for the player.</summary>
    public event Action<string>? Ended;

    /// <summary>The run scene currently attached (null between scenes).</summary>
    public IRunLink? Run { get; set; }

    /// <summary>Host: the party's join code (normalised, e.g. "K7Q29M"; show it with
    /// <see cref="JoinCode.Format"/>). Empty when not hosting.</summary>
    public string Code { get; private set; } = "";

    /// <summary>Host: the router port mapping (UPnP) for this lobby.</summary>
    public PortMapper Ports { get; private set; } = null!;

    /// <summary>Host: the port the lobby listens on.</summary>
    public int Port { get; private set; }

    /// <summary>Host: who was turned away or kicked and why, newest last (the lobby
    /// shows the last few; tests assert on it).</summary>
    public List<string> SecurityLog { get; } = new();

    /// <summary>Tests only: send this instead of the real hello JSON.</summary>
    public string? HelloJsonOverride { get; set; }

    /// <summary>Tests only: don't send the hello at all (a peer that has the code
    /// but never introduces itself).</summary>
    public bool TestSkipHello { get; set; }

    /// <summary>Host: peers the session still keeps state for (tests: churn must
    /// leave nothing behind).</summary>
    public int TrackedPeers => _pending.Count + _budgets.Count + _authAnswered.Count + _kicked.Count;

    private LobbyMember? _me;
    private string _joinCode = "";
    private SceneMultiplayer? _scene;
    private MultiplayerApi _api = null!;
    private readonly AttemptLimiter _attempts = new();
    private readonly Dictionary<long, PeerBudget> _budgets = new();
    private readonly HashSet<long> _authAnswered = new();
    private readonly HashSet<long> _kicked = new();
    private bool _releasedOnExit;

    public static NetSession? Get(Node context) =>
        context.IsInsideTree() ? context.GetTree().Root.GetNodeOrNull<NetSession>("NetSession") : null;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Ports = new PortMapper(() => GatewayFactory());
        Ports.Changed += OnPortsChanged;
        // Kept: the API this node was wired to (a test can move a branch to another one).
        _api = Multiplayer;
        _api.PeerConnected += OnPeerConnected;
        _api.PeerDisconnected += OnPeerDisconnected;
        _api.ConnectedToServer += OnConnectedToServer;
        _api.ConnectionFailed += OnConnectionFailed;
        _api.ServerDisconnected += OnServerDisconnected;
        _scene = _api as SceneMultiplayer;
        if (_scene is not null)
        {
            _scene.PeerAuthenticating += OnPeerAuthenticating;
            _scene.PeerAuthenticationFailed += OnPeerAuthenticationFailed;
        }
    }

    public override void _ExitTree()
    {
        // The game is closing (or a test tears the tree down): the router mapping goes first.
        ReleaseOnExit();
        Ports.Changed -= OnPortsChanged;
        _api.PeerConnected -= OnPeerConnected;
        _api.PeerDisconnected -= OnPeerDisconnected;
        _api.ConnectedToServer -= OnConnectedToServer;
        _api.ConnectionFailed -= OnConnectionFailed;
        _api.ServerDisconnected -= OnServerDisconnected;
        if (_scene is not null)
        {
            _scene.PeerAuthenticating -= OnPeerAuthenticating;
            _scene.PeerAuthenticationFailed -= OnPeerAuthenticationFailed;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            // Window closed while hosting: remove the router mapping before the
            // process ends (a crash instead leaves it to the lease).
            ReleaseOnExit();
        }
    }

    /// <summary>Quitting: leave the party and remove the router mapping, waiting
    /// for the router (longer if discovery is still running, so the removal
    /// queued behind it can go through). Idempotent.</summary>
    private void ReleaseOnExit()
    {
        if (_releasedOnExit)
        {
            return; // window close, then _ExitTree: wait for the router only once
        }

        _releasedOnExit = true;
        Leave();
        // A discovery still running is cancelled (it maps nothing once it returns),
        // so this only waits for a router call already in flight and the removal.
        Ports.CloseAndWait(1500);
    }

    public override void _Process(double delta)
    {
        Ports.Poll(Time.GetTicksMsec());
        if (IsHost)
        {
            RelayPresence(delta);
        }

        if (IsClient && Phase == JoinPhase.Connecting && Clock - PhaseSince > ConnectTimeout)
        {
            End(UnreachableMessage(), SessionEnd.Unreachable);
        }
    }

    private void SetPhase(JoinPhase phase)
    {
        if (Phase != phase)
        {
            Phase = phase;
            PhaseSince = Clock;
        }
    }

    private string UnreachableMessage() =>
        $"No answer from {JoinTarget} after {Clock - JoinStarted:0} s. The host's game may not be reachable from where you are.";

    private void OnPortsChanged() => LobbyChanged?.Invoke();

    /// <summary>Host: removes the router mapping now (the run ended — nobody can
    /// join any more). The session itself carries on.</summary>
    public void ReleasePortMapping() => Ports.Close();

    /// <summary>Host: a fresh join code for players who haven't joined yet.</summary>
    public void NewCode()
    {
        if (IsHost && !InRun)
        {
            Code = JoinCode.Generate();
            LobbyChanged?.Invoke();
        }
    }

    /// <summary>Both ends: the join code is checked in the engine's authentication
    /// step, before the peer is admitted — until then the engine drops every other
    /// packet from it, so no RPC of an unauthenticated peer ever runs.</summary>
    private bool ConfigureAuth()
    {
        if (_scene is null || !ReferenceEquals(_scene, Multiplayer))
        {
            // No SceneMultiplayer to run the join-code step on (or not the one this
            // node's RPCs go through): never open a lobby that would admit anyone.
            return false;
        }

        _scene.AuthCallback = new Callable(this, MethodName.OnAuth);
        _scene.AuthTimeout = NetLimits.AuthTimeout;
        // Clients only ever talk to the host: no relaying between clients (it
        // would also tell every client the others' peer ids).
        _scene.ServerRelay = false;
        return true;
    }

    private void Security(string line)
    {
        SecurityLog.Add(line);
        if (SecurityLog.Count > 50)
        {
            SecurityLog.RemoveAt(0);
        }

        GD.Print($"Co-op: {line}");
        LobbyChanged?.Invoke();
    }

    public static LobbyMember MemberFor(CharacterData c, Vector2 view) => new()
    {
        Name = c.Name,
        ClassName = c.AscendancyDef?.Name ?? c.Class.Name,
        Level = c.Level,
        Character = Core.Profile.SerializeCharacter(c),
        ViewWidth = view.X,
        ViewHeight = view.Y,
    };

    // --- starting and ending --------------------------------------------------------

    /// <summary>Opens a lobby on <paramref name="port"/>. <paramref name="code"/>: the
    /// join code (null draws a random one; tests pass a fixed one).
    /// <paramref name="openRouterPort"/>: try UPnP (off for tests and scripts on one PC).</summary>
    public Error Host(int port, CharacterData character, string? code = null, bool openRouterPort = true)
    {
        Leave();
        string chosen = code is null ? JoinCode.Generate() : JoinCode.Normalize(code);
        if (!JoinCode.IsWellFormed(chosen))
        {
            Status = $"A join code is {JoinCode.Length} letters or digits (no 0, O, 1, I or L).";
            return Error.InvalidParameter;
        }

        if (!ConfigureAuth())
        {
            Status = "Co-op is unavailable (the join-code check could not be set up).";
            return Error.Unconfigured;
        }

        (MultiplayerPeer? peer, Error error) = NetTransport.Host(port, NetTransport.MaxConnections);
        if (peer is null)
        {
            Status = $"Could not host on port {port} ({error}). Is another game using it?";
            return error;
        }

        _scene!.MultiplayerPeer = peer;
        _releasedOnExit = false;
        Role = SessionRole.Host;
        Joined = true;
        InRun = false;
        Code = chosen;
        Port = port;
        _attempts.Clear();
        _me = MemberFor(character, ViewSize());
        _me.PeerId = 1;
        Members.Clear();
        Members.Add(_me);
        ResetParty();
        _places[1] = MemberPlace.Menu;
        LastEnd = SessionEnd.None;
        JoinStarted = Clock;
        SetPhase(JoinPhase.InLobby);
        Status = $"Hosting on port {port}.";
        if (openRouterPort)
        {
            Ports.Open(port);
        }

        LobbyChanged?.Invoke();
        return Error.Ok;
    }

    public Error Join(string address, int port, CharacterData character, string code)
    {
        Leave();
        LastEnd = SessionEnd.None;
        string normalised = JoinCode.Normalize(code);
        if (!JoinCode.IsWellFormed(normalised))
        {
            Status = $"Type the host's join code: {JoinCode.Length} letters or digits, like K7Q-29M.";
            return Error.InvalidParameter;
        }

        _joinCode = normalised;
        if (!ConfigureAuth())
        {
            Status = "Co-op is unavailable (the join-code check could not be set up).";
            return Error.Unconfigured;
        }

        (MultiplayerPeer? peer, Error error) = NetTransport.Join(address, port);
        if (peer is null)
        {
            Status = $"Could not connect to {address}:{port} ({error}).";
            return error;
        }

        _scene!.MultiplayerPeer = peer;
        NetTransport.ShortenConnectTimeout(peer, (int)(ConnectTimeout * 1000));
        Role = SessionRole.Client;
        LastEnd = SessionEnd.None;
        JoinTarget = $"{address}:{port}";
        JoinStarted = Clock;
        SetPhase(JoinPhase.Connecting);
        Joined = false;
        InRun = false;
        _me = MemberFor(character, ViewSize());
        Members.Clear();
        ResetParty();
        Status = $"Connecting to {address}:{port}…";
        LobbyChanged?.Invoke();
        return Error.Ok;
    }

    private Vector2 ViewSize() => GetViewport()?.GetVisibleRect().Size ?? new Vector2(1280f, 720f);

    /// <summary>Leaves quietly (the player chose to). Safe to call offline.</summary>
    public void Leave()
    {
        if (!Active)
        {
            return;
        }

        MultiplayerPeer? peer = _api.MultiplayerPeer;
        Ports.Close();
        Role = SessionRole.Offline;
        Phase = JoinPhase.None;
        LastEnd = SessionEnd.None;
        Joined = false;
        InRun = false;
        Members.Clear();
        Code = "";
        _joinCode = "";
        _budgets.Clear();
        _authAnswered.Clear();
        _kicked.Clear();
        _pending.Clear();
        ResetParty();
        _me = null;
        if (peer is not null and not OfflineMultiplayerPeer)
        {
            peer.Close();
        }

        _api.MultiplayerPeer = new OfflineMultiplayerPeer();
        NetMode.ClientSide = false;
    }

    private void End(string reason, SessionEnd why)
    {
        bool wasActive = Active;
        bool wasClient = IsClient;
        bool wasInWorld = LocalPlace != MemberPlace.Menu;
        IRunLink? run = Run;
        Leave();
        if (wasActive)
        {
            // A guest out in the world goes back to the main menu, which shows why.
            MenuNotice = wasClient && wasInWorld ? reason : null;
            LastEnd = why;
            Status = reason;
            run?.OnHostLost(reason);
            Ended?.Invoke(reason);
            LobbyChanged?.Invoke();
        }
    }

    // --- engine callbacks ----------------------------------------------------------

    /// <summary>Seconds a newcomer has to send its hello before the host drops it
    /// (a peer that never says hello would hold a party slot).</summary>
    public const double HelloTimeout = 10.0;

    private void OnPeerConnected(long id)
    {
        // The host waits for the newcomer's hello (RpcHello) before listing them.
        if (!IsHost)
        {
            return;
        }

        _pending.Remove(id);
        GetTree().CreateTimer(HelloTimeout).Timeout += () =>
        {
            if (IsInstanceValid(this) && IsHost && Member(id) is null && !_kicked.Contains(id))
            {
                Kick(id);
            }
        };
    }

    private void OnPeerDisconnected(long id)
    {
        if (!IsHost)
        {
            return;
        }

        _pending.Remove(id);
        _budgets.Remove(id);
        _authAnswered.Remove(id);
        _kicked.Remove(id);

        int removed = Members.RemoveAll(m => m.PeerId == id);
        _places.Remove(id);
        CurrentRun?.Participants.Remove(id);
        Run?.OnPeerLeft(id);
        if (removed > 0)
        {
            BroadcastLobby();
            OnMemberLeft(id);
            BroadcastRoster();
        }
    }

    private void OnConnectedToServer()
    {
        if (_me is null)
        {
            return;
        }

        Status = "Connected — joining the party…";
        SetPhase(JoinPhase.Joining);
        if (TestSkipHello)
        {
            return;
        }

        RpcId(1, MethodName.RpcHello, Protocol, HelloJsonOverride ?? JsonSerializer.Serialize(_me));
        LobbyChanged?.Invoke();
    }

    private void OnConnectionFailed() => End(UnreachableMessage(), SessionEnd.Unreachable);

    private void OnServerDisconnected()
    {
        string host = HostName == "someone" ? "The host" : HostName;
        End(InRun ? $"{host} left — the run is over." : LocalPlace != MemberPlace.Menu ? $"{host} closed the party." : "The host closed the session.",
            InRun ? SessionEnd.HostLeftRun : SessionEnd.HostClosed);
    }

    // --- authentication: the join code (engine auth step, before any RPC) ------------

    private void OnPeerAuthenticating(long id)
    {
        if (IsClient && id == 1)
        {
            // First message to the host: magic, protocol, code. Nothing else is sent
            // (or would be accepted) until the host answers.
            byte[] code = System.Text.Encoding.ASCII.GetBytes(_joinCode);
            byte[] message = new byte[AuthMagic.Length + 2 + code.Length];
            AuthMagic.CopyTo(message, 0);
            message[4] = (byte)Protocol;
            message[5] = (byte)(Protocol >> 8);
            code.CopyTo(message, 6);
            _scene?.SendAuth(1, message);
            // Reached the host: from here on a slow link may stall as long as ENet allows.
            NetTransport.RestoreTimeout(_api.MultiplayerPeer);
            SetPhase(JoinPhase.CheckingCode);
            Status = "Connected — checking the join code…";
            LobbyChanged?.Invoke();
            return;
        }

        if (!IsHost)
        {
            return;
        }

        string address = NetTransport.RemoteAddress(_api.MultiplayerPeer, id);
        if (_attempts.IsBlocked(address, Now))
        {
            // Too many wrong codes from there: not even an answer.
            Ignore(id);
            Security($"ignored {address}: too many wrong codes, blocked for a minute");
            return;
        }

        if (_attempts.GloballyBlocked(Now))
        {
            Ignore(id);
            Security($"ignored {address}: too many wrong codes from everyone lately, nobody new for a minute");
            return;
        }

        if (_pending.Values.Count(a => a == address) >= NetLimits.PendingPerAddress)
        {
            Ignore(id);
            Security($"ignored {address}: already {NetLimits.PendingPerAddress} connections waiting for their code");
            return;
        }

        _pending[id] = address;
        // Joining is open whenever the party has room (docs/design/multiplayer.md -> "Party session"):
        // a newcomer lands in the hideout, even while a rift runs (they wait for it there).
        if (Members.Count >= PartyRules.MaxPlayers)
        {
            Refuse(id, JoinRefusal.Full);
        }
    }

    /// <summary>Host: drops a connecting peer without an answer (and without
    /// counting it as a failure: it never got to try a code).</summary>
    private void Ignore(long id)
    {
        _authAnswered.Add(id);
        _kicked.Add(id);
        _api.MultiplayerPeer?.DisconnectPeer((int)id);
        DropLater(id);
    }

    /// <summary>Host: peers still in the authentication step, by address.</summary>
    private readonly Dictionary<long, string> _pending = new();

    private void OnPeerAuthenticationFailed(long id)
    {
        if (IsClient && id == 1 && Active)
        {
            End("The host closed the connection before checking your code (too many wrong codes from your address lately, or the host's game closed).", SessionEnd.NotAccepted);
        }
        else if (IsHost)
        {
            // Never admitted, so no PeerDisconnected will come for it. One that never
            // sent a code (timed out, or connected only to hold a slot) counts as a
            // failed attempt, so slot-holding from one address ends in a block.
            if (!_authAnswered.Contains(id) && _pending.TryGetValue(id, out string? address))
            {
                bool blocked = _attempts.Fail(address, Now);
                Security($"dropped {address}: no join code within {NetLimits.AuthTimeout:0} s{(blocked ? " (blocked for a minute)" : "")}");
            }

            _pending.Remove(id);
            _budgets.Remove(id);
            _authAnswered.Remove(id);
            _kicked.Remove(id);
        }
    }

    /// <summary>The engine's auth callback: the host reads the joiner's code, the
    /// joiner reads the host's answer.</summary>
    private void OnAuth(long id, byte[] data)
    {
        if (_scene is null)
        {
            return;
        }

        if (IsClient)
        {
            if (id != 1)
            {
                return;
            }

            if (data.Length >= 1 && data[0] == 1)
            {
                // Engine quirk (4.1.1): complete_auth sends its confirmation to the last
                // target peer; on a client that's always the host.
                _scene.CompleteAuth(1);
                SetPhase(JoinPhase.Joining);
                Status = "Join code accepted — joining the party…";
                LobbyChanged?.Invoke();
            }
            else
            {
                string refusal = data.Length > 1 ? System.Text.Encoding.UTF8.GetString(data, 1, Math.Min(data.Length - 1, 200)) : "The host turned you away.";
                End(refusal, JoinRefusal.Classify(refusal));
            }

            return;
        }

        if (!IsHost || _kicked.Contains(id))
        {
            return;
        }

        if (!_authAnswered.Add(id))
        {
            // One code per connection; anything more only spends its budget.
            Spend(id);
            return;
        }

        string address = NetTransport.RemoteAddress(_api.MultiplayerPeer, id);
        if (_attempts.IsBlocked(address, Now))
        {
            // Connected before its address was blocked (several connections at once).
            Ignore(id);
            return;
        }

        string? reason = null;
        bool counts = true;
        if (data.Length > NetLimits.AuthBytes || data.Length < AuthMagic.Length + 2 || !data.AsSpan(0, AuthMagic.Length).SequenceEqual(AuthMagic))
        {
            reason = JoinRefusal.NotClient;
        }
        else if ((data[4] | (data[5] << 8)) != Protocol)
        {
            reason = JoinRefusal.Version;
            counts = false;
        }
        else
        {
            string given = JoinCode.Normalize(System.Text.Encoding.ASCII.GetString(data, 6, data.Length - 6));
            if (!JoinCode.Matches(Code, given))
            {
                reason = JoinRefusal.WrongCode;
            }
        }

        if (reason is not null)
        {
            bool blocked = counts && _attempts.Fail(address, Now);
            Refuse(id, reason);
            Security($"turned away {address}: {reason}{(blocked ? " (blocked for a minute)" : "")}");
            return;
        }

        // SendAuth sets the target peer, channel 0 and reliable mode, which the
        // following CompleteAuth's confirmation then reuses (4.1.1 doesn't set them).
        _scene.SendAuth((int)id, new byte[] { 1 });
        _scene.CompleteAuth((int)id);
    }

    /// <summary>Host: answers a connecting peer with a reason and closes it once sent.</summary>
    private void Refuse(long id, string reason)
    {
        _authAnswered.Add(id);
        _kicked.Add(id);
        byte[] text = System.Text.Encoding.UTF8.GetBytes(reason);
        byte[] message = new byte[text.Length + 1];
        text.CopyTo(message, 1);
        _scene?.SendAuth((int)id, message);
        NetTransport.DisconnectAfterSend(_api.MultiplayerPeer, id);
        DropLater(id);
    }

    /// <summary>Host: a turned-away peer that never acknowledges its answer (so
    /// ENet's graceful disconnect would wait ~30 s) is dropped after a grace period.
    /// Not forced: a forced drop skips the engine's disconnect signals.</summary>
    private void DropLater(long id)
    {
        MultiplayerPeer? peer = _api.MultiplayerPeer;
        GetTree().CreateTimer(NetLimits.RefuseGraceSeconds, processAlways: true).Timeout += () =>
        {
            if (IsInstanceValid(this) && IsHost && ReferenceEquals(peer, _api.MultiplayerPeer)
                && peer is ENetMultiplayerPeer enet && enet.GetPeer((int)id) is { } packetPeer
                && packetPeer.GetState() != ENetPacketPeer.PeerState.Disconnected)
            {
                packetPeer.PeerDisconnectNow();
            }
        };
    }

    private static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>Host: one message from <paramref name="peer"/>; kicks it past its budget.</summary>
    private bool Spend(long peer)
    {
        if (!_budgets.TryGetValue(peer, out PeerBudget? budget))
        {
            budget = new PeerBudget();
            _budgets[peer] = budget;
        }

        if (budget.Spend(Now))
        {
            return true;
        }

        KickFor(peer, $"more than {NetLimits.MessagesPerSecond:0} messages a second");
        return false;
    }

    /// <summary>Host: may this message from a client be processed? Checks the
    /// sender's budget and the payload size (kicking on either), and that the
    /// sender completed a valid hello (otherwise the message is ignored).</summary>
    private bool Admit(long sender, int size, int cap, string what, bool needMember = true)
    {
        if (!IsHost || _kicked.Contains(sender) || !Spend(sender))
        {
            return false;
        }

        if (size > cap)
        {
            KickFor(sender, $"{what} of {size} bytes (limit {cap})");
            return false;
        }

        return !needMember || Member(sender) is not null;
    }

    private void KickFor(long peer, string why)
    {
        if (!_kicked.Add(peer))
        {
            return;
        }

        string name = Member(peer)?.Name ?? $"peer {peer}";
        Security($"kicked {name} ({NetTransport.RemoteAddress(_api.MultiplayerPeer, peer)}): {why}");
        Kick(peer);
    }

    private void BroadcastLobby()
    {
        string json = JsonSerializer.Serialize(Members);
        Rpc(MethodName.RpcLobby, json);
        LobbyChanged?.Invoke();
    }

    /// <summary>The members of the lobby, as the party's other machines see them.</summary>
    public LobbyMember? Member(long peer) => Members.FirstOrDefault(m => m.PeerId == peer);

    // --- RPCs: lobby (channel 0) ------------------------------------------------------

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcHello(int protocol, string memberJson)
    {
        long sender = _api.GetRemoteSenderId();
        if (!Admit(sender, memberJson.Length, NetLimits.HelloChars, "hello", needMember: false) || Member(sender) is not null)
        {
            return; // over a limit (kicked), or a repeated hello (ignored)
        }

        string? reason = protocol != Protocol ? JoinRefusal.Version
            : Members.Count >= PartyRules.MaxPlayers ? JoinRefusal.Full
            : null;
        LobbyMember? member = null;
        if (reason is null)
        {
            try
            {
                member = JsonSerializer.Deserialize<LobbyMember>(memberJson);
            }
            catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
            {
                member = null;
            }

            reason = member is null ? "Your character could not be read." : CheckMember(member);
        }

        if (reason is not null)
        {
            _kicked.Add(sender);
            Security($"turned away peer {sender} ({NetTransport.RemoteAddress(_api.MultiplayerPeer, sender)}): {reason}");
            RpcId(sender, MethodName.RpcRejected, reason);
            NetTransport.DisconnectAfterSend(_api.MultiplayerPeer, sender);
            DropLater(sender);
            return;
        }

        member!.PeerId = sender;
        Members.Add(member);
        _places[sender] = MemberPlace.Menu;
        BroadcastLobby();
        OnMemberJoined(sender);
        BroadcastRoster();
    }

    /// <summary>Host: is a joining player's lobby entry and character sane
    /// (docs/design/multiplayer.md -> "Security")? Null if so; fixes up the
    /// entry's name and level from the character itself.</summary>
    private static string? CheckMember(LobbyMember member)
    {
        if (!float.IsFinite(member.ViewWidth) || !float.IsFinite(member.ViewHeight))
        {
            return "Your character was refused (bad window size).";
        }

        if (member.Character is null || member.Character.Length > NetLimits.HelloChars)
        {
            return "Your character was refused (too large).";
        }

        if (Core.Profile.ValidateCharacterJson(member.Character, out string name, out int level) is { } why)
        {
            return $"Your character was refused ({why}).";
        }

        member.Name = name;
        member.Level = level;
        member.ClassName = (member.ClassName ?? "").Length > NetLimits.NameChars ? member.ClassName![..NetLimits.NameChars] : member.ClassName ?? "";
        member.ViewWidth = Mathf.Clamp(member.ViewWidth, 320f, 7680f);
        member.ViewHeight = Mathf.Clamp(member.ViewHeight, 240f, 4320f);
        return null;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcLobby(string json)
    {
        if (!IsClient)
        {
            return;
        }

        List<LobbyMember>? members = SanitizeLobby(json);
        if (members is null)
        {
            return;
        }

        Members.Clear();
        Members.AddRange(members);
        Joined = Members.Any(m => m.PeerId == LocalPeerId);
        Status = Joined ? "In the party — waiting for the host to start." : Status;
        if (Joined)
        {
            SetPhase(JoinPhase.InLobby);
        }

        LobbyChanged?.Invoke();
    }

    /// <summary>Client: the host's lobby list, made safe to build a party from
    /// (docs/design/multiplayer.md -> "Security" -> "A hostile host"). At most
    /// <see cref="PartyRules.MaxPlayers"/> members with distinct ids; names cut,
    /// window sizes finite and clamped, and a character that fails the host's own
    /// sanity checks is dropped (that player gets a fresh build here). Null if
    /// the list can't be read at all.</summary>
    public static List<LobbyMember>? SanitizeLobby(string json)
    {
        if (json.Length > NetLimits.HelloChars * PartyRules.MaxPlayers + 4096)
        {
            return null;
        }

        List<LobbyMember?>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<List<LobbyMember?>>(json);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return null;
        }

        if (raw is null)
        {
            return null;
        }

        var members = new List<LobbyMember>();
        var seen = new HashSet<long>();
        foreach (LobbyMember? m in raw)
        {
            if (m is null || m.PeerId is < 1 or > int.MaxValue || !seen.Add(m.PeerId))
            {
                continue;
            }

            m.Name = Cut(m.Name, NetLimits.NameChars);
            m.ClassName = Cut(m.ClassName, NetLimits.NameChars);
            m.Level = Math.Clamp(m.Level, 0, 999);
            m.ViewWidth = float.IsFinite(m.ViewWidth) ? Mathf.Clamp(m.ViewWidth, 320f, 7680f) : 1280f;
            m.ViewHeight = float.IsFinite(m.ViewHeight) ? Mathf.Clamp(m.ViewHeight, 240f, 4320f) : 720f;
            if (m.Character is null || m.Character.Length > NetLimits.HelloChars || Core.Profile.ValidateCharacterJson(m.Character, out _, out _) is not null)
            {
                m.Character = "";
            }

            members.Add(m);
            if (members.Count == PartyRules.MaxPlayers)
            {
                break;
            }
        }

        return members;
    }

    private static string Cut(string? text, int max) => text is null ? "" : text.Length > max ? text[..max] : text;

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcRejected(string reason)
    {
        string cut = Cut(reason, 200);
        End(cut, JoinRefusal.Classify(cut));
    }

    // --- RPCs: the run ---------------------------------------------------------------
    //
    // Everything is on transfer channel 0 on purpose. Godot 4.1.1's
    // ENetMultiplayerPeer.put_packet only applies the transfer mode on channel 0:
    // for any channel above 0 it sends with no ENet flags, i.e. *unreliable*,
    // whatever the RPC says — and ENet's throttle then drops those packets when
    // the round trip spikes (a "reliable" pick or despawn silently lost). Found by
    // CoopSmokeTest; see docs/design/multiplayer.md -> "Engine notes". Channel 0
    // still separates reliable (ENet system channel 0) from unreliable (channel 1)
    // traffic, so snapshots never wait behind reliable events.

    /// <summary>Bytes handed to the transport this session, by kind (payload only;
    /// <see cref="NetTransport.PopWireBytes"/> has the on-the-wire totals).</summary>
    public long SnapshotBytesSent { get; private set; }
    public long EventBytesSent { get; private set; }
    public long InputBytesSent { get; private set; }

    public void SendSnapshot(long peer, byte[] data)
    {
        if (!Active)
        {
            return; // the session closed under a run still on screen
        }

        SnapshotBytesSent += data.Length;
        RpcId(peer, MethodName.RpcSnapshot, data);
    }

    public void SendHostEvents(long peer, byte[] data)
    {
        if (!Active)
        {
            return; // the session closed under a run still on screen
        }

        EventBytesSent += data.Length;
        RpcId(peer, MethodName.RpcHostEvents, data);
    }

    public void SendInput(byte[] data)
    {
        if (!Active)
        {
            return; // the session closed under a run still on screen
        }

        InputBytesSent += data.Length;
        RpcId(1, MethodName.RpcInput, data);
    }

    public void SendClientEvents(byte[] data)
    {
        if (!Active)
        {
            return; // the session closed under a run still on screen
        }

        EventBytesSent += data.Length;
        RpcId(1, MethodName.RpcClientEvents, data);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 0)]
    private void RpcSnapshot(byte[] data) => Run?.OnSnapshot(data);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcHostEvents(byte[] data) => Run?.OnHostEvents(data);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 0)]
    private void RpcInput(byte[] data)
    {
        long sender = _api.GetRemoteSenderId();
        if (Admit(sender, data.Length, NetLimits.InputBytes, "input"))
        {
            Run?.OnClientInput(sender, data);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcClientEvents(byte[] data)
    {
        long sender = _api.GetRemoteSenderId();
        if (Admit(sender, data.Length, NetLimits.ClientEventBytes, "event batch"))
        {
            Run?.OnClientEvents(sender, data);
        }
    }

    /// <summary>Host: drops a peer (e.g. one that never finished loading).</summary>
    public void Kick(long peer)
    {
        if (IsHost && _api.MultiplayerPeer is { } p)
        {
            p.DisconnectPeer((int)peer);
        }
    }
}
