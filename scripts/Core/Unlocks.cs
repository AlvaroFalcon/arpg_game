#nullable enable
using System;
using System.Collections.Generic;

namespace ArpgGame.Core;

/// <summary>
/// Account-wide unlocks earned from Rift challenges (docs/design/survival-roadmap.md -> M7):
/// keystones, evolution recipes and start-pick skills that only enter the Survival Rift's
/// pools once unlocked. Saved in the profile (<c>Profile.Unlocks</c>), mirrored here so pure
/// code (offer rolls, the evolution catalog) can ask without a node. With
/// <see cref="Features.RiftChallenges"/> off, everything counts as unlocked.
/// </summary>
public static class Unlocks
{
    private static readonly HashSet<string> Set = new();

    public static event Action<string>? Granted;

    public static bool Has(string id) => !Features.RiftChallenges || Set.Contains(id);

    public static IReadOnlyCollection<string> All => Set;

    /// <summary>Replaces the set (the profile loaded).</summary>
    public static void Load(IEnumerable<string> ids)
    {
        Set.Clear();
        foreach (string id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id) && id.Length <= 64)
            {
                Set.Add(id);
            }
        }
    }

    /// <summary>Adds one; true if it was new.</summary>
    public static bool Grant(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !Set.Add(id))
        {
            return false;
        }

        Granted?.Invoke(id);
        return true;
    }

    /// <summary>Tests: forget everything.</summary>
    public static void ClearForTest() => Set.Clear();
}
