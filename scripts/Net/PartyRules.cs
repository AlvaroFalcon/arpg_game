#nullable enable
using Godot;

namespace ArpgGame.Net;

/// <summary>
/// Every co-op number in one place — docs/design/multiplayer.md -> "Party rules".
/// A party of one gets exactly 1 from every function, so single-player is
/// untouched. These are proposals for the owner to tune, not settled balance.
///
/// <code>
/// players  spawn rate  enemy cap  monster life  boss life  xp/loot share  party xp to level
///    1        ×1.00      ×1.00       ×1.00        ×1.00       ×1.00            ×1
///    2        ×1.50      ×1.15       ×1.50        ×1.70       ×0.67            ×2
///    3        ×2.00      ×1.30       ×2.00        ×2.40       ×0.50            ×3
///    4        ×2.50      ×1.45       ×2.50        ×3.10       ×0.40            ×4
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

    // --- level-ups (docs/design/multiplayer.md -> "Level-ups") ----------------------

    /// <summary>
    /// Co-op experience is one party bar: every player's gems fill it, and it needs
    /// this many times the solo amount per level. Each player still collects their
    /// own gems, worth <see cref="Share"/> of a solo gem, from a party that kills
    /// about <see cref="SpawnRate"/> times as much, so the bar gains
    /// players × share × spawn rate = players × the solo rate. Scaling the need by
    /// the party size keeps levelling at about the solo pace (some twenty level-ups
    /// in a full run), so the party does not pause more often than one player would.
    /// </summary>
    public static float PartyExperience(int players) => Mathf.Clamp(players, 1, MaxPlayers);

    /// <summary>Co-op: seconds every player has to choose a card (the host's clock).
    /// Then the first card of the row is taken for whoever has not chosen.</summary>
    public const float LevelUpTimeout = 10f;

    /// <summary>The countdown turns red (and the tick sharper) from this many seconds.</summary>
    public const int LevelUpUrgentSeconds = 5;

    /// <summary>Host limit: how many times one player may report their row in one
    /// round (the first row, then one report per reroll — two per run — plus slack).</summary>
    public const int OfferReportsPerRound = 6;

    /// <summary>Host limit: cards in a reported row (the screen picks with keys 1-9).</summary>
    public const int MaxOffersPerRow = 9;

    /// <summary>Host limit: the longest card key accepted ("g:support_id:skill_id").</summary>
    public const int MaxOfferKeyChars = 64;
}
