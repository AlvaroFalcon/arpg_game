#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ArpgGame.Characters;
using ArpgGame.Net;

namespace ArpgGame.Core;

public partial class Profile
{
    /// <summary>
    /// Co-op host: is this character JSON (from a joining player) sane? Returns
    /// null if it is, else the reason it was refused — docs/design/multiplayer.md
    /// -> "Security". Characters are trusted (friends only, no anti-cheat), so
    /// this is not a legality check of the build: it only keeps out what could
    /// break the host — wrong shapes, absurd sizes, non-finite or huge numbers,
    /// out-of-range levels. Reads plain data only: no item node is created.
    /// </summary>
    public static string? ValidateCharacterJson(string json, out string name, out int level)
    {
        name = "";
        level = 0;
        SaveCharacter? s;
        try
        {
            s = JsonSerializer.Deserialize<SaveCharacter>(json);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return "the character data could not be read";
        }

        if (s is null)
        {
            return "the character data is empty";
        }

        name = s.Name ?? "";
        level = s.Level;
        if (name.Trim().Length == 0 || name.Length > NetLimits.NameChars)
        {
            return "bad character name";
        }

        if (s.Level is < 1 or > CharacterData.MaxLevel || !Finite(s.Experience) || s.Experience < 0f)
        {
            return $"level {s.Level} is out of range";
        }

        if (!ClassCatalog.Classes.Any(c => c.Id == s.ClassId))
        {
            return "unknown class";
        }

        if (s.Ascendancy is { } asc && asc.Length > NetLimits.IdChars)
        {
            return "bad ascendancy";
        }

        if (s.Passives is null || s.Passives.Count > NetLimits.MaxPassives || s.Passives.Any(BadId)
            || s.AscendancyNodes is null || s.AscendancyNodes.Count > NetLimits.MaxAscendancyNodes || s.AscendancyNodes.Any(BadId)
            || s.AscendancyPoints is < 0 or > 100)
        {
            return "bad passive tree";
        }

        if (s.Gems is null || s.Gems.Count > NetLimits.MaxGems
            || s.Gems.Any(g => g is null || BadId(g.Id) || BadId(g.Uid) || g.Level is < 1 or > GemData.MaxLevel || !Finite(g.Experience) || g.Experience < 0f))
        {
            return "bad gems";
        }

        if (s.Loadout is null || s.Loadout.Count > CharacterData.LoadoutSlots
            || s.Loadout.Any(l => l is not null && ((l.SkillUid is { } uid && BadId(uid)) || l.SupportUids is null || l.SupportUids.Count > 8 || l.SupportUids.Any(BadId))))
        {
            return "bad skill loadout";
        }

        if (s.Items is null || s.Items.Count > NetLimits.MaxItems)
        {
            return "too many items";
        }

        foreach (SaveItem item in s.Items)
        {
            if (BadItem(item) is { } why)
            {
                return why;
            }
        }

        if (s.Shards is null || s.Shards.Count > 64 || s.Shards.Any(kv => kv.Key.Length > 8 || kv.Value < 0)
            || s.Currency is null || s.Currency.Count > 64 || s.Currency.Any(kv => BadId(kv.Key) || kv.Value < 0)
            || s.Gold < 0 || !Finite(s.BestSurvivalTime))
        {
            return "bad wallet";
        }

        if (s.Quests is { } quests && (quests.Count > NetLimits.MaxQuests
            || quests.Any(kv => BadId(kv.Key) || kv.Value is null || kv.Value.S is < 0 or > (int)ArpgGame.Quests.QuestStatus.Skipped || kv.Value.C is < 0 or > NetLimits.MaxQuestCount)))
        {
            return "bad quest data";
        }

        return null;
    }

    /// <summary>
    /// Co-op client: is one item's JSON (a gear drop the host sent) sane? Same
    /// checks as the items of a character in <see cref="ValidateCharacterJson"/>.
    /// Null if so — docs/design/multiplayer.md -> "Security" -> "A hostile host".
    /// </summary>
    public static string? ValidateItemJson(string json)
    {
        if (json.Length > 16 * 1024)
        {
            return "item too large";
        }

        SaveItem? item;
        try
        {
            item = JsonSerializer.Deserialize<SaveItem>(json);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return "the item could not be read";
        }

        return BadItem(item);
    }

    private static string? BadItem(SaveItem? item)
    {
        if (item is null || BadId(item.Base) || (item.Name?.Length ?? 0) > NetLimits.IdChars
            || item.Rarity < 0 || item.Rarity > (int)Items.Rarity.Set
            || item.Ilvl < 0 || item.Ilvl > NetLimits.MaxItemLevel
            || item.SealCount is < 0 or > 16
            || (item.Slot is { } slot && (slot.Length > NetLimits.IdChars || !Enum.TryParse(slot, out Items.EquipmentSlot parsed) || !Enum.IsDefined(parsed)))
            || item.X is < -1 or > 64 || item.Y is < -1 or > 64)
        {
            return "bad item";
        }

        if (item.BaseStats is null || item.BaseStats.Count > NetLimits.MaxBaseStatsPerItem
            || item.BaseStats.Any(v => v is null || v.Length != 4 || v.Any(f => !Sane(f))
                || !Enum.IsDefined(typeof(Stats.StatId), (int)v[0]) || !Enum.IsDefined(typeof(Stats.ModifierType), (int)v[1])))
        {
            return "bad item stats";
        }

        if (item.Affixes is null || item.Affixes.Count > NetLimits.MaxAffixesPerItem
            || item.Affixes.Any(a => a is null || BadId(a.Id) || a.Tier is < 0 or > 64 || !Sane(a.Value)))
        {
            return "bad item affixes";
        }

        return null;
    }

    private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    private static bool Sane(float v) => Finite(v) && Math.Abs(v) <= NetLimits.MaxStatValue;

    private static bool BadId(string? id) => id is null || id.Length > NetLimits.IdChars;
}
