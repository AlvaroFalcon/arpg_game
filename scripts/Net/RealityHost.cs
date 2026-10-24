#nullable enable
using Godot;
using ArpgGame.Run;
using ArpgGame.World;

namespace ArpgGame.Net;

/// <summary>
/// The host's half of a co-op Altered Reality — docs/design/multiplayer.md ->
/// "Co-op realities". The shared replication (<see cref="CoopHost"/>) carries the
/// monsters, effects, players and instanced drops; this adds the reality's
/// numbers to every snapshot (kills, monsters, the shared portal pool, the boss
/// fight, the receiver's mana: realities cast with mana), gem drops, and the
/// reality's own moments (<see cref="RealityProtocol"/>): teleports, the gate,
/// the clear, the exit portal and the end. The <see cref="RealityController"/>
/// decides; this only tells the clients.
/// </summary>
public partial class RealityHost : CoopHost
{
    public RealityController Reality { get; set; } = null!;

    protected override ICoopWorld World => Reality.Party;

    /// <summary>The whole party is down: nobody gets up in place, the wipe brings everyone back at the entrance.</summary>
    protected override bool RevivesHeld => Reality.WipePending;

    /// <summary>Realities cast with mana: up with at least the same share of it as of life
    /// (docs/design/multiplayer.md -> "Deaths and revives"; the old respawn refilled it).</summary>
    protected override void ModeGotUp(RunPlayer rp, bool byAlly)
    {
        if (rp.Player?.Stats.Mana is { } mana)
        {
            mana.Current = Mathf.Max(mana.Current, mana.Max * PartyRules.RespawnLife);
        }
    }

    /// <summary>A reality's packs sleep until someone comes near: far sleeping ones aren't re-sent.</summary>
    protected override bool SkipFarDormant => true;

    protected override void WriteModeShared(NetWriter w, Link link)
    {
        RealityController r = Reality;
        w.U16((ushort)Mathf.Clamp(r.Kills, 0, ushort.MaxValue));
        w.U16((ushort)Mathf.Clamp(r.MonstersTotal, 0, ushort.MaxValue));
        w.U8((byte)Mathf.Clamp(r.PortalsLeft, 0, 255));
        w.U8((byte)((r.BossFightActive ? RealityProtocol.FlagBossFight : 0) | (r.Completed ? RealityProtocol.FlagCompleted : 0) | (r.Collapsed ? RealityProtocol.FlagCollapsed : 0)));
        float mana = link.Player.Player?.Stats.Mana?.Current ?? 0f;
        w.U16((ushort)Mathf.Clamp(Mathf.RoundToInt(mana), 0, ushort.MaxValue));
    }

    protected override string PickupKey(Pickup pickup) => pickup.Kind == PickupKind.Gem ? pickup.GemId : pickup.CurrencyKey;

    protected override void WritePickupExtra(Link link, Pickup pickup)
    {
        if (pickup.Kind == PickupKind.Gem)
        {
            link.Events.U8(RealityProtocol.PickupExtra);
            link.Events.U32(pickup.NetId);
            link.Events.Bool(pickup.GemSupport);
        }
    }

    /// <summary>A client loaded: which replica is the boss, the gate and the exit portal as they are now.</summary>
    protected override void CatchUp(Link link)
    {
        // The boss was spawned before anyone had loaded: say which replica it is.
        if (Reality.Boss is { NetId: > 0 } boss && IsInstanceValid(boss))
        {
            link.Events.U8(RiftProtocol.BossSpawned);
            link.Events.U16(boss.NetId);
        }

        if (Reality.BossFightActive)
        {
            link.Events.U8(RealityProtocol.GateState);
            link.Events.Bool(true);
        }

        if (Reality.ExitPortal is { } portal)
        {
            link.Events.U8(RealityProtocol.ExitPortal);
            link.Events.Pos(portal.GlobalPosition);
        }
    }

    /// <summary>The host moved <paramref name="peer"/>'s player to <paramref name="at"/>.</summary>
    public void Teleport(long peer, Vector2 at)
    {
        if (LinkByPeer(peer) is { } link)
        {
            link.Events.U8(RealityProtocol.Teleport);
            link.Events.Pos(at);
        }
    }

    /// <summary>A banner on one client's screen (its own fall).</summary>
    public void BannerTo(long peer, string title, string subtitle, Color color)
    {
        if (LinkByPeer(peer) is { } link)
        {
            link.Events.U8(RiftProtocol.Banner);
            link.Events.Str(title);
            link.Events.Str(subtitle);
            link.Events.Colour(color);
        }
    }

    /// <summary>A banner on every client (the host shows its own).</summary>
    public void BannerAll(string title, string subtitle, Color color) => Broadcast(w =>
    {
        w.U8(RiftProtocol.Banner);
        w.Str(title);
        w.Str(subtitle);
        w.Colour(color);
    });

    public void Gate(bool closed) => Broadcast(w =>
    {
        w.U8(RealityProtocol.GateState);
        w.Bool(closed);
    });

    public void Cleared(int tier) => Broadcast(w =>
    {
        w.U8(RealityProtocol.RealityCleared);
        w.U8((byte)Mathf.Clamp(tier, 1, RealityTiers.MaxTier));
    });

    public void Exit(Vector2 at) => Broadcast(w =>
    {
        w.U8(RealityProtocol.ExitPortal);
        w.Pos(at);
    });

    /// <summary>The reality is over for everyone (sent at once: the host may leave the scene next).</summary>
    public void End(byte reason)
    {
        Broadcast(w =>
        {
            w.U8(RealityProtocol.RealityEnd);
            w.U8(reason);
        });
        FlushEvents();
    }

    /// <summary>The reality's boss is tracked like the Rift's (phase, attack, callouts).</summary>
    public void AdoptBossNow(Enemies.BossEnemy boss) => AdoptBoss(boss);

    /// <summary>A participant went home (or dropped): the controller re-checks the boss fight.</summary>
    protected override void ModePeerLeft(long peer) => Reality.OnParticipantLeft();
}
