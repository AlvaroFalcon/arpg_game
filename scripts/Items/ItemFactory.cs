#nullable enable
using System.Collections.Generic;
using Godot;

namespace ArpgGame.Items;

/// <summary>
/// Creates equipment items as pure data (not placed on the ground) and looks
/// up item definitions by id — the shared plumbing behind end-of-run rewards
/// and profile save/load (docs/design/menus.md -> "Rewards" / "Profile").
/// Uses the exact same <see cref="EquipmentItem.Initialize"/> roll path drops
/// use, so a reward item can never be something a drop could not be.
/// </summary>
public static class ItemFactory
{
    public const string DropScenePath = "res://scenes/items/EquipmentDrop.tscn";
    public const string BasesDir = "res://resources/items/bases";
    public const string AffixLibraryPath = "res://resources/items/AffixLibrary.tres";

    private static List<BaseItemDefinition>? _bases;
    private static AffixLibrary? _affixes;

    public static IReadOnlyList<BaseItemDefinition> Bases => _bases ??= LoadBases();

    public static AffixLibrary Affixes => _affixes ??= GD.Load<AffixLibrary>(AffixLibraryPath);

    private static List<BaseItemDefinition> LoadBases()
    {
        var list = new List<BaseItemDefinition>();
        using DirAccess? dir = DirAccess.Open(BasesDir);
        if (dir is null)
        {
            return list;
        }

        foreach (string file in dir.GetFiles())
        {
            // Exported builds list remapped resources as "x.tres.remap".
            string name = file.EndsWith(".remap") ? file[..^6] : file;
            if (!name.EndsWith(".tres") && !name.EndsWith(".res"))
            {
                continue;
            }

            if (GD.Load<BaseItemDefinition>($"{BasesDir}/{name}") is { } def && !list.Contains(def))
            {
                list.Add(def);
            }
        }

        list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return list;
    }

    public static BaseItemDefinition? FindBase(string id)
    {
        foreach (BaseItemDefinition b in Bases)
        {
            if (b.Id == id)
            {
                return b;
            }
        }

        return null;
    }

    public static AffixDefinition? FindAffix(string id)
    {
        foreach (AffixDefinition a in Affixes.Entries)
        {
            if (a.Id == id)
            {
                return a;
            }
        }

        return null;
    }

    public static EquipmentItem NewBlank() => GD.Load<PackedScene>(DropScenePath).Instantiate<EquipmentItem>();

    public static EquipmentItem Roll(BaseItemDefinition baseItem, Rarity rarity, int itemLevel, RandomNumberGenerator rng)
    {
        EquipmentItem item = NewBlank();
        item.Initialize(baseItem, rarity, itemLevel, Affixes.Entries, rng);
        return item;
    }

    /// <summary>A random base at a rarity weighted toward better results as
    /// <paramref name="quality"/> (0..1) rises.</summary>
    public static EquipmentItem RollRandom(int itemLevel, float quality, RandomNumberGenerator rng)
    {
        BaseItemDefinition baseItem = Bases[rng.RandiRange(0, Bases.Count - 1)];
        float roll = rng.Randf() + quality * 0.6f;
        Rarity rarity = roll > 1.05f ? Rarity.Rare : roll > 0.5f ? Rarity.Magic : Rarity.Common;
        return Roll(baseItem, rarity, itemLevel, rng);
    }
}
