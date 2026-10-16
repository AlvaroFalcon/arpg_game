#nullable enable
using Godot;

namespace ArpgGame.Net;

/// <summary>
/// Every co-op number in one place — docs/design/multiplayer.md -> "Party rules".
/// A party of one gets exactly 1 from every function, so single-player is
/// untouched. These are proposals for the owner to tune, not settled balance.
///
/// <code>
/// players  spawn rate  enemy cap  monster life  boss life  xp/loot share
///    1        ×1.00      ×1.00       ×1.00        ×1.00       ×1.00
///    2        ×1.50      ×1.15       ×1.50        ×1.70       ×0.67
///    3        ×2.00      ×1.30       ×2.00        ×2.40       ×0.50
///    4        ×2.50      ×1.45       ×2.50        ×3.10       ×0.40
/// </code>
///
/// More enemies and tougher enemies, because two to four builds kill much faster
/// than one. The enemy cap grows slowly on purpose: the host simulates every
/// monster, and a first table (cap ×2.05 for four) put 535 monsters on the
/// host, ~4× its solo physics cost (docs/design/multiplayer.md -> "Performance").
/// The extra pressure comes from spawn rate and monster life instead. Loot is instanced (every player rolls their own drops from every
/// kill), so the share brings each player's experience and drop rate back to
/// about what they would get alone: the party is not a loot multiplier.
/// </summary>
public static class PartyRules
{
    public const int MaxPlayers = 4;

    /// <summary>Instanced loot (each player sees and collects only their own drops).
    /// The one switch for a future shared-loot mode: when false, drops would be
    /// rolled once per kill and visible to everyone (not implemented yet — see
    /// docs/design/multiplayer.md -> "Shared loot later").</summary>
    public const bool InstancedLoot = true;

    private static int Extra(int players) => Mathf.Clamp(players, 1, MaxPlayers) - 1;

    /// <summary>Multiplier on the director's spawn budget rate.</summary>
    public static float SpawnRate(int players) => 1f + 0.5f * Extra(players);

    /// <summary>Multiplier on how many enemies may be alive at once.</summary>
    public static float EnemyCap(int players) => 1f + 0.15f * Extra(players);

    /// <summary>Multiplier on ordinary (and elite) monster life.</summary>
    public static float MonsterLife(int players) => 1f + 0.5f * Extra(players);

    /// <summary>Multiplier on boss life: everybody focuses the boss.</summary>
    public static float BossLife(int players) => 1f + 0.7f * Extra(players);

    /// <summary>Each player's share of a kill's experience and of the chance-based
    /// drops of ordinary monsters (1 / spawn rate, so levelling pace and loot per
    /// minute stay close to solo). Elite and boss drops are not shared down.</summary>
    public static float Share(int players) => 1f / SpawnRate(players);

    // --- deaths ------------------------------------------------------------------

    /// <summary>How close (px) a living ally must stand to revive a downed player.</summary>
    public const float ReviveRadius = 70f;

    /// <summary>Seconds of an ally standing next to a downed player to revive them.</summary>
    public const float ReviveTime = 3f;

    /// <summary>Life a revived player comes back with (fraction of max).</summary>
    public const float ReviveLife = 0.4f;

    /// <summary>Invulnerability after a revive (seconds).</summary>
    public const float ReviveGrace = 2f;

    // --- level-ups ---------------------------------------------------------------

    /// <summary>Co-op never pauses: a player choosing a card is warded (cannot be
    /// hurt, cannot act, monsters ignore them) for at most this long, then the
    /// first card is taken for them.</summary>
    public const float LevelUpTimeout = 20f;
}
