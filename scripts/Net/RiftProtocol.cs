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

    // host → client events
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

    // client → host events
    public const byte ClientReady = 1;
    public const byte ClientPick = 2;
    public const byte ClientTest = 3;

    // snapshot player flags
    public const byte PlayerDead = 1;
    public const byte PlayerWarded = 2;
    public const byte PlayerInvulnerable = 4;

    // snapshot enemy flags (bit 0: hit since the last snapshot; bits 1-3: ailment tint code)
    public const byte EnemyHurt = 1;
}
