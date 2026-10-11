#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using PathToExile.Characters;
using PathToExile.Combat;
using PathToExile.Items;
using PathToExile.Stats;

namespace PathToExile.Core;

/// <summary>
/// The save file: a list of persistent characters and which one is active —
/// docs/design/endgame.md / classes.md. (First pass, docs/design/menus.md ->
/// "Profile", held a single character's gear; version 1 files are migrated into
/// one Arcanist.)
///
/// The live <see cref="Player"/> owns its inventory/equipment while a scene is
/// running. Scenes call <see cref="ApplyTo"/> when a player spawns (gear, gold
/// and the character build: class, passives, ascendancy, gems) and
/// <see cref="CaptureFrom"/> before leaving; the profile is written to
/// <c>user://profile.json</c> on every capture.
/// </summary>
[GlobalClass]
public partial class Profile : Node
{
    public const string SavePath = "user://profile.json";
    public const int MaxCharacters = 6;

    /// <summary>Matches <c>resources/currency/Gold.tres</c>'s Id.</summary>
    public static readonly CurrencyId GoldId = "gold";

    public List<CharacterData> Characters { get; } = new();

    public int ActiveIndex { get; private set; }

    /// <summary>When false, nothing is written to disk (tests).</summary>
    public bool Persist { get; set; } = true;

    public static Profile? Get(Node context) =>
        context.IsInsideTree() ? context.GetTree().Root.GetNodeOrNull<Profile>("Profile") : null;

    /// <summary>The active character, creating a default one if there is none
    /// (quick paths like the sandbox must always have someone to play).</summary>
    public CharacterData Active
    {
        get
        {
            if (Characters.Count == 0)
            {
                Characters.Add(CharacterData.Create("Exile", "arcanist"));
                ActiveIndex = 0;
            }

            return Characters[Mathf.Clamp(ActiveIndex, 0, Characters.Count - 1)];
        }
    }

    public bool HasCharacters => Characters.Count > 0;

    // Legacy accessors used by the survivor-run screens.
    public long Gold => Active.Gold;
    public int RunsPlayed => Active.SurvivalRuns;
    public int Victories => Active.RealitiesCleared;
    public float BestTime => Active.BestSurvivalTime;
    public int BestLevel => Active.Level;

    public override void _Ready()
    {
        Load();
    }

    public void Select(int index)
    {
        ActiveIndex = Mathf.Clamp(index, 0, Mathf.Max(0, Characters.Count - 1));
        Save();
    }

    public CharacterData CreateCharacter(string name, string classId)
    {
        CharacterData data = CharacterData.Create(string.IsNullOrWhiteSpace(name) ? "Exile" : name.Trim(), classId);
        Characters.Add(data);
        ActiveIndex = Characters.Count - 1;
        Save();
        return data;
    }

    public void DeleteCharacter(int index)
    {
        if (index < 0 || index >= Characters.Count)
        {
            return;
        }

        Characters.RemoveAt(index);
        ActiveIndex = Mathf.Clamp(ActiveIndex >= index ? ActiveIndex - 1 : ActiveIndex, 0, Mathf.Max(0, Characters.Count - 1));
        Save();
    }

    public void ApplyTo(Player player)
    {
        CharacterData c = Active;
        foreach ((EquipmentSlot slot, EquipmentItem item) in c.Equipped)
        {
            player.Equipment.TryEquip(slot, item, out _);
        }

        foreach ((EquipmentItem item, Vector2I cell) in c.Inventory)
        {
            if (!player.Inventory.TryPlaceAt(item, cell))
            {
                player.Inventory.TryAdd(item);
            }
        }

        if (c.Gold > 0)
        {
            player.Wallet.Add(GoldId, c.Gold);
        }

        CharacterBuild.Attach(player, c);

        // Equipping must never leave the character spawning hurt.
        if (player.GetNodeOrNull<ActorStats>("Stats") is { } stats)
        {
            if (stats.Life is { } life)
            {
                life.Current = life.Max;
            }

            if (stats.Mana is { } mana)
            {
                mana.Current = mana.Max;
            }
        }
    }

    public void CaptureFrom(Player player)
    {
        CharacterData c = Active;
        c.Equipped.Clear();
        foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
        {
            if (player.Equipment.GetEquipped(slot) is { } item)
            {
                c.Equipped[slot] = item;
            }
        }

        c.Inventory.Clear();
        foreach ((EquipmentItem item, Vector2I cell) in player.Inventory.Placements)
        {
            c.Inventory.Add((item, cell));
        }

        c.Gold = player.Wallet.GetBalance(GoldId);
        Save();
    }

    /// <summary>Adds an item straight into the stored inventory (a reward claimed
    /// while no player owns the grid). Returns false if nothing fits.</summary>
    public bool AddToStash(EquipmentItem item)
    {
        CharacterData c = Active;
        var occupancy = new bool[Inventory.Width, Inventory.Height];
        foreach ((EquipmentItem it, Vector2I cell) in c.Inventory)
        {
            Mark(occupancy, it, cell);
        }

        int w = item.Descriptor.Width;
        int h = item.Descriptor.Height;
        for (int y = 0; y + h <= Inventory.Height; y++)
        {
            for (int x = 0; x + w <= Inventory.Width; x++)
            {
                if (Fits(occupancy, x, y, w, h))
                {
                    c.Inventory.Add((item, new Vector2I(x, y)));
                    return true;
                }
            }
        }

        return false;
    }

    private static void Mark(bool[,] grid, EquipmentItem item, Vector2I cell)
    {
        for (int y = 0; y < item.Descriptor.Height; y++)
        {
            for (int x = 0; x < item.Descriptor.Width; x++)
            {
                if (cell.X + x < Inventory.Width && cell.Y + y < Inventory.Height)
                {
                    grid[cell.X + x, cell.Y + y] = true;
                }
            }
        }
    }

    private static bool Fits(bool[,] grid, int x0, int y0, int w, int h)
    {
        for (int y = y0; y < y0 + h; y++)
        {
            for (int x = x0; x < x0 + w; x++)
            {
                if (grid[x, y])
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Records a Survival Rift run.</summary>
    public void RecordRun(float time, int level, int kills, bool victory)
    {
        CharacterData c = Active;
        c.SurvivalRuns++;
        c.BestSurvivalTime = Mathf.Max(c.BestSurvivalTime, time);
        c.TotalKills += kills;
        Save();
    }

    // --- persistence ----------------------------------------------------------

    private sealed class SaveItem
    {
        public string Base { get; set; } = "";
        public int Rarity { get; set; }
        public int Ilvl { get; set; }
        public string Name { get; set; } = "";
        public List<float[]> BaseStats { get; set; } = new();
        public List<SaveAffix> Affixes { get; set; } = new();
        public string? Slot { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    private sealed class SaveAffix
    {
        public string Id { get; set; } = "";
        public int Tier { get; set; }
        public float Value { get; set; }
    }

    private sealed class SaveCharacter
    {
        public string Name { get; set; } = "Exile";
        public string ClassId { get; set; } = "arcanist";
        public int Level { get; set; } = 1;
        public float Experience { get; set; }
        public List<string> Passives { get; set; } = new();
        public string? Ascendancy { get; set; }
        public List<string> AscendancyNodes { get; set; } = new();
        public int AscendancyPoints { get; set; }
        public List<GemData> Gems { get; set; } = new();
        public List<LoadoutSlot> Loadout { get; set; } = new();
        public Dictionary<string, int> Shards { get; set; } = new();
        public long Gold { get; set; }
        public int HighestTierCleared { get; set; }
        public int RealitiesCleared { get; set; }
        public int Deaths { get; set; }
        public int TotalKills { get; set; }
        public int SurvivalRuns { get; set; }
        public float BestSurvivalTime { get; set; }
        public List<SaveItem> Items { get; set; } = new();
    }

    private sealed class SaveFile
    {
        public int Version { get; set; } = 2;
        public int Active { get; set; }
        public List<SaveCharacter> Characters { get; set; } = new();

        // Version 1 fields (single character), read for migration only.
        public long Gold { get; set; }
        public List<SaveItem>? Items { get; set; }
    }

    private static SaveItem ToSave(EquipmentItem item) => new()
    {
        Base = item.Base?.Id ?? "",
        Rarity = (int)item.Rarity,
        Ilvl = item.Descriptor.ItemLevel,
        Name = item.Descriptor.DisplayName,
        BaseStats = item.RolledBaseStats.Select(s => new[] { (float)s.Stat, (float)s.Type, s.Value, (float)s.Condition }).ToList(),
        Affixes = item.RolledAffixes.Select(a => new SaveAffix { Id = a.Definition.Id, Tier = a.Tier, Value = a.Value }).ToList(),
    };

    private static EquipmentItem? FromSave(SaveItem s)
    {
        if (ItemFactory.FindBase(s.Base) is not { } baseItem)
        {
            return null;
        }

        var affixes = new List<AffixRoller.RolledAffix>();
        foreach (SaveAffix a in s.Affixes)
        {
            if (ItemFactory.FindAffix(a.Id) is { } def)
            {
                affixes.Add(new AffixRoller.RolledAffix(def, a.Tier, a.Value));
            }
        }

        var baseStats = s.BaseStats
            .Where(v => v.Length == 4)
            .Select(v => ((StatId)(int)v[0], (ModifierType)(int)v[1], v[2], (SkillTags)(int)v[3]))
            .ToList();

        EquipmentItem item = ItemFactory.NewBlank();
        item.Restore(baseItem, (Rarity)s.Rarity, s.Ilvl, s.Name, baseStats, affixes);
        return item;
    }

    private static SaveCharacter ToSave(CharacterData c)
    {
        var s = new SaveCharacter
        {
            Name = c.Name,
            ClassId = c.ClassId,
            Level = c.Level,
            Experience = c.Experience,
            Passives = c.Passives.ToList(),
            Ascendancy = c.Ascendancy,
            AscendancyNodes = c.AscendancyNodes.ToList(),
            AscendancyPoints = c.AscendancyPoints,
            Gems = c.Gems,
            Loadout = c.Loadout.ToList(),
            Shards = c.Shards.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            Gold = c.Gold,
            HighestTierCleared = c.HighestTierCleared,
            RealitiesCleared = c.RealitiesCleared,
            Deaths = c.Deaths,
            TotalKills = c.TotalKills,
            SurvivalRuns = c.SurvivalRuns,
            BestSurvivalTime = c.BestSurvivalTime,
        };
        foreach ((EquipmentSlot slot, EquipmentItem item) in c.Equipped)
        {
            SaveItem si = ToSave(item);
            si.Slot = slot.ToString();
            s.Items.Add(si);
        }

        foreach ((EquipmentItem item, Vector2I cell) in c.Inventory)
        {
            SaveItem si = ToSave(item);
            si.X = cell.X;
            si.Y = cell.Y;
            s.Items.Add(si);
        }

        return s;
    }

    private static CharacterData FromSave(SaveCharacter s)
    {
        var c = new CharacterData
        {
            Name = s.Name,
            ClassId = s.ClassId,
            Level = Mathf.Clamp(s.Level, 1, CharacterData.MaxLevel),
            Experience = s.Experience,
            Passives = new HashSet<string>(s.Passives),
            Ascendancy = s.Ascendancy,
            AscendancyNodes = new HashSet<string>(s.AscendancyNodes),
            AscendancyPoints = s.AscendancyPoints,
            Gems = s.Gems ?? new List<GemData>(),
            Gold = s.Gold,
            HighestTierCleared = s.HighestTierCleared,
            RealitiesCleared = s.RealitiesCleared,
            Deaths = s.Deaths,
            TotalKills = s.TotalKills,
            SurvivalRuns = s.SurvivalRuns,
            BestSurvivalTime = s.BestSurvivalTime,
        };
        for (int i = 0; i < CharacterData.LoadoutSlots && i < s.Loadout.Count; i++)
        {
            c.Loadout[i] = s.Loadout[i] ?? new LoadoutSlot();
        }

        foreach ((string tier, int n) in s.Shards)
        {
            if (int.TryParse(tier, out int t))
            {
                c.Shards[t] = n;
            }
        }

        c.Passives.Add(c.Class.TreeStart);
        LoadItems(c, s.Items);
        return c;
    }

    private static void LoadItems(CharacterData c, IEnumerable<SaveItem> items)
    {
        foreach (SaveItem si in items)
        {
            if (FromSave(si) is not { } item)
            {
                continue;
            }

            if (si.Slot is not null && Enum.TryParse(si.Slot, out EquipmentSlot slot))
            {
                c.Equipped[slot] = item;
            }
            else
            {
                c.Inventory.Add((item, new Vector2I(si.X, si.Y)));
            }
        }
    }

    public void Save()
    {
        if (!Persist)
        {
            return;
        }

        var file = new SaveFile { Active = ActiveIndex, Characters = Characters.Select(ToSave).ToList() };
        using FileAccess? f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Load()
    {
        Characters.Clear();
        if (!FileAccess.FileExists(SavePath))
        {
            return;
        }

        try
        {
            using FileAccess? f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
            if (f is null || JsonSerializer.Deserialize<SaveFile>(f.GetAsText()) is not { } file)
            {
                return;
            }

            if (file.Characters.Count > 0)
            {
                Characters.AddRange(file.Characters.Select(FromSave));
                ActiveIndex = Mathf.Clamp(file.Active, 0, Characters.Count - 1);
                return;
            }

            // Version 1: one implicit character with gear and gold.
            if (file.Items is { Count: > 0 } || file.Gold > 0)
            {
                CharacterData migrated = CharacterData.Create("Exile", "arcanist");
                migrated.Gold = file.Gold;
                LoadItems(migrated, file.Items ?? new List<SaveItem>());
                Characters.Add(migrated);
                ActiveIndex = 0;
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"Profile: could not read {SavePath} ({e.Message}); starting fresh.");
        }
    }

    /// <summary>Wipes every character.</summary>
    public void Reset()
    {
        Characters.Clear();
        ActiveIndex = 0;
        Save();
    }
}
