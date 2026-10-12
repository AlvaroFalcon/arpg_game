#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArpgGame.Items;

/// <summary>
/// Name generation per docs/design/affixes.md -> "Name generation": varies by
/// rarity and lives entirely behind <see cref="ItemDescriptor.DisplayName"/>, so
/// label code never needs to know how a name was built (docs/design/items.md ->
/// "Drop pipeline": "The label never switches on type").
/// </summary>
public static class ItemNaming
{
    /// <summary>
    /// <b>Placeholder content, not real content.</b> docs/design/affixes.md ->
    /// "Name generation": "A Rare rolls 3-4 affixes but has only one Temper and
    /// one Sigil slot's worth of name, so it takes a generated name from an
    /// authored word pool instead, decoupled from its affixes. The pool itself is
    /// not authored yet." This milestone's task brief is explicit that authoring
    /// the real pool is out of scope; these eight-and-eight words exist only so
    /// <see cref="Generate"/> has something to draw from and Rare items get a
    /// distinct, non-empty name. Replace freely — nothing depends on these exact
    /// words.
    /// </summary>
    private static readonly string[] RareWordsFirst =
    {
        "Corpse", "Storm", "Iron", "Grim", "Ashen", "Frost", "Ember", "Void",
    };

    private static readonly string[] RareWordsSecond =
    {
        "Bite", "Ward", "Fang", "Grasp", "Ember", "Howl", "Reach", "Bloom",
    };

    /// <summary>
    /// Builds the display name for a freshly-rolled item. <paramref name="rolled"/>
    /// is the exact set of affixes that landed on this item (see
    /// <see cref="AffixRoller.Roll"/>) — Magic names read the actual rolled Temper
    /// and Sigil words; Rare deliberately ignores <paramref name="rolled"/> and
    /// draws from the placeholder word pool instead, per the design doc's own
    /// "decoupled from its affixes."
    /// </summary>
    public static string Generate(
        Rarity rarity,
        string baseName,
        IReadOnlyList<AffixRoller.RolledAffix> rolled,
        RandomNumberGenerator rng)
    {
        switch (rarity)
        {
            case Rarity.Common:
                return baseName;

            case Rarity.Magic:
                return GenerateMagicName(baseName, rolled);

            case Rarity.Rare:
                string first = RareWordsFirst[rng.RandiRange(0, RareWordsFirst.Length - 1)];
                string second = RareWordsSecond[rng.RandiRange(0, RareWordsSecond.Length - 1)];
                return $"{first} {second}, {baseName}";

            default:
                // Majestic/Set: authored fixed names, per the design doc — no
                // pool exists yet (milestone 4's Set/Majestic authoring), and no
                // milestone-2a spawner rolls these rarities for equipment (see
                // EquipmentDropSpawner). Falling back to the base name rather
                // than throwing keeps this function total.
                return baseName;
        }
    }

    private static string GenerateMagicName(string baseName, IReadOnlyList<AffixRoller.RolledAffix> rolled)
    {
        AffixRoller.RolledAffix? temper = rolled.FirstOrDefault(a => a.Definition.Pool == AffixPoolKind.Temper);
        AffixRoller.RolledAffix? sigil = rolled.FirstOrDefault(a => a.Definition.Pool == AffixPoolKind.Sigil);

        string name = baseName;
        if (temper is not null)
        {
            name = $"{temper.Definition.DisplayWord} {name}";
        }

        if (sigil is not null)
        {
            name = $"{name} {sigil.Definition.DisplayWord}";
        }

        return name;
    }
}
