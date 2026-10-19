#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.World;

namespace ArpgGame.Net;

/// <summary>
/// The shared hideout — docs/design/multiplayer.md -> "Shared hideout". Every
/// machine runs its own <c>Hideout.tscn</c> with its own player, panels and
/// wallet; what is shared is the host's town layout and where everyone stands
/// (presence). Both live here in the autoload, not in the hideout scene: the
/// host keeps relaying presence between the friends who stayed home while its
/// own scene is a rift.
/// </summary>
public partial class NetSession
{
    /// <summary>A guest out in the world lost the party (the host left, the
    /// connection dropped): the main menu shows it once and clears it.</summary>
    public string? MenuNotice { get; set; }

    /// <summary>Client: the host's town as last received (sanitised); null until it arrives.</summary>
    public HideoutData? HostLayout { get; private set; }

    /// <summary>Client: <see cref="HostLayout"/> changed (the hideout rebuilds its props and floors).</summary>
    public event Action? LayoutChanged;

    /// <summary>Host: the town to send (the hideout scene sets it while loaded;
    /// otherwise the account's saved town).</summary>
    public Func<HideoutData?>? LayoutSource { get; set; }

    /// <summary>Payload bytes of presence sent this session (host: to everyone; client: its own).</summary>
    public long PresenceBytesSent { get; private set; }

    /// <summary>Presence packets sent this session (diagnostics, tests).</summary>
    public int PresenceSent { get; private set; }

    /// <summary>Presence packets received this session (diagnostics, tests).</summary>
    public int PresenceReceived { get; private set; }

    /// <summary>Payload bytes of layouts sent this session.</summary>
    public long LayoutBytesSent { get; private set; }

    /// <summary>How far behind the newest presence the hideout draws other players.</summary>
    public const double PresenceDelay = 1.5 / PartyRules.PresenceHz;

    private sealed class Presence
    {
        public NetTrack Track = null!;
        public byte Flags;
        public ushort Seq;
        public bool Fresh;
    }

    // Presence is sent reliably. Unreliable was tried first: ENet's packet throttle (it drops a
    // share of unreliable packets whenever round trips vary, as they do with a game servicing the
    // socket once per frame) lost 30–70 % of the guests' presence on loopback (HideoutCoopTest,
    // ENet's own loss counter at 0). Presence is ~7–32 bytes at 10 Hz, so reliability costs
    // nothing worth measuring, and the sequence number still drops anything stale.
    private readonly Dictionary<long, Presence> _presence = new();
    private Vector2? _hostPresence;
    private byte _hostFlags;
    private ushort _presenceSeq;
    private double _presenceIn;
    private double _nextPresenceSend;
    private string _lastLayoutJson = "";

    /// <summary>Client: the smallest (local clock − host clock) seen; host times plus
    /// this are local times. Drifts up slowly so a long-ago fast packet doesn't stick.</summary>
    private double _presenceOffset = double.MaxValue;

    private void ResetHideout()
    {
        HostLayout = null;
        _presence.Clear();
        _hostPresence = null;
        _presenceOffset = double.MaxValue;
        _presenceIn = 0.0;
        _nextPresenceSend = 0.0;
        _lastLayoutJson = "";
    }

    // --- phases ------------------------------------------------------------------------------

    /// <summary>Host (the lobby's button, scripts): the party goes to the host's hideout.</summary>
    public void EnterHideout()
    {
        if (!IsHost || InRun)
        {
            return;
        }

        EnterHideoutInPlace();
        ReportPlace(MemberPlace.Loading);
        Core.Scenes.Go(this, Core.Scenes.Hideout);
    }

    /// <summary>Host: the party's phase becomes the hideout without a scene change
    /// ("Invite friends" while already home).</summary>
    public void EnterHideoutInPlace()
    {
        if (!IsHost || InRun)
        {
            return;
        }

        PartyPhase = SessionPhase.Hideout;
        BroadcastRoster();
    }

    /// <summary>Host: the rift on this machine ended (results showing): the party
    /// is "returning" until the host is home again. Nothing else changes: the
    /// session, the router port and everyone in the hideout carry on.</summary>
    public void OnHostRunEnded()
    {
        if (IsHost && PartyPhase == SessionPhase.Run)
        {
            PartyPhase = SessionPhase.Returning;
            BroadcastRoster();
        }
    }

    /// <summary>This machine leaves its rift for the hideout (results → Hideout).
    /// The scene change follows; the hideout reports itself on arrival.</summary>
    public void ReturnToHideout()
    {
        if (!Active)
        {
            return;
        }

        InRun = false;
        CurrentRun = null;
        if (IsHost)
        {
            HostRun = null;
            if (PartyPhase is SessionPhase.Run or SessionPhase.Returning)
            {
                PartyPhase = SessionPhase.Hideout;
            }
        }

        ReportPlace(MemberPlace.Loading);
    }

    /// <summary>Client: leaves the running rift but stays in the party (the host
    /// drops this player from the run; the results screen follows here).</summary>
    public void LeaveRun()
    {
        if (!IsClient || !InRun)
        {
            return;
        }

        InRun = false;
        CurrentRun = null;
        ReportPlace(MemberPlace.Loading);
    }

    /// <summary>Client: a roster arrived. The party left the lobby and this machine
    /// is still in it: go to the hideout once the host's town is here.</summary>
    private void MaybeEnterHideout()
    {
        if (IsClient && Joined && LocalPlace == MemberPlace.Menu && PartyPhase != SessionPhase.Lobby && HostLayout is not null)
        {
            ReportPlace(MemberPlace.Loading);
            Core.Scenes.Go(this, Core.Scenes.Hideout);
        }
    }

    // --- the layout --------------------------------------------------------------------------

    private HideoutData? CurrentLayout() => LayoutSource?.Invoke() ?? Core.Profile.Get(this)?.Hideout;

    /// <summary>Host: sends the town to every member (after an edit), or to one.</summary>
    public void PublishLayout(long onlyTo = 0)
    {
        if (!IsHost || CurrentLayout() is not { } data)
        {
            return;
        }

        string json = HideoutLayoutRules.Serialize(data, withStock: false);
        foreach (LobbyMember m in Members.Where(m => m.PeerId != 1 && (onlyTo == 0 || m.PeerId == onlyTo)))
        {
            LayoutBytesSent += json.Length;
            RpcId(m.PeerId, MethodName.RpcHideoutLayout, json);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcHideoutLayout(string json)
    {
        if (!IsClient || json == _lastLayoutJson || HideoutLayoutRules.SanitizeReceived(json, NetLimits.LayoutChars) is not { } data)
        {
            return; // the same town again (a resend on arrival), or one that can't be built
        }

        _lastLayoutJson = json;
        HostLayout = data;
        LayoutChanged?.Invoke();
        MaybeEnterHideout();
    }

    // --- presence ----------------------------------------------------------------------------

    /// <summary>This machine's player in the hideout, every frame (the host keeps it;
    /// a client sends it at <see cref="PartyRules.PresenceHz"/>).</summary>
    public void SetLocalPresence(Vector2 at, byte flags)
    {
        if (!Active || LocalPlace != MemberPlace.Hideout)
        {
            return;
        }

        if (IsHost)
        {
            _hostPresence = at;
            _hostFlags = flags;
            return;
        }

        if (Clock < _nextPresenceSend || !Joined)
        {
            return;
        }

        _nextPresenceSend = Clock + 1.0 / PartyRules.PresenceHz;
        var w = new NetWriter(8);
        w.U16(++_presenceSeq);
        w.Pos(at);
        w.U8(flags);
        byte[] data = w.ToArray();
        PresenceBytesSent += data.Length;
        PresenceSent++;
        RpcId(1, MethodName.RpcPresenceToHost, data);
    }

    /// <summary>Where <paramref name="peer"/> stands in the hideout now (drawn
    /// <see cref="PresenceDelay"/> in the past), and its flags; false if unknown.</summary>
    public bool TryPresence(long peer, out Vector2 at, out byte flags)
    {
        at = Vector2.Zero;
        flags = 0;
        if (!_presence.TryGetValue(peer, out Presence? p))
        {
            return false;
        }

        double offset = IsHost || _presenceOffset == double.MaxValue ? 0.0 : _presenceOffset;
        at = p.Track.Sample(Clock - offset - PresenceDelay);
        flags = p.Flags;
        return true;
    }

    /// <summary>Forgets a member's presence (they left the hideout).</summary>
    private void DropPresence(long peer) => _presence.Remove(peer);

    private static bool SaneTownPoint(Vector2 at) =>
        float.IsFinite(at.X) && float.IsFinite(at.Y) && HideoutTown.Bounds.Grow(64f).HasPoint(at);

    private void Store(long peer, double time, Vector2 at, byte flags, ushort seq, bool checkSeq)
    {
        if (!_presence.TryGetValue(peer, out Presence? p))
        {
            p = new Presence { Track = new NetTrack(at) };
            _presence[peer] = p;
        }
        else if (checkSeq && (short)(seq - p.Seq) <= 0)
        {
            return; // older than what we have (unreliable, may arrive out of order)
        }

        p.Seq = seq;
        p.Flags = flags;
        p.Fresh = true;
        p.Track.Push(time, at);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcPresenceToHost(byte[] data)
    {
        long sender = _api.GetRemoteSenderId();
        if (!Admit(sender, data.Length, NetLimits.PresenceBytes, "presence") || PlaceOf(sender) != MemberPlace.Hideout)
        {
            return;
        }

        PresenceReceived++;
        var r = new NetReader(data);
        ushort seq = r.U16();
        Vector2 at = r.Pos();
        byte flags = r.U8();
        if (SaneTownPoint(at))
        {
            Store(sender, Clock, at, flags, seq, checkSeq: true);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcPresenceToClients(byte[] data)
    {
        if (!IsClient || LocalPlace != MemberPlace.Hideout)
        {
            return;
        }

        PresenceReceived++;
        var r = new NetReader(data);
        float hostTime = r.F32();
        if (!float.IsFinite(hostTime))
        {
            return;
        }

        double offset = Clock - hostTime;
        _presenceOffset = _presenceOffset == double.MaxValue || offset < _presenceOffset ? offset : _presenceOffset + 0.001;
        int count = Math.Min((int)r.U8(), PartyRules.MaxPlayers);
        for (int i = 0; i < count; i++)
        {
            long peer = r.U32();
            Vector2 at = r.Pos();
            byte flags = r.U8();
            if (peer != LocalPeerId && Member(peer) is not null && SaneTownPoint(at))
            {
                Store(peer, hostTime, at, flags, 0, checkSeq: false);
            }
        }
    }

    /// <summary>Host, every frame: relays everyone's presence to the members in the
    /// hideout at <see cref="PartyRules.PresenceHz"/>, whatever scene the host shows.</summary>
    private void RelayPresence(double delta)
    {
        if (!IsHost || (_presenceIn -= delta) > 0.0)
        {
            return;
        }

        _presenceIn += 1.0 / PartyRules.PresenceHz;
        if (_presenceIn < 0.0)
        {
            _presenceIn = 1.0 / PartyRules.PresenceHz;
        }

        var home = Members.Where(m => PlaceOf(m.PeerId) == MemberPlace.Hideout).Select(m => m.PeerId).ToList();
        foreach (long receiver in home.Where(p => p != 1))
        {
            var w = new NetWriter(48);
            w.F32((float)Clock);
            int countAt = w.Length;
            w.U8(0);
            int count = 0;
            foreach (long peer in home.Where(p => p != receiver))
            {
                Vector2 at;
                byte flags;
                if (peer == 1)
                {
                    if (_hostPresence is not { } own)
                    {
                        continue;
                    }

                    (at, flags) = (own, _hostFlags);
                }
                else if (_presence.TryGetValue(peer, out Presence? p))
                {
                    (at, flags) = (p.Track.Latest, p.Flags);
                }
                else
                {
                    continue;
                }

                w.U32((uint)peer);
                w.Pos(at);
                w.U8(flags);
                count++;
            }

            if (count == 0)
            {
                continue;
            }

            byte[] data = w.ToArray();
            data[countAt] = (byte)count;
            PresenceBytesSent += data.Length;
            PresenceSent++;
            RpcId(receiver, MethodName.RpcPresenceToClients, data);
        }
    }
}
