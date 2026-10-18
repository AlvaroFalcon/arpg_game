#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Quests;
using ArpgGame.World;

namespace ArpgGame.Tests;

/// <summary>
/// Headless smoke test for the quests (docs/design/quests.md -> "Test plan"). Run with:
/// ./scripts/run_all_tests.sh QuestSmokeTest
///
/// The rules first, on <see cref="QuestBook"/> alone (no scene), then the save format
/// and the co-op caps, then the live pieces in a hideout: markers, the E prompt, the
/// dialogue, the auto turn-in, the log, the toast. ALTAR-CONFIRM lives in
/// EndgameSmokeTest (it was built ahead of the quests) and the introduction's gold in
/// its GEMSHOP-AFFORDABLE-AFTER-INTRO; QUEST-ODRAN-AFFORDABLE checks the same promise
/// from the quest's side.
/// </summary>
public partial class QuestSmokeTest : Node2D
{
    private bool _failed;
    private const string ResultPath = "user://test_results/QuestSmokeTest.txt";
    private readonly System.Text.StringBuilder _log = new();

    public override void _Ready()
    {
        GD.Print("[QUESTTEST] Ready.");
        _ = RunAll();
    }

    // The hideout touches the NavigationServer and may hang at shutdown (CLAUDE.md):
    // every line is mirrored to a file the runner reads.
    private void Log(string line)
    {
        GD.Print(line);
        _log.AppendLine(line);
        DirAccess.MakeDirRecursiveAbsolute("user://test_results");
        using FileAccess? f = FileAccess.Open(ResultPath, FileAccess.ModeFlags.Write);
        f?.StoreString(_log.ToString());
    }

    private void Check(string name, bool ok, string detail)
    {
        Log($"[QUESTTEST] {name}: {(ok ? "PASS" : "FAIL")} — {detail}");
        if (!ok)
        {
            _failed = true;
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private async Task Process(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task RunAll()
    {
        try
        {
            await Frames(2);
            RuleChecks();
            PersistChecks();
        }
        catch (System.Exception e)
        {
            Check("EXCEPTION", false, e.ToString());
        }

        Log($"[QUESTTEST] RESULT: {(_failed ? "FAIL" : "PASS")}");
        GetTree().Quit(_failed ? 1 : 0);
    }

    // --- pure rules --------------------------------------------------------------------

    private sealed class TestSink : IRewardSink
    {
        public long GoldPaid;
        public readonly Dictionary<string, int> Orbs = new();
        public readonly Dictionary<int, int> Shard = new();
        public int Jewels;
        public int JewelRoom = 100;

        public void Gold(long amount) => GoldPaid += amount;

        public void Currency(string id, int amount) => Orbs[id] = Orbs.GetValueOrDefault(id) + amount;

        public void Shards(int tier, int amount) => Shard[tier] = Shard.GetValueOrDefault(tier) + amount;

        public bool Jewel(int amount)
        {
            if (JewelRoom < amount)
            {
                return false;
            }

            JewelRoom -= amount;
            Jewels += amount;
            return true;
        }

        public void Gem(string id, bool support)
        {
        }
    }

    private static QuestContext Ctx(CharacterData c, JewelData? jewels = null) => new() { Character = c, Jewels = jewels ?? new JewelData() };

    private static QuestBook BookOf(CharacterData c, Dictionary<string, QuestProgress> account) => new(c.Quests, account);

    private static void Ready(QuestBook book, QuestContext ctx, string id, QuestEvent e)
    {
        book.Evaluate(ctx);
        book.Notify(ctx, e);
    }

    private void RuleChecks()
    {
        // The catalog.
        var ids = QuestCatalog.All.Select(q => q.Id).ToList();
        CharacterData probe = CharacterData.Create("Probe", "warrior");
        probe.HighestTierCleared = 5;
        probe.AscendancyPoints = 4;
        QuestContext probeCtx = Ctx(probe, new JewelData { Found = 1 });
        string badText = string.Join(", ", QuestCatalog.All.Where(q => new[] { q.Title, q.Toast, q.Objective, q.Hint, q.TurnIn, q.Speaker, q.OpenText, q.Explain(probeCtx) }.Any(string.IsNullOrWhiteSpace)).Select(q => q.Id));
        string badStation = string.Join(", ", QuestCatalog.All.Where(q => HideoutCatalog.Prop(q.Station) is not { Station: true }).Select(q => q.Id));
        string badOrb = string.Join(", ", QuestCatalog.All.SelectMany(q => q.Rewards).Where(r => r.Kind == RewardKind.Currency && Items.Crafting.Find(r.Id) is null).Select(r => r.Id));
        string badAfter = string.Join(", ", QuestCatalog.All.Where(q => q.After is { } a && QuestCatalog.Get(a) is null).Select(q => q.Id));
        Check("QUEST-CATALOG-VALID", ids.Count == 6 && ids.Distinct().Count() == 6 && badText.Length == 0 && badStation.Length == 0 && badOrb.Length == 0 && badAfter.Length == 0,
            $"{ids.Count} quests, ids unique={ids.Distinct().Count() == ids.Count}; missing text: [{badText}], unknown stations: [{badStation}], unknown orbs: [{badOrb}], unknown prerequisites: [{badAfter}]");

        // Unlocking the altar's quest (the tier-5 boss).
        var account = new Dictionary<string, QuestProgress>();
        CharacterData hero = CharacterData.Create("Hero", "warrior");
        QuestBook book = BookOf(hero, account);
        QuestContext ctx = Ctx(hero);
        book.Evaluate(ctx);
        bool lockedFirst = book.StatusOf(QuestCatalog.AscendId) == QuestStatus.Locked && book.MarkerAt("altar") == QuestMarker.None;
        hero.AscendancyPoints = 4;
        IReadOnlyList<QuestDef> unlocked = book.Evaluate(ctx);
        Check("QUEST-UNLOCK-ASCEND", lockedFirst && unlocked.Any(q => q.Id == QuestCatalog.AscendId) && book.StatusOf(QuestCatalog.AscendId) == QuestStatus.New && book.MarkerAt("altar") == QuestMarker.New,
            $"locked before the points={lockedFirst}; with 4 points: {book.StatusOf(QuestCatalog.AscendId)}, altar marker {book.MarkerAt("altar")}");

        // Veterans.
        CharacterData old = CharacterData.Create("Old", "warrior");
        old.AscendancyPoints = 4;
        old.Ascendancy = "paladin";
        old.ShardVendorStock = 3;
        old.HighestTierCleared = 6;
        var oldAccount = new Dictionary<string, QuestProgress>();
        QuestBook oldBook = BookOf(old, oldAccount);
        oldBook.Evaluate(Ctx(old, new JewelData { Found = 4, Socketed = { "b0s1" } }));
        Check("QUEST-SKIP-VETERAN", oldBook.StatusOf(QuestCatalog.AscendId) == QuestStatus.Skipped && oldBook.MarkerAt("altar") == QuestMarker.None
            && oldBook.StatusOf(QuestCatalog.ArehucazId) == QuestStatus.Skipped && oldBook.StatusOf(QuestCatalog.JewelId) == QuestStatus.Skipped
            && !oldBook.Log().Any(q => q.Def.Id is QuestCatalog.AscendId or QuestCatalog.ArehucazId or QuestCatalog.JewelId) && oldBook.StatusOf(QuestCatalog.OdranId) == QuestStatus.New,
            $"ascendancy chosen → {oldBook.StatusOf(QuestCatalog.AscendId)}, he has sold shards before → {oldBook.StatusOf(QuestCatalog.ArehucazId)}, a socketed jewel → {oldBook.StatusOf(QuestCatalog.JewelId)}; none listed; Odran's has no record to check → {oldBook.StatusOf(QuestCatalog.OdranId)}");

        // Objective met, then claimed once.
        QuestEvent other = new(QuestEvents.AscendancyChosen, 0, "paladin");
        book.Notify(ctx, new QuestEvent(QuestEvents.Disenchanted, 5, "Common"));
        bool unrelated = book.StatusOf(QuestCatalog.AscendId) == QuestStatus.New;
        IReadOnlyList<QuestDef> ready = book.Notify(ctx, other);
        Check("QUEST-NOTIFY-READY", unrelated && ready.Any(q => q.Id == QuestCatalog.AscendId) && book.StatusOf(QuestCatalog.AscendId) == QuestStatus.Ready && book.MarkerAt("altar") == QuestMarker.Ready,
            $"an unrelated event leaves it {(unrelated ? "New" : "changed")}; ascendancy_chosen → {book.StatusOf(QuestCatalog.AscendId)}, marker {book.MarkerAt("altar")}");

        var sink = new TestSink();
        bool first = book.TurnIn(QuestCatalog.AscendId, ctx, sink, out IReadOnlyList<string> lines);
        long goldAfterFirst = sink.GoldPaid;
        bool second = book.TurnIn(QuestCatalog.AscendId, ctx, sink, out _);
        Check("QUEST-TURNIN-ONCE", first && !second && goldAfterFirst == 1000 && sink.GoldPaid == 1000 && sink.Orbs.GetValueOrDefault("crown_orb") == 2
            && sink.Orbs.GetValueOrDefault("tempest_orb") == 2 && sink.Orbs.GetValueOrDefault("tuning_orb") == 1 && book.StatusOf(QuestCatalog.AscendId) == QuestStatus.Done && book.MarkerAt("altar") == QuestMarker.None,
            $"first claim pays 1,000 gold, 2 Crown, 2 Tempest, 1 Tuning (got {sink.GoldPaid} gold, {string.Join(", ", sink.Orbs.Select(kv => $"{kv.Value} {kv.Key}"))}); the second returns {second} and pays nothing more; lines: {string.Join(" | ", lines)}");

        // A ready quest that is not turned in does not pay, and a New one cannot be claimed.
        var guard = new TestSink();
        bool notReady = !BookOf(CharacterData.Create("Z", "rogue"), new Dictionary<string, QuestProgress>()).TurnIn(QuestCatalog.OdranId, Ctx(hero), guard, out _) && guard.GoldPaid == 0;
        Check("QUEST-TURNIN-NEEDS-READY", notReady, "a quest that was never completed pays nothing");

        // Account scope.
        var shared = new Dictionary<string, QuestProgress>();
        CharacterData a = CharacterData.Create("A", "warrior");
        CharacterData b = CharacterData.Create("B", "rogue");
        QuestBook bookA = BookOf(a, shared);
        QuestBook bookB = BookOf(b, shared);
        QuestContext ctxA = Ctx(a);
        QuestContext ctxB = Ctx(b);
        bookA.Evaluate(ctxA);
        bookA.Notify(ctxA, new QuestEvent(QuestEvents.GemBought, 0, "spark"));
        var paySink = new TestSink();
        bookA.TurnIn(QuestCatalog.OdranId, ctxA, paySink, out _);
        bool noEarlyForge = bookA.StatusOf(QuestCatalog.DisenchantId) == QuestStatus.Locked;
        bookA.Notify(ctxA, new QuestEvent(QuestEvents.HideoutEntered));
        bookA.Notify(ctxA, new QuestEvent(QuestEvents.Disenchanted, 4, "Magic"));
        bookA.TurnIn(QuestCatalog.DisenchantId, ctxA, paySink, out _);
        bookB.Evaluate(ctxB);
        bookB.Notify(ctxB, new QuestEvent(QuestEvents.HideoutEntered));
        Check("QUEST-ACCOUNT-SCOPE", bookB.StatusOf(QuestCatalog.DisenchantId) == QuestStatus.Done && !bookB.Log().Any(q => q.Def.Id == QuestCatalog.DisenchantId)
            && bookA.Log().Any(q => q.Def.Id == QuestCatalog.DisenchantId) && bookB.Log().Any(q => q.Def.Id == QuestCatalog.OdranId) && bookB.StatusOf(QuestCatalog.OdranId) == QuestStatus.New,
            $"forge_disenchant done on A: A lists it, B doesn't and isn't offered it again; Odran's (per character) is New for B={bookB.StatusOf(QuestCatalog.OdranId)}");

        // The chain.
        var chainAccount = new Dictionary<string, QuestProgress>();
        CharacterData c = CharacterData.Create("C", "arcanist");
        QuestBook chain = BookOf(c, chainAccount);
        QuestContext chainCtx = Ctx(c);
        chain.Evaluate(chainCtx);
        chain.Notify(chainCtx, new QuestEvent(QuestEvents.GemBought));
        chain.TurnIn(QuestCatalog.OdranId, chainCtx, new TestSink(), out _);
        chain.Evaluate(chainCtx);
        bool waits = chain.StatusOf(QuestCatalog.DisenchantId) == QuestStatus.Locked;
        chain.Notify(chainCtx, new QuestEvent(QuestEvents.HideoutEntered));
        bool nextVisit = chain.StatusOf(QuestCatalog.DisenchantId) == QuestStatus.New;
        chain.Notify(chainCtx, new QuestEvent(QuestEvents.Disenchanted));
        chain.TurnIn(QuestCatalog.DisenchantId, chainCtx, new TestSink(), out _);
        Check("QUEST-CHAIN-FORGE", waits && nextVisit && chain.StatusOf(QuestCatalog.ForgeId) == QuestStatus.New,
            $"Odran's done: the forge waits for the next visit ({waits}), is New after hideout_entered ({nextVisit}); turning in the disenchant makes the forge quest {chain.StatusOf(QuestCatalog.ForgeId)} at once");

        // Arehucaz.
        CharacterData d = CharacterData.Create("D", "rogue");
        d.HighestTierCleared = 1;
        d.AddShards(2);
        QuestBook dealer = BookOf(d, new Dictionary<string, QuestProgress>());
        QuestContext dealerCtx = Ctx(d);
        dealer.Evaluate(dealerCtx);
        bool lockedAtOne = dealer.StatusOf(QuestCatalog.ArehucazId) == QuestStatus.Locked;
        d.HighestTierCleared = 2;
        dealer.Evaluate(dealerCtx);
        bool newAtTwo = dealer.StatusOf(QuestCatalog.ArehucazId) == QuestStatus.New;
        dealer.Notify(dealerCtx, new QuestEvent(QuestEvents.ShardBought, 2));
        d.HighestTierCleared = 7;
        var shardSink = new TestSink();
        dealer.TurnIn(QuestCatalog.ArehucazId, dealerCtx, shardSink, out _);
        Check("QUEST-AREHUCAZ-TIER2", lockedAtOne && newAtTwo && shardSink.Shard.GetValueOrDefault(7) == 1 && shardSink.GoldPaid == 1000,
            $"tier 1 → {(lockedAtOne ? "Locked" : "unlocked")}, tier 2 → {(newAtTwo ? "New" : "not New")}; the reward shard is of the best tier at turn-in: {string.Join(", ", shardSink.Shard.Select(kv => $"tier {kv.Key} ×{kv.Value}"))}");

        // Counted objectives (no tutorial quest counts, the model does).
        Check("QUEST-EXPLAIN-TEXT", QuestCatalog.Get(QuestCatalog.AscendId)!.Explain(probeCtx).Contains("a Barbarian, a Paladin or a Samurai") && QuestCatalog.Get(QuestCatalog.ArehucazId)!.Explain(probeCtx).Contains("tier 5 costs"),
            "the altar's text lists the class's ascendancies, Arehucaz's names the best cleared tier and its price");
    }

    // --- saves and co-op ---------------------------------------------------------------

    private void PersistChecks()
    {
        var profile = new Profile { Persist = false };
        CharacterData a = CharacterData.Create("Alpha", "warrior");
        CharacterData b = CharacterData.Create("Beta", "rogue");
        profile.Characters.Add(a);
        profile.Characters.Add(b);
        var statuses = new[] { QuestStatus.New, QuestStatus.Active, QuestStatus.Ready, QuestStatus.Done, QuestStatus.Skipped };
        a.Quests[QuestCatalog.OdranId] = new QuestProgress { Status = QuestStatus.Active, Count = 3, Since = 1234 };
        a.Quests[QuestCatalog.AscendId] = new QuestProgress { Status = QuestStatus.Ready };
        b.Quests[QuestCatalog.ArehucazId] = new QuestProgress { Status = QuestStatus.Done, Since = 99 };
        for (int i = 0; i < statuses.Length; i++)
        {
            profile.Quests[QuestCatalog.All[i].Id] = new QuestProgress { Status = statuses[i], Count = i };
        }

        string json = profile.ToJson();
        var back = new Profile { Persist = false };
        back.LoadJson(json);
        bool roundTrip = back.Characters.Count == 2
            && back.Characters[0].Quests[QuestCatalog.OdranId] is { Status: QuestStatus.Active, Count: 3, Since: 1234 }
            && back.Characters[0].Quests[QuestCatalog.AscendId].Status == QuestStatus.Ready
            && back.Characters[1].Quests[QuestCatalog.ArehucazId].Status == QuestStatus.Done
            && back.Quests.Count == statuses.Length && Enumerable.Range(0, statuses.Length).All(i => back.Quests[QuestCatalog.All[i].Id].Status == statuses[i] && back.Quests[QuestCatalog.All[i].Id].Count == i);

        // A save from before quests: no field at all.
        JsonNode file = JsonNode.Parse(json)!;
        file.AsObject().Remove("Quests");
        foreach (JsonNode? ch in file["Characters"]!.AsArray())
        {
            ch!.AsObject().Remove("Quests");
        }

        var legacy = new Profile { Persist = false };
        legacy.LoadJson(file.ToJsonString());
        bool oldSave = legacy.Characters.Count == 2 && legacy.Characters.All(ch => ch.Quests.Count == 0) && legacy.Quests.Count == 0;

        // Unknown ids dropped, statuses and counts clamped.
        JsonNode hostile = JsonNode.Parse(json)!;
        hostile["Quests"]!.AsObject()["not_a_quest"] = new JsonObject { ["S"] = 2, ["C"] = 1, ["T"] = 0 };
        hostile["Quests"]!.AsObject()[QuestCatalog.JewelId] = new JsonObject { ["S"] = 99, ["C"] = 500000, ["T"] = 5 };
        var cleaned = new Profile { Persist = false };
        cleaned.LoadJson(hostile.ToJsonString());
        bool dropped = !cleaned.Quests.ContainsKey("not_a_quest") && cleaned.Quests[QuestCatalog.JewelId] is { Status: QuestStatus.Skipped, Count: Net.NetLimits.MaxQuestCount };
        Check("QUEST-PERSIST", roundTrip && oldSave && dropped, $"every status and count round-trips (account {back.Quests.Count} entries, characters {back.Characters.Sum(ch => ch.Quests.Count)}); a save without the field loads empty={oldSave}; unknown id dropped and 99/500000 clamped={dropped}");
        profile.Free();
        back.Free();
        legacy.Free();
        cleaned.Free();

        // Co-op: the character payload carries quests, and the host caps them.
        a.Quests.Clear();
        a.Quests[QuestCatalog.OdranId] = new QuestProgress { Status = QuestStatus.Ready, Count = 1 };
        string valid = Profile.SerializeCharacter(a);
        JsonNode Mutate(System.Action<JsonObject> edit)
        {
            JsonNode node = JsonNode.Parse(valid)!;
            edit(node["Quests"]!.AsObject());
            return node;
        }

        JsonNode crowd = Mutate(q =>
        {
            for (int i = 0; i < 1000; i++)
            {
                q[$"quest_{i}"] = new JsonObject { ["S"] = 1, ["C"] = 0, ["T"] = 0 };
            }
        });
        JsonNode longId = Mutate(q => q[new string('x', 65)] = new JsonObject { ["S"] = 1, ["C"] = 0, ["T"] = 0 });
        JsonNode badStatus = Mutate(q => q[QuestCatalog.AscendId] = new JsonObject { ["S"] = 6, ["C"] = 0, ["T"] = 0 });
        JsonNode badCount = Mutate(q => q[QuestCatalog.AscendId] = new JsonObject { ["S"] = 1, ["C"] = 1000, ["T"] = 0 });
        JsonNode nullEntry = Mutate(q => q[QuestCatalog.AscendId] = null);
        JsonNode atCap = Mutate(q =>
        {
            for (int i = 0; i < Net.NetLimits.MaxQuests - 1; i++)
            {
                q[$"quest_{i}"] = new JsonObject { ["S"] = 5, ["C"] = 999, ["T"] = 0 };
            }
        });
        string? ok = Profile.ValidateCharacterJson(valid, out _, out _);
        string? many = Profile.ValidateCharacterJson(crowd.ToJsonString(), out _, out _);
        string? longReason = Profile.ValidateCharacterJson(longId.ToJsonString(), out _, out _);
        bool refused = many == "bad quest data" && longReason == "bad quest data"
            && Profile.ValidateCharacterJson(badStatus.ToJsonString(), out _, out _) == "bad quest data"
            && Profile.ValidateCharacterJson(badCount.ToJsonString(), out _, out _) == "bad quest data"
            && Profile.ValidateCharacterJson(nullEntry.ToJsonString(), out _, out _) == "bad quest data";
        Check("QUEST-VALIDATE-COOP", ok is null && refused && Profile.ValidateCharacterJson(atCap.ToJsonString(), out _, out _) is null,
            $"a payload with quests passes ({ok ?? "ok"}); 1,000 entries → '{many}', a 65-character id → '{longReason}', status 6, count 1000 and a null entry are refused; exactly {Net.NetLimits.MaxQuests} entries still pass");
    }
}
