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
    /// <summary>Bumped whenever the wire format changes: a mismatched build is turned away.</summary>
    public const int Protocol = 1;

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

    /// <summary>The run has started (no one may join any more).</summary>
    public bool InRun { get; private set; }

    public long LocalPeerId => Active ? Multiplayer.GetUniqueId() : 0;

    /// <summary>The lobby in join order (the host first).</summary>
    public List<LobbyMember> Members { get; } = new();

    public string Status { get; private set; } = "";

    /// <summary>The lobby changed (someone joined, left, or the list arrived).</summary>
    public event Action? LobbyChanged;

    /// <summary>The session ended: the reason, for the player.</summary>
    public event Action<string>? Ended;

    /// <summary>The run scene currently attached (null between scenes).</summary>
    public IRunLink? Run { get; set; }

    private LobbyMember? _me;

    public static NetSession? Get(Node context) =>
        context.IsInsideTree() ? context.GetTree().Root.GetNodeOrNull<NetSession>("NetSession") : null;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    public override void _ExitTree()
    {
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
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

    public Error Host(int port, CharacterData character)
    {
        Leave();
        (MultiplayerPeer? peer, Error error) = NetTransport.Host(port, PartyRules.MaxPlayers - 1);
        if (peer is null)
        {
            Status = $"Could not host on port {port} ({error}). Is another game using it?";
            return error;
        }

        Multiplayer.MultiplayerPeer = peer;
        Role = SessionRole.Host;
        Joined = true;
        InRun = false;
        _me = MemberFor(character, ViewSize());
        _me.PeerId = 1;
        Members.Clear();
        Members.Add(_me);
        Status = $"Hosting on port {port}. Friends join with your address and this port.";
        LobbyChanged?.Invoke();
        return Error.Ok;
    }

    public Error Join(string address, int port, CharacterData character)
    {
        Leave();
        (MultiplayerPeer? peer, Error error) = NetTransport.Join(address, port);
        if (peer is null)
        {
            Status = $"Could not connect to {address}:{port} ({error}).";
            return error;
        }

        Multiplayer.MultiplayerPeer = peer;
        Role = SessionRole.Client;
        Joined = false;
        InRun = false;
        _me = MemberFor(character, ViewSize());
        Members.Clear();
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

        MultiplayerPeer? peer = Multiplayer.MultiplayerPeer;
        Role = SessionRole.Offline;
        Joined = false;
        InRun = false;
        Members.Clear();
        _me = null;
        if (peer is not null and not OfflineMultiplayerPeer)
        {
            peer.Close();
        }

        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        NetMode.ClientSide = false;
    }

    private void End(string reason)
    {
        bool wasActive = Active;
        IRunLink? run = Run;
        Leave();
        Status = reason;
        if (wasActive)
        {
            run?.OnHostLost(reason);
            Ended?.Invoke(reason);
            LobbyChanged?.Invoke();
        }
    }

    /// <summary>Host: everyone loads the Survival Rift. Nobody can join after this.</summary>
    public void StartRun()
    {
        if (!IsHost || InRun)
        {
            return;
        }

        InRun = true;
        Rpc(MethodName.RpcStartRun);
        Core.Scenes.Go(this, Core.Scenes.Run);
    }

    // --- engine callbacks ----------------------------------------------------------

    private void OnPeerConnected(long id)
    {
        // The host waits for the newcomer's hello (RpcHello) before listing them.
    }

    private void OnPeerDisconnected(long id)
    {
        if (!IsHost)
        {
            return;
        }

        int removed = Members.RemoveAll(m => m.PeerId == id);
        Run?.OnPeerLeft(id);
        if (removed > 0)
        {
            BroadcastLobby();
        }
    }

    private void OnConnectedToServer()
    {
        if (_me is null)
        {
            return;
        }

        Status = "Connected — joining the party…";
        RpcId(1, MethodName.RpcHello, Protocol, JsonSerializer.Serialize(_me));
        LobbyChanged?.Invoke();
    }

    private void OnConnectionFailed() => End("Could not reach the host. Check the address, the port and the host's firewall or VPN.");

    private void OnServerDisconnected() => End(InRun ? "The host left — the run is over." : "The host closed the session.");

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
        if (!IsHost)
        {
            return;
        }

        long sender = Multiplayer.GetRemoteSenderId();
        string? reason = protocol != Protocol ? "Your game version does not match the host's."
            : InRun ? "The run has already started."
            : Members.Count >= PartyRules.MaxPlayers ? "The party is full."
            : null;
        LobbyMember? member = null;
        if (reason is null)
        {
            try
            {
                member = JsonSerializer.Deserialize<LobbyMember>(memberJson);
            }
            catch (JsonException)
            {
                member = null;
            }

            if (member is null)
            {
                reason = "Your character could not be read.";
            }
        }

        if (reason is not null)
        {
            RpcId(sender, MethodName.RpcRejected, reason);
            return;
        }

        member!.PeerId = sender;
        member.ViewWidth = Mathf.Clamp(member.ViewWidth, 320f, 7680f);
        member.ViewHeight = Mathf.Clamp(member.ViewHeight, 240f, 4320f);
        Members.RemoveAll(m => m.PeerId == sender);
        Members.Add(member);
        BroadcastLobby();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcLobby(string json)
    {
        if (!IsClient)
        {
            return;
        }

        List<LobbyMember>? members;
        try
        {
            members = JsonSerializer.Deserialize<List<LobbyMember>>(json);
        }
        catch (JsonException)
        {
            members = null;
        }

        if (members is null)
        {
            return;
        }

        Members.Clear();
        Members.AddRange(members);
        Joined = Members.Any(m => m.PeerId == LocalPeerId);
        Status = Joined ? "In the party — waiting for the host to start." : Status;
        LobbyChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcRejected(string reason) => End(reason);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcStartRun()
    {
        if (!IsClient || InRun)
        {
            return;
        }

        InRun = true;
        Core.Scenes.Go(this, Core.Scenes.Run);
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
        if (IsHost)
        {
            Run?.OnClientInput(Multiplayer.GetRemoteSenderId(), data);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcClientEvents(byte[] data)
    {
        if (IsHost)
        {
            Run?.OnClientEvents(Multiplayer.GetRemoteSenderId(), data);
        }
    }

    /// <summary>Host: drops a peer (e.g. one that never finished loading).</summary>
    public void Kick(long peer)
    {
        if (IsHost && Multiplayer.MultiplayerPeer is { } p)
        {
            p.DisconnectPeer((int)peer);
        }
    }
}
