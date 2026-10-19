#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using ArpgGame.Characters;

namespace ArpgGame.Net;

/// <summary>A rift proposal as one machine sees it (the popup draws it).</summary>
public sealed class ProposalView
{
    public int Id { get; init; }
    public long Proposer { get; init; }
    public string ProposerName { get; init; } = "";
    public RiftSpec Spec { get; init; } = RiftSpec.SurvivalRift();

    /// <summary>This machine's clock when the answers close.</summary>
    public double Deadline { get; set; }

    public int Accepted { get; set; }
    public int Declined { get; set; }
    public int Pending { get; set; }

    /// <summary>This machine's answer (null: not yet). The proposer has accepted by proposing.</summary>
    public bool? Mine { get; set; }

    public int Asked => Accepted + Declined + Pending;

    public double SecondsLeft => Math.Max(0.0, Deadline - NetSession.Clock);
}

/// <summary>
/// Rift proposals — docs/design/multiplayer.md -> "Proposals". Anyone in the
/// hideout proposes a rift; the host arbitrates (one at a time, its clock), asks
/// everyone home, and starts it with whoever accepted. A participant's
/// character is re-sent with their "yes", so the host builds their current gear.
/// </summary>
public partial class NetSession
{
    /// <summary>The open proposal as this machine sees it (null when none).</summary>
    public ProposalView? OpenProposal { get; private set; }

    /// <summary>A proposal opened or its counts changed.</summary>
    public event Action? ProposalChanged;

    /// <summary>A proposal closed: the view, whether the rift starts, and why not.</summary>
    public event Action<ProposalView, bool, string>? ProposalClosed;

    /// <summary>A short message for this player (a refused proposal: "The party is in a rift").</summary>
    public event Action<string>? Notice;

    /// <summary>Tests: seconds to answer instead of <see cref="PartyRules.ProposalSeconds"/>.</summary>
    public static float? ProposalSecondsOverride { get; set; }

    /// <summary>Set by the hideout: captures the local player into the profile and
    /// returns the active character (so what is sent is what the player wears now).</summary>
    public Func<CharacterData?>? LocalCharacter { get; set; }

    /// <summary>Raised on every machine just before it loads a rift (the hideout saves its player).</summary>
    public event Action? Launching;

    /// <summary>Host: character updates refused (tests).</summary>
    public int RefusedCharacterUpdates { get; private set; }

    private RiftProposal? _proposal;
    private int _proposalIds;
    private readonly Dictionary<long, double> _proposalCooldown = new();
    private readonly Dictionary<long, double> _lastCharacterUpdate = new();

    private void ResetProposals()
    {
        _proposal = null;
        OpenProposal = null;
        _proposalCooldown.Clear();
        _lastCharacterUpdate.Clear();
    }

    // --- the local player's actions ----------------------------------------------------------

    /// <summary>Proposes <paramref name="spec"/> to the party (the host checks it).</summary>
    public void Propose(RiftSpec spec)
    {
        if (IsHost)
        {
            HostPropose(1, spec);
        }
        else if (IsClient && Joined)
        {
            SendCharacterUpdate();
            SendToHost(SessionProtocol.Propose, spec.Write);
        }
    }

    /// <summary>Answers the open proposal: join, or stay in the hideout.</summary>
    public void AnswerProposal(bool accept)
    {
        if (OpenProposal is not { Mine: null } view)
        {
            return;
        }

        if (IsHost)
        {
            HostAnswer(1, view.Id, accept);
            return;
        }

        if (accept)
        {
            SendCharacterUpdate();
        }

        view.Mine = accept;
        SendToHost(SessionProtocol.Answer, w =>
        {
            w.U16((ushort)view.Id);
            w.Bool(accept);
        });
        ProposalChanged?.Invoke();
    }

    /// <summary>The proposer withdraws their proposal.</summary>
    public void CancelProposal()
    {
        if (OpenProposal is not { } view || view.Proposer != LocalPeerId)
        {
            return;
        }

        if (IsHost)
        {
            _proposal?.Cancel(1, NameOf(1));
            TickProposal();
            return;
        }

        SendToHost(SessionProtocol.CancelProposal, w => w.U16((ushort)view.Id));
    }

    /// <summary>Client: re-sends this player's character (gear changed in the hideout).</summary>
    public void SendCharacterUpdate()
    {
        if (!IsClient || !Joined || (LocalCharacter?.Invoke() ?? Core.Profile.Get(this)?.Active) is not { } character)
        {
            return;
        }

        RpcId(1, MethodName.RpcCharacterUpdate, JsonSerializer.Serialize(MemberFor(character, ViewSize())));
    }

    // --- host ----------------------------------------------------------------------------------

    private string? WhyNoProposal(long proposer, RiftSpec spec)
    {
        if (PartyPhase != SessionPhase.Hideout || InRun)
        {
            return "The party is in a rift — wait for them to return";
        }

        if (PlaceOf(proposer) != MemberPlace.Hideout)
        {
            return "Only someone in the hideout can open a rift";
        }

        if (_proposal is { State: RiftProposal.Result.Open } open)
        {
            return $"{NameOf(open.Proposer)} is already opening a rift";
        }

        if (_proposalCooldown.TryGetValue(proposer, out double until) && Clock < until)
        {
            return "Wait a moment before proposing again";
        }

        if (!spec.Sanitize())
        {
            return "That rift can't be opened";
        }

        if (spec.Kind == RiftKind.Reality && !PartyRules.CoopRealities)
        {
            return "Altered Realities with friends arrive in the next update";
        }

        return null;
    }

    private void TellPlayer(long peer, string text)
    {
        if (peer == 1)
        {
            Notice?.Invoke(text);
        }
        else
        {
            SendToClient(peer, SessionProtocol.Notice, w => w.Str(text));
        }
    }

    private void HostPropose(long proposer, RiftSpec spec)
    {
        if (WhyNoProposal(proposer, spec) is { } why)
        {
            TellPlayer(proposer, why);
            return;
        }

        var asked = Members.Where(m => PlaceOf(m.PeerId) == MemberPlace.Hideout).Select(m => m.PeerId).ToList();
        _proposal = new RiftProposal(++_proposalIds, proposer, spec.Copy(), asked, Clock, ProposalSecondsOverride ?? PartyRules.ProposalSeconds);
        OpenProposal = ViewOf(_proposal, 1);
        foreach (long peer in _proposal.Asked.Where(p => p != 1))
        {
            SendToClient(peer, SessionProtocol.Proposal, w =>
            {
                w.U16((ushort)_proposal.Id);
                w.U32((uint)_proposal.Proposer);
                _proposal.Spec.Write(w);
                w.F32((float)(_proposal.Deadline - Clock));
                WriteCounts(w, _proposal);
                w.U8(_proposal.AnswerOf(peer) switch { true => 1, false => 2, _ => 0 });
            });
        }

        ProposalChanged?.Invoke();
        TickProposal(); // a party of one starts at once
    }

    private ProposalView ViewOf(RiftProposal p, long viewer) => new()
    {
        Id = p.Id,
        Proposer = p.Proposer,
        ProposerName = NameOf(p.Proposer),
        Spec = p.Spec,
        Deadline = p.Deadline,
        Accepted = p.Accepted,
        Declined = p.Declined,
        Pending = p.Pending,
        Mine = p.AnswerOf(viewer),
    };

    private static void WriteCounts(NetWriter w, RiftProposal p)
    {
        w.U8((byte)p.Accepted);
        w.U8((byte)p.Declined);
        w.U8((byte)p.Pending);
    }

    private void HostAnswer(long peer, int id, bool accept)
    {
        if (_proposal is not { } p || p.Id != id || !p.Answer(peer, accept))
        {
            return;
        }

        if (OpenProposal is { } view)
        {
            (view.Accepted, view.Declined, view.Pending, view.Mine) = (p.Accepted, p.Declined, p.Pending, p.AnswerOf(1));
        }

        foreach (long other in p.Asked.Where(x => x != 1))
        {
            SendToClient(other, SessionProtocol.ProposalStatus, w =>
            {
                w.U16((ushort)p.Id);
                WriteCounts(w, p);
            });
        }

        ProposalChanged?.Invoke();
        TickProposal();
    }

    /// <summary>Host, every frame: closes the proposal when its outcome is known.</summary>
    private void TickProposal()
    {
        if (!IsHost || _proposal is not { } p)
        {
            return;
        }

        RiftProposal.Result result = p.Tick(Clock, peer => Member(peer) is not null && PlaceOf(peer) == MemberPlace.Hideout, NameOf(p.Proposer));
        if (result == RiftProposal.Result.Open)
        {
            return;
        }

        _proposal = null;
        bool started = result == RiftProposal.Result.Started;
        if (!started)
        {
            _proposalCooldown[p.Proposer] = Clock + PartyRules.ProposalCooldownSeconds;
        }

        foreach (long peer in p.Asked.Where(x => x != 1))
        {
            SendToClient(peer, SessionProtocol.ProposalClosed, w =>
            {
                w.U16((ushort)p.Id);
                w.Bool(started);
                w.Str(p.Reason);
            });
        }

        ProposalView view = OpenProposal ?? ViewOf(p, 1);
        OpenProposal = null;
        ProposalClosed?.Invoke(view, started, p.Reason);
        if (started)
        {
            RiftSpec spec = p.Spec.Copy();
            if (spec.Kind == RiftKind.Reality && spec.Seed == 0)
            {
                spec.Seed = ((ulong)GD.Randi() << 32) | GD.Randi();
            }

            StartRift(spec, p.Participants, p.Proposer);
        }
    }

    // --- wire ----------------------------------------------------------------------------------

    /// <summary>Host: proposal traffic from a client.</summary>
    private void OnProposalToHost(long sender, byte kind, NetReader r)
    {
        switch (kind)
        {
            case SessionProtocol.Propose:
                if (RiftSpec.Read(r) is { } spec)
                {
                    HostPropose(sender, spec);
                }
                else
                {
                    TellPlayer(sender, "That rift can't be opened");
                }

                break;
            case SessionProtocol.Answer:
                int id = r.U16();
                HostAnswer(sender, id, r.Bool());
                break;
            case SessionProtocol.CancelProposal:
                if (_proposal is { } p && p.Id == r.U16() && p.Cancel(sender, NameOf(sender)))
                {
                    TickProposal();
                }

                break;
        }
    }

    /// <summary>Client: proposal traffic from the host.</summary>
    private void OnProposalToClient(byte kind, NetReader r)
    {
        switch (kind)
        {
            case SessionProtocol.Proposal:
            {
                int id = r.U16();
                long proposer = r.U32();
                RiftSpec? spec = RiftSpec.Read(r);
                float left = r.F32();
                int accepted = r.U8();
                int declined = r.U8();
                int pending = r.U8();
                byte mine = r.U8();
                if (spec is null || !float.IsFinite(left))
                {
                    return;
                }

                OpenProposal = new ProposalView
                {
                    Id = id,
                    Proposer = proposer,
                    ProposerName = NameOf(proposer),
                    Spec = spec,
                    Deadline = Clock + Mathf.Clamp(left, 0f, 120f),
                    Accepted = Math.Min(accepted, PartyRules.MaxPlayers),
                    Declined = Math.Min(declined, PartyRules.MaxPlayers),
                    Pending = Math.Min(pending, PartyRules.MaxPlayers),
                    Mine = mine switch { 1 => true, 2 => false, _ => null },
                };
                ProposalChanged?.Invoke();
                break;
            }
            case SessionProtocol.ProposalStatus:
            {
                int id = r.U16();
                if (OpenProposal is { } view && view.Id == id)
                {
                    view.Accepted = Math.Min((int)r.U8(), PartyRules.MaxPlayers);
                    view.Declined = Math.Min((int)r.U8(), PartyRules.MaxPlayers);
                    view.Pending = Math.Min((int)r.U8(), PartyRules.MaxPlayers);
                    ProposalChanged?.Invoke();
                }

                break;
            }
            case SessionProtocol.ProposalClosed:
            {
                int id = r.U16();
                bool started = r.Bool();
                string reason = SessionProtocol.Cut(r.Str());
                if (OpenProposal is { } view && view.Id == id)
                {
                    OpenProposal = null;
                    ProposalClosed?.Invoke(view, started, reason);
                }

                break;
            }
            case SessionProtocol.Notice:
                Notice?.Invoke(SessionProtocol.Cut(r.Str()));
                break;
        }
    }

    /// <summary>Host: a member re-sent their character (checked like a hello; the old one is
    /// kept when it fails, and at most one update per <see cref="NetLimits.CharacterUpdateSeconds"/>).</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    private void RpcCharacterUpdate(string memberJson)
    {
        long sender = _api.GetRemoteSenderId();
        if (!Admit(sender, memberJson.Length, NetLimits.HelloChars, "character update") || Member(sender) is not { } member)
        {
            return;
        }

        if (_lastCharacterUpdate.TryGetValue(sender, out double last) && Clock - last < NetLimits.CharacterUpdateSeconds)
        {
            RefusedCharacterUpdates++;
            return; // too soon: the previous update stands
        }

        _lastCharacterUpdate[sender] = Clock;
        LobbyMember? fresh;
        try
        {
            fresh = JsonSerializer.Deserialize<LobbyMember>(memberJson);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            fresh = null;
        }

        string? why = fresh is null ? "unreadable" : CheckMember(fresh);
        if (why is not null)
        {
            RefusedCharacterUpdates++;
            Security($"kept {member.Name}'s previous character: the update was refused ({why})");
            return;
        }

        member.Character = fresh!.Character;
        member.Name = fresh.Name;
        member.Level = fresh.Level;
        member.ClassName = fresh.ClassName;
        member.ViewWidth = fresh.ViewWidth;
        member.ViewHeight = fresh.ViewHeight;
    }

    /// <summary>Host: its own lobby entry from the character it plays right now.</summary>
    private void RefreshOwnCharacter()
    {
        if (_me is null || LocalCharacter?.Invoke() is not { } character)
        {
            return;
        }

        LobbyMember fresh = MemberFor(character, ViewSize());
        _me.Character = fresh.Character;
        _me.Name = fresh.Name;
        _me.Level = fresh.Level;
        _me.ClassName = fresh.ClassName;
    }
}
