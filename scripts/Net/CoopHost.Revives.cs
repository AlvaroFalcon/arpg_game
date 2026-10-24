#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Run;

namespace ArpgGame.Net;

/// <summary>
/// Deaths and revives, shared by every co-op mode — docs/design/multiplayer.md ->
/// "Deaths and revives" (owner decision 2026-10-24: "When a friend dies, they get back
/// up after a 10 s delay, or an ally can revive them with a 5 s cast without moving,
/// cancellable in case they need to dodge").
/// <list type="bullet">
/// <item>A downed player gets up alone after <see cref="PartyRules.RespawnSeconds"/>.</item>
/// <item>A living ally within <see cref="PartyRules.ReviveRadius"/> holds E: the client
/// sends a start intent (<see cref="RiftProtocol.ClientRevive"/>), this host checks it
/// (in range, the reviver standing, the target down and nobody else on them, the rate
/// limit) and runs a <see cref="PartyRules.ReviveChannelSeconds"/> channel. Moving,
/// dodging, a pressed cast, losing control, going down, releasing E or walking out of
/// range stops it, and the progress is lost (<see cref="PartyRules.ChannelBreak"/>).</item>
/// <item>One reviver per downed player: a second ally's start is refused
/// (<see cref="PartyRules.ReviveStop.Taken"/>) and their prompt names who is reviving.</item>
/// </list>
/// The host decides everything; the channel, the get-up timer and who revives whom go out
/// in every snapshot (<see cref="WriteShared"/>).
/// </summary>
public abstract partial class CoopHost
{
    /// <summary>Revive intents refused (forged, out of range, rate-limited, taken; tests).</summary>
    public int RefusedRevives { get; private set; }

    /// <summary>Refused revive intents by reason (tests, diagnostics).</summary>
    public IReadOnlyDictionary<PartyRules.ReviveStop, int> ReviveRefusals => _refusals;

    private readonly Dictionary<PartyRules.ReviveStop, int> _refusals = new();

    /// <summary>Why the last revive intent was refused.</summary>
    public PartyRules.ReviveStop LastReviveRefusal { get; private set; }

    /// <summary>Why the last channel stopped before completing (tests, diagnostics).</summary>
    public PartyRules.ReviveStop LastReviveCancel { get; private set; }

    /// <summary>Revives completed by an ally since the start (tests).</summary>
    public int AllyRevives { get; private set; }

    /// <summary>Seconds the last completed ally revive took on this host's clock (tests).</summary>
    public float LastReviveSeconds { get; private set; }

    /// <summary>True holds the downed rule this step (the Rift: a level-up round pauses the game;
    /// a reality: the whole party is down and about to be brought back at the entrance).</summary>
    protected virtual bool RevivesHeld => false;

    /// <summary>After a player got up (life and grace already set): the mode's extras (a reality's mana).</summary>
    protected virtual void ModeGotUp(RunPlayer rp, bool byAlly)
    {
    }

    private readonly HashSet<RunPlayer> _castThisStep = new();
    private readonly List<(Skills.SkillUser User, Callable Call)> _castHooks = new();
    private readonly Dictionary<RunPlayer, float> _channelStarted = new();

    /// <summary>Listens to every player's casts: a pressed cast stops their revive channel.</summary>
    private void HookRevives()
    {
        foreach (RunPlayer rp in World.Players)
        {
            if (rp.Player is not { } p)
            {
                continue;
            }

            RunPlayer who = rp;
            Callable call = Callable.From((int _) => OnPlayerCast(who));
            p.SkillUser.Connect(Skills.SkillUser.SignalName.CastStarted, call);
            _castHooks.Add((p.SkillUser, call));
        }
    }

    private void UnhookRevives()
    {
        foreach ((Skills.SkillUser user, Callable call) in _castHooks)
        {
            if (IsInstanceValid(user) && user.IsConnected(Skills.SkillUser.SignalName.CastStarted, call))
            {
                user.Disconnect(Skills.SkillUser.SignalName.CastStarted, call);
            }
        }

        _castHooks.Clear();
    }

    private void OnPlayerCast(RunPlayer rp)
    {
        // The Survival Rift casts by itself on a timer: that is not the player acting, and
        // stopping the channel for it would make reviving impossible there.
        if (rp.Player?.GetNodeOrNull<PlayerSkillInput>("PlayerSkillInput") is { } input
            && (input.Mode == PlayerSkillInput.CastMode.Survival || input.ForceAutoCast))
        {
            return;
        }

        _castThisStep.Add(rp);
    }

    /// <summary>The host's own player pressed or released E next to a downed ally.</summary>
    public PartyRules.ReviveStop LocalReviveIntent(bool start, long targetPeer)
    {
        RunPlayer? me = null;
        foreach (RunPlayer rp in World.Players)
        {
            if (rp.IsLocal)
            {
                me = rp;
            }
        }

        if (me is null)
        {
            return PartyRules.ReviveStop.Unknown;
        }

        if (!start)
        {
            StopChannelOf(me, PartyRules.ReviveStop.Released);
            return PartyRules.ReviveStop.None;
        }

        return StartRevive(me, targetPeer);
    }

    /// <summary>A client's intent (<see cref="RiftProtocol.ClientRevive"/>): rate-limited, then checked.</summary>
    private void OnClientRevive(Link link, bool start, long targetPeer)
    {
        if (!link.ReviveBudget.Spend(Time.GetTicksMsec() / 1000.0))
        {
            Refuse(PartyRules.ReviveStop.RateLimited);
            return;
        }

        if (!start)
        {
            StopChannelOf(link.Player, PartyRules.ReviveStop.Released);
            return;
        }

        StartRevive(link.Player, targetPeer);
    }

    private PartyRules.ReviveStop Refuse(PartyRules.ReviveStop why)
    {
        RefusedRevives++;
        LastReviveRefusal = why;
        _refusals[why] = _refusals.GetValueOrDefault(why) + 1;
        return why;
    }

    private PartyRules.ReviveStop StartRevive(RunPlayer reviver, long targetPeer)
    {
        if (World.Ended || RevivesHeld)
        {
            return Refuse(PartyRules.ReviveStop.Unknown);
        }

        RunPlayer? target = null;
        foreach (RunPlayer rp in World.Players)
        {
            if (rp.PeerId == targetPeer)
            {
                target = rp;
            }
        }

        if (target?.Player is not { } downed || reviver.Player is not { } p)
        {
            return Refuse(PartyRules.ReviveStop.Unknown);
        }

        if (target == reviver)
        {
            return Refuse(PartyRules.ReviveStop.Self);
        }

        if (target.Reviver == reviver)
        {
            return PartyRules.ReviveStop.None; // already channelling on them: progress kept
        }

        if (target.Reviver is { } other && other != reviver && World.Players.Contains(other))
        {
            return Refuse(PartyRules.ReviveStop.Taken);
        }

        PartyRules.ReviveStop why = PartyRules.ChannelBreak(reviver.Downed, target.Downed, p.LastMoveDir != Vector2.Zero,
            p.Dodge is { IsDashing: true }, false, Stunned(p), p.GlobalPosition.DistanceTo(downed.GlobalPosition));
        if (why != PartyRules.ReviveStop.None)
        {
            return Refuse(why);
        }

        StopChannelOf(reviver, PartyRules.ReviveStop.Released); // switching to another downed ally
        target.Reviver = reviver;
        target.ReviveChannel = 0f;
        p.RevivingTarget = downed;
        _castThisStep.Remove(reviver);
        _channelStarted[target] = HostTime;
        return PartyRules.ReviveStop.None;
    }

    private static bool Stunned(Player p) => p.Warded || !p.ControlsEnabled;

    /// <summary>Stops whatever channel <paramref name="reviver"/> is on.</summary>
    private void StopChannelOf(RunPlayer reviver, PartyRules.ReviveStop why)
    {
        foreach (RunPlayer rp in World.Players)
        {
            if (rp.Reviver == reviver)
            {
                StopChannel(rp, why);
            }
        }

        if (reviver.Player is { } p)
        {
            p.RevivingTarget = null;
        }
    }

    private void StopChannel(RunPlayer downed, PartyRules.ReviveStop why)
    {
        if (downed.Reviver?.Player is { } r && IsInstanceValid(r))
        {
            r.RevivingTarget = null;
        }

        downed.Reviver = null;
        downed.ReviveChannel = 0f;
        _channelStarted.Remove(downed);
        LastReviveCancel = why;
    }

    /// <summary>One step of the downed rule for every player (after the inputs are applied,
    /// so a reviver's movement and dodge this frame are already known).</summary>
    private void StepRevives(float dt)
    {
        if (World.Ended || RevivesHeld)
        {
            _castThisStep.Clear();
            return;
        }

        foreach (RunPlayer rp in World.Players)
        {
            if (rp.Player is not { } downed || !IsInstanceValid(downed))
            {
                continue;
            }

            if (!rp.Downed)
            {
                if (rp.Reviver is not null)
                {
                    StopChannel(rp, PartyRules.ReviveStop.TargetUp);
                }

                rp.DownedFor = 0f;
                downed.ReviveProgress = 0f;
                downed.GetUpProgress = 0f;
                continue;
            }

            downed.RevivingTarget = null; // the downed revive nobody
            bool channelling = false;
            if (rp.Reviver is { } reviver)
            {
                PartyRules.ReviveStop why = !World.Players.Contains(reviver) || reviver.Player is not { } r || !IsInstanceValid(r)
                    ? PartyRules.ReviveStop.Left
                    : PartyRules.ChannelBreak(reviver.Downed, true, r.LastMoveDir != Vector2.Zero, r.Dodge is { IsDashing: true },
                        _castThisStep.Contains(reviver), Stunned(r), r.GlobalPosition.DistanceTo(downed.GlobalPosition));
                if (why == PartyRules.ReviveStop.None)
                {
                    channelling = true;
                }
                else
                {
                    StopChannel(rp, why);
                }
            }

            (rp.DownedFor, rp.ReviveChannel, PartyRules.ReviveOutcome outcome) = PartyRules.StepDowned(rp.DownedFor, rp.ReviveChannel, channelling, dt);
            downed.ReviveProgress = rp.ReviveChannel;
            downed.GetUpProgress = rp.DownedFor / PartyRules.RespawnSeconds;
            if (outcome != PartyRules.ReviveOutcome.StillDown)
            {
                GetUp(rp, outcome == PartyRules.ReviveOutcome.RevivedByAlly);
            }
        }

        _castThisStep.Clear();
    }

    private void GetUp(RunPlayer rp, bool byAlly)
    {
        Player p = rp.Player!;
        RunPlayer? reviver = rp.Reviver;
        if (byAlly)
        {
            AllyRevives++;
            LastReviveSeconds = _channelStarted.TryGetValue(rp, out float started) ? HostTime - started : 0f;
        }

        StopChannel(rp, PartyRules.ReviveStop.None);
        LastReviveCancel = PartyRules.ReviveStop.None;
        p.ReviveProgress = 0f;
        p.GetUpProgress = 0f;
        p.Health!.Revive(byAlly ? PartyRules.ReviveLife : PartyRules.RespawnLife);
        p.Health.GrantInvulnerability(PartyRules.ReviveGrace);
        ModeGotUp(rp, byAlly);
        ToastAll(byAlly && reviver is not null ? $"{NameOf(reviver)} revived {NameOf(rp)}!" : $"{NameOf(rp)} gets back up!", new Color(0.5f, 1f, 0.6f));
    }

    /// <summary>A player is leaving the party: whatever channel they were part of stops.</summary>
    private void RevivesPeerLeft(RunPlayer leaving)
    {
        StopChannelOf(leaving, PartyRules.ReviveStop.Left);
        if (leaving.Reviver is not null)
        {
            StopChannel(leaving, PartyRules.ReviveStop.Left);
        }
    }

    /// <summary>The index in the snapshot's player list of the downed player <paramref name="rp"/>
    /// is reviving, or 255.</summary>
    private byte RevivingIndex(RunPlayer rp)
    {
        IReadOnlyList<RunPlayer> players = World.Players;
        for (int i = 0; i < players.Count && i < 255; i++)
        {
            if (players[i].Reviver == rp)
            {
                return (byte)i;
            }
        }

        return 255;
    }
}
