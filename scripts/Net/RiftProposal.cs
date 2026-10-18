#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArpgGame.Net;

/// <summary>
/// One rift proposal on the host — docs/design/multiplayer.md -> "Proposals".
/// Someone in the hideout proposes a rift; everyone home is asked and has
/// <see cref="PartyRules.ProposalSeconds"/> to answer. Owner rules (2026-10-18):
/// the host always takes part, so the host declining (or not answering in time)
/// cancels it for everyone; otherwise it starts with whoever accepted, once
/// everyone answered or the time ran out. A non-answer counts as "stay".
/// Pure (no nodes, no clock of its own): the session ticks it with the host's
/// clock, and the test checks it without a network.
/// </summary>
public sealed class RiftProposal
{
    public enum Result
    {
        Open,
        Cancelled,
        Started,
    }

    public const long HostPeer = 1;

    public int Id { get; }
    public long Proposer { get; }
    public RiftSpec Spec { get; }
    public double Deadline { get; }

    /// <summary>Everyone asked (the proposer and the host included).</summary>
    public IReadOnlyCollection<long> Asked => _asked;

    public Result State { get; private set; } = Result.Open;

    /// <summary>Why it was cancelled ("the host declined"), for the toast.</summary>
    public string Reason { get; private set; } = "";

    /// <summary>Who goes, once started: the proposer, the host and the accepters still home.</summary>
    public IReadOnlyList<long> Participants { get; private set; } = Array.Empty<long>();

    private readonly HashSet<long> _asked;
    private readonly Dictionary<long, bool> _answers = new();

    public RiftProposal(int id, long proposer, RiftSpec spec, IEnumerable<long> asked, double now, double seconds)
    {
        Id = id;
        Proposer = proposer;
        Spec = spec;
        Deadline = now + seconds;
        _asked = asked.ToHashSet();
        _asked.Add(proposer);
        _asked.Add(HostPeer);
        // Proposing is accepting; the host proposing is the host accepting.
        _answers[proposer] = true;
    }

    public int Accepted => _answers.Count(a => a.Value);
    public int Declined => _answers.Count(a => !a.Value);
    public int Pending => _asked.Count - _answers.Count;

    /// <summary>What <paramref name="peer"/> answered (null: not yet, or not asked).</summary>
    public bool? AnswerOf(long peer) => _answers.TryGetValue(peer, out bool a) ? a : null;

    /// <summary>Records an answer. Ignored (false) if not asked, already answered, or closed.</summary>
    public bool Answer(long peer, bool accept)
    {
        if (State != Result.Open || !_asked.Contains(peer) || _answers.ContainsKey(peer))
        {
            return false;
        }

        _answers[peer] = accept;
        return true;
    }

    /// <summary>The proposer withdraws it (anyone else: ignored).</summary>
    public bool Cancel(long by, string name)
    {
        if (State != Result.Open || by != Proposer)
        {
            return false;
        }

        Close(Result.Cancelled, $"{name} cancelled it");
        return true;
    }

    /// <summary>
    /// Advances the proposal to <paramref name="now"/> (host clock). <paramref name="home"/>
    /// says whether a member is still in the party and in the hideout;
    /// <paramref name="proposerName"/> words the "left" reason.
    /// </summary>
    public Result Tick(double now, Func<long, bool> home, string proposerName)
    {
        if (State != Result.Open)
        {
            return State;
        }

        if (!home(Proposer))
        {
            return Close(Result.Cancelled, $"{proposerName} left");
        }

        if (AnswerOf(HostPeer) == false)
        {
            return Close(Result.Cancelled, "the host declined");
        }

        bool timedOut = now >= Deadline;
        if (timedOut && AnswerOf(HostPeer) is null)
        {
            return Close(Result.Cancelled, "the host didn't answer");
        }

        bool everyone = _asked.Where(home).All(p => _answers.ContainsKey(p));
        if (AnswerOf(HostPeer) == true && (everyone || timedOut))
        {
            var going = new List<long> { HostPeer };
            if (Proposer != HostPeer)
            {
                going.Add(Proposer);
            }

            going.AddRange(_answers.Where(a => a.Value && a.Key != HostPeer && a.Key != Proposer && home(a.Key)).Select(a => a.Key).OrderBy(p => p));
            Participants = going;
            return Close(Result.Started, "");
        }

        return Result.Open;
    }

    private Result Close(Result result, string reason)
    {
        State = result;
        Reason = reason;
        return result;
    }
}
