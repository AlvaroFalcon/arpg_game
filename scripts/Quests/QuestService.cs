#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;

namespace ArpgGame.Quests;

/// <summary>
/// The Godot side of the quests — docs/design/quests.md -> "The Godot facade". Builds the
/// book and its context from the <see cref="Profile"/> autoload, feeds it the events
/// of <see cref="GameEvents"/>, and announces what changed: it saves the profile, plays
/// the sound (<c>quest_new</c>, <c>quest_done</c>) and emits <see cref="Profile.QuestChanged"/>.
/// Every entry point is a no-op without a profile or a character, so scenes run
/// directly by tests keep working. (Named QuestService, not Quests: a class called
/// Quests would hide the <c>ArpgGame.Quests</c> namespace.)
/// </summary>
public static class QuestService
{
    /// <summary>The last reward claimed: the quest id and its reward line (the toast shows it).</summary>
    public static (string Id, string Text)? LastClaim { get; private set; }

    public static QuestContext? ContextOf(Profile profile) =>
        profile.HasCharacters ? new QuestContext { Character = profile.Active, Jewels = profile.Jewels } : null;

    public static QuestBook? BookOf(Profile profile) =>
        profile.HasCharacters ? new QuestBook(profile.Active.Quests, profile.Quests) : null;

    // --- changes ---------------------------------------------------------------------

    /// <summary>Unlocks whatever became available (hideout entry, a boss, a pickup).</summary>
    public static void Evaluate(Node context)
    {
        if (Profile.Get(context) is { } profile)
        {
            Evaluate(profile);
        }
    }

    public static void Evaluate(Profile profile) => Track(profile, (book, ctx) => book.Evaluate(ctx));

    /// <summary>Something happened: advances objectives, then unlocks what that opened.</summary>
    public static void Raise(Node context, string eventId, int value = 0, string key = "")
    {
        if (Profile.Get(context) is { } profile)
        {
            Raise(profile, eventId, value, key);
        }
    }

    public static void Raise(Profile profile, string eventId, int value = 0, string key = "") =>
        Track(profile, (book, ctx) => book.Notify(ctx, new QuestEvent(eventId, value, key)));

    /// <summary>The explanation was read: New becomes Active.</summary>
    public static void MarkRead(Profile profile, string id) => Track(profile, (book, _) => book.MarkRead(id));

    /// <summary>
    /// Claims a Ready quest into <paramref name="sink"/>. Plays the claim sound and
    /// remembers the reward line for the toast. Returns false if it was not Ready.
    /// </summary>
    public static bool TurnIn(Profile profile, string id, IRewardSink sink, out IReadOnlyList<string> lines)
    {
        IReadOnlyList<string> got = Array.Empty<string>();
        bool ok = false;
        Track(profile, (book, ctx) =>
        {
            ok = book.TurnIn(id, ctx, sink, out got);
            if (ok)
            {
                // Before the change is announced: the toast reads it when the signal arrives.
                LastClaim = (id, string.Join(" · ", got));
            }
        });
        lines = got;
        if (ok)
        {
            Sfx.Play(profile, "quest_claim", -2f);
        }

        return ok;
    }

    private static void Track(Profile profile, Action<QuestBook, QuestContext> action)
    {
        if (BookOf(profile) is not { } book || ContextOf(profile) is not { } ctx)
        {
            return;
        }

        Dictionary<string, QuestStatus> before = QuestCatalog.All.ToDictionary(q => q.Id, q => book.StatusOf(q.Id));
        action(book, ctx);
        List<QuestDef> changed = QuestCatalog.All.Where(q => book.StatusOf(q.Id) != before[q.Id]).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        profile.Save();
        if (changed.Any(q => book.StatusOf(q.Id) == QuestStatus.New))
        {
            Sfx.Play(profile, "quest_new", -4f);
        }

        if (changed.Any(q => book.StatusOf(q.Id) == QuestStatus.Ready))
        {
            Sfx.Play(profile, "quest_done");
        }

        foreach (QuestDef q in changed)
        {
            profile.EmitSignal(Profile.SignalName.QuestChanged, q.Id, (int)book.StatusOf(q.Id));
        }
    }

    // --- queries (null-safe) -----------------------------------------------------------

    public static QuestStatus StatusOf(Node context, string id) =>
        Profile.Get(context) is { } p && BookOf(p) is { } book ? book.StatusOf(id) : QuestStatus.Locked;

    public static QuestMarker MarkerAt(Node context, string station) =>
        Profile.Get(context) is { } p ? MarkerAt(p, station) : QuestMarker.None;

    public static QuestMarker MarkerAt(Profile profile, string station) =>
        BookOf(profile) is { } book ? book.MarkerAt(station) : QuestMarker.None;

    public static QuestDef? FirstAt(Node context, string station) =>
        Profile.Get(context) is { } p && BookOf(p) is { } book ? book.FirstAt(station) : null;

    public static IReadOnlyList<(QuestDef Def, QuestProgress Progress)> Log(Node context) =>
        Profile.Get(context) is { } p && BookOf(p) is { } book ? book.Log().ToList() : new List<(QuestDef, QuestProgress)>();

    /// <summary>New plus Ready quests (the J chip's badge).</summary>
    public static int Attention(Node context) =>
        Profile.Get(context) is { } p && BookOf(p) is { } book ? book.AttentionCount : 0;

    public static QuestContext? ContextOf(Node context) => Profile.Get(context) is { } p ? ContextOf(p) : null;
}

/// <summary>
/// Pays quest rewards while a scene runs (docs/design/quests.md -> "Where the events come
/// from"): gold and orbs into <c>Player.Wallet</c>, the source of truth while a scene runs
/// (orbs reach <c>Profile.Currency</c> when the profile captures the player), shards into
/// the character, jewels into the account (capped at the tree's size), gems like drops.
/// </summary>
public sealed class PlayerRewardSink : IRewardSink
{
    private readonly Player _player;
    private readonly Profile _profile;

    public PlayerRewardSink(Player player, Profile profile)
    {
        _player = player;
        _profile = profile;
    }

    public void Gold(long amount) => _player.Wallet.Add(Profile.GoldId, amount);

    public void Currency(string id, int amount) => _player.Wallet.Add(id, amount);

    public void Shards(int tier, int amount) => _profile.Active.AddShards(tier, amount);

    public bool Jewel(int amount)
    {
        if (_profile.Jewels.Found + amount > World.JewelTree.Sockets)
        {
            return false;
        }

        _profile.Jewels.Found += amount;
        return true;
    }

    public void Gem(string id, bool support)
    {
        (_, long gold) = GemLoot.Collect(_profile.Active, id, support);
        if (gold > 0)
        {
            Gold(gold);
        }
    }
}
