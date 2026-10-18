#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Progression;
using ArpgGame.Run;

namespace ArpgGame.Net;

/// <summary>
/// The party's level-up rounds — docs/design/multiplayer.md -> "Level-ups".
///
/// <list type="bullet">
/// <item>One party bar (<see cref="RunState.PartyLevel"/>): when it fills, the host
/// opens a <b>round</b> and pauses the whole game on every machine (the host's scene
/// tree, so the director, monsters, projectiles, telegraphs, ailments, cooldowns,
/// the run clock and every player stop exactly as offline; clients pause theirs
/// when the round reaches them). The network nodes keep running (ProcessMode Always).</item>
/// <item>Every player — downed ones too — picks one card from a row rolled on their
/// own machine. The host applies each pick to its copy of that player's build
/// (a client's only after checking it) and tells that client which card it applied.</item>
/// <item>The round closes when every player still in the party has chosen, or at the
/// host's deadline (<see cref="PartyRules.LevelUpTimeout"/>), when the first card of
/// each missing player's row is taken for them. More levels queued: the next round
/// opens at once, still paused. Then the game resumes.</item>
/// <item>The run-start skill pick is round one, held before the director starts.</item>
/// </list>
/// </summary>
public partial class RiftHost
{
    private sealed class Round
    {
        public ushort Id;
        public bool Start;

        /// <summary>The Colossus offer: picks are "leave" or "stay" (a missing answer counts as leave).</summary>
        public bool CashOut;
        public int Level;
        public int Remaining;
        public ulong DeadlineMsec;
        public ulong OpenedMsec;

        /// <summary>Peer → the key applied for them ("" = nothing to offer). Absent = not chosen yet.</summary>
        public readonly Dictionary<long, string> Picks = new();

        /// <summary>Peer → the row their screen shows (the timer takes the first).</summary>
        public readonly Dictionary<long, List<string>> Offers = new();

        public readonly Dictionary<long, int> Reports = new();
    }

    /// <summary>Test and bench hook: every round is decided at once (the first card for everyone).</summary>
    public static bool AutoPickAll { get; set; }

    /// <summary>Set by the run for tool runs with a preset first skill: no start round.</summary>
    public bool SkipStartPick { get; set; }

    private Round? _round;
    private ushort _roundId;
    private readonly RandomNumberGenerator _roundRng = new();

    /// <summary>True while a round holds the game (tests, diagnostics).</summary>
    public bool RoundOpen => _round is not null;

    /// <summary>The round open now: id, start pick or not, chosen / total (tests).</summary>
    public (int Id, bool Start, int Picked, int Total) RoundInfo =>
        _round is { } r ? (r.Id, r.Start, r.Picks.Count, State.Players.Count) : (0, false, 0, State.Players.Count);

    /// <summary>Real seconds the last finished round was open (tests).</summary>
    public float LastRoundSeconds { get; private set; }

    /// <summary>Client messages about rounds the host refused (tests, diagnostics).</summary>
    public int RefusedPicks { get; private set; }

    /// <summary>Why the last one was refused (tests, diagnostics).</summary>
    public string LastRefusal { get; private set; } = "";

    private void Refuse(long peer, string why)
    {
        RefusedPicks++;
        LastRefusal = $"peer {peer}: {(why.Length > 120 ? why[..120] : why)}";
    }

    private void HookRounds()
    {
        _roundRng.Randomize();
        Run.LevelUp.PartyChosen += OnLocalChosen;
        Run.CashOut.PartyChosen += OnLocalCashOut;
        Director.CashOutOffered += OnCashOutOffered;
    }

    private void UnhookRounds()
    {
        Run.LevelUp.PartyChosen -= OnLocalChosen;
        Run.CashOut.PartyChosen -= OnLocalCashOut;
        Director.CashOutOffered -= OnCashOutOffered;
    }

    /// <summary>The Colossus fell and the director's hold is over: a round opens as soon as no
    /// other round holds the game.</summary>
    private bool _cashOutPending;

    private void OnCashOutOffered() => _cashOutPending = true;

    /// <summary>Tests: a shorter time for the cash-out round (null = <see cref="PartyRules.CashOutTimeout"/>).</summary>
    public static float? CashOutTimeoutOverride { get; set; }

    /// <summary>True while the Colossus offer holds the game (tests, diagnostics).</summary>
    public bool CashOutRoundOpen => _round is { CashOut: true };

    /// <summary>Each player's answer in the last cash-out round, "name=leave,name=stay" (tests).</summary>
    public string LastCashOutPicks { get; private set; } = "";

    /// <summary>Test hook: the Colossus offer, as the director raises it, without a Colossus.</summary>
    public void OpenCashOutRoundForTest() => _cashOutPending = true;

    /// <summary>What the last cash-out round decided (tests): true = the party left.</summary>
    public bool? LastCashOutLeft { get; private set; }

    /// <summary>Who kept the party in the last cash-out round, if anyone (tests).</summary>
    public string LastCashOutStayer { get; private set; } = "";

    /// <summary>Every physics frame (also while paused): opens a round when the party
    /// has a level to spend, and runs the deadline.</summary>
    private void TickRounds()
    {
        if (State.Ended)
        {
            return;
        }

        if (_round is null && _begun && State.PartyPendingLevelUps > 0)
        {
            OpenRound(start: false);
        }
        else if (_round is null && _cashOutPending)
        {
            OpenCashOutRound();
        }

        if (_round is { } r && Time.GetTicksMsec() >= r.DeadlineMsec)
        {
            TimeOut(r);
        }
    }

    private void OpenRound(bool start)
    {
        _roundId++;
        if (_roundId == 0)
        {
            _roundId = 1;
        }

        int pending = Mathf.Max(1, State.PartyPendingLevelUps);
        ulong now = Time.GetTicksMsec();
        _round = new Round
        {
            Id = _roundId,
            Start = start,
            Level = start ? 0 : State.PartyLevel - pending + 1,
            Remaining = start ? 1 : pending,
            OpenedMsec = now,
            DeadlineMsec = now + (ulong)(PartyRules.LevelUpTimeout * 1000f),
        };
        SetPaused(true);
        Run.LevelUp.OpenPartyRound(_round.Id, start, _round.Level, _round.Remaining);
        RoundChanged();
        if (AutoPickAll)
        {
            TimeOut(_round);
        }
    }

    /// <summary>
    /// The Colossus offer as a party round (docs/design/run.md -> "Co-op"): everyone answers
    /// "leave" or "stay" on the same panel, the game paused for all, with the same countdown as
    /// the cards, except the time is <see cref="PartyRules.CashOutTimeout"/>. The party leaves
    /// only if everyone says leave; nobody answering counts as leave.
    /// </summary>
    private void OpenCashOutRound()
    {
        _cashOutPending = false;
        if (State.Ended)
        {
            return;
        }

        _roundId++;
        if (_roundId == 0)
        {
            _roundId = 1;
        }

        ulong now = Time.GetTicksMsec();
        _round = new Round
        {
            Id = _roundId,
            CashOut = true,
            OpenedMsec = now,
            DeadlineMsec = now + (ulong)((CashOutTimeoutOverride ?? PartyRules.CashOutTimeout) * 1000f),
        };
        SetPaused(true);
        Run.CashOut.OpenPartyOffer(_round.Id);
        RoundChanged();
        if (AutoPickAll && _round is { } auto)
        {
            TimeOut(auto);
        }
    }

    /// <summary>The host's own answer (its panel): the host is the authority.</summary>
    private void OnLocalCashOut(int round, bool leave)
    {
        if (_round is not { CashOut: true } r || r.Id != round || r.Picks.ContainsKey(State.Local.PeerId))
        {
            return;
        }

        r.Picks[State.Local.PeerId] = leave ? "leave" : "stay";
        RoundChanged();
    }

    /// <summary>The whole simulation stops on the host: its scene tree, exactly the
    /// offline level-up pause. This node, the session and the screens keep running.</summary>
    private void SetPaused(bool paused)
    {
        if (IsInsideTree())
        {
            GetTree().Paused = paused && !State.Ended;
        }
    }

    /// <summary>The host's own choice (its screen): applied at once, the host is the authority.</summary>
    private void OnLocalChosen(int round, UpgradeOffer offer)
    {
        if (_round is not { } r || r.Id != round || r.Picks.ContainsKey(State.Local.PeerId))
        {
            return;
        }

        offer.Apply();
        r.Picks[State.Local.PeerId] = offer.Key;
        RoundChanged();
    }

    /// <summary>A client's row for the round (on open and after each reroll).</summary>
    private void ClientOffers(Link link, ushort round, List<string> keys)
    {
        long peer = link.Peer;
        if (_round is not { } r || r.Id != round || r.Picks.ContainsKey(peer))
        {
            Refuse(peer, $"row for round {round}, not open for them");
            return;
        }

        if (r.CashOut)
        {
            Refuse(peer, "a row for the cash-out round, which has a fixed one");
            return;
        }

        if (keys.Count == 0 || keys.Count > PartyRules.MaxOffersPerRow || !keys.TrueForAll(k => WellFormedKey(k, r.Start)))
        {
            Refuse(peer, $"malformed row of {keys.Count}: {string.Join(",", keys.Take(4))}");
            return;
        }

        r.Reports.TryGetValue(peer, out int reports);
        if (reports >= PartyRules.OfferReportsPerRound)
        {
            Refuse(peer, $"more than {PartyRules.OfferReportsPerRound} rows in one round");
            return;
        }

        r.Reports[peer] = reports + 1;
        r.Offers[peer] = keys;
    }

    /// <summary>A client's choice: only in the open round, once, one of the cards it
    /// reported, and only if it applies to the host's copy of that build.</summary>
    private void ClientPick(Link link, ushort round, string key)
    {
        long peer = link.Peer;
        if (_round is not { } r || r.Id != round || r.Picks.ContainsKey(peer))
        {
            Refuse(peer, $"pick for round {round}, not open for them");
            return;
        }

        if (r.CashOut)
        {
            // The row is fixed: only "leave" or "stay", once, in the open round.
            if (key is not ("leave" or "stay"))
            {
                Refuse(peer, $"'{(key.Length > 16 ? key[..16] : key)}' is not an answer to the cash-out offer");
                return;
            }

            r.Picks[peer] = key;
            RoundChanged();
            return;
        }

        string? why = !WellFormedKey(key, r.Start) ? $"malformed key '{key}'"
            : !r.Offers.TryGetValue(peer, out List<string>? row) || !row.Contains(key) ? $"'{key}' is not on their row"
            : link.Player.Build?.ApplyKey(key) != true ? $"'{key}' does not apply to their build"
            : null;
        if (why is not null)
        {
            Refuse(peer, why);
            return;
        }

        r.Picks[peer] = key;
        WriteApplied(link, r.Id, key, forced: false);
        RoundChanged();
    }

    /// <summary>Shape of a card key ("s:spark", "l:spark", "g:support:skill", "p:might:2",
    /// "k:juggernaut"); the start pick only takes skill cards.</summary>
    public static bool WellFormedKey(string key, bool start)
    {
        if (key.Length < 3 || key.Length > PartyRules.MaxOfferKeyChars || key[1] != ':')
        {
            return false;
        }

        if (start)
        {
            return key[0] is 's' or 'l';
        }

        if (key[0] == 'p')
        {
            // "p:id", or "p:id:rarity:scales" (rarity 0-2, scales 0/1).
            string[] parts = key.Split(':');
            return parts.Length == 2
                || (parts.Length is 3 or 4 && parts[2] is "0" or "1" or "2" && (parts.Length == 3 || parts[3] is "0" or "1"));
        }

        return key[0] is 's' or 'l' or 'g' or 'k';
    }

    /// <summary>The host's deadline: the first card of the row for everyone who hasn't chosen.</summary>
    private void TimeOut(Round r)
    {
        foreach (RunPlayer rp in State.Players.ToArray())
        {
            if (_round != r || r.Picks.ContainsKey(rp.PeerId))
            {
                continue;
            }

            if (r.CashOut)
            {
                // Nobody answering counts as "leave": if they are away, going home is what is
                // safe for them, and it can't drag the others along (the others still decide).
                if (rp.IsLocal)
                {
                    Run.CashOut.AnswerLeaveForTimeout();
                }

                if (_round == r && !r.Picks.ContainsKey(rp.PeerId))
                {
                    r.Picks[rp.PeerId] = "leave";
                }

                continue;
            }

            if (rp.IsLocal)
            {
                if (Run.LevelUp.PartyRound == r.Id && !Run.LevelUp.PartyChosenThisRound && Run.LevelUp.FirstOffer is { } first)
                {
                    Run.LevelUp.ChooseIndex(0); // through the screen: OnLocalChosen applies it
                    if (Run.LevelUp.PartyRound == r.Id)
                    {
                        Run.LevelUp.ShowPartyApplied(first.Title, forced: true);
                    }
                }

                if (_round == r && !r.Picks.ContainsKey(rp.PeerId))
                {
                    r.Picks[rp.PeerId] = "";
                }

                continue;
            }

            string key = ForcedKey(r, rp);
            r.Picks[rp.PeerId] = key;
            if (_links.TryGetValue(rp.PeerId, out Link? link))
            {
                WriteApplied(link, r.Id, key, forced: true);
            }
        }

        if (_round == r)
        {
            RoundChanged();
        }
    }

    /// <summary>The first card the player was shown, else the first of a row the host
    /// rolls from its copy of their build (a client that never reported one); applied here.</summary>
    private string ForcedKey(Round r, RunPlayer rp)
    {
        if (rp.Build is not { } build)
        {
            return "";
        }

        if (r.Offers.TryGetValue(rp.PeerId, out List<string>? row))
        {
            foreach (string key in row)
            {
                if (build.ApplyKey(key))
                {
                    return key;
                }
            }
        }

        List<UpgradeOffer> offers = r.Start ? build.RollStartOffers(_roundRng) : build.RollOffers(r.Level, _roundRng);
        foreach (UpgradeOffer offer in offers)
        {
            if (build.ApplyKey(offer.Key))
            {
                return offer.Key;
            }
        }

        return "";
    }

    private static void WriteApplied(Link link, ushort round, string key, bool forced)
    {
        link.Events.U8(RiftProtocol.PickApplied);
        link.Events.U16(round);
        link.Events.Str(key);
        link.Events.Bool(forced);
    }

    /// <summary>Someone chose, left or timed out: tell everyone, and close the round
    /// once every player still in the party has a card.</summary>
    private void RoundChanged()
    {
        if (_round is not { } r)
        {
            return;
        }

        bool complete = true;
        foreach (RunPlayer rp in State.Players)
        {
            complete &= r.Picks.ContainsKey(rp.PeerId);
        }

        if (complete)
        {
            FinishRound(r);
            return;
        }

        SendRoundState();
    }

    private void FinishRound(Round r)
    {
        LastRoundSeconds = (Time.GetTicksMsec() - r.OpenedMsec) / 1000f;
        _round = null;
        if (r.CashOut)
        {
            FinishCashOut(r);
            return;
        }

        Run.LevelUp.ClosePartyRound();
        if (r.Start)
        {
            Begin();
        }
        else
        {
            State.ConsumePartyLevelUp();
        }

        if (!State.Ended && State.PartyPendingLevelUps > 0)
        {
            OpenRound(start: false); // a burst of levels: the next round, still paused
            return;
        }

        if (_cashOutPending && !State.Ended)
        {
            OpenCashOutRound(); // the Colossus fell while the cards were up: now the offer, still paused
            return;
        }

        Resume();
    }

    /// <summary>The game runs again on every machine, with a second of grace for everyone.</summary>
    private void Resume()
    {
        SetPaused(false);
        foreach (RunPlayer rp in State.Players)
        {
            rp.Player?.Health?.GrantInvulnerability(PartyRules.ResumeGraceSeconds);
        }

        SendRoundState();
    }

    /// <summary>Everyone answered (or timed out): the party leaves only if everyone chose leave.</summary>
    private void FinishCashOut(Round r)
    {
        Run.CashOut.ClosePartyRound();
        RunPlayer? stayer = State.Players.FirstOrDefault(p => r.Picks.GetValueOrDefault(p.PeerId) == "stay");
        LastCashOutPicks = string.Join(",", State.Players.Select(p => $"{NameOf(p)}={r.Picks.GetValueOrDefault(p.PeerId, "leave")}"));
        LastCashOutLeft = stayer is null;
        LastCashOutStayer = stayer is null ? "" : NameOf(stayer);
        if (stayer is null)
        {
            State.EndRun(true); // victory for all; RoundRunEnded and the broadcast follow
            return;
        }

        Director.BeginEndless();
        ToastAll($"The party stays ({LastCashOutStayer} chose to keep going). You can still leave alone: Esc → Leave the party, with your reward.", new Color(1f, 0.7f, 0.4f));
        Resume();
    }

    /// <summary>The round as it stands, to every loaded client and to the host's own screen.</summary>
    private void SendRoundState()
    {
        foreach (Link link in _links.Values)
        {
            if (link.Ready)
            {
                WriteRound(link);
            }
        }

        if (_round is { } r)
        {
            if (r.CashOut)
            {
                Run.CashOut.SetPartyStatus(r.Picks.Count, State.Players.Count, r.DeadlineMsec, r.Picks.ContainsKey(State.Local.PeerId));
            }
            else
            {
                Run.LevelUp.SetPartyStatus(r.Picks.Count, State.Players.Count, r.DeadlineMsec, r.Picks.ContainsKey(State.Local.PeerId));
            }
        }
    }

    private void WriteRound(Link link)
    {
        NetWriter w = link.Events;
        w.U8(RiftProtocol.PartyRound);
        if (_round is not { } r)
        {
            w.U16(0);
            w.U8(RiftProtocol.RoundNone);
            w.U16((ushort)Mathf.Clamp(State.PartyLevel, 0, ushort.MaxValue));
            w.U8(0);
            w.U8(0);
            w.U8((byte)Mathf.Min(State.Players.Count, 255));
            w.F32(0f);
            w.Bool(false);
            return;
        }

        w.U16(r.Id);
        w.U8(r.CashOut ? RiftProtocol.RoundCashOut : r.Start ? RiftProtocol.RoundStart : RiftProtocol.RoundLevel);
        w.U16((ushort)Mathf.Clamp(r.Level, 0, ushort.MaxValue));
        w.U8((byte)Mathf.Clamp(r.Remaining, 0, 255));
        w.U8((byte)Mathf.Min(r.Picks.Count, 255));
        w.U8((byte)Mathf.Min(State.Players.Count, 255));
        // The deadline on the host clock (the batch stamps' clock): clients count down to it.
        w.F32((float)(HostTime + (r.DeadlineMsec - Mathf.Min(r.DeadlineMsec, Time.GetTicksMsec())) / 1000.0));
        w.Bool(r.Picks.ContainsKey(link.Peer));
    }

    /// <summary>A player left mid-round: they drop out of the total, and the round
    /// closes if everyone left has chosen.</summary>
    private void RoundPeerLeft(long peer)
    {
        if (_round is { } r)
        {
            r.Picks.Remove(peer);
            r.Offers.Remove(peer);
            r.Reports.Remove(peer);
        }

        // A smaller party needs less per level: the bar may be full now.
        State.CheckPartyLevel();
        RoundChanged();
    }

    /// <summary>The run ended (everyone down, or the host ended it): no more rounds.</summary>
    private void RoundRunEnded()
    {
        _round = null;
        _cashOutPending = false;
        SetPaused(false);
        Run.LevelUp.ClosePartyRound();
        Run.CashOut.ClosePartyRound();
    }
}
