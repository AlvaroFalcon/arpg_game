#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using ArpgGame.Enemies;
using ArpgGame.Run;

namespace ArpgGame.Net;

/// <summary>
/// A co-op client's half of the Survival Rift — docs/design/multiplayer.md.
/// The shared replication (prediction, replicas, replayed effects, drops) lives
/// in <see cref="CoopClient"/>; this adds what only the Rift has: the run's
/// numbers mirrored from every snapshot, the run level, the director's banners,
/// boss and portal, the party's level-up and cash-out rounds, and the run's end.
/// </summary>
public partial class RiftClient : CoopClient
{
    public RunController Run { get; set; } = null!;

    private RunState State => Run.State;
    private RunDirector Director => Run.Director;

    protected override Player Local => Run.Player;

    protected override ICoopWorld World => State;

    protected override void ModeReady()
    {
        State.Mirror = true;
        State.ClockRunning = false;
    }

    protected override bool Choosing => Run.LevelUp.IsOpen || Run.CashOut.IsOpen;

    /// <summary>Tells the host which card this player took in level-up round <paramref name="round"/>.</summary>
    public void SendPick(int round, string key)
    {
        _events.Clear();
        _events.U8(RiftProtocol.ClientPick);
        _events.U16((ushort)round);
        _events.Str(key);
        _session.SendClientEvents(_events.ToArray());
    }

    /// <summary>Tells the host the row on screen (opened, or one card rerolled), so its
    /// timer takes a card this player was really shown.</summary>
    public void SendOffers(int round, IReadOnlyList<Progression.UpgradeOffer> offers)
    {
        _events.Clear();
        _events.U8(RiftProtocol.ClientOffers);
        _events.U16((ushort)round);
        int count = Math.Min(offers.Count, PartyRules.MaxOffersPerRow);
        _events.U8((byte)count);
        for (int i = 0; i < count; i++)
        {
            _events.Str(offers[i].Key);
        }

        _session.SendClientEvents(_events.ToArray());
    }

    /// <summary>The last round the host applied a card for this player (each is applied once).</summary>
    private int _appliedRound = -1;

    /// <summary>
    /// The party's level-up round from the host (docs/design/multiplayer.md ->
    /// "Level-ups"): pause this machine's game and show this player's row, the
    /// "chosen / total" line and the host's countdown; or close it and resume.
    /// </summary>
    private void ApplyRound(int id, byte kind, int level, int remaining, int picked, int total, double hostDeadline, bool mine)
    {
        if (id == 0 || kind == RiftProtocol.RoundNone)
        {
            Run.LevelUp.ClosePartyRound();
            Run.CashOut.ClosePartyRound();
            if (!State.Ended)
            {
                GetTree().Paused = false;
            }

            return;
        }

        if (State.Ended || (kind != RiftProtocol.RoundStart && kind != RiftProtocol.RoundLevel && kind != RiftProtocol.RoundCashOut))
        {
            return;
        }

        GetTree().Paused = true;
        if (kind == RiftProtocol.RoundCashOut)
        {
            // The Colossus offer: the same panel as on the host, with the host's countdown.
            if (Run.CashOut.PartyRound != id)
            {
                Run.CashOut.OpenPartyOffer(id);
            }

            double now = HostNow;
            float timeLeft = Sane((float)(hostDeadline - now), 0f, 60f);
            Run.CashOut.SetPartyStatus(picked, total, Time.GetTicksMsec() + (ulong)(timeLeft * 1000f), mine);
            return;
        }

        if (Run.LevelUp.PartyRound != id)
        {
            Run.LevelUp.OpenPartyRound(id, kind == RiftProtocol.RoundStart, level, remaining);
        }

        // The host's deadline on this machine's clock: when a packet the host sends
        // at its deadline would arrive here (the clock offset, else this batch's stamp).
        double hostNow = HostNow;
        float left = Sane((float)(hostDeadline - hostNow), 0f, 60f);
        ulong deadline = Time.GetTicksMsec() + (ulong)(left * 1000f);
        Run.LevelUp.SetPartyStatus(picked, total, deadline, mine);
    }

    /// <summary>The host applied <paramref name="key"/> for this player in round <paramref name="id"/>: the same card here.</summary>
    private void ApplyPick(int id, string key, bool forced)
    {
        if (id == _appliedRound)
        {
            return;
        }

        _appliedRound = id;
        string title = "a card";
        foreach (Progression.UpgradeOffer offer in Run.LevelUp.Offers)
        {
            if (offer.Key == key)
            {
                title = offer.Title;
            }
        }

        if (key.Length > 0 && key.Length <= PartyRules.MaxOfferKeyChars)
        {
            Run.Build.ApplyKey(key);
        }

        if (forced && Run.LevelUp.PartyRound == id)
        {
            if (Run.LevelUp.FirstOffer is { } first && first.Key == key)
            {
                title = first.Title;
            }

            Run.LevelUp.ShowPartyApplied(title, forced: true);
        }
    }

    protected override void ReadModeShared(NetReader r, double hostTime)
    {
        // These numbers become this player's rewards and best time in their profile:
        // keep them finite and within a day of play.
        float elapsed = Sane(r.F32(), 0f, 86_400f);
        int kills = Math.Max(0, r.I32());
        int elites = r.U16();
        int bosses = r.U16();
        byte flags = r.U8();
        State.ApplyShared(elapsed, kills, elites, bosses);
        Director.ApplyRemoteFlags((flags & 1) != 0, (flags & 2) != 0);
    }

    protected override void Announce(string title, string subtitle, Color color) => Director.Announce(title, subtitle, color);

    protected override void BossReplicaSpawned(Enemy boss) => Director.NotifyBossSpawned(boss);

    protected override bool ApplyModeEvent(byte type, NetReader r)
    {
        switch (type)
        {
            case RiftProtocol.Progress:
            {
                int level = Math.Clamp((int)r.U16(), 1, 999);
                float xp = Sane(r.F32(), 0f, 1e12f);
                int levels = r.U8();
                State.ApplyLocalProgress(level, xp, levels);
                return true;
            }

            case RiftProtocol.Portal:
                Director.ShowRemotePortal(r.Pos());
                return true;
            case RiftProtocol.RunBegin:
                State.ClockRunning = true;
                return true;
            case RiftProtocol.RunEnded:
                State.EndRun(r.Bool());
                return true;
            case RiftProtocol.PartyRound:
            {
                int id = r.U16();
                byte kind = r.U8();
                int level = Math.Clamp((int)r.U16(), 0, 999);
                int remaining = Math.Clamp((int)r.U8(), 0, 99);
                int picked = Math.Clamp((int)r.U8(), 0, PartyRules.MaxPlayers);
                int total = Math.Clamp((int)r.U8(), 1, PartyRules.MaxPlayers);
                float deadline = r.F32();
                bool mine = r.Bool();
                ApplyRound(id, kind, level, remaining, picked, total, float.IsFinite(deadline) ? deadline : 0.0, mine);
                return true;
            }

            case RiftProtocol.PickApplied:
            {
                int id = r.U16();
                string key = r.Str();
                bool forced = r.Bool();
                ApplyPick(id, key, forced);
                return true;
            }
        }

        return false;
    }

    /// <summary>The host credited something to this player: into the local wallet
    /// (saved with this machine's profile at the end), with the usual sounds.</summary>
    protected override void Collected(PickupKind kind, float value, string key, int detail)
    {
        switch (kind)
        {
            case PickupKind.Experience:
                Core.Sfx.Play(this, "pickup", -10f);
                break;
            case PickupKind.Life:
                Core.Sfx.Play(this, "pickup");
                break;
            case PickupKind.Gold:
                Core.Sfx.Play(this, "pickup");
                Local.Wallet.Add(Core.Profile.GoldId, (long)value);
                UI.LootFeed.Gold(this, (long)value);
                State.ApplyLocalCollected(kind, (long)value);
                break;
            case PickupKind.Currency when Items.Crafting.Find(key) is { } currency:
                Core.Sfx.Play(this, "pickup", -2f);
                Local.Wallet.Add(currency.Id, 1);
                State.ApplyLocalCollected(kind, 1);
                UI.LootFeed.Currency(this, currency.Id, 1);
                break;
            case PickupKind.Nuke:
                Core.Sfx.Play(this, "boss_warn");
                ScreenFlash.Spawn(this, new Color(1f, 0.55f, 0.3f));
                State.ApplyLocalCollected(kind, 1);
                State.ShowToast($"Cataclysm — {detail} slain", new Color(1f, 0.45f, 0.3f));
                break;
            case PickupKind.Magnet:
                Core.Sfx.Play(this, "levelup", -6f);
                ScreenFlash.Spawn(this, new Color(0.45f, 0.75f, 1f), 0.25f);
                State.ShowToast("Lodestone — everything comes to you", new Color(0.55f, 0.8f, 1f));
                break;
        }
    }

    /// <summary>The host is gone (docs/design/multiplayer.md -> "Disconnects"):
    /// the run ends here and now, with this player's rewards for what they did.</summary>
    public override void OnHostLost(string reason)
    {
        // Whatever the host already sent still counts.
        FlushQueuedEvents();
        if (!State.Ended)
        {
            Director.Announce("The host left", reason, new Color(1f, 0.6f, 0.4f));
            State.EndRun(Director.ColossusDefeated);
        }
    }
}
