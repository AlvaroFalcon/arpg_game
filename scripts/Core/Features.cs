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
}
