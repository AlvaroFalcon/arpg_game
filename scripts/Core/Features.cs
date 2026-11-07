#nullable enable

namespace ArpgGame.Core;

/// <summary>
/// Switches for systems that are parked rather than deleted.
/// </summary>
public static class Features
{
    /// <summary>
    /// Support gems — off since the skill rework of 2026-10-30 (each skill's upgrade
    /// tree at Escaldón took their job; docs/design/skills.md -> "Support gems parked").
    /// The code, the catalog and saved gems all stay: with this false no support drops,
    /// is sold, is offered as a Rift card or starts on a new character, the skills panel
    /// shows no sockets, and supports already socketed in a save do nothing. Settable so
    /// tests that cover the support rules can switch it back on.
    /// </summary>
    public static bool SupportGems { get; set; }

    // The Survival Rift's 2026-11 improvements (docs/design/survival-roadmap.md), one per
    // milestone: on, settable so a half-tuned system can be switched off and tests can
    // run the old Rift.

    /// <summary>M1: Skip, Banish, gold rerolls and Luck's 4th card on the level-up screen.</summary>
    public static bool RiftPickControl { get; set; } = true;

    /// <summary>M2: chests from elites and bosses.</summary>
    public static bool RiftChests { get; set; } = true;

    /// <summary>M3: shrines in the arena.</summary>
    public static bool RiftShrines { get; set; } = true;

    /// <summary>M4: Rift tiers and Fear cards.</summary>
    public static bool RiftTiers { get; set; } = true;

    /// <summary>M5: skill evolutions.</summary>
    public static bool RiftEvolutions { get; set; } = true;

    /// <summary>M6: the Onslaught before the Colossus and the endless milestones.</summary>
    public static bool RiftOnslaught { get; set; } = true;

    /// <summary>M7: Rift challenges and the content they unlock.</summary>
    public static bool RiftChallenges { get; set; } = true;
}
