#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Items;
using ArpgGame.Quests;
using ArpgGame.Run;
using ArpgGame.UI;
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
            await LiveChecks();
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
        Check("QUEST-EXPLAIN-TEXT", QuestCatalog.Get(QuestCatalog.AscendId)!.Explain(probeCtx).Contains("a Juggernaut or a Paladin") && QuestCatalog.Get(QuestCatalog.ArehucazId)!.Explain(probeCtx).Contains("tier 5 costs"),
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

    // --- live: hideout, reality, rift ------------------------------------------------------

    private static List<T> FindAll<T>(Node root) where T : Node
    {
        var found = new List<T>();
        void Walk(Node n)
        {
            if (n is T t)
            {
                found.Add(t);
            }

            foreach (Node child in n.GetChildren())
            {
                Walk(child);
            }
        }

        Walk(root);
        return found;
    }

    private async Task LiveChecks()
    {
        // Never touch the real account: the autoload is emptied for the test and put back after.
        Profile? profile = Profile.Get(this);
        if (profile is null)
        {
            Check("QUEST-LIVE", false, "the Profile autoload is missing");
            return;
        }

        string snapshot = profile.ToJson();
        bool persisted = profile.Persist;
        profile.Persist = false;
        try
        {
            await HideoutScenario(profile);
            await LogScenario(profile);
            await RealityScenario(profile);
            await IntroScenario(profile);
            await RiftScenario(profile);
        }
        finally
        {
            profile.LoadJson(snapshot);
            profile.Persist = persisted;
        }
    }

    private async Task<Hideout> NewHideout(Profile profile, CharacterData hero)
    {
        profile.Hideout = new HideoutData { GuideSeen = true };
        var hideout = GD.Load<PackedScene>("res://scenes/Hideout.tscn").Instantiate<Hideout>();
        AddChild(hideout);
        await Frames(4);
        await Process(2);
        _ = hero;
        return hideout;
    }

    private async Task LeaveHideout(Hideout hideout)
    {
        GetTree().Paused = false;
        PanelStack.Clear();
        hideout.QueueFree();
        await Frames(2);
    }

    private async Task HideoutScenario(Profile profile)
    {
        profile.Reset();
        CharacterData hero = profile.CreateCharacter("Quester", "warrior");
        hero.TutorialDone = false;
        hero.AscendancyPoints = AscendancyCatalog.FirstPoints;
        int newSounds = Sfx.RequestCount("quest_new");
        Hideout hideout = await NewHideout(profile, hero);
        HideoutHud hud = hideout.Hud;
        QuestToast toast = hideout.GetNode<QuestToast>("QuestToast");
        string banner = hud.BannerSub;
        Check("QUEST-ARRIVAL", QuestService.StatusOf(hideout, QuestCatalog.AscendId) == QuestStatus.New && banner == "1 new quest · J opens your quests"
            && toast.Showing is null && toast.Queued == 0 && hud.Tracker.RowIds.Contains(QuestCatalog.AscendId) && Sfx.RequestCount("quest_new") > newSounds,
            $"entering the hideout with 4 ascendancy points: ascend is {QuestService.StatusOf(hideout, QuestCatalog.AscendId)}, banner '{banner}', no new-quest card (showing {toast.Showing?.Kind} '{toast.Showing?.Title}', {toast.Queued} queued), in the tracker={hud.Tracker.RowIds.Contains(QuestCatalog.AscendId)}, quest_new requested={Sfx.RequestCount("quest_new") > newSounds}");

        // Markers: over the station, on the directory row, on both maps, on the E prompt, in the tracker, on the J chip.
        Vector2 altarAt = hideout.Data.Find("altar")!.Position;
        hideout.Player.GlobalPosition = HideoutTown.World(HideoutTown.SpawnCell);
        await Process(3);
        var marker = hideout.Props["altar"].GetNode<QuestMarker2D>("QuestMarker");
        bool inWorld = marker.Marker == QuestMarker.New && marker.Visible && hideout.Props["forge"].GetNode<QuestMarker2D>("QuestMarker").Marker == QuestMarker.None;
        bool onRow = hud.DirectoryMarkers["altar"] == QuestMarker.New && hud.DirectoryMarkers["forge"] == QuestMarker.None;
        bool onMaps = hud.Minimap.QuestMarkerOf("altar") == QuestMarker.New && hud.BigMap.QuestMarkerOf("altar") == QuestMarker.New;
        hideout.Player.GlobalPosition = altarAt + new Vector2(0f, 40f);
        await Process(3);
        string prompt = hud.PromptText;
        bool promptCarries = prompt.Contains("New quest") && hud.PromptMarker == QuestMarker.New && !marker.Visible;
        bool tracked = hud.Tracker.Visible && hud.Tracker.RowIds.Count == 1 && hud.Tracker.RowIds[0] == QuestCatalog.AscendId;
        int badge = hud.ChipBadge("J");
        Check("QUEST-HIDEOUT-MARKERS", inWorld && onRow && onMaps && promptCarries && tracked && badge == 1,
            $"world marker {marker.Marker}/visible far={inWorld}, directory row={onRow}, minimap and big map={onMaps}, prompt '{prompt}' (marker {hud.PromptMarker}, world marker hidden while it shows={!marker.Visible}), tracker rows {hud.Tracker.RowIds.Count}, J badge {badge}");

        // E at the altar: the dialogue first, the altar after.
        var altar = hideout.GetNode<AscendancyPanel>("AscendancyPanel");
        QuestDialog dialog = hideout.QuestDialog;
        Input.ActionPress("interact");
        await Process(2);
        Input.ActionRelease("interact");
        bool viaKey = dialog.IsOpen;
        if (!viaKey)
        {
            hideout.Interact("altar");
            await Process(1);
        }

        bool dialogFirst = dialog.IsOpen && !altar.IsOpen && dialog.Quest?.Id == QuestCatalog.AscendId && !dialog.TurnInMode && dialog.PrimaryText == "Open the altar";
        dialog.PressPrimary();
        await Process(1);
        bool then = altar.IsOpen && QuestService.StatusOf(hideout, QuestCatalog.AscendId) == QuestStatus.Active && QuestService.MarkerAt(hideout, "altar") == QuestMarker.Active
            && altar.QuestStripVisible && altar.QuestStripText.StartsWith("Quest: Choose an ascendancy") && !altar.QuestStripDone;
        Check("QUEST-E-DIALOG-FIRST", dialogFirst && then, $"E opened the dialogue and not the altar={dialogFirst} (by the real key={viaKey}); its primary opened the altar={altar.IsOpen}, quest Active={QuestService.StatusOf(hideout, QuestCatalog.AscendId)}, strip '{altar.QuestStripText}'");

        // Two clicks choose; the second makes the quest Ready (green strip, quest_done) and no card covers the panel.
        int doneSounds = Sfx.RequestCount("quest_done");
        bool armed = !altar.Choose("paladin") && altar.PendingChoice == "paladin";
        bool chosen = altar.Choose("paladin");
        bool ready = QuestService.StatusOf(hideout, QuestCatalog.AscendId) == QuestStatus.Ready && altar.QuestStripDone && altar.QuestStripText.StartsWith("Quest complete")
            && Sfx.RequestCount("quest_done") > doneSounds;
        bool noCard = !toast.AnnouncesDone(QuestCatalog.AscendId);
        Check("QUEST-ASCEND-READY", armed && chosen && ready && noCard, $"first click arms={armed}, second chooses={chosen}; Ready with a green strip '{altar.QuestStripText}' and quest_done requested={ready}; no 'complete' card over the panel={noCard}");

        // Closing the altar opens the turn-in; claiming pays exactly the table.
        Wallet wallet = hideout.Player.Wallet;
        long gold0 = wallet.GetBalance(Profile.GoldId);
        long crown0 = wallet.GetBalance("crown_orb");
        long tempest0 = wallet.GetBalance("tempest_orb");
        long tuning0 = wallet.GetBalance("tuning_orb");
        altar.Close();
        await Process(2);
        bool turnIn = dialog.IsOpen && dialog.TurnInMode && dialog.Quest?.Id == QuestCatalog.AscendId && dialog.PrimaryText == "Claim reward";
        dialog.PressPrimary();
        await Process(1);
        bool paid = wallet.GetBalance(Profile.GoldId) == gold0 + 1000 && wallet.GetBalance("crown_orb") == crown0 + 2 && wallet.GetBalance("tempest_orb") == tempest0 + 2 && wallet.GetBalance("tuning_orb") == tuning0 + 1;
        bool done = QuestService.StatusOf(hideout, QuestCatalog.AscendId) == QuestStatus.Done && QuestService.MarkerAt(hideout, "altar") == QuestMarker.None && hud.Tracker.RowIds.Count == 0
            && profile.Currency.GetValueOrDefault("crown_orb") >= 2 && hero.Gold >= 1000 && !hideout.QuestDialog.IsOpen;
        bool rewardCard = toast.Queued > 0 || toast.Showing is { Kind: QuestToast.Kind.Reward } || QuestService.LastClaim is { Id: QuestCatalog.AscendId };
        Check("QUEST-AUTO-TURNIN", turnIn && paid && done && rewardCard, $"closing the altar opened the turn-in={turnIn}; claim paid 1,000 gold + 2 Crown + 2 Tempest + 1 Tuning through the wallet={paid} (gold {wallet.GetBalance(Profile.GoldId) - gold0}); Done, no marker, tracker empty, captured into the profile={done}; reward line '{QuestService.LastClaim?.Text}'");

        // The forge: the objective is met inside the panel, which closes into the turn-in.
        profile.Quests[QuestCatalog.DisenchantId] = new QuestProgress { Status = QuestStatus.Active };
        var forge = hideout.GetNode<ArtificerPanel>("ArtificerPanel");
        forge.Open();
        var rng = new RandomNumberGenerator { Seed = 5 };
        EquipmentItem scrap = ItemFactory.RollRandom(20, DropSource.NormalMonster, 1f, rng);
        hideout.Player.Inventory.TryAdd(scrap);
        long azure0 = wallet.GetBalance("azure_dust");
        long grey0 = wallet.GetBalance("grey_dust");
        long forgeGold0 = wallet.GetBalance(Profile.GoldId);
        bool strip = forge.QuestStripVisible && forge.QuestStripText.Contains("Disenchant an item");
        forge.Disenchant(scrap);
        bool forgeReady = QuestService.StatusOf(hideout, QuestCatalog.DisenchantId) == QuestStatus.Ready && forge.QuestStripDone;
        bool noForgeCard = !toast.AnnouncesDone(QuestCatalog.DisenchantId);
        forge.Close();
        await Process(2);
        bool forgeTurnIn = dialog.IsOpen && dialog.TurnInMode && dialog.Quest?.Id == QuestCatalog.DisenchantId;
        dialog.PressPrimary();
        await Process(1);
        long dustAzure = wallet.GetBalance("azure_dust") - azure0;
        long dustGrey = wallet.GetBalance("grey_dust") - grey0;
        bool chained = QuestService.StatusOf(hideout, QuestCatalog.ForgeId) == QuestStatus.New && QuestService.MarkerAt(hideout, "forge") == QuestMarker.New;
        Check("QUEST-FORGE-FLOW", strip && forgeReady && noForgeCard && forgeTurnIn && dustAzure >= 6 && dustGrey >= 6 && wallet.GetBalance(Profile.GoldId) >= forgeGold0 + 100 && chained,
            $"the forge showed its quest strip={strip}; disenchanting made it Ready with no card={forgeReady && noForgeCard}; closing it opened the turn-in={forgeTurnIn}; reward dust azure +{dustAzure}, grey +{dustGrey} (at least 6 each, the item's own dust is in there too), gold +{wallet.GetBalance(Profile.GoldId) - forgeGold0}; the forge quest followed at once={chained}");

        // The mirror jewel with a full tree: gold instead, never past 69.
        profile.Jewels.Found = JewelTree.Sockets;
        profile.Quests[QuestCatalog.JewelId] = new QuestProgress { Status = QuestStatus.Ready };
        long jewelGold0 = wallet.GetBalance(Profile.GoldId);
        hideout.Interact("mirror");
        await Process(1);
        bool jewelDialog = dialog.IsOpen && dialog.TurnInMode && dialog.Quest?.Id == QuestCatalog.JewelId;
        dialog.PressPrimary();
        await Process(1);
        Check("QUEST-JEWEL-CAP", jewelDialog && profile.Jewels.Found == JewelTree.Sockets && wallet.GetBalance(Profile.GoldId) == jewelGold0 + 2000,
            $"tree full ({JewelTree.Sockets} found): the turn-in opened={jewelDialog}, Found stays {profile.Jewels.Found}, gold +{wallet.GetBalance(Profile.GoldId) - jewelGold0} (1,000 and 1,000 instead of the jewel)");

        // A new quest gets no card (owner, 2026-10-29): only its sound, the tracker and the log.
        hero.Quests.Clear();
        profile.Quests.Clear();
        hero.AscendancyPoints = 0;
        hero.Ascendancy = null;
        hero.TutorialDone = false;
        hero.HighestTierCleared = 1;
        var fresh = new QuestToast { Name = "FreshToast" };
        hideout.AddChild(fresh);
        int sounds = Sfx.RequestCount("quest_new");
        hero.TutorialDone = true;
        QuestService.Evaluate(hideout);
        bool unlocked = QuestService.StatusOf(hideout, QuestCatalog.OdranId) == QuestStatus.New;
        await Process(2);
        bool noNewCard = fresh.Showing is null && fresh.Queued == 0 && Sfx.RequestCount("quest_new") == sounds + 1;
        Check("QUEST-TOAST", unlocked && noNewCard, $"an unlock (Odran's wares {QuestService.StatusOf(hideout, QuestCatalog.OdranId)}) requests quest_new and shows no card: showing {fresh.Showing?.Kind}, {fresh.Queued} queued, sounds +{Sfx.RequestCount("quest_new") - sounds}");
        fresh.QueueFree();

        await LeaveHideout(hideout);
    }

    private async Task LogScenario(Profile profile)
    {
        profile.Reset();
        CharacterData hero = profile.CreateCharacter("Reader", "rogue");
        hero.TutorialDone = false;
        Hideout hideout = await NewHideout(profile, hero);
        QuestLogPanel log = hideout.GetNode<CharacterPanels>("CharacterPanels").Quests;

        // Empty first.
        Input.ActionPress("toggle_quests");
        await Process(2);
        Input.ActionRelease("toggle_quests");
        bool viaKey = log.IsOpen;
        if (!viaKey)
        {
            log.Open();
        }

        bool empty = log.IsOpen && log.ShowingEmpty && QuestLogPanel.EmptyText.StartsWith("No quests yet.");
        log.Close();
        await Process(1);

        hero.Quests[QuestCatalog.OdranId] = new QuestProgress { Status = QuestStatus.Done };
        hero.Quests[QuestCatalog.AscendId] = new QuestProgress { Status = QuestStatus.New };
        hero.Quests[QuestCatalog.ArehucazId] = new QuestProgress { Status = QuestStatus.Active };
        profile.Quests[QuestCatalog.JewelId] = new QuestProgress { Status = QuestStatus.Ready };
        log.Open();
        await Process(1);
        string[] expected = { "READY TO TURN IN (1)", QuestCatalog.JewelId, "ACTIVE (2)", QuestCatalog.AscendId, QuestCatalog.ArehucazId, "COMPLETED (1)" };
        bool sections = log.Listing.SequenceEqual(expected) && log.SelectedId == QuestCatalog.JewelId;
        log.OpenOn(QuestCatalog.AscendId);
        await Process(1);
        bool walkable = log.SelectedId == QuestCatalog.AscendId && log.CanWalk;
        log.WalkThere();
        await Frames(3);
        bool walking = !log.IsOpen && hideout.WalkingTo == "altar";
        hideout.Player.StopAutoWalk();

        // Walk there must actually arrive, for every quest's turn-in station.
        var walkNotes = new List<string>();
        bool allArrive = true;
        foreach (QuestDef def in QuestCatalog.All)
        {
            (def.Scope == QuestScope.Account ? profile.Quests : hero.Quests)[def.Id] = new QuestProgress { Status = QuestStatus.Active };
            log.Open();
            log.OpenOn(def.Id);
            await Process(1);
            if (log.SelectedId != def.Id)
            {
                allArrive = false;
                walkNotes.Add($"{def.Id}:NOT SELECTABLE");
                log.Close();
                continue;
            }

            log.WalkThere();
            int frames = 0;
            await Frames(2);
            while (hideout.WalkingTo is not null && frames < 60 * 40)
            {
                await Frames(1);
                frames++;
            }

            bool arrived = hideout.NearestStation()?.Prop.Uid == def.Station;
            allArrive &= arrived;
            walkNotes.Add($"{def.Id}->{def.Station}:{(arrived ? "ok" : "MISSED")}({frames}f, {hideout.Player.GlobalPosition.DistanceTo(hideout.Data.Find(def.Station)!.Position):0}px, nearest {hideout.NearestStation()?.Prop.Uid})");
        }

        // The real thing: a mouse click on the button (GUI path), then arrive.
        hero.Quests[QuestCatalog.AscendId] = new QuestProgress { Status = QuestStatus.Active };
        hideout.Player.GlobalPosition = new Vector2(1300, 900);
        await Frames(3);
        log.Open();
        log.OpenOn(QuestCatalog.AscendId);
        await Process(6);
        Vector2 click = log.WalkButtonRect.GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = click, GlobalPosition = click }, true);
        await Process(1);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = click, GlobalPosition = click }, true);
        Input.ActionPress("skill_slot_lmb");
        await Process(1);
        Input.ActionRelease("skill_slot_lmb");
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = click, GlobalPosition = click }, true);
        await Frames(3);
        bool clicked = !log.IsOpen && hideout.WalkingTo == "altar";
        int clickFrames = 0;
        while (hideout.WalkingTo is not null && clickFrames < 60 * 40)
        {
            await Frames(1);
            clickFrames++;
        }

        bool clickArrived = hideout.NearestStation()?.Prop.Uid == "altar";
        Check("QUEST-LOG-CLICK-WALKS", clicked && clickArrived, $"a mouse click at {click} (button rect {log.WalkButtonRect}) on Walk there closed the log={!log.IsOpen} and started the walk={clicked} (walking {hideout.WalkingTo}); arrived at the altar={clickArrived} ({clickFrames} frames)");
        Check("QUEST-LOG-WALK-ARRIVES", allArrive, string.Join(", ", walkNotes));
        Check("QUEST-LOG", empty && sections && walkable && walking, $"J opens the log (by the real key={viaKey}); empty state={empty}; sections [{string.Join(" | ", log.Listing)}] selected '{log.SelectedId}'; Walk there closed the log and the hideout is walking to '{hideout.WalkingTo}'");
        await LeaveHideout(hideout);
    }

    private async Task RealityScenario(Profile profile)
    {
        profile.Reset();
        CharacterData hero = profile.CreateCharacter("Slayer", "rogue");
        hero.TutorialDone = true;
        hero.Quests[QuestCatalog.OdranId] = new QuestProgress { Status = QuestStatus.Done };
        hero.Quests[QuestCatalog.ArehucazId] = new QuestProgress { Status = QuestStatus.Active };
        RealityLaunch.Tier = AscendancyCatalog.Tier1Unlock;
        var reality = GD.Load<PackedScene>(Scenes.Reality).Instantiate<RealityController>();
        AddChild(reality);
        await Frames(5);
        QuestToast toast = reality.GetNode<QuestToast>("QuestToast");
        int before = Sfx.RequestCount("quest_new");
        reality.Boss!.Health.ApplyResolvedDamage(1e9f, reality.Player);
        await Process(1);
        bool unlocked = hero.AscendancyPoints == AscendancyCatalog.FirstPoints && QuestService.StatusOf(reality, QuestCatalog.AscendId) == QuestStatus.New;
        bool shown = toast.Showing is null && toast.Queued == 0 && Sfx.RequestCount("quest_new") > before;
        Pickup jewel = Pickup.Spawn(reality, PickupKind.Jewel, 1f, reality.Player.GlobalPosition);
        reality.OnPickup(jewel, reality.Player);
        bool jewelQuest = profile.Jewels.Found == 1 && QuestService.StatusOf(reality, QuestCatalog.JewelId) == QuestStatus.New;
        bool tier = hero.HighestTierCleared == AscendancyCatalog.Tier1Unlock;
        Check("QUEST-BOSS-TOAST", unlocked && shown && jewelQuest && tier, $"a tier-{AscendancyCatalog.Tier1Unlock} boss kill: ascendancy points {hero.AscendancyPoints}, quest {QuestService.StatusOf(reality, QuestCatalog.AscendId)}; its sound played and no card showed={shown} ({toast.Showing?.Title}); a jewel pickup unlocks the Mirror's quest={jewelQuest}; the tier record is set={tier}");
        GetTree().Paused = false;
        PanelStack.Clear();
        reality.QueueFree();
        await Frames(2);
    }

    private async Task IntroScenario(Profile profile)
    {
        profile.Reset();
        profile.CreateCharacter("Newcomer", "warrior");
        RealityLaunch.Tutorial = true;
        var reality = GD.Load<PackedScene>(Scenes.Reality).Instantiate<RealityController>();
        AddChild(reality);
        await Frames(5);
        reality.Boss!.Health.ApplyResolvedDamage(1e9f, reality.Player);
        await Frames(2);
        float gold = reality.GetChildren().OfType<Pickup>().Where(k => k.Kind == PickupKind.Gold).Sum(k => k.Value) + reality.Player.Wallet.GetBalance(Profile.GoldId);
        long cheapest = GemShop.Offers.Min(o => o.Price);
        Check("QUEST-ODRAN-AFFORDABLE", gold >= cheapest && QuestService.StatusOf(reality, QuestCatalog.AscendId) == QuestStatus.Locked,
            $"a new character clearing the introduction holds {gold} gold, the cheapest gem costs {cheapest}; the introduction's boss unlocks no tier quest");
        reality.QueueFree();
        await Frames(2);
    }

    private async Task RiftScenario(Profile profile)
    {
        profile.Reset();
        profile.CreateCharacter("Runner", "arcanist").AscendancyPoints = 4;
        var rift = GD.Load<PackedScene>(Scenes.Run).Instantiate<Node>();
        AddChild(rift);
        await Frames(3);
        GameEvents.Raise(GameEvents.HideoutEntered);
        await Process(2);
        int toasts = FindAll<QuestToast>(rift).Count + FindAll<QuestTracker>(rift).Count + FindAll<QuestLogPanel>(rift).Count + FindAll<CharacterPanels>(rift).Count;
        Check("QUEST-NO-RIFT-UI", toasts == 0 && FindAll<QuestToast>(GetTree().Root).All(t => !rift.IsAncestorOf(t)),
            $"the Survival Rift has {toasts} quest toasts, trackers, logs or character panels (the J binding lives in the character panels)");
        rift.QueueFree();
        await Frames(2);
    }
}
