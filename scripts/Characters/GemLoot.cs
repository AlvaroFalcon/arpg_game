#nullable enable
using System.Linq;
using ArpgGame.Progression;

namespace ArpgGame.Characters;

/// <summary>
/// What picking up a gem does — docs/design/classes.md -> "Gems as items".
/// A new skill joins the character's gems; a duplicate skill is melted into
/// experience for the best copy owned (so duplicates are never dead drops).
/// Supports stack up to <see cref="MaxSupportCopies"/> copies (one per loadout
/// slot); a fifth copy becomes gold.
/// </summary>
public static class GemLoot
{
    public const int MaxSupportCopies = CharacterData.LoadoutSlots;
    public const long SupportSurplusGold = 25;

    public static string NameOf(string id, bool support) =>
        support
            ? UpgradeCatalog.Supports.FirstOrDefault(s => s.Id == id)?.Name ?? id
            : UpgradeCatalog.FindSkill(id)?.Definition.DisplayName ?? id;

    /// <summary>Adds the gem to the character; returns the toast text and any
    /// gold it was converted into (the caller credits the player's wallet, which
    /// is the source of truth for gold while an activity runs).</summary>
    public static (string Text, long Gold) Collect(CharacterData c, string id, bool support)
    {
        string name = NameOf(id, support);
        if (support)
        {
            if (c.Gems.Count(g => g.IsSupport && g.Id == id) >= MaxSupportCopies)
            {
                return ($"{name} (surplus) → {SupportSurplusGold} gold", SupportSurplusGold);
            }

            c.AddGem(id, true);
            return ($"Support gem: {name}", 0);
        }

        GemData? best = c.Gems.Where(g => !g.IsSupport && g.Id == id).OrderByDescending(g => g.Level).FirstOrDefault();
        if (best is null)
        {
            c.AddGem(id, false);
            return ($"New skill gem: {name} — equip it in the skills panel (K)", 0);
        }

        // Worth roughly two levels of a low gem, a fraction of one late.
        float xp = GemData.ExperienceToNext(best.Level) * 0.6f + 120f;
        bool levelled = best.AddExperience(xp);
        return (levelled ? $"{name} absorbed → level {best.Level}" : $"{name} absorbed (+gem experience)", 0);
    }
}
