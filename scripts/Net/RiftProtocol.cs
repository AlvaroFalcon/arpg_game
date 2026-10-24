#nullable enable
namespace ArpgGame.Net;

/// <summary>
/// Message ids of the co-op Survival Rift — docs/design/multiplayer.md ->
/// "Wire format". Host → client events travel in reliable batches (one per
/// physics frame per peer), snapshots unreliable at <see cref="SnapshotHz"/>,
/// client inputs unreliable every physics frame.
/// </summary>
public static class RiftProtocol
{
    public const int SnapshotHz = 20;

    /// <summary>A snapshot is split into chunks of at most this many bytes, so no
    /// unreliable packet is fragmented (one lost fragment loses the whole packet).</summary>
    public const int ChunkBytes = 1100;

    /// <summary>Monsters farther than this from a player reach that player's
    /// client at a quarter of the snapshot rate (interest management).</summary>
    public const float NearRadius = 1400f;

    /// <summary>Clients draw the world this far in the past, between two snapshots.</summary>
    public const float InterpolationDelay = 0.1f;

    // host → client events. Shared by every co-op mode (CoopHost/CoopClient): 1-16,
    // 18-22, 26-29. Survival Rift only (RiftHost/RiftClient): Progress 17, Portal 23,
    // RunBegin 24, RunEnded 25, PartyRound 30, PickApplied 31. Altered Realities:
    // RealityProtocol, 64-79 (never reused here).
    public const byte StringDef = 1;
    public const byte EnemySpawn = 2;
    public const byte EnemyGone = 3;
    public const byte PlayerLeft = 4;
    public const byte Burst = 5;
    public const byte Line = 6;
    public const byte Bullet = 7;
    public const byte Slash = 8;
    public const byte Orbit = 9;
    public const byte ProjSpawn = 10;
    public const byte ProjTurn = 11;
    public const byte ProjEnd = 12;
    public const byte PickupSpawn = 13;
    public const byte PickupGone = 14;
    public const byte Collected = 15;
    public const byte AttractAll = 16;
    public const byte Progress = 17;
    public const byte CharacterXp = 18;
    public const byte ItemDrop = 19;
    public const byte Banner = 20;
    public const byte BossSpawned = 21;
    public const byte BossInfo = 22;
    public const byte Portal = 23;
    public const byte RunBegin = 24;
    public const byte RunEnded = 25;
    public const byte DamageNumber = 26;
    public const byte Toast = 27;
    public const byte Callout = 28;
    public const byte TestPing = 29;

    /// <summary>The party's level-up round (docs/design/multiplayer.md -> "Level-ups"):
    /// round id (0 = none, the game runs), kind, level, rounds left, chosen / total,
    /// the host-clock deadline, and whether the receiver has chosen. A full state,
    /// sent whenever it changes, so a late or repeated one is harmless.</summary>
    public const byte PartyRound = 30;

    /// <summary>The card the host applied for the receiver in a round (its own pick, or the timer's).</summary>
    public const byte PickApplied = 31;

    // party round kinds
    public const byte RoundNone = 0;
    public const byte RoundStart = 1;
    public const byte RoundLevel = 2;

    /// <summary>The Colossus offer (docs/design/run.md -> "Cash-out offer and the infinite Rift"):
    /// the row is fixed, ["leave", "stay"]; a client's <see cref="ClientPick"/> carries one of those keys.</summary>
    public const byte RoundCashOut = 3;

    // client → host events (shared: ClientReady, ClientTest, ClientRevive; Rift only: ClientPick, ClientOffers)
    public const byte ClientReady = 1;
    /// <summary>A card chosen in a round: round id, key.</summary>
    public const byte ClientPick = 2;
    public const byte ClientTest = 3;

    /// <summary>The row on the client's screen in a round: round id, count, keys (sent on open and per reroll).</summary>
    public const byte ClientOffers = 4;

    /// <summary>Every co-op mode (docs/design/multiplayer.md -> "Deaths and revives"): start (u8 1)
    /// or stop (u8 0) a revive on the downed player of peer i32. The host checks range, that the
    /// sender stands and the target is down and free, and a rate limit (NetLimits).</summary>
    public const byte ClientRevive = 5;

    // snapshot player flags
    public const byte PlayerDead = 1;
    public const byte PlayerWarded = 2;
    public const byte PlayerInvulnerable = 4;

    // snapshot enemy flags (bit 0: hit since the last snapshot; bits 1-3: ailment tint code)
    public const byte EnemyHurt = 1;
}
