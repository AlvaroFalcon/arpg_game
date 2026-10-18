#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArpgGame.Net;

/// <summary>
/// The party session on top of the lobby — docs/design/multiplayer.md -> "Party
/// session". A session no longer lasts one run: the party sits in the host's
/// hideout between rifts, and a rift runs with whoever takes part. This file
/// holds the session's phase, where each member is (<see cref="MemberPlace"/>),
/// the rift being run (<see cref="CurrentRun"/>) and the two byte channels every
/// party message travels on (<see cref="SessionProtocol"/>), all on transfer
/// channel 0 like every other RPC here.
/// </summary>
public partial class NetSession
{
    /// <summary>Where the party is (the host decides; clients read it from the roster).</summary>
    public SessionPhase PartyPhase { get; private set; }

    /// <summary>Where this machine said it is.</summary>
    public MemberPlace LocalPlace { get; private set; }

    /// <summary>The rift running now, if any. Host: always set while it runs.
    /// Client: set only on a participant (from <see cref="SessionProtocol.StartRift"/>).</summary>
    public PartyRun? CurrentRun { get; private set; }

    /// <summary>The party's rift as the roster describes it (everyone, participants or not):
    /// what it is, who is in it, and this machine's clock when it started. Null when none runs.</summary>
    public PartyRun? HostRun { get; private set; }

    /// <summary>Tests only: honour <see cref="SessionProtocol.TestAsk"/>/<see cref="SessionProtocol.TestAnswer"/>.
    /// Off in the game, so a host can't make a client answer anything.</summary>
    public static bool TestHooks { get; set; }

    /// <summary>Host, tests: a client answered a test question (peer, question id, text).</summary>
    public event Action<long, int, string>? TestAnswered;

    /// <summary>Client, tests: the host asked something (question id, text).</summary>
    public event Action<int, string>? TestAsked;

    /// <summary>The roster changed (phase, places, the rift, edit rights).</summary>
    public event Action? PartyChanged;

    private readonly Dictionary<long, MemberPlace> _places = new();
    private int _runIds;

    /// <summary>Where a member is (Menu when unknown).</summary>
    public MemberPlace PlaceOf(long peer) => _places.TryGetValue(peer, out MemberPlace p) ? p : MemberPlace.Menu;

    /// <summary>The members taking part in the current rift (empty when none or offline).</summary>
    public IReadOnlyList<LobbyMember> RunMembers =>
        CurrentRun is { } run ? Members.Where(m => run.Includes(m.PeerId)).ToList() : Array.Empty<LobbyMember>();

    /// <summary>Is <paramref name="peer"/> in the party's running rift?</summary>
    public bool InPartyRun(long peer) => HostRun is { } run && run.Includes(peer);

    /// <summary>Name of a member, or "someone".</summary>
    public string NameOf(long peer) => Member(peer)?.Name is { Length: > 0 } n ? n : "someone";

    public string HostName => NameOf(1);

    /// <summary>Clears every party field (leaving, or a fresh session).</summary>
    private void ResetParty()
    {
        PartyPhase = SessionPhase.Lobby;
        LocalPlace = MemberPlace.Menu;
        CurrentRun = null;
        HostRun = null;
        _places.Clear();
        EditHolder = 0;
        _canEdit.Clear();
        ResetHideout();
    }

    // --- places ----------------------------------------------------------------------

    /// <summary>This machine is now at <paramref name="place"/> (scenes call it from _Ready).</summary>
    public void ReportPlace(MemberPlace place)
    {
        if (!Active)
        {
            return;
        }

        LocalPlace = place;
        if (IsHost)
        {
            SetPlace(1, place);
        }
        else if (Joined)
        {
            SendToHost(SessionProtocol.Place, w => w.U8((byte)place));
        }
    }

    /// <summary>Host: records where a member is and reacts to the change.</summary>
    private void SetPlace(long peer, MemberPlace place)
    {
        MemberPlace old = PlaceOf(peer);
        _places[peer] = place;
        if (old == place)
        {
            return;
        }

        if (old == MemberPlace.Run && place != MemberPlace.Run && CurrentRun is { } run && run.Includes(peer))
        {
            // Left the rift (results → hideout, "Leave the run"): out of the run, still in the party.
            if (peer != 1)
            {
                run.Participants.Remove(peer);
                Run?.OnPeerLeft(peer);
            }
        }

        OnPlaceChanged(peer, old, place);
        BroadcastRoster();
    }

    /// <summary>Host: a member's hello was accepted (the roster follows).</summary>
    private void OnMemberJoined(long peer) => PublishLayout(peer);

    /// <summary>Host: a member left or dropped (the roster follows).</summary>
    private void OnMemberLeft(long peer) => DropPresence(peer);

    /// <summary>Host: hook for what a place change means (the hideout's layout, the phase).</summary>
    private void OnPlaceChanged(long peer, MemberPlace old, MemberPlace place)
    {
        if (place != MemberPlace.Hideout)
        {
            if (peer == 1)
            {
                _hostPresence = null;
            }

            DropPresence(peer);
            return;
        }

        if (peer == 1 && PartyPhase is SessionPhase.Run or SessionPhase.Returning)
        {
            PartyPhase = SessionPhase.Hideout;
        }
        else if (peer != 1)
        {
            PublishLayout(peer); // whatever changed while they were away (ignored when the same)
        }
    }

    // --- the rift ------------------------------------------------------------------------

    /// <summary>
    /// Host: the participants load <paramref name="spec"/>'s scene (the host always
    /// takes part). Everyone else stays where they are. Their copies of each other's
    /// characters are refreshed first: the lobby goes out before the start, on the
    /// same reliable channel, so it arrives first.
    /// </summary>
    public void StartRift(RiftSpec spec, IEnumerable<long> participants)
    {
        if (!IsHost || InRun || !spec.Sanitize())
        {
            return;
        }

        var peers = new List<long> { 1 };
        peers.AddRange(participants.Where(p => p != 1 && Member(p) is not null).Distinct());
        var run = new PartyRun { Id = ++_runIds, Spec = spec.Copy(), Participants = peers, StartedAt = Clock };
        CurrentRun = run;
        HostRun = run;
        InRun = true;
        PartyPhase = SessionPhase.Run;
        BroadcastLobby();
        byte[] start = StartRiftMessage(run.Id, run.Spec, peers);
        foreach (long peer in peers.Where(p => p != 1))
        {
            RpcId(peer, MethodName.RpcPartyToClient, start);
        }

        ReportPlace(MemberPlace.Loading);
        Launch(run.Spec);
    }

    /// <summary>Host: everyone in the lobby loads the Survival Rift (the lobby's start, tools, tests).</summary>
    public void StartRun() => StartRift(RiftSpec.SurvivalRift(), Members.Select(m => m.PeerId));

    /// <summary>The <see cref="SessionProtocol.StartRift"/> message (tests send a forged one).</summary>
    public static byte[] StartRiftMessage(int id, RiftSpec spec, IReadOnlyCollection<long> peers)
    {
        var w = new NetWriter(64);
        w.U8(SessionProtocol.StartRift);
        w.U16((ushort)id);
        spec.Write(w);
        w.U8((byte)Math.Min(peers.Count, PartyRules.MaxPlayers));
        foreach (long p in peers.Take(PartyRules.MaxPlayers))
        {
            w.U32((uint)p);
        }

        return w.ToArray();
    }

    /// <summary>Loads the rift's scene on this machine.</summary>
    private void Launch(RiftSpec spec)
    {
        if (spec.Kind == RiftKind.Reality)
        {
            World.RealityLaunch.Tier = spec.Tier;
            World.RealityLaunch.LayoutId = spec.LayoutId.Length > 0 ? spec.LayoutId : null;
            World.RealityLaunch.Modifiers = new List<string>(spec.Modifiers);
            World.RealityLaunch.Seed = spec.Seed != 0 ? spec.Seed : null;
            Core.Scenes.Go(this, Core.Scenes.Reality);
            return;
        }

        Core.Scenes.Go(this, Core.Scenes.Run);
    }

    // --- the roster -------------------------------------------------------------------------

    /// <summary>Host: sends the phase, the rift and every member's place to every member.</summary>
    private void BroadcastRoster()
    {
        if (!IsHost)
        {
            return;
        }

        var w = new NetWriter(96);
        w.U8(SessionProtocol.Roster);
        w.U8((byte)PartyPhase);
        WriteRunInfo(w, HostRun);
        WriteEditInfo(w);
        w.U8((byte)Math.Min(Members.Count, PartyRules.MaxPlayers));
        foreach (LobbyMember m in Members.Take(PartyRules.MaxPlayers))
        {
            w.U32((uint)m.PeerId);
            w.U8((byte)PlaceOf(m.PeerId));
            w.Bool(CanEditOf(m.PeerId));
        }

        byte[] data = w.ToArray();
        foreach (LobbyMember m in Members.Where(m => m.PeerId != 1))
        {
            RpcId(m.PeerId, MethodName.RpcPartyToClient, data);
        }

        PartyChanged?.Invoke();
    }

    private static void WriteRunInfo(NetWriter w, PartyRun? run)
    {
        w.Bool(run is not null);
        if (run is null)
        {
            return;
        }

        w.U16((ushort)run.Id);
        w.U8((byte)run.Spec.Kind);
        w.U8((byte)run.Spec.Tier);
        w.F32((float)Math.Max(0.0, Clock - run.StartedAt));
        w.U8((byte)Math.Min(run.Participants.Count, PartyRules.MaxPlayers));
        foreach (long p in run.Participants.Take(PartyRules.MaxPlayers))
        {
            w.U32((uint)p);
        }
    }

    private void ReadRoster(NetReader r)
    {
        byte phase = r.U8();
        PartyRun? run = null;
        if (r.Bool())
        {
            int id = r.U16();
            var kind = (RiftKind)r.U8();
            int tier = r.U8();
            float elapsed = r.F32();
            int count = Math.Min((int)r.U8(), PartyRules.MaxPlayers);
            var peers = new List<long>();
            for (int i = 0; i < count; i++)
            {
                peers.Add(r.U32());
            }

            var spec = new RiftSpec { Kind = kind, Tier = tier };
            if (spec.Sanitize())
            {
                elapsed = float.IsFinite(elapsed) ? Mathf.Clamp(elapsed, 0f, 86400f) : 0f;
                run = new PartyRun { Id = id, Spec = spec, Participants = peers, StartedAt = Clock - elapsed };
            }
        }

        ReadEditInfo(r);
        int members = Math.Min((int)r.U8(), PartyRules.MaxPlayers);
        var places = new Dictionary<long, MemberPlace>();
        var canEdit = new HashSet<long>();
        for (int i = 0; i < members; i++)
        {
            long peer = r.U32();
            byte place = r.U8();
            bool edit = r.Bool();
            places[peer] = place <= (byte)MemberPlace.Run ? (MemberPlace)place : MemberPlace.Menu;
            if (edit)
            {
                canEdit.Add(peer);
            }
        }

        if (phase > (byte)SessionPhase.Returning)
        {
            return;
        }

        SessionPhase was = PartyPhase;
        PartyPhase = (SessionPhase)phase;
        // Keep the old start time for the same rift (the elapsed time is only an estimate).
        HostRun = run is not null && HostRun is { } old && old.Id == run.Id ? new PartyRun { Id = run.Id, Spec = run.Spec, Participants = run.Participants, StartedAt = old.StartedAt } : run;
        _places.Clear();
        foreach ((long peer, MemberPlace place) in places)
        {
            _places[peer] = place;
        }

        SetEditRights(canEdit);
        OnRosterRead(was);
        PartyChanged?.Invoke();
    }

    /// <summary>Client: hook after a roster arrived (moving into the hideout, the waiting banner).</summary>
    private void OnRosterRead(SessionPhase was) => MaybeEnterHideout();

    // Edit rights (increment "edit permission"): the roster carries them; the lock lives here.
    public long EditHolder { get; private set; }
    private readonly HashSet<long> _canEdit = new();

    /// <summary>May <paramref name="peer"/> edit the host's town (the host always may)?</summary>
    public bool CanEditOf(long peer) => peer == 1 || _canEdit.Contains(peer);

    private void WriteEditInfo(NetWriter w) => w.U32((uint)EditHolder);

    private void ReadEditInfo(NetReader r) => EditHolder = r.U32();

    private void SetEditRights(HashSet<long> peers)
    {
        _canEdit.Clear();
        _canEdit.UnionWith(peers.Where(p => p != 1));
    }

    // --- sending ----------------------------------------------------------------------------

    private static byte[] Message(byte kind, Action<NetWriter>? body)
    {
        var w = new NetWriter(64);
        w.U8(kind);
        body?.Invoke(w);
        return w.ToArray();
    }

    private void SendToHost(byte kind, Action<NetWriter>? body = null)
    {
        if (IsClient)
        {
            RpcId(1, MethodName.RpcPartyToHost, Message(kind, body));
        }
    }

    private void SendToClient(long peer, byte kind, Action<NetWriter>? body = null)
    {
        if (IsHost && peer != 1 && Member(peer) is not null)
        {
            RpcId(peer, MethodName.RpcPartyToClient, Message(kind, body));
        }
    }

    // --- tests --------------------------------------------------------------------------------

    /// <summary>Host, tests: asks a client a question outside any run (answered through <see cref="TestAnswered"/>).</summary>
    public void SendTestAsk(long peer, int id, string question) =>
        SendToClient(peer, SessionProtocol.TestAsk, w =>
        {
            w.U16((ushort)id);
            w.Str(question);
        });

    /// <summary>Client, tests: answers question <paramref name="id"/>.</summary>
    public void SendTestAnswer(int id, string text) =>
        SendToHost(SessionProtocol.TestAnswer, w =>
        {
            w.U16((ushort)id);
            w.Str(text);
        });

    // --- RPCs: the party (channel 0) ------------------------------------------------------------

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcPartyToClient(byte[] data)
    {
        if (!IsClient || data.Length == 0)
        {
            return;
        }

        var r = new NetReader(data);
        byte kind = r.U8();
        try
        {
            switch (kind)
            {
                case SessionProtocol.Roster:
                    ReadRoster(r);
                    break;
                case SessionProtocol.StartRift:
                    ReadStartRift(r);
                    break;
                case SessionProtocol.TestAsk:
                    if (TestHooks)
                    {
                        int id = r.U16();
                        TestAsked?.Invoke(id, SessionProtocol.Cut(r.Str(), 2000));
                    }

                    break;
                default:
                    OnPartyToClient(kind, r);
                    break;
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IndexOutOfRangeException or FormatException or OverflowException)
        {
            GD.PushWarning($"Co-op: a malformed party message ({kind}) from the host was dropped: {e.Message}");
        }
    }

    /// <summary>Client: hook for the message kinds of later steps (proposals, edits, notices).</summary>
    private void OnPartyToClient(byte kind, NetReader r)
    {
    }

    private void ReadStartRift(NetReader r)
    {
        int id = r.U16();
        RiftSpec? spec = RiftSpec.Read(r);
        int count = Math.Min((int)r.U8(), PartyRules.MaxPlayers);
        var peers = new List<long>();
        for (int i = 0; i < count; i++)
        {
            peers.Add(r.U32());
        }

        if (spec is null || InRun || !peers.Contains(LocalPeerId))
        {
            return;
        }

        CurrentRun = new PartyRun { Id = id, Spec = spec, Participants = peers, StartedAt = Clock };
        HostRun ??= CurrentRun;
        InRun = true;
        ReportPlace(MemberPlace.Loading);
        Launch(spec);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcPartyToHost(byte[] data)
    {
        long sender = _api.GetRemoteSenderId();
        if (!Admit(sender, data.Length, NetLimits.PartyMessageBytes, "party message") || data.Length == 0)
        {
            return;
        }

        var r = new NetReader(data);
        byte kind = r.U8();
        switch (kind)
        {
            case SessionProtocol.Place:
                byte place = r.U8();
                if (place <= (byte)MemberPlace.Run)
                {
                    SetPlace(sender, (MemberPlace)place);
                }

                break;
            case SessionProtocol.TestAnswer:
                if (TestHooks)
                {
                    int id = r.U16();
                    TestAnswered?.Invoke(sender, id, SessionProtocol.Cut(r.Str(), 4000));
                }

                break;
            default:
                OnPartyToHost(sender, kind, r);
                break;
        }
    }

    /// <summary>Host: hook for the message kinds of later steps.</summary>
    private void OnPartyToHost(long sender, byte kind, NetReader r)
    {
    }
}
