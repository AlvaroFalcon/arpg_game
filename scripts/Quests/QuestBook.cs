#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ArpgGame.Characters;
using ArpgGame.Items;

namespace ArpgGame.Quests;

/// <summary>
/// The quest rules — docs/design/quests.md -> "Data model and code". Pure rules over
/// two progress maps (a character's and the account's), no scene needed, so it is
/// unit-testable headless. The Godot side (<see cref="QuestService"/>) feeds it the
/// events and the contexts and announces what changed.
/// </summary>
public sealed class QuestBook
{
    /// <summary>Gold a Mirror Jewel reward pays when the tree is already full.</summary>
    public const long JewelFallbackGold = 1000;

    /// <summary>Unix seconds, replaceable in tests.</summary>
    public static Func<long> Clock { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private readonly Dictionary<string, QuestProgress> _character;
    private readonly Dictionary<string, QuestProgress> _account;

    public QuestBook(Dictionary<string, QuestProgress> character, Dictionary<string, QuestProgress> account)
    {
        _character = character;
        _account = account;
    }

    private Dictionary<string, QuestProgress> Map(QuestDef def) => def.Scope == QuestScope.Account ? _account : _character;

    public QuestProgress? ProgressOf(string id) =>
        QuestCatalog.Get(id) is { } def && Map(def).TryGetValue(id, out QuestProgress? p) ? p : null;

    public QuestStatus StatusOf(string id) => ProgressOf(id)?.Status ?? QuestStatus.Locked;

    private void Set(QuestDef def, QuestStatus status)
    {
        Dictionary<string, QuestProgress> map = Map(def);
        if (!map.TryGetValue(def.Id, out QuestProgress? p))
        {
            map[def.Id] = p = new QuestProgress();
        }

        p.Status = status;
        p.Since = Clock();
    }

    /// <summary>Unlocks what became available. Returns the quests that just turned New.</summary>
    public IReadOnlyList<QuestDef> Evaluate(QuestContext ctx) => EvaluateCore(ctx, false);

    private List<QuestDef> EvaluateCore(QuestContext ctx, bool visit)
    {
        var unlocked = new List<QuestDef>();
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (QuestDef def in QuestCatalog.All)
            {
                if (StatusOf(def.Id) != QuestStatus.Locked)
                {
                    continue;
                }

                // Veterans: the first evaluation of a quest nobody has a record of.
                if (def.AlreadyDone?.Invoke(ctx) == true)
                {
                    Set(def, QuestStatus.Skipped);
                    changed = true;
                    continue;
                }

                if (def.After is { } after && StatusOf(after) is not (QuestStatus.Done or QuestStatus.Skipped))
                {
                    continue;
                }

                if ((def.AfterNextVisit && !visit) || !def.Unlock(ctx))
                {
                    continue;
                }

                Set(def, QuestStatus.New);
                unlocked.Add(def);
                changed = true;
            }
        }

        return unlocked;
    }

    /// <summary>
    /// Something happened. Advances the objectives it matches (a New or Active quest
    /// whose count is reached becomes Ready) and then unlocks what that opened
    /// (a <see cref="QuestEvents.HideoutEntered"/> also unlocks the "next visit"
    /// quests). Returns the quests that just turned Ready, then those that turned New.
    /// </summary>
    public IReadOnlyList<QuestDef> Notify(QuestContext ctx, QuestEvent e)
    {
        var result = new List<QuestDef>();
        foreach (QuestDef def in QuestCatalog.All)
        {
            if (def.Event != e.Id || StatusOf(def.Id) is not (QuestStatus.New or QuestStatus.Active)
                || (def.Match is { } match && !match(e)))
            {
                continue;
            }

            QuestProgress p = ProgressOf(def.Id)!;
            p.Count++;
            if (p.Count >= def.Count)
            {
                Set(def, QuestStatus.Ready);
                result.Add(def);
            }
        }

        result.AddRange(EvaluateCore(ctx, e.Id == QuestEvents.HideoutEntered));
        return result;
    }

    /// <summary>The explanation was read: New becomes Active.</summary>
    public void MarkRead(string id)
    {
        if (QuestCatalog.Get(id) is { } def && StatusOf(id) == QuestStatus.New)
        {
            Set(def, QuestStatus.Active);
        }
    }

    /// <summary>
    /// Claims a Ready quest: pays its rewards into <paramref name="sink"/> once, marks it
    /// Done (an account quest also leaves a Done mark on this character, so only the
    /// character that finished it lists it) and unlocks what follows. Anything else
    /// pays nothing and returns false. <paramref name="lines"/> describes the rewards.
    /// </summary>
    public bool TurnIn(string id, QuestContext ctx, IRewardSink sink, out IReadOnlyList<string> lines)
    {
        var text = new List<string>();
        lines = text;
        if (QuestCatalog.Get(id) is not { } def || StatusOf(id) != QuestStatus.Ready)
        {
            return false;
        }

        Set(def, QuestStatus.Done);
        if (def.Scope == QuestScope.Account)
        {
            _character[def.Id] = new QuestProgress { Status = QuestStatus.Done, Since = Clock() };
        }

        foreach (QuestReward reward in def.Rewards)
        {
            text.Add(Pay(reward, ctx, sink));
        }

        EvaluateCore(ctx, false);
        return true;
    }

    private static string Pay(QuestReward reward, QuestContext ctx, IRewardSink sink)
    {
        switch (reward.Kind)
        {
            case RewardKind.Gold:
                sink.Gold(reward.Amount);
                return $"+{reward.Amount:N0} gold";
            case RewardKind.Currency:
                sink.Currency(reward.Id, reward.Amount);
                return Plural(reward.Amount, Crafting.Find(reward.Id)?.Name ?? reward.Id);
            case RewardKind.Shard:
                int tier = reward.Id == "best" || !int.TryParse(reward.Id, out int fixedTier)
                    ? Math.Max(2, ctx.Character.HighestTierCleared)
                    : fixedTier;
                sink.Shards(tier, reward.Amount);
                return Plural(reward.Amount, "Mirror Shard") + $" (tier {tier})";
            case RewardKind.Jewel:
                if (sink.Jewel(reward.Amount))
                {
                    return Plural(reward.Amount, "Mirror Jewel");
                }

                sink.Gold(JewelFallbackGold * reward.Amount);
                return $"+{JewelFallbackGold * reward.Amount:N0} gold";
            case RewardKind.Gem:
                bool support = GemShop.Offers.Any(o => o.Support && o.Id == reward.Id);
                sink.Gem(reward.Id, support);
                return GemLoot.NameOf(reward.Id, support);
            default:
                return "";
        }
    }

    /// <summary>"6 Azure Dust", "1 Crown Orb", "2 Graft Shards".</summary>
    public static string Plural(int amount, string name) =>
        amount == 1 || name.EndsWith("Dust") ? $"{amount} {name}" : $"{amount} {name}s";

    /// <summary>A reward as the dialogue and the log list it: "1,000 gold", "2 Crown Orbs".</summary>
    public static string Describe(QuestReward reward, QuestContext? ctx = null) => reward.Kind switch
    {
        RewardKind.Gold => $"{reward.Amount:N0} gold",
        RewardKind.Currency => Plural(reward.Amount, Crafting.Find(reward.Id)?.Name ?? reward.Id),
        RewardKind.Shard => Plural(reward.Amount, "Mirror Shard") + (reward.Id == "best" ? " of your best cleared tier" : $" (tier {reward.Id})"),
        RewardKind.Jewel => Plural(reward.Amount, "Mirror Jewel"),
        RewardKind.Gem => GemLoot.NameOf(reward.Id, false),
        _ => "",
    };

    // --- queries ---------------------------------------------------------------------

    /// <summary>What a station shows: Ready, else New, else Active, else none.</summary>
    public QuestMarker MarkerAt(string station)
    {
        QuestMarker best = QuestMarker.None;
        foreach (QuestDef def in QuestCatalog.At(station))
        {
            QuestMarker m = StatusOf(def.Id) switch
            {
                QuestStatus.Ready => QuestMarker.Ready,
                QuestStatus.New => QuestMarker.New,
                QuestStatus.Active => QuestMarker.Active,
                _ => QuestMarker.None,
            };
            if (m > best)
            {
                best = m;
            }
        }

        return best;
    }

    /// <summary>The quest the station deals with first: Ready, then New, then Active.</summary>
    public QuestDef? FirstAt(string station)
    {
        QuestDef? best = null;
        QuestMarker bestMarker = QuestMarker.None;
        foreach (QuestDef def in QuestCatalog.At(station))
        {
            QuestMarker m = StatusOf(def.Id) switch
            {
                QuestStatus.Ready => QuestMarker.Ready,
                QuestStatus.New => QuestMarker.New,
                QuestStatus.Active => QuestMarker.Active,
                _ => QuestMarker.None,
            };
            if (m > bestMarker)
            {
                best = def;
                bestMarker = m;
            }
        }

        return best;
    }

    /// <summary>New plus Ready quests: what the J chip's badge counts.</summary>
    public int AttentionCount => Log().Count(q => q.Progress.Status is QuestStatus.New or QuestStatus.Ready);

    /// <summary>
    /// What the quest log lists, in catalog order: New, Active, Ready and Done quests.
    /// Skipped and Locked ones are not listed, and an account quest that another
    /// character finished is not either (it was never this character's to see).
    /// </summary>
    public IEnumerable<(QuestDef Def, QuestProgress Progress)> Log()
    {
        foreach (QuestDef def in QuestCatalog.All)
        {
            QuestProgress? p = ProgressOf(def.Id);
            if (p is null || p.Status is QuestStatus.Locked or QuestStatus.Skipped)
            {
                continue;
            }

            if (def.Scope == QuestScope.Account && p.Status == QuestStatus.Done
                && !(_character.TryGetValue(def.Id, out QuestProgress? mine) && mine.Status == QuestStatus.Done))
            {
                continue;
            }

            yield return (def, p);
        }
    }
}
