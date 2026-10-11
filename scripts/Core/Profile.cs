#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using PathToExile.Combat;
using PathToExile.Items;
using PathToExile.Stats;

namespace PathToExile.Core;

/// <summary>
/// The persistent character — docs/design/menus.md -> "Profile". Everything
/// that survives between runs lives here: equipped gear, the inventory grid,
/// gold, and a handful of records. Runs themselves are disposable (the
/// survivor half of the game); gear is the part that carries over (the ARPG
/// half), which is why the level-up build resets every run but your items do not.
///
/// The live <see cref="Player"/> owns its <see cref="Inventory"/>/
/// <see cref="Items.Equipment"/> while a scene is running. Scenes call
/// <see cref="ApplyTo"/> when a player spawns and <see cref="CaptureFrom"/>
/// before leaving, and the profile is written to <c>user://profile.json</c> on
/// every capture.
/// </summary>
[GlobalClass]
public partial class Profile : Node
{
    public const string SavePath = "user://profile.json";

    /// <summary>Matches <c>resources/currency/Gold.tres</c>'s Id.</summary>
    public static readonly CurrencyId GoldId = "gold";

    private readonly List<(EquipmentItem Item, Vector2I Cell)> _inventory = new();
    private readonly Dictionary<EquipmentSlot, EquipmentItem> _equipped = new();

    public long Gold { get; private set; }
    public int RunsPlayed { get; private set; }
    public int Victories { get; private set; }
    public float BestTime { get; private set; }
    public int BestLevel { get; private set; }
    public int TotalKills { get; private set; }

    /// <summary>Skill id the player picked last time, preselected next run.</summary>
    public string LastStartingSkill { get; set; } = "fireball";

    public static Profile? Get(Node context) =>
        context.IsInsideTree() ? context.GetTree().Root.GetNodeOrNull<Profile>("Profile") : null;

    public override void _Ready()
    {
        Load();
    }

    public void ApplyTo(Player player)
    {
        foreach ((EquipmentSlot slot, EquipmentItem item) in _equipped)
        {
            player.Equipment.TryEquip(slot, item, out _);
        }

        foreach ((EquipmentItem item, Vector2I cell) in _inventory)
        {
            if (!player.Inventory.TryPlaceAt(item, cell))
            {
                player.Inventory.TryAdd(item);
            }
        }

        if (Gold > 0)
        {
            player.Wallet.Add(GoldId, Gold);
        }

        // Equipping must never leave the character spawning hurt.
        if (player.GetNodeOrNull<ActorStats>("Stats") is { Life: { } life })
        {
            life.Current = life.Max;
        }
    }

    public void CaptureFrom(Player player)
    {
        _equipped.Clear();
        foreach (EquipmentSlot slot in Enum.GetValues<EquipmentSlot>())
        {
            if (player.Equipment.GetEquipped(slot) is { } item)
            {
                _equipped[slot] = item;
            }
        }

        _inventory.Clear();
        foreach ((EquipmentItem item, Vector2I cell) in player.Inventory.Placements)
        {
            _inventory.Add((item, cell));
        }

        Gold = player.Wallet.GetBalance(GoldId);
        Save();
    }

    /// <summary>Adds an item straight into the stored inventory (end-of-run reward
    /// claimed while no player owns the grid). Returns false if nothing fits.</summary>
    public bool AddToStash(EquipmentItem item)
    {
        var occupancy = new bool[Inventory.Width, Inventory.Height];
        foreach ((EquipmentItem it, Vector2I cell) in _inventory)
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
                    _inventory.Add((item, new Vector2I(x, y)));
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

    public void RecordRun(float time, int level, int kills, bool victory)
    {
        RunsPlayed++;
        if (victory)
        {
            Victories++;
        }

        BestTime = Mathf.Max(BestTime, time);
        BestLevel = Mathf.Max(BestLevel, level);
        TotalKills += kills;
        Save();
    }

    public int StashCount => _inventory.Count;

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

    private sealed class SaveFile
    {
        public int Version { get; set; } = 1;
        public long Gold { get; set; }
        public int RunsPlayed { get; set; }
        public int Victories { get; set; }
        public float BestTime { get; set; }
        public int BestLevel { get; set; }
        public int TotalKills { get; set; }
        public string LastStartingSkill { get; set; } = "fireball";
        public List<SaveItem> Items { get; set; } = new();
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

    public void Save()
    {
        var file = new SaveFile
        {
            Gold = Gold,
            RunsPlayed = RunsPlayed,
            Victories = Victories,
            BestTime = BestTime,
            BestLevel = BestLevel,
            TotalKills = TotalKills,
            LastStartingSkill = LastStartingSkill,
        };
        foreach ((EquipmentSlot slot, EquipmentItem item) in _equipped)
        {
            SaveItem s = ToSave(item);
            s.Slot = slot.ToString();
            file.Items.Add(s);
        }

        foreach ((EquipmentItem item, Vector2I cell) in _inventory)
        {
            SaveItem s = ToSave(item);
            s.X = cell.X;
            s.Y = cell.Y;
            file.Items.Add(s);
        }

        using FileAccess? f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Load()
    {
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

            Gold = file.Gold;
            RunsPlayed = file.RunsPlayed;
            Victories = file.Victories;
            BestTime = file.BestTime;
            BestLevel = file.BestLevel;
            TotalKills = file.TotalKills;
            LastStartingSkill = file.LastStartingSkill;
            _equipped.Clear();
            _inventory.Clear();
            foreach (SaveItem s in file.Items)
            {
                if (FromSave(s) is not { } item)
                {
                    continue;
                }

                if (s.Slot is not null && Enum.TryParse(s.Slot, out EquipmentSlot slot))
                {
                    _equipped[slot] = item;
                }
                else
                {
                    _inventory.Add((item, new Vector2I(s.X, s.Y)));
                }
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"Profile: could not read {SavePath} ({e.Message}); starting fresh.");
        }
    }

    /// <summary>Wipes the profile (settings menu "Reset progress").</summary>
    public void Reset()
    {
        _equipped.Clear();
        _inventory.Clear();
        Gold = 0;
        RunsPlayed = 0;
        Victories = 0;
        BestTime = 0;
        BestLevel = 0;
        TotalKills = 0;
        Save();
    }
}
