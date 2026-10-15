using Godot;
using Godot.Collections;
using ArpgGame.Combat;
using ArpgGame.Items;
using ArpgGame.Stats;

namespace ArpgGame.Tools;

/// <summary>
/// One-shot resource-authoring tool for milestone 2a: builds the nine base items
/// from docs/design/items.md -> "The first base items" and the 28 affixes from
/// docs/design/affixes.md's Temper/Sigil tables, then saves each as its own
/// `.tres` plus an <see cref="AffixLibrary"/> referencing all 28. Same rationale
/// as <see cref="SeedFireball"/>/<see cref="SeedStatBlocks"/>: hand-typing 37
/// `.tres` files (nine of which nest a <c>BaseStats</c> array, one of which nests
/// 28 external references) is exactly what Godot's own serializer should
/// produce.
///
/// Run with:
///   dotnet build ArpgGame.csproj
///   ./scripts/run_headless.sh res://scenes/tools/SeedItems.tscn --quit-after 2
///
/// Safe to re-run — always overwrites everything under res://resources/items/.
/// Values are transcribed directly from the design docs' tables except where
/// noted inline (unit conversions, ilvl-step uniformity, item-class gating) —
/// see the milestone report for the full list of judgment calls made here.
/// </summary>
public partial class SeedItems : Node
{
    // "Roughly 17 item levels per band" over the 1-120 range (stats.md's third
    // pass), applied uniformly to every affix rather than hand-tuned per affix.

    public override void _Ready()
    {
        SeedBaseItems();
        SeedAffixesAndLibrary();
        GetTree().Quit();
    }

    // ------------------------------------------------------------------
    // Base items — docs/design/items.md -> "The first base items".
    // ------------------------------------------------------------------

    private static void SeedBaseItems()
    {
        SaveBase(new BaseItemDefinition
        {
            Id = "iron_wand",
            DisplayName = "Iron Wand",
            ItemClass = ItemClass.Wand,
            Width = 1,
            Height = 2,
            RequiredLevel = 1,
            // "+3-6 Damage (Spell)" — granted as a flat Damage modifier
            // conditioned on the Spell tag (docs/design/items.md -> "Weapon base
            // damage is granted as a flat Damage modifier").
            BaseStats = new Array<BaseItemStatEntry>
            {
                new() { Stat = StatId.Damage, Type = ModifierType.Flat, Min = 3f, Max = 6f, Condition = SkillTags.Spell },
            },
        });

        SaveBase(new BaseItemDefinition
        {
            Id = "oak_buckler",
            DisplayName = "Oak Buckler",
            ItemClass = ItemClass.Offhand,
            Width = 2,
            Height = 2,
            RequiredLevel = 1,
            BaseStats = new Array<BaseItemStatEntry>
            {
                new() { Stat = StatId.Armour, Type = ModifierType.Flat, Min = 12f, Max = 12f },
            },
        });

        SaveBase(new BaseItemDefinition
        {
            Id = "leather_cap",
            DisplayName = "Leather Cap",
            ItemClass = ItemClass.Helmet,
            Width = 2,
            Height = 2,
            RequiredLevel = 1,
            BaseStats = new Array<BaseItemStatEntry>
            {
                new() { Stat = StatId.Armour, Type = ModifierType.Flat, Min = 8f, Max = 8f },
            },
        });

        SaveBase(new BaseItemDefinition
        {
            Id = "ringmail_vest",
            DisplayName = "Ringmail Vest",
            ItemClass = ItemClass.BodyArmour,
            Width = 2,
            Height = 3,
            RequiredLevel = 1,
            BaseStats = new Array<BaseItemStatEntry>
            {
                new() { Stat = StatId.Armour, Type = ModifierType.Flat, Min = 30f, Max = 30f },
            },
        });

        SaveBase(new BaseItemDefinition
        {
            Id = "rawhide_gloves",
            DisplayName = "Rawhide Gloves",
            ItemClass = ItemClass.Gloves,
            Width = 2,
            Height = 2,
            RequiredLevel = 1,
            BaseStats = new Array<BaseItemStatEntry>
            {
                new() { Stat = StatId.Armour, Type = ModifierType.Flat, Min = 6f, Max = 6f },
            },
        });

        SaveBase(new BaseItemDefinition
        {
            Id = "rawhide_boots",
            DisplayName = "Rawhide Boots",
            ItemClass = ItemClass.Boots,
            Width = 2,
            Height = 2,
            RequiredLevel = 1,
            BaseStats = new Array<BaseItemStatEntry>
            {
                new() { Stat = StatId.Armour, Type = ModifierType.Flat, Min = 6f, Max = 6f },
            },
        });

        // Belt, amulet, ring: pure affix carriers by design — no BaseStats.
        SaveBase(new BaseItemDefinition
        {
            Id = "rope_belt",
            DisplayName = "Rope Belt",
            ItemClass = ItemClass.Belt,
            Width = 2,
            Height = 1,
            RequiredLevel = 1,
        });

        SaveBase(new BaseItemDefinition
        {
            Id = "copper_amulet",
            DisplayName = "Copper Amulet",
            ItemClass = ItemClass.Amulet,
            Width = 1,
            Height = 1,
            RequiredLevel = 1,
        });

        SaveBase(new BaseItemDefinition
        {
            Id = "copper_ring",
            DisplayName = "Copper Ring",
            ItemClass = ItemClass.Ring,
            Width = 1,
            Height = 1,
            RequiredLevel = 1,
        });
    }

    private static void SaveBase(BaseItemDefinition baseItem)
    {
        string path = $"res://resources/items/bases/{baseItem.Id}.tres";
        Error err = ResourceSaver.Save(baseItem, path);
        GD.Print(err == Error.Ok ? $"[SEED] Saved {path}" : $"[SEED] FAILED to save {path}: {err}");
    }

    // ------------------------------------------------------------------
    // Affixes — docs/design/affixes.md -> "The Temper pool" / "The Sigil pool".
    // ------------------------------------------------------------------

    private static readonly Array<ItemClass> ArmourSlots = new()
    {
        ItemClass.Offhand, ItemClass.Helmet, ItemClass.BodyArmour, ItemClass.Gloves, ItemClass.Boots,
    };

    // docs/design/affixes.md -> "Flat added damage": weapons and jewellery only.
    private static readonly Array<ItemClass> DamageSlots = new()
    {
        ItemClass.Wand, ItemClass.Sword, ItemClass.Axe, ItemClass.Dagger, ItemClass.Amulet, ItemClass.Ring,
    };

    private static void SeedAffixesAndLibrary()
    {
        var library = new AffixLibrary();

        // --- Temper pool ---------------------------------------------------

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "heavy", DisplayWord = "Heavy", Pool = AffixPoolKind.Temper,
            Stat = StatId.Damage, Type = ModifierType.Flat,
            MinIlvl = 1,
            ValueAtT1Min = 2f, ValueAtT1Max = 4f, ValueAtT7Min = 45f, ValueAtT7Max = 70f,
            AllowedItemClasses = new Array<ItemClass>(DamageSlots),
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "blazing", DisplayWord = "Blazing", Pool = AffixPoolKind.Temper,
            Stat = StatId.Damage, Type = ModifierType.Flat, Condition = SkillTags.Fire,
            MinIlvl = 1,
            ValueAtT1Min = 3f, ValueAtT1Max = 5f, ValueAtT7Min = 55f, ValueAtT7Max = 85f,
            AllowedItemClasses = new Array<ItemClass>(DamageSlots),
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "frozen", DisplayWord = "Frozen", Pool = AffixPoolKind.Temper,
            Stat = StatId.Damage, Type = ModifierType.Flat, Condition = SkillTags.Cold,
            MinIlvl = 1,
            ValueAtT1Min = 3f, ValueAtT1Max = 5f, ValueAtT7Min = 55f, ValueAtT7Max = 85f,
            AllowedItemClasses = new Array<ItemClass>(DamageSlots),
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "charged", DisplayWord = "Charged", Pool = AffixPoolKind.Temper,
            Stat = StatId.Damage, Type = ModifierType.Flat, Condition = SkillTags.Lightning,
            MinIlvl = 1,
            ValueAtT1Min = 2f, ValueAtT1Max = 7f, ValueAtT7Min = 40f, ValueAtT7Max = 100f,
            AllowedItemClasses = new Array<ItemClass>(DamageSlots),
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "savage", DisplayWord = "Savage", Pool = AffixPoolKind.Temper,
            Stat = StatId.Damage, Type = ModifierType.Increased,
            MinIlvl = 1,
            ValueAtT1Min = 8f, ValueAtT1Max = 12f, ValueAtT7Min = 45f, ValueAtT7Max = 60f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "arcane", DisplayWord = "Arcane", Pool = AffixPoolKind.Temper,
            Stat = StatId.Damage, Type = ModifierType.Increased, Condition = SkillTags.Spell,
            MinIlvl = 5,
            ValueAtT1Min = 10f, ValueAtT1Max = 15f, ValueAtT7Min = 55f, ValueAtT7Max = 75f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "brutal", DisplayWord = "Brutal", Pool = AffixPoolKind.Temper,
            Stat = StatId.Damage, Type = ModifierType.Increased, Condition = SkillTags.Attack,
            MinIlvl = 5,
            ValueAtT1Min = 10f, ValueAtT1Max = 15f, ValueAtT7Min = 55f, ValueAtT7Max = 75f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "volatile", DisplayWord = "Volatile", Pool = AffixPoolKind.Temper,
            Stat = StatId.Damage, Type = ModifierType.Increased,
            ConditionAny = SkillTags.Fire | SkillTags.Cold | SkillTags.Lightning,
            MinIlvl = 12,
            ValueAtT1Min = 9f, ValueAtT1Max = 14f, ValueAtT7Min = 50f, ValueAtT7Max = 68f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "vigorous", DisplayWord = "Vigorous", Pool = AffixPoolKind.Temper,
            Stat = StatId.MaxLife, Type = ModifierType.Flat,
            MinIlvl = 1,
            ValueAtT1Min = 12f, ValueAtT1Max = 20f, ValueAtT7Min = 110f, ValueAtT7Max = 150f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "titanic", DisplayWord = "Titanic", Pool = AffixPoolKind.Temper,
            Stat = StatId.MaxLife, Type = ModifierType.Increased,
            MinIlvl = 30,
            ValueAtT1Min = 3f, ValueAtT1Max = 5f, ValueAtT7Min = 12f, ValueAtT7Max = 16f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "plated", DisplayWord = "Plated", Pool = AffixPoolKind.Temper,
            Stat = StatId.Armour, Type = ModifierType.Flat,
            MinIlvl = 1,
            ValueAtT1Min = 15f, ValueAtT1Max = 25f, ValueAtT7Min = 250f, ValueAtT7Max = 400f,
            AllowedItemClasses = new Array<ItemClass>(ArmourSlots),
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "mighty", DisplayWord = "Mighty", Pool = AffixPoolKind.Temper,
            Stat = StatId.Strength, Type = ModifierType.Flat,
            MinIlvl = 1,
            ValueAtT1Min = 3f, ValueAtT1Max = 6f, ValueAtT7Min = 35f, ValueAtT7Max = 50f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "agile", DisplayWord = "Agile", Pool = AffixPoolKind.Temper,
            Stat = StatId.Dexterity, Type = ModifierType.Flat,
            MinIlvl = 1,
            ValueAtT1Min = 3f, ValueAtT1Max = 6f, ValueAtT7Min = 35f, ValueAtT7Max = 50f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "studious", DisplayWord = "Studious", Pool = AffixPoolKind.Temper,
            Stat = StatId.Intelligence, Type = ModifierType.Flat,
            MinIlvl = 1,
            ValueAtT1Min = 3f, ValueAtT1Max = 6f, ValueAtT7Min = 35f, ValueAtT7Max = 50f,
        }));

        // --- Sigil pool ------------------------------------------------------

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "of_vigor", DisplayWord = "of Vigor", Pool = AffixPoolKind.Sigil,
            Stat = StatId.LifeRegen, Type = ModifierType.Flat,
            MinIlvl = 1,
            ValueAtT1Min = 1f, ValueAtT1Max = 2f, ValueAtT7Min = 14f, ValueAtT7Max = 20f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "of_warding", DisplayWord = "of Warding", Pool = AffixPoolKind.Sigil,
            Stat = StatId.FireResistance, Type = ModifierType.Flat,
            MinIlvl = 1,
            ValueAtT1Min = 6f, ValueAtT1Max = 10f, ValueAtT7Min = 40f, ValueAtT7Max = 46f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "of_insulation", DisplayWord = "of Insulation", Pool = AffixPoolKind.Sigil,
            Stat = StatId.ColdResistance, Type = ModifierType.Flat,
            MinIlvl = 1,
            ValueAtT1Min = 6f, ValueAtT1Max = 10f, ValueAtT7Min = 40f, ValueAtT7Max = 46f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "of_grounding", DisplayWord = "of Grounding", Pool = AffixPoolKind.Sigil,
            Stat = StatId.LightningResistance, Type = ModifierType.Flat,
            MinIlvl = 1,
            ValueAtT1Min = 6f, ValueAtT1Max = 10f, ValueAtT7Min = 40f, ValueAtT7Max = 46f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "of_resilience", DisplayWord = "of Resilience", Pool = AffixPoolKind.Sigil,
            // Stat is representative only — see AffixDefinition.IsAllResistances.
            Stat = StatId.FireResistance, Type = ModifierType.Flat, IsAllResistances = true,
            MinIlvl = 20,
            ValueAtT1Min = 3f, ValueAtT1Max = 5f, ValueAtT7Min = 18f, ValueAtT7Max = 22f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "of_the_mind", DisplayWord = "of the Mind", Pool = AffixPoolKind.Sigil,
            Stat = StatId.MaxMana, Type = ModifierType.Flat,
            MinIlvl = 1,
            ValueAtT1Min = 10f, ValueAtT1Max = 18f, ValueAtT7Min = 90f, ValueAtT7Max = 125f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "of_haste", DisplayWord = "of Haste", Pool = AffixPoolKind.Sigil,
            Stat = StatId.AttackSpeed, Type = ModifierType.Increased,
            MinIlvl = 8,
            ValueAtT1Min = 4f, ValueAtT1Max = 7f, ValueAtT7Min = 15f, ValueAtT7Max = 19f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "of_focus", DisplayWord = "of Focus", Pool = AffixPoolKind.Sigil,
            Stat = StatId.CastSpeed, Type = ModifierType.Increased,
            MinIlvl = 8,
            ValueAtT1Min = 4f, ValueAtT1Max = 7f, ValueAtT7Min = 15f, ValueAtT7Max = 19f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "of_recovery", DisplayWord = "of Recovery", Pool = AffixPoolKind.Sigil,
            Stat = StatId.CooldownRecovery, Type = ModifierType.Increased,
            MinIlvl = 15,
            ValueAtT1Min = 5f, ValueAtT1Max = 8f, ValueAtT7Min = 18f, ValueAtT7Max = 24f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "of_efficiency", DisplayWord = "of Efficiency", Pool = AffixPoolKind.Sigil,
            Stat = StatId.ManaCostMultiplier, Type = ModifierType.Increased,
            MinIlvl = 12,
            // Negative Increased modifier on a base-1.0 multiplier — docs/design/
            // affixes.md -> "of Efficiency", not a fourth ModifierType. Min is the
            // more-negative end of each range (RandfRange needs min <= max).
            ValueAtT1Min = -7f, ValueAtT1Max = -4f, ValueAtT7Min = -24f, ValueAtT7Max = -18f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "of_precision", DisplayWord = "of Precision", Pool = AffixPoolKind.Sigil,
            Stat = StatId.CritChance, Type = ModifierType.Increased,
            MinIlvl = 10,
            ValueAtT1Min = 10f, ValueAtT1Max = 15f, ValueAtT7Min = 55f, ValueAtT7Max = 75f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "of_ruin", DisplayWord = "of Ruin", Pool = AffixPoolKind.Sigil,
            Stat = StatId.CritMultiplier, Type = ModifierType.Flat,
            MinIlvl = 20,
            // CritMultiplier's native unit is a fraction of 1 (base 1.5 = 150%),
            // so the design table's "8-12% / 30-38%" is authored here pre-divided
            // by 100 — see the milestone report for why this is a data-only fix
            // rather than a roller special case.
            ValueAtT1Min = 0.08f, ValueAtT1Max = 0.12f, ValueAtT7Min = 0.30f, ValueAtT7Max = 0.38f,
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "of_swiftness", DisplayWord = "of Swiftness", Pool = AffixPoolKind.Sigil,
            Stat = StatId.MovementSpeed, Type = ModifierType.Increased,
            MinIlvl = 15,
            ValueAtT1Min = 3f, ValueAtT1Max = 5f, ValueAtT7Min = 12f, ValueAtT7Max = 15f,
            // Movement speed is boots-only — a genre-standard convention (PoE),
            // and a deliberate demonstration that AllowedItemClasses gates for
            // real, not just Plated's armour-slot restriction above.
            AllowedItemClasses = new Array<ItemClass> { ItemClass.Boots },
        }));

        library.Entries.Add(SaveAffix(new AffixDefinition
        {
            Id = "of_thorns", DisplayWord = "of Thorns", Pool = AffixPoolKind.Sigil,
            Stat = StatId.ThornsDamage, Type = ModifierType.Flat,
            MinIlvl = 5,
            ValueAtT1Min = 3f, ValueAtT1Max = 6f, ValueAtT7Min = 40f, ValueAtT7Max = 65f,
        }));

        const string libraryPath = "res://resources/items/AffixLibrary.tres";
        Error libErr = ResourceSaver.Save(library, libraryPath);
        GD.Print(libErr == Error.Ok
            ? $"[SEED] Saved {libraryPath} ({library.Entries.Count} affixes)"
            : $"[SEED] FAILED to save {libraryPath}: {libErr}");
    }

    /// <summary>
    /// Saves one affix to its own `.tres`, then reloads it. The reload matters:
    /// <see cref="ResourceSaver.Save"/> does not retroactively stamp the
    /// in-memory object's <c>resource_path</c>, so an <see cref="AffixLibrary"/>
    /// built from the original in-memory instances would embed 28 duplicate
    /// copies as sub-resources instead of referencing the standalone files —
    /// exactly the ".tres file and nothing else" property this schema exists to
    /// buy would be lost (editing the standalone affix later wouldn't touch the
    /// library's copy). Loading the just-saved file back gives an instance whose
    /// path *is* set, so <see cref="ResourceSaver.Save"/> on the library
    /// serializes it as <c>ext_resource</c>.
    /// </summary>
    private static AffixDefinition SaveAffix(AffixDefinition affix)
    {
        string path = $"res://resources/items/affixes/{affix.Id}.tres";
        Error err = ResourceSaver.Save(affix, path);
        GD.Print(err == Error.Ok ? $"[SEED] Saved {path}" : $"[SEED] FAILED to save {path}: {err}");
        return ResourceLoader.Load<AffixDefinition>(path, cacheMode: ResourceLoader.CacheMode.Replace);
    }
}
