#nullable enable
using System;
using System.Collections.Generic;
using ArpgGame.Characters;

namespace ArpgGame.Quests;

/// <summary>Whose progress a quest keeps (docs/design/quests.md -> "Concepts").</summary>
public enum QuestScope
{
    /// <summary>Per character: ascendancy, Arehucaz, Odran's gems.</summary>
    Character,

    /// <summary>Once per account, for account-wide systems (Mirror jewels, the forge).</summary>
    Account,
}

/// <summary>Locked -> New -> Active -> Ready -> Done, plus Skipped for veterans. The numbers are saved.</summary>
public enum QuestStatus
{
    Locked = 0,
    New = 1,
    Active = 2,
    Ready = 3,
    Done = 4,
    Skipped = 5,
}

/// <summary>What a station shows over it, in priority order (a later value wins).</summary>
public enum QuestMarker
{
    None,
    Active,
    New,
    Ready,
}

public enum RewardKind
{
    Gold,
    Currency,
    Shard,
    Jewel,
    Gem,
}

/// <summary>
/// One reward. Currency: Id = an orb or dust id (<c>Crafting.Find</c>). Shard: Id =
/// "best" (the best cleared tier, at least 2) or a tier number. Gem: Id = skill or
/// support id (Amount 1). Jewel: Amount jewels.
/// </summary>
public sealed record QuestReward(RewardKind Kind, string Id = "", int Amount = 1);

/// <summary>One lesson about one station.</summary>
public sealed class QuestDef
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";

    /// <summary>Hideout station uid of the turn-in ("altar").</summary>
    public string Station { get; init; } = "";

    public string Speaker { get; init; } = "";
    public QuestScope Scope { get; init; }

    /// <summary>Prerequisite quest id (must be Done, or Skipped).</summary>
    public string? After { get; init; }

    /// <summary>Unlock only during a <see cref="QuestEvents.HideoutEntered"/> event: the
    /// prerequisite is turned in at a station, i.e. after this visit's event, so the next
    /// one is necessarily a later visit, even across a restart. No counter to save.</summary>
    public bool AfterNextVisit { get; init; }

    public Func<QuestContext, bool> Unlock { get; init; } = _ => true;

    /// <summary>Migration: true on the first evaluation means <see cref="QuestStatus.Skipped"/>.</summary>
    public Func<QuestContext, bool>? AlreadyDone { get; init; }

    /// <summary>The <see cref="QuestEvents"/> id that advances the objective.</summary>
    public string Event { get; init; } = "";

    public Func<QuestEvent, bool>? Match { get; init; }
    public int Count { get; init; } = 1;

    /// <summary>The dialogue; <c>**word**</c> marks gold emphasis, blank lines split paragraphs.</summary>
    public Func<QuestContext, string> Explain { get; init; } = _ => "";

    public string Objective { get; init; } = "";
    public string Hint { get; init; } = "";
    public string TurnIn { get; init; } = "";

    /// <summary>Where the quest lives, for the toast ("Odran the Trader, in the plaza").</summary>
    public string Toast { get; init; } = "";

    /// <summary>The tab the dialogue's primary button opens: "gems", "disenchant", "forge", "jewels".</summary>
    public string? OpenTab { get; init; }

    /// <summary>The dialogue's primary button text.</summary>
    public string OpenText { get; init; } = "Open";

    public IReadOnlyList<QuestReward> Rewards { get; init; } = Array.Empty<QuestReward>();
}

/// <summary>Saved state of one quest.</summary>
public sealed class QuestProgress
{
    public QuestStatus Status { get; set; }
    public int Count { get; set; }

    /// <summary>Unix seconds of the last status change.</summary>
    public long Since { get; set; }
}

public readonly record struct QuestEvent(string Id, int Value = 0, string Key = "");

/// <summary>What the predicates read.</summary>
public sealed class QuestContext
{
    public CharacterData Character { get; init; } = null!;

    /// <summary>The account's Mirror jewels (null: none known).</summary>
    public World.JewelData? Jewels { get; init; }
}

/// <summary>Where rewards go. The hideout's sink pays into <c>Player.Wallet</c> and the character.</summary>
public interface IRewardSink
{
    void Gold(long amount);
    void Currency(string id, int amount);
    void Shards(int tier, int amount);

    /// <summary>Adds jewels to the account; false when the tree is already full (the book pays gold instead).</summary>
    bool Jewel(int amount);

    void Gem(string id, bool support);
}

/// <summary>Event ids (the same strings as <see cref="Core.GameEvents"/>).</summary>
public static class QuestEvents
{
    public const string HideoutEntered = Core.GameEvents.HideoutEntered;
    public const string BossKilled = Core.GameEvents.BossKilled;
    public const string JewelFound = Core.GameEvents.JewelFound;
    public const string ShardFound = Core.GameEvents.ShardFound;
    public const string AscendancyChosen = Core.GameEvents.AscendancyChosen;
    public const string JewelSocketed = Core.GameEvents.JewelSocketed;
    public const string ShardBought = Core.GameEvents.ShardBought;
    public const string Disenchanted = Core.GameEvents.Disenchanted;
    public const string Forged = Core.GameEvents.Forged;
    public const string GemBought = Core.GameEvents.GemBought;
    public const string ItemSold = Core.GameEvents.ItemSold;
}
