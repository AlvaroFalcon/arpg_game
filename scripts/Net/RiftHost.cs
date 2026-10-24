#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using ArpgGame.Run;

namespace ArpgGame.Net;

/// <summary>
/// The host's half of the co-op Survival Rift — docs/design/multiplayer.md.
/// The shared replication (snapshots, events, inputs, instanced loot, bosses)
/// lives in <see cref="CoopHost"/>; this adds what only the Rift has: the run's
/// numbers in every snapshot (time, kills, elites, bosses, surge/Colossus flags),
/// the party's run level, the director's banners and portal, the party's level-up
/// rounds (RiftHost.Rounds.cs: one bar, everyone picks, the game paused) and the
/// begin-when-everyone-has-loaded handshake. Deaths and revives are shared (CoopHost.Revives).
/// </summary>
public partial class RiftHost : CoopHost
{
    public RunController Run { get; set; } = null!;

    private RunState State => Run.State;
    private RunDirector Director => Run.Director;

    protected override ICoopWorld World => State;

    private bool _begun;
    private Action? _onBegin;
    private float _waiting;

    /// <summary>Seconds the host waits for slow loaders before starting anyway.</summary>
    public const float LoadTimeout = 20f;

    protected override void ModeReady()
    {
        State.RunEnded += OnRunEnded;
        State.Levelled += OnLevelled;
        Director.Announced += OnAnnounced;
        Director.BossSpawned += AdoptBoss;
        Director.PortalOpened += OnPortalOpened;
        HookRounds();
    }

    protected override void ModeExit()
    {
        State.RunEnded -= OnRunEnded;
        State.Levelled -= OnLevelled;
        Director.Announced -= OnAnnounced;
        Director.BossSpawned -= AdoptBoss;
        Director.PortalOpened -= OnPortalOpened;
        UnhookRounds();
    }

    /// <summary>Holds the clock and the director until every client has loaded
    /// (or <see cref="LoadTimeout"/> passes), then runs <paramref name="begin"/>.</summary>
    public void BeginWhenReady(Action begin)
    {
        _onBegin = begin;
        State.ClockRunning = false;
    }

    // --- the frame ------------------------------------------------------------------

    protected override void ModePreTick(float dt)
    {
        if (!_begun && _onBegin is not null && _round is null)
        {
            _waiting += dt;
            if (ReadyPeers == _links.Count || _waiting >= LoadTimeout)
            {
                // Everyone is here: the run-start skill pick for the whole party
                // (docs/design/progression.md -> "The start pick"), then the run.
                if (SkipStartPick)
                {
                    Begin();
                }
                else
                {
                    OpenRound(start: true);
                }
            }
        }

        TickRounds();
    }

    protected override bool HoldInputs => _round is not null;

    /// <summary>A level-up round pauses the game: nobody gets up meanwhile (docs/design/multiplayer.md -> "Level-ups").</summary>
    protected override bool RevivesHeld => _round is not null;

    /// <summary>The run starts: clock, director, and every client told.</summary>
    private void Begin()
    {
        if (_begun)
        {
            return;
        }

        _begun = true;
        State.ClockRunning = true;
        _onBegin?.Invoke();
        Broadcast(w => w.U8(RiftProtocol.RunBegin));
    }

    /// <summary>The run's level and bar for this player (the party's, mirrored on every <see cref="RunPlayer"/>).</summary>
    protected override void ModeProgress(Link link, float dt)
    {
        RunPlayer rp = link.Player;
        link.ProgressTimer -= dt;
        bool changed = rp.Level != link.SentLevel || !Mathf.IsEqualApprox(rp.Experience, link.SentExperience);
        if (rp.UnreportedLevelUps > 0 || (changed && link.ProgressTimer <= 0f))
        {
            link.ProgressTimer = 0.1f;
            link.SentLevel = rp.Level;
            link.SentExperience = rp.Experience;
            int levels = rp.UnreportedLevelUps;
            rp.UnreportedLevelUps = 0;
            link.Events.U8(RiftProtocol.Progress);
            link.Events.U16((ushort)rp.Level);
            link.Events.F32(rp.Experience);
            link.Events.U8((byte)Mathf.Min(levels, 255));
        }
    }

    /// <summary>The run's numbers at the head of every snapshot.</summary>
    protected override void WriteModeShared(NetWriter w, Link link)
    {
        w.F32(State.Elapsed);
        w.I32(State.Kills);
        w.U16((ushort)Mathf.Min(State.ElitesKilled, ushort.MaxValue));
        w.U16((ushort)Mathf.Min(State.BossesKilled, ushort.MaxValue));
        w.U8((byte)((Director.Surging ? 1 : 0) | (Director.ColossusDefeated ? 2 : 0)));
    }

    // --- run events ------------------------------------------------------------------

    private void OnAnnounced(string title, string subtitle, Color color) => Broadcast(w =>
    {
        w.U8(RiftProtocol.Banner);
        w.Str(title);
        w.Str(subtitle);
        w.Colour(color);
    });

    private void OnPortalOpened(Vector2 at) => Broadcast(w =>
    {
        w.U8(RiftProtocol.Portal);
        w.Pos(at);
    });

    private static void OnLevelled(RunPlayer who, int levels)
    {
        if (!who.IsLocal)
        {
            who.UnreportedLevelUps += levels;
        }
    }

    private void OnRunEnded(bool victory)
    {
        RoundRunEnded();
        // The party lives on (docs/design/multiplayer.md -> "Party session"): the router
        // port stays open for friends joining the hideout; the phase says the rift is over.
        _session.OnHostRunEnded();
        Broadcast(w =>
        {
            w.U8(RiftProtocol.RunEnded);
            w.Bool(victory);
        });
        FlushEvents();
    }

    // --- from clients --------------------------------------------------------------------

    protected override bool OnModeClientEvent(Link link, byte type, NetReader r)
    {
        switch (type)
        {
            case RiftProtocol.ClientPick:
            {
                // Checked against the open round (RiftHost.Rounds): a stray, repeated,
                // unoffered or malformed pick is ignored and the timer decides.
                ushort round = r.U16();
                string key = r.Str();
                if (link.Ready)
                {
                    ClientPick(link, round, key);
                }

                break;
            }

            case RiftProtocol.ClientOffers:
            {
                ushort round = r.U16();
                int count = r.U8();
                var keys = new List<string>(Math.Min(count, PartyRules.MaxOffersPerRow + 1));
                for (int i = 0; i < count && r.More; i++)
                {
                    string key = r.Str();
                    if (keys.Count <= PartyRules.MaxOffersPerRow)
                    {
                        keys.Add(key);
                    }
                }

                if (link.Ready && keys.Count == count)
                {
                    ClientOffers(link, round, keys);
                }
                else
                {
                    RefusedPicks++;
                }

                break;
            }

            default:
                return false;
        }

        return true;
    }

    /// <summary>A client loaded late: the run may have begun and a round may be open.</summary>
    protected override void CatchUp(Link link)
    {
        if (_begun)
        {
            link.Events.U8(RiftProtocol.RunBegin);
        }

        if (_round is not null)
        {
            WriteRound(link); // a round already open (the load timeout passed): it picks too
        }
    }

    /// <summary>A client's player left the run.</summary>
    protected override void ModePeerLeft(long peer)
    {
        if (!State.Ended && State.AllDown)
        {
            State.EndRun(Director.ColossusDefeated);
        }
        else
        {
            RoundPeerLeft(peer);
        }
    }
}
