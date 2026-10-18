#nullable enable
using System.Collections.Generic;
using System.Linq;
using ArpgGame.Combat;
using ArpgGame.Core;
using ArpgGame.Items;
using ArpgGame.Progression;
using ArpgGame.Skills;

namespace ArpgGame.Characters;

/// <summary>One gem on sale at Odran's Skill gems tab.</summary>
public sealed record GemOffer(string Id, bool Support, int Level)
{
    /// <summary>Skills cost 50 gold per unlock level, supports 20 + 40 per unlock level.</summary>
    public long Price => Support ? 20 + 40 * Level : 50 * Level;
}

/// <summary>
/// Odran's Skill gems tab, the rules — docs/design/hideout.md -> "Odran's Skill gems
/// tab". Every skill and support of <see cref="UpgradeCatalog"/> is on sale from a
/// character level, always at gem level 1 (a bought gem joins the character's
/// collection like a dropped one and levels with use). A skill is bought once
/// (duplicates are not melted into experience: gold must not become skill power);
/// a support up to <see cref="GemLoot.MaxSupportCopies"/> times, one per loadout
/// slot. Static and node-free so it can be tested without a scene.
/// </summary>
public static class GemShop
{
    // Unlock levels. Starting gems at 1 so every class can try the others' basics;
    // the damage multipliers last because they are the biggest single jumps.
    private static readonly (int Level, string[] Ids)[] SkillTable =
    {
        (1, new[] { "fireball", "double_strike", "cleave" }),
        (4, new[] { "spark", "throwing_knives", "ground_slam" }),
        (6, new[] { "frost_shards" }),
        (8, new[] { "blades", "frost_nova" }),
        (10, new[] { "storm_strike" }),
        (12, new[] { "sunder" }),
        (14, new[] { "glacial_hammer" }),
        (16, new[] { "meteor" }),
    };

    private static readonly (int Level, string[] Ids)[] SupportTable =
    {
        (1, new[] { "faster_casting", "increased_area", "spell_echo" }),
        (2, new[] { "pierce", "faster_projectiles", "melee_splash", "extended_duration" }),
        (6, new[] { "multiple_projectiles", "fork", "chain", "multistrike", "blade_storm" }),
        (10, new[] { "ignition", "overcharge", "cold_conversion", "fire_conversion", "lightning_conversion" }),
        (14, new[] { "concentrated_effect", "elemental_focus", "brutality" }),
    };

    /// <summary>Every offer: skills then supports, by unlock level, then name.</summary>
    public static readonly IReadOnlyList<GemOffer> Offers = Build();

    private static List<GemOffer> Build()
    {
        var all = new List<GemOffer>();
        all.AddRange(SkillTable.SelectMany(t => t.Ids.Select(id => new GemOffer(id, false, t.Level))));
        all.AddRange(SupportTable.SelectMany(t => t.Ids.Select(id => new GemOffer(id, true, t.Level))));
        return all.OrderBy(o => o.Support).ThenBy(o => o.Level).ThenBy(o => NameOf(o), System.StringComparer.Ordinal).ToList();
    }

    public static string NameOf(GemOffer o) => GemLoot.NameOf(o.Id, o.Support);

    public static GemOffer? Find(string id, bool support) => Offers.FirstOrDefault(o => o.Id == id && o.Support == support);

    public static SkillDefinition? SkillOf(GemOffer o) => o.Support ? null : UpgradeCatalog.FindSkill(o.Id)?.Definition;

    public static SupportDefinition? SupportOf(GemOffer o) => o.Support ? UpgradeCatalog.Supports.FirstOrDefault(s => s.Id == o.Id) : null;

    /// <summary>The skill tags of a skill, or the tags a support asks for.</summary>
    public static SkillTags TagsOf(GemOffer o) => o.Support ? SupportOf(o)?.RequiresAny ?? SkillTags.None : SkillOf(o)?.Tags ?? SkillTags.None;

    /// <summary>Copies of this gem the character owns (skills 0 or 1).</summary>
    public static int Owned(CharacterData c, GemOffer o) => c.Gems.Count(g => g.Id == o.Id && g.IsSupport == o.Support);

    /// <summary>The level of the owned skill gem, or 0.</summary>
    public static int OwnedLevel(CharacterData c, GemOffer o) => c.Gems.Where(g => g.Id == o.Id && g.IsSupport == o.Support).Select(g => g.Level).DefaultIfEmpty(0).Max();

    public static int MaxCopies(GemOffer o) => o.Support ? GemLoot.MaxSupportCopies : 1;

    public static bool Locked(CharacterData c, GemOffer o) => c.Level < o.Level;

    /// <summary>Offers that unlocked since the character last opened the tab.</summary>
    public static int NewCount(CharacterData c) => Offers.Count(o => o.Level <= c.Level && o.Level > c.GemShopSeenLevel);

    /// <summary>The classes whose Rift skill list favours a skill (no class favours a support).</summary>
    public static IEnumerable<ClassDefinition> FavouredBy(string skillId) => ClassCatalog.Classes.Where(k => k.RiftSkills.Contains(skillId));

    /// <summary>A support that fits at least one skill the character owns.</summary>
    public static bool FitsAny(CharacterData c, GemOffer support) =>
        SupportOf(support) is { } def && c.Gems.Any(g => !g.IsSupport && UpgradeCatalog.FindSkill(g.Id)?.Definition is { } skill && def.Accepts(skill));

    /// <summary>The names of the owned skills a support fits, for "Fits your: ...".</summary>
    public static IEnumerable<string> FitNames(CharacterData c, GemOffer support) =>
        SupportOf(support) is { } def
            ? c.Gems.Where(g => !g.IsSupport && UpgradeCatalog.FindSkill(g.Id)?.Definition is { } skill && def.Accepts(skill)).Select(g => GemLoot.NameOf(g.Id, false)).Distinct()
            : Enumerable.Empty<string>();

    /// <summary>Why the offer can't be bought now, or null.</summary>
    public static string? CannotBuy(CharacterData c, Wallet wallet, GemOffer o)
    {
        if (Locked(c, o))
        {
            return $"Unlocks at character level {o.Level}";
        }

        int owned = Owned(c, o);
        if (!o.Support && owned > 0)
        {
            return "You have this skill. Skill gems level up as you use them.";
        }

        if (o.Support && owned >= MaxCopies(o))
        {
            return $"You have {owned} of {MaxCopies(o)}, one per loadout slot";
        }

        long gold = wallet.GetBalance(Profile.GoldId);
        return gold < o.Price ? $"Not enough gold: {o.Price - gold:N0} more" : null;
    }

    /// <summary>Spends the gold and adds a level-1 gem; raises <see cref="GameEvents.GemBought"/>.</summary>
    public static bool TryBuy(CharacterData c, Wallet wallet, GemOffer o, out string message)
    {
        if (CannotBuy(c, wallet, o) is { } why)
        {
            message = why;
            return false;
        }

        wallet.TrySpend(Profile.GoldId, o.Price);
        c.AddGem(o.Id, o.Support);
        message = $"Bought {NameOf(o)}. It's in your skills (K) at level 1.";
        GameEvents.Raise(GameEvents.GemBought, o.Support ? 1 : 0, o.Id);
        return true;
    }
}
