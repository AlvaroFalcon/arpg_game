#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Progression;
using ArpgGame.Run;
using ArpgGame.Skills;
using ArpgGame.Stats;
using ArpgGame.UI;

namespace ArpgGame.Tests;

/// <summary>
/// The Survival Rift's 2026-11 loop (docs/design/survival-roadmap.md): difficulty (M0, M4),
/// pick control (M1), chests (M2), evolutions (M5) and unlocks (M7) — rules and short
/// scene checks; the shrines and the timeline live in RiftShrineTest. Run:
///   ./scripts/run_headless.sh res://scenes/tests/RiftLoopTest.tscn --quit-after 6000
/// </summary>
public partial class RiftLoopTest : Node2D
{
    private const string ResultPath = "user://test_results/RiftLoopTest.txt";
    private readonly System.Text.StringBuilder _log = new();
    private bool _failed;
    private readonly RandomNumberGenerator _rng = new() { Seed = 4321 };

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _ = RunAll();
    }

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
        Log($"[RIFTLOOP] {name}: {(ok ? "PASS" : "FAIL")} — {detail}");
        _failed |= !ok;
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private async Task RunAll()
    {
        Profile? real = Profile.Get(this);
        string? snapshot = real?.ToJson();
        bool persisted = real?.Persist ?? false;
        if (real is not null)
        {
            real.Persist = false;
        }

        try
        {
            DifficultyChecks();
            await PickControlChecks();
            await PickControlUiChecks();
            await ChestRuleChecks();
            await ChestSceneChecks();
            await EvolutionChecks();
            UnlockChecks();
        }
        catch (System.Exception e)
        {
            Check("EXCEPTION", false, e.ToString());
        }

        GetTree().Paused = false;
        if (real is not null && snapshot is not null)
        {
            real.LoadJson(snapshot);
            real.Persist = persisted;
        }

        Log($"[RIFTLOOP] === RESULT: {(_failed ? "FAIL" : "PASS")} ===");
        GetTree().Quit(_failed ? 1 : 0);
    }

    private static int _spawnX = 30000;

    private async Task<(Player, RunBuild)> RunPlayer(params string[] skills)
    {
        var player = GD.Load<PackedScene>("res://scenes/Player.tscn").Instantiate<Player>();
        AddChild(player);
        player.GlobalPosition = new Vector2(_spawnX += 3000, 30000);
        var build = new RunBuild { Name = "RunBuild" };
        player.AddChild(build);
        foreach (SkillSlotId slot in CharacterBuild.Slots)
        {
            player.SkillUser.ClearSlot(slot);
        }

        RunController.FinishRunCharacter(player, build);
        await Frames(1);
        foreach (string id in skills)
        {
            build.AddSkill(id);
        }

        return (player, build);
    }

    // --- M0 / M4: difficulty ---------------------------------------------------------------

    private void DifficultyChecks()
    {
        var d = new RiftDifficulty();
        bool identity = d.IsDefault && d.Life == 1f && d.Damage == 1f && d.Spawn == 1f && d.Speed == 1f && d.SwarmInterval == 1f
            && d.Experience == 1f && d.Gold == 1f && d.RarityBonus == 0f && d.ItemLevelBonus == 0 && d.MagicChance == 0f && d.Describe() == "";
        RunRewards a = RunRewards.Preview(450f, 3, 600, true);
        RunRewards b = RunRewards.Preview(450f, 3, 600, true, d);
        Check("DIFFICULTY-DEFAULT-IS-IDENTITY", identity && a.Gold == b.Gold && a.OrbCount == b.OrbCount && a.ItemLevel == b.ItemLevel && Mathf.IsEqualApprox(a.RarityMultiplier, b.RarityMultiplier),
            $"every multiplier 1; a 7:30 win pays {a.Gold} gold / {a.OrbCount} orbs / ilvl {a.ItemLevel} with or without it");

        var t3 = new RiftDifficulty { Tier = 3 };
        t3.Fears.Add(RiftDifficulty.FindFear("thick")!);
        t3.Fears.Add(RiftDifficulty.FindFear("swift")!);
        RunRewards c = RunRewards.Preview(450f, 3, 600, true, t3);
        Check("TIER-AND-FEARS-SCALE", Mathf.IsEqualApprox(t3.Life, 2f * 1.4f) && Mathf.IsEqualApprox(t3.Damage, 1.4f) && Mathf.IsEqualApprox(t3.Speed, 1.2f)
                && c.Gold > a.Gold && c.ItemLevel == a.ItemLevel + 12 && c.RarityMultiplier > a.RarityMultiplier + 0.7f && t3.Describe().Contains("Tier 3"),
            $"tier 3 + Thick + Swift: life ×{t3.Life:0.00}, damage ×{t3.Damage:0.00}, speed ×{t3.Speed:0.00}; gold {a.Gold} → {c.Gold}, ilvl {a.ItemLevel} → {c.ItemLevel}, rarity ×{a.RarityMultiplier:0.00} → ×{c.RarityMultiplier:0.00}; \"{t3.Describe()}\"");

        var greedy = new RiftDifficulty { Greed = 3 };
        Check("GREED-STACKS", Mathf.IsEqualApprox(greedy.Life, 1.18f) && Mathf.IsEqualApprox(greedy.Experience, 1.3f) && Mathf.IsEqualApprox(greedy.Gold, 1.3f),
            $"Greed ×3: monsters ×{greedy.Life:0.00}, experience ×{greedy.Experience:0.00}, gold ×{greedy.Gold:0.00}");
        Check("TIER-MAGIC-MONSTERS", new RiftDifficulty { Tier = 3 }.MagicChance == 0f && new RiftDifficulty { Tier = 4 }.MagicChance > 0f && new RiftDifficulty { Tier = 7 }.MagicChance > new RiftDifficulty { Tier = 4 }.MagicChance,
            "magic monsters from tier 4, more from tier 7");
    }

    // --- M1: pick control ------------------------------------------------------------------

    private async Task PickControlChecks()
    {
        // Skips and Banishes per run count the account's Rift unlocks (+1 each from challenges):
        // start from none, or a machine whose real profile holds them fails these checks.
        Unlocks.ClearForTest();
        (Player player, RunBuild build) = await RunPlayer("fireball", "spark");
        bool skipKeystone = !build.CanSkip(5) && !build.CanSkip(10) && build.CanSkip(6);
        bool skipped = build.TrySkip(6) && build.TrySkip(7) && build.TrySkip(8) && !build.TrySkip(9) && build.SkipsLeft == 0;
        Check("SKIP-LIMITS", skipKeystone && skipped, $"keystone rows refuse a skip ({!build.CanSkip(5)}, {!build.CanSkip(10)}); {OfferRules.SkipsPerRun} skips then none (left {build.SkipsLeft})");

        build.SkipsLeft = 1;
        bool keyOk = build.ApplyKey(Net.RiftHost.SkipKey) && !build.ApplyKey(Net.RiftHost.SkipKey) && Net.RiftHost.WellFormedKey(Net.RiftHost.SkipKey, start: false) && !Net.RiftHost.WellFormedKey(Net.RiftHost.SkipKey, start: true);
        Check("SKIP-KEY", keyOk, "the co-op skip key applies once per charge, is well formed on a level row and refused on the start pick");

        List<UpgradeOffer> row = build.RollOffers(6, _rng);
        string target = row.First(o => o.Kind == UpgradeKind.Passive || o.Kind == UpgradeKind.SkillLevel).Identity;
        int index = row.FindIndex(o => o.Identity == target);
        UpgradeOffer? replacement = build.TryBanish(6, row, index, _rng);
        bool never = Enumerable.Range(0, 400).SelectMany(_ => build.RollOffers(6, _rng)).All(o => o.Identity != target);
        Check("BANISH-NEVER-RETURNS", replacement is not null && replacement.Identity != target && never && build.BanishesLeft == 1,
            $"banished {target}: replaced by {replacement?.Identity}, absent from 400 rows, {build.BanishesLeft} banish left");

        build.RerollsLeft = 0;
        long[] prices = Enumerable.Range(0, 4).Select(OfferRules.GoldRerollPrice).ToArray();
        bool broke = build.TryReroll(6, row, 0, _rng) is null;
        player.Wallet.Add(Profile.GoldId, 200);
        UpgradeOffer? bought = build.TryReroll(6, row, 0, _rng);
        long after = player.Wallet.GetBalance(Profile.GoldId);
        Check("REROLL-GOLD-PRICE", prices.SequenceEqual(new long[] { 25, 40, 64, 102 }) && broke && bought is not null && after == 175 && build.RerollPrice == 40,
            $"prices {string.Join(", ", prices)}; with no gold refused={broke}; with 200 gold one reroll → {after} left, next costs {build.RerollPrice}");

        int fours = 0;
        for (int i = 0; i < 400; i++)
        {
            fours += build.RollLevelOffers(6, _rng).Count == 4 ? 1 : 0;
        }

        player.Stats.Get(StatId.Luck).AddModifier(new StatModifier(ModifierType.Flat, 0.5f, "test"));
        int foursLucky = 0;
        bool keystoneRowsStay = true;
        for (int i = 0; i < 400; i++)
        {
            foursLucky += build.RollLevelOffers(6, _rng).Count == 4 ? 1 : 0;
            keystoneRowsStay &= build.RollLevelOffers(10, _rng).Count <= 3;
        }

        Check("FOURTH-CARD-WITH-LUCK", fours == 0 && foursLucky is > 140 and < 260 && keystoneRowsStay,
            $"4-card rows: {fours}/400 at Luck 1, {foursLucky}/400 at Luck 1.5 (~200 expected); keystone rows never get one");
        Check("FORTUNE-CARD", UpgradeCatalog.Passives.Any(p => p.Id == "luck"), "the Fortune power-up (Luck) is in the pool");
        player.QueueFree();
        await Frames(1);
    }

    private async Task PickControlUiChecks()
    {
        (Player player, RunBuild build) = await RunPlayer("fireball");
        var state = new RunState { Player = player };
        state.Build = build;
        AddChild(state);
        var screen = new LevelUpScreen { State = state, Build = build, PauseTree = false };
        AddChild(screen);
        await Frames(1);
        // Straight to level 6 (an ordinary row): five levels, the first four consumed silently.
        for (int i = 1; i < 6; i++)
        {
            state.AddExperience(RunState.ExperienceToNext(i));
        }

        await Frames(1);
        while (screen.IsOpen && state.Level - state.PendingLevelUps + 1 < 6)
        {
            await WaitArmed(screen);
            screen.ChooseIndex(0);
            await Frames(1);
        }

        await WaitArmed(screen);
        float before = state.Experience;
        int skips = build.SkipsLeft;
        bool skipped = screen.SkipRow();
        await Frames(1);
        float refund = RunState.ExperienceToNext(5) * OfferRules.SkipRefund * player.Stats.Get(StatId.ExperienceGain).Value;
        Check("SKIP-UI-REFUNDS", skipped && !screen.IsOpen && build.SkipsLeft == skips - 1 && Mathf.IsEqualApprox(state.Experience, before + refund, 0.01f),
            $"skipped level 6: closed={!screen.IsOpen}, skips {skips} → {build.SkipsLeft}, experience {before:0.0} → {state.Experience:0.0} (+{refund:0.0})");

        state.AddExperience(RunState.ExperienceToNext(state.Level) - state.Experience + 0.01f);
        await Frames(1);
        await WaitArmed(screen);
        UpgradeOffer[] row = screen.Offers.ToArray();
        bool banished = screen.BanishCard(1);
        UpgradeOffer[] after = screen.Offers.ToArray();
        Check("BANISH-UI", banished && after[0] == row[0] && after[1].Identity != row[1].Identity && build.Banished.Contains(row[1].Identity),
            $"card 2 {row[1].Title} banished and replaced by {after[1].Title}");
        var skipButton = screen.FindChild("Skip", true, false) as Button;
        var banishButton = screen.FindChild("Banish1", true, false) as Button;
        Check("PICK-CONTROL-BUTTONS", skipButton is { Visible: true } && banishButton is { Visible: true },
            $"Skip button visible={skipButton?.Visible}, Banish button visible={banishButton?.Visible}");
        await WaitArmed(screen);
        screen.ChooseIndex(0);
        await Frames(1);
        screen.QueueFree();
        state.QueueFree();
        player.QueueFree();
        await Frames(1);
    }

    private async Task WaitArmed(LevelUpScreen screen)
    {
        for (int i = 0; i < 600 && !screen.Armed; i++)
        {
            await Frames(1);
        }
    }

    // --- M2: chests -------------------------------------------------------------------------

    private async Task ChestRuleChecks()
    {
        int[] counts = new int[6];
        int[] lucky = new int[6];
        for (int i = 0; i < 10_000; i++)
        {
            counts[ChestRules.RollSize(1f, _rng)]++;
            lucky[ChestRules.RollSize(2f, _rng)]++;
        }

        Check("CHEST-SIZE-ODDS", counts[5] is > 220 and < 380 && counts[3] is > 950 and < 1400 && lucky[5] > counts[5] * 1.6f && lucky[3] > counts[3] * 1.5f,
            $"Luck 1: {counts[5]} fives, {counts[3]} threes, {counts[1]} ones; Luck 2: {lucky[5]}, {lucky[3]}, {lucky[1]}");
        Check("CHEST-BOSS-SIZES", ChestRules.BossSize(false, false) == 3 && ChestRules.BossSize(true, false) == 5 && ChestRules.BossSize(false, true) == 5,
            "Herald 3, Colossus 5, endless bosses 5");

        (Player player, RunBuild build) = await RunPlayer("fireball", "spark");
        build.ApplyKey("p:damage:0:1");
        bool onlyOwned = true;
        for (int i = 0; i < 300; i++)
        {
            foreach (string key in build.ChestRewards(5, _rng, 300f))
            {
                onlyOwned &= key.StartsWith("l:fireball") || key.StartsWith("l:spark") || key.StartsWith("p:damage:") || key.StartsWith(ChestRules.GoldPrefix);
            }
        }

        Check("CHEST-ONLY-OWNED", onlyOwned, "300 five-chests: only levels of owned skills and ranks of owned power-ups");

        List<string> keys = build.ChestRewards(5, _rng, 300f);
        (Player other, RunBuild copy) = await RunPlayer("fireball", "spark");
        copy.ApplyKey("p:damage:0:1");
        foreach (string key in keys.Where(k => !k.StartsWith(ChestRules.GoldPrefix)))
        {
            build.ApplyKey(key);
            copy.ApplyKey(key);
        }

        string Summary(RunBuild b) => string.Join(",", b.Skills.Select(s => $"{s.Definition.DisplayName}{s.Level}")) + "/" + string.Join(",", b.PassiveRanks.Select(kv => $"{kv.Key}{kv.Value}"));
        Check("CHEST-KEYS-REPLAY", Summary(build) == Summary(copy), $"keys [{string.Join(", ", keys)}] give the same build on a second copy (co-op): {Summary(copy)}");

        (Player maxed, RunBuild full) = await RunPlayer("fireball");
        full.FindSkill("fireball")!.Level = RunBuild.MaxSkillLevel;
        List<string> gold = full.ChestRewards(3, _rng, 300f);
        Check("CHEST-FALLBACK-GOLD", gold.Count == 3 && gold.All(k => k.StartsWith(ChestRules.GoldPrefix)), $"a maxed build's 3-chest: [{string.Join(", ", gold)}]");
        player.QueueFree();
        other.QueueFree();
        maxed.QueueFree();
        await Frames(1);
    }

    private async Task ChestSceneChecks()
    {
        (Player player, RunBuild build) = await RunPlayer("fireball");
        var state = new RunState { Player = player, Name = "RunState" };
        state.Build = build;
        AddChild(state);
        state.AddToGroup(Pickup.HandlerGroup);
        await Frames(1);
        long gold = player.Wallet.GetBalance(Profile.GoldId);
        Pickup chest = state.DropChest(player.GlobalPosition + new Vector2(200f, 0f), 3);
        await Frames(10);
        bool waits = IsInstanceValid(chest) && !chest.IsQueuedForDeletion();
        player.GlobalPosition = chest.GlobalPosition;
        await Frames(3);
        int level = build.FindSkill("fireball")!.Level;
        Check("CHEST-OPENS-BY-TOUCH", waits && state.ChestsOpened == 1 && !GetTree().Paused && (level > 1 || player.Wallet.GetBalance(Profile.GoldId) > gold),
            $"the chest waited 200 px away (not pulled in)={waits}, opened on touch: {state.ChestsOpened} opened, fireball Lv {level}, the game never paused");
        state.RemoveFromGroup(Pickup.HandlerGroup);
        state.QueueFree();
        player.QueueFree();
        await Frames(1);
    }

    // --- M5: evolutions ---------------------------------------------------------------------

    private async Task EvolutionChecks()
    {
        (Player player, RunBuild build) = await RunPlayer("fireball");
        SkillInstance fireball = build.FindSkill("fireball")!;
        int baseCount = player.SkillUser.ResolveContext(fireball, Vector2.Right).Projectile!.Count;
        fireball.Level = RunBuild.MaxSkillLevel;
        bool notYet = !build.ReadyEvolutions().Any();
        build.ApplyKey("p:area:0:1");
        bool ready = build.ReadyEvolutions().Any(e => e.SkillId == "fireball") && build.EvolutionReady(fireball);
        Check("EVOLVE-NEEDS-BOTH", notYet && ready, $"level 6 alone: not ready={notYet}; with Expanse: ready={ready}");

        bool smallChest = !build.ChestRewards(1, _rng, 300f).Any(k => k.StartsWith("e:"));
        List<string> keys = build.ChestRewards(3, _rng, 300f);
        Check("EVOLVE-FROM-CHEST", smallChest && keys[0] == "e:fireball", $"a 1-chest never evolves; a 3-chest's first slot: {keys[0]}");

        bool applied = build.ApplyKey("e:fireball");
        CastContext ctx = player.SkillUser.ResolveContext(fireball, Vector2.Right);
        ProjectileParams evolved = ctx.Projectile!;
        Check("EVOLVED-FIREBALL", applied && fireball.IsEvolved && fireball.DisplayName == "Firestorm" && evolved.Count == baseCount + 2 && evolved.OnHitExplode
                && ctx.DamageMultiplier >= EvolutionCatalog.DamageBonus - 0.01f && !build.ApplyKey("e:fireball") && !build.ReadyEvolutions().Any(),
            $"Firestorm: {baseCount} → {evolved.Count} fireballs, burst={evolved.OnHitExplode}, damage ×{ctx.DamageMultiplier:0.00}; evolves once");

        bool everyKnownSkill = EvolutionCatalog.All.All(e => UpgradeCatalog.FindSkill(e.SkillId) is not null && UpgradeCatalog.Passives.Any(p => p.Id == e.PartnerId));
        Check("EVOLUTION-CATALOG", everyKnownSkill && EvolutionCatalog.All.Count >= 6, $"{EvolutionCatalog.All.Count} evolutions, every skill and partner exists");
        player.QueueFree();
        await Frames(1);
    }

    // --- M7: unlocks -----------------------------------------------------------------------

    private void UnlockChecks()
    {
        Unlocks.ClearForTest();
        bool locked = !Unlocks.Has("test_unlock");
        bool granted = Unlocks.Grant("test_unlock") && !Unlocks.Grant("test_unlock") && Unlocks.Has("test_unlock");
        Unlocks.ClearForTest();
        Check("UNLOCKS-GRANT", locked && granted, "an unlock is absent, granted once, then present");
    }
}
