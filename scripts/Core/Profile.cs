#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Combat;
using ArpgGame.Items;
using ArpgGame.Stats;

namespace ArpgGame.Core;

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

    // --- Stash (docs/design/items.md -> "Stash"): account-wide, PoE-style ------------

    public const int StashColumns = 12;
    public const int StashRows = 10;
    public const int MaxStashTabs = 8;

    /// <summary>Stash tabs, each a list of items and their grid cells. The first
    /// tab is free; more are bought with gold.</summary>
    public List<List<(EquipmentItem Item, Vector2I Cell)>> StashTabs { get; } = new() { new() };

    /// <summary>Gold for the next tab: 150, 300, 600, 1 200… (doubling).</summary>
    /// <summary>Crafting orbs and dust by id, shared by every character
    /// (docs/design/crafting.md -> "Shared between characters").</summary>
    public Dictionary<string, long> Currency { get; } = new();

    /// <summary>The player-arranged hideout town (docs/design/hideout.md).</summary>
    public World.HideoutData Hideout { get; set; } = new();

    /// <summary>The Mirror's jewels, account-wide (docs/design/endgame.md -> "Mirror jewels").</summary>
    public World.JewelData Jewels { get; set; } = new();

    // --- Pending items (docs/design/items.md -> "Pending items"): reward overflow ---------

    /// <summary>Most rewards Pending items holds; past it the oldest is sold.</summary>
    public const int OverflowCap = 20;

    /// <summary>Pending items is shown like a stash tab: a 12×10 grid (anything fits).</summary>
    public const int OverflowColumns = StashColumns;
    public const int OverflowRows = StashRows;

    /// <summary>Rewards that found a full inventory, oldest first, account-wide.
    /// Receive-only: the player moves items out (stash panel → Pending), never in.</summary>
    public List<(EquipmentItem Item, Vector2I Cell)> Overflow { get; } = new();

    /// <summary>Where a claimed reward went.</summary>
    public readonly record struct RewardClaim(bool Stored, bool ToOverflow, EquipmentItem? SoldOldest, long SoldFor);

    /// <summary>
    /// A reward for the active character: its inventory if it fits, otherwise
    /// Pending items (<see cref="AddToOverflow"/>). Saves. Never fails while a
    /// profile exists — a reward is never lost to a full bag.
    /// </summary>
    public RewardClaim ClaimReward(EquipmentItem item)
    {
        if (AddToStash(item))
        {
            Save();
            return new RewardClaim(true, false, null, 0);
        }

        RewardClaim claim = AddToOverflow(item);
        Save();
        return claim;
    }

    /// <summary>
    /// Puts <paramref name="item"/> in Pending items. At <see cref="OverflowCap"/>
    /// (or when the grid has no room for it) the oldest item is sold to Odran at
    /// his price (<see cref="Crafting.SellPrice"/>) and the gold goes to the active
    /// character — the newest reward always gets in, and nothing vanishes for nothing.
    /// Does not save.
    /// </summary>
    public RewardClaim AddToOverflow(EquipmentItem item)
    {
        EquipmentItem? sold = null;
        long gold = 0;
        while (true)
        {
            if (Overflow.Count < OverflowCap && FirstFit(Overflow, OverflowColumns, OverflowRows, item) is { } cell)
            {
                Overflow.Add((item, cell));
                if (sold is not null)
                {
                    Active.Gold += gold;
                }

                return new RewardClaim(true, true, sold, gold);
            }

            if (Overflow.Count == 0)
            {
                return new RewardClaim(false, false, null, 0); // cannot happen: an empty 12×10 grid fits any item
            }

            (EquipmentItem oldest, Vector2I _) = Overflow[0];
            Overflow.RemoveAt(0);
            gold += Crafting.SellPrice(oldest);
            sold ??= oldest;
        }
    }

    private static Vector2I? FirstFit(List<(EquipmentItem Item, Vector2I Cell)> placed, int columns, int rows, EquipmentItem item)
    {
        var occupancy = new bool[columns, rows];
        foreach ((EquipmentItem it, Vector2I cell) in placed)
        {
            for (int y = 0; y < it.Descriptor.Height; y++)
            {
                for (int x = 0; x < it.Descriptor.Width; x++)
                {
                    if (cell.X + x < columns && cell.Y + y < rows && cell.X + x >= 0 && cell.Y + y >= 0)
                    {
                        occupancy[cell.X + x, cell.Y + y] = true;
                    }
                }
            }
        }

        int w = item.Descriptor.Width;
        int h = item.Descriptor.Height;
        for (int y = 0; y + h <= rows; y++)
        {
            for (int x = 0; x + w <= columns; x++)
            {
                bool free = true;
                for (int dy = 0; dy < h && free; dy++)
                {
                    for (int dx = 0; dx < w && free; dx++)
                    {
                        free = !occupancy[x + dx, y + dy];
                    }
                }

                if (free)
                {
                    return new Vector2I(x, y);
                }
            }
        }

        return null;
    }

    public long NextStashTabCost => StashTabs.Count >= MaxStashTabs ? -1 : 150L << (StashTabs.Count - 1);

    public int ActiveIndex { get; private set; }

    /// <summary>When false, nothing is written to disk (tests).</summary>
    public bool Persist { get; set; } = true;

    /// <summary>Where <see cref="Save"/> writes and <see cref="Load"/> reads (tests
    /// point it at a scratch file; the game always uses <see cref="SavePath"/>).</summary>
    public string FilePath { get; set; } = SavePath;

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
                Characters.Add(CharacterData.Create("Hero", "arcanist"));
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
        // scripts/coop_local.sh: the second window plays a throwaway copy of the
        // profile, so two instances on one PC never overwrite each other's save.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--profile-readonly") >= 0)
        {
            Persist = false;
        }

        Load();
    }

    public void Select(int index)
    {
        ActiveIndex = Mathf.Clamp(index, 0, Mathf.Max(0, Characters.Count - 1));
        Save();
    }

    public CharacterData CreateCharacter(string name, string classId)
    {
        CharacterData data = CharacterData.Create(string.IsNullOrWhiteSpace(name) ? "Hero" : name.Trim(), classId);
        data.TutorialDone = false;
        Characters.Add(data);
        ActiveIndex = Characters.Count - 1;
        Save();
        return data;
    }

    /// <summary>
    /// Admin mode's test character (docs/design/menus.md -> "Admin mode"): maximum
    /// level with every passive point free, the introduction skipped, gems at their
    /// top level, a million gold, three shards of every tier with all tiers counted
    /// as cleared, and 50 of every crafting orb for the account.
    /// </summary>
    public CharacterData CreateTestCharacter(string name, string classId)
    {
        CharacterData data = CharacterData.Create(string.IsNullOrWhiteSpace(name) ? "Tester" : name.Trim(), classId);
        data.Level = CharacterData.MaxLevel;
        data.TutorialDone = true;
        foreach (GemData gem in data.Gems)
        {
            gem.Level = GemData.MaxLevel;
        }

        data.Gold = 1_000_000;
        data.HighestTierCleared = World.RealityTiers.MaxTier;
        for (int tier = 1; tier <= World.RealityTiers.MaxTier; tier++)
        {
            data.AddShards(tier, 3);
        }

        foreach ((string orb, float _) in Items.DropTables.Orbs)
        {
            Currency[orb] = Currency.GetValueOrDefault(orb) + 50;
        }

        Currency[Items.DropTables.FallbackOrb] = Currency.GetValueOrDefault(Items.DropTables.FallbackOrb) + 50;
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

    /// <summary>
    /// Co-op host and clients: dresses another party member's player node from the
    /// character their own machine sent (docs/design/multiplayer.md -> "Players"):
    /// gear, gems, passives, class sprite. No wallet — their currency lives on
    /// their machine — and nothing here is ever saved.
    /// </summary>
    public static void ApplyCharacterTo(Player player, CharacterData c)
    {
        foreach ((EquipmentSlot slot, EquipmentItem item) in c.Equipped)
        {
            player.Equipment.TryEquip(slot, item, out _);
        }

        CharacterBuild.Attach(player, c);
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

        // The copy's bag is never shown: free the item nodes it was read into.
        foreach ((EquipmentItem item, Vector2I _) in c.Inventory)
        {
            if (GodotObject.IsInstanceValid(item) && !item.IsInsideTree())
            {
                item.QueueFree();
            }
        }

        c.Inventory.Clear();
    }

    /// <summary>One item as JSON and back (co-op: gear a remote player found).</summary>
    public static string SerializeItem(EquipmentItem item) => JsonSerializer.Serialize(ToSave(item));

    public static EquipmentItem? DeserializeItem(string json) =>
        JsonSerializer.Deserialize<SaveItem>(json) is { } s ? FromSave(s) : null;

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

        foreach ((string id, long amount) in Currency)
        {
            if (amount > 0)
            {
                player.Wallet.Add(id, amount);
            }
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
        Currency.Clear();
        foreach (CraftingCurrency currency in Crafting.All)
        {
            long amount = player.Wallet.GetBalance(currency.Id);
            if (amount > 0)
            {
                Currency[currency.Id] = amount;
            }
        }

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
        public int SealCount { get; set; }
        public string? Slot { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    private sealed class SaveAffix
    {
        public string Id { get; set; } = "";
        public int Tier { get; set; }
        public float Value { get; set; }
        public bool Crafted { get; set; }
        public bool Sealed { get; set; }
    }

    private sealed class SaveCharacter
    {
        public string Name { get; set; } = "Hero";
        public string ClassId { get; set; } = "arcanist";
        public int Level { get; set; } = 1;
        public float Experience { get; set; }
        public List<string> Passives { get; set; } = new();

        /// <summary><see cref="ArpgGame.Characters.PassiveTree.Version"/> the passives were saved with (absent = 1).</summary>
        public int TreeVersion { get; set; } = 1;
        public string? Ascendancy { get; set; }
        public List<string> AscendancyNodes { get; set; } = new();
        public int AscendancyPoints { get; set; }
        public List<GemData> Gems { get; set; } = new();
        public List<LoadoutSlot> Loadout { get; set; } = new();
        public Dictionary<string, int> Shards { get; set; } = new();
        public long Gold { get; set; }
        public Dictionary<string, long> Currency { get; set; } = new();
        public int HighestTierCleared { get; set; }
        public int RealitiesCleared { get; set; }
        public int? ShardVendorStock { get; set; }
        public int ShardVendorSince { get; set; }
        public int GemShopSeenLevel { get; set; }
        public int Deaths { get; set; }

        /// <summary>Null in saves from before the tutorial existed: those characters skip it.</summary>
        public bool? TutorialDone { get; set; }
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

        public List<List<SaveItem>> Stash { get; set; } = new();

        public Dictionary<string, long> Currency { get; set; } = new();

        public World.HideoutData? Hideout { get; set; }

        public World.JewelData? Jewels { get; set; }

        /// <summary>Pending items (reward overflow), oldest first.</summary>
        public List<SaveItem>? Overflow { get; set; }

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
        SealCount = item.SealCount,
        Affixes = item.RolledAffixes.Select(a => new SaveAffix { Id = a.Definition.Id, Tier = a.Tier, Value = a.Value, Crafted = a.Crafted, Sealed = a.Sealed }).ToList(),
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
                affixes.Add(new AffixRoller.RolledAffix(def, a.Tier, a.Value, a.Crafted, a.Sealed));
            }
        }

        var baseStats = s.BaseStats
            .Where(v => v.Length == 4)
            .Select(v => ((StatId)(int)v[0], (ModifierType)(int)v[1], v[2], (SkillTags)(int)v[3]))
            .ToList();

        EquipmentItem item = ItemFactory.NewBlank();
        item.Restore(baseItem, (Rarity)s.Rarity, s.Ilvl, s.Name, baseStats, affixes);
        item.SealCount = s.SealCount;
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
            TreeVersion = ArpgGame.Characters.PassiveTree.Version,
            Ascendancy = c.Ascendancy,
            AscendancyNodes = c.AscendancyNodes.ToList(),
            AscendancyPoints = c.AscendancyPoints,
            Gems = c.Gems,
            Loadout = c.Loadout.ToList(),
            Shards = c.Shards.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            Gold = c.Gold,
            HighestTierCleared = c.HighestTierCleared,
            RealitiesCleared = c.RealitiesCleared,
            ShardVendorStock = c.ShardVendorStock,
            ShardVendorSince = c.ShardVendorSince,
            GemShopSeenLevel = c.GemShopSeenLevel,
            Deaths = c.Deaths,
            TutorialDone = c.TutorialDone,
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
            // Saves from before orbs were account-wide carried them per character;
            // Load folds these into the account (Profile.Currency).
            Currency = s.Currency is null ? new Dictionary<string, long>() : new Dictionary<string, long>(s.Currency),
            HighestTierCleared = s.HighestTierCleared,
            RealitiesCleared = s.RealitiesCleared,
            ShardVendorStock = Mathf.Clamp(s.ShardVendorStock ?? World.ShardVendor.StockSize, 0, World.ShardVendor.StockSize),
            ShardVendorSince = s.ShardVendorSince,
            GemShopSeenLevel = Mathf.Clamp(s.GemShopSeenLevel, 0, CharacterData.MaxLevel),
            Deaths = s.Deaths,
            TutorialDone = s.TutorialDone ?? true,
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

        // A save from an older tree (other node ids): every point back, silently.
        if (s.TreeVersion < ArpgGame.Characters.PassiveTree.Version || c.Passives.Any(id => ArpgGame.Characters.PassiveTree.Get(id) is null))
        {
            ArpgGame.Characters.PassiveTree.ResetAll(c);
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

    /// <summary>One character as JSON and back — the exact save-file format (tests use it).</summary>
    public static string SerializeCharacter(CharacterData c) => JsonSerializer.Serialize(ToSave(c));

    public static CharacterData? DeserializeCharacter(string json) =>
        JsonSerializer.Deserialize<SaveCharacter>(json) is { } s ? FromSave(s) : null;

    public void Save()
    {
        if (!Persist)
        {
            return;
        }

        // Serialise first, then replace the file in one step: the old version opened
        // (truncated) the file before serialising, so anything that made the JSON
        // writer throw — a NaN or infinity anywhere in the account, e.g. from a
        // hostile co-op host — left an empty save and lost every character.
        string json;
        try
        {
            json = ToJson();
        }
        catch (Exception e)
        {
            GD.PushError($"Profile: not saved, the account could not be written ({e.Message}); the file on disk is unchanged.");
            return;
        }

        WriteAtomically(FilePath, json);
    }

    /// <summary>Writes <paramref name="text"/> to a temporary file next to
    /// <paramref name="path"/>, then moves it over the old file, so a crash or
    /// error mid-write never leaves a truncated save behind.</summary>
    public static bool WriteAtomically(string path, string text)
    {
        string target = ProjectSettings.GlobalizePath(path);
        string temp = target + ".tmp";
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            System.IO.File.WriteAllText(temp, text);
            System.IO.File.Move(temp, target, overwrite: true);
            return true;
        }
        catch (Exception e)
        {
            GD.PushError($"Profile: could not write {path} ({e.Message}).");
            try
            {
                System.IO.File.Delete(temp);
            }
            catch (Exception)
            {
                // nothing more to do
            }

            return false;
        }
    }

    /// <summary>The whole account as the save-file JSON (tests round-trip it).</summary>
    public string ToJson()
    {
        var file = new SaveFile
        {
            Active = ActiveIndex,
            Characters = Characters.Select(ToSave).ToList(),
            Stash = StashTabs.Select(tab => tab.Select(e => { SaveItem si = ToSave(e.Item); si.X = e.Cell.X; si.Y = e.Cell.Y; return si; }).ToList()).ToList(),
            Currency = new Dictionary<string, long>(Currency),
            Hideout = Hideout,
            Jewels = Jewels,
            Overflow = Overflow.Select(e => { SaveItem si = ToSave(e.Item); si.X = e.Cell.X; si.Y = e.Cell.Y; return si; }).ToList(),
        };
        return JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
    }

    public void Load()
    {
        LegacySave.Migrate();
        Characters.Clear();
        if (!FileAccess.FileExists(FilePath))
        {
            return;
        }

        try
        {
            using FileAccess? f = FileAccess.Open(FilePath, FileAccess.ModeFlags.Read);
            if (f is not null)
            {
                LoadJson(f.GetAsText());
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"Profile: could not read {FilePath} ({e.Message}); starting fresh.");
        }
    }

    /// <summary>Replaces this account with one read from save-file JSON.</summary>
    public void LoadJson(string json)
    {
        Characters.Clear();
        if (JsonSerializer.Deserialize<SaveFile>(json) is not { } file)
        {
            return;
        }

        StashTabs.Clear();
        foreach (List<SaveItem> tab in file.Stash)
        {
            var items = new List<(EquipmentItem, Vector2I)>();
            foreach (SaveItem si in tab)
            {
                if (FromSave(si) is { } item)
                {
                    items.Add((item, new Vector2I(si.X, si.Y)));
                }
            }

            StashTabs.Add(items);
        }

        if (StashTabs.Count == 0)
        {
            StashTabs.Add(new List<(EquipmentItem, Vector2I)>());
        }

        Overflow.Clear();
        foreach (SaveItem si in file.Overflow ?? new List<SaveItem>())
        {
            if (Overflow.Count < OverflowCap && FromSave(si) is { } item)
            {
                Overflow.Add((item, new Vector2I(Mathf.Clamp(si.X, 0, OverflowColumns - 1), Mathf.Clamp(si.Y, 0, OverflowRows - 1))));
            }
        }

        Currency.Clear();
        foreach ((string id, long n) in file.Currency ?? new Dictionary<string, long>())
        {
            Currency[id] = n;
        }

        Hideout = file.Hideout ?? new World.HideoutData();
        Jewels = file.Jewels ?? new World.JewelData();
        // Sockets that no longer exist (a reshaped tree) give their jewel back.
        Jewels.Socketed.RemoveWhere(id => World.JewelTree.Get(id) is null);

        if (file.Characters.Count > 0)
        {
            Characters.AddRange(file.Characters.Select(FromSave));
            ActiveIndex = Mathf.Clamp(file.Active, 0, Characters.Count - 1);
            // Orbs used to be per character: pool them into the account.
            foreach (CharacterData c in Characters)
            {
                foreach ((string id, long n) in c.Currency)
                {
                    Currency[id] = Currency.GetValueOrDefault(id) + n;
                }

                c.Currency.Clear();
            }

            return;
        }

        // Version 1: one implicit character with gear and gold.
        if (file.Items is { Count: > 0 } || file.Gold > 0)
        {
            CharacterData migrated = CharacterData.Create("Hero", "arcanist");
            migrated.Gold = file.Gold;
            LoadItems(migrated, file.Items ?? new List<SaveItem>());
            Characters.Add(migrated);
            ActiveIndex = 0;
        }
    }

    /// <summary>Wipes every character.</summary>
    public void Reset()
    {
        Characters.Clear();
        Currency.Clear();
        Hideout = new World.HideoutData();
        Jewels = new World.JewelData();
        StashTabs.Clear();
        StashTabs.Add(new List<(EquipmentItem, Vector2I)>());
        Overflow.Clear();
        ActiveIndex = 0;
        Save();
    }
}
