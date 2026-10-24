#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Items;
using ArpgGame.Progression;
using ArpgGame.Run;
using ArpgGame.Skills;
using ArpgGame.Stats;
using ArpgGame.UI;

namespace ArpgGame.Tests;

/// <summary>
/// Headless test for the Survival Rift's progression rules (docs/design/progression.md,
/// run.md -> "Rewards", items.md -> "Pending items"). Run with:
///   ./scripts/run_all_tests.sh RiftProgressionTest
///
/// Keystone cadence (lesser at 5/15/25, great at 10/20/30, none elsewhere), the
/// offer composition, the start pick (character skills at level 1, a coherent
/// class pick for a skill-less character), per-card rerolls (logic and keys),
/// the minimum-loot rule, Pending items (overflow, save/load, cap, moving out,
/// the results screen) and the character's passive tree and ascendancy applied
/// in a real Run scene. The real profile is swapped out and restored, never saved.
/// </summary>
public partial class RiftProgressionTest : Node2D
{
    private const string ResultPath = "user://test_results/RiftProgressionTest.txt";
    private readonly System.Text.StringBuilder _log = new();
    private bool _failed;
    private readonly RandomNumberGenerator _rng = new() { Seed = 1234 };

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
        Log($"[RIFTTEST] {name}: {(ok ? "PASS" : "FAIL")} — {detail}");
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

    private async Task Seconds(float s) =>
        await ToSignal(GetTree().CreateTimer(s, true, false, true), SceneTreeTimer.SignalName.Timeout);

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
            await CadenceChecks();
            await StartSkillChecks();
            await RerollChecks();
            await RerollUiChecks();
            await MinimumLootChecks();
            await EnergyShieldChecks();
            await NoRegenMaxLifeChecks();
            OverflowChecks();
            DownedRuleChecks();
            await OverflowUiChecks(real);
            await RiftSceneChecks(real);
            await CashOutChecks(real);
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

        Log($"[RIFTTEST] === RESULT: {(_failed ? "FAIL" : "PASS")} ===");
        GetTree().Quit(_failed ? 1 : 0);
    }

    // --- helpers ---------------------------------------------------------------------

    private static int _spawnX = 20000;

    /// <summary>A player set up the way RunController sets up a run character.</summary>
    private async Task<(Player, RunBuild)> RunPlayer(CharacterData? character)
    {
        var player = GD.Load<PackedScene>("res://scenes/Player.tscn").Instantiate<Player>();
        AddChild(player);
        player.GlobalPosition = new Vector2(_spawnX += 3000, 20000);
        var build = new RunBuild { Name = "RunBuild" };
        player.AddChild(build);
        if (character is not null)
        {
            Profile.ApplyCharacterTo(player, character);
        }
        else
        {
            foreach (SkillSlotId slot in CharacterBuild.Slots)
            {
                player.SkillUser.ClearSlot(slot);
            }
        }

        RunController.FinishRunCharacter(player, build);
        await Frames(1);
        return (player, build);
    }

    private static CharacterData SkillLess(string classId)
    {
        CharacterData c = CharacterData.Create("Empty", classId);
        foreach (Characters.LoadoutSlot slot in c.Loadout)
        {
            slot.SkillUid = null;
            slot.SupportUids.Clear();
        }

        return c;
    }

    private static bool HasModifier(Player player, StatId stat, object source, ModifierType type, float value) =>
        player.Stats.Get(stat).Modifiers.Any(m => m.Source == source && m.Type == type && Mathf.IsEqualApprox(m.Value, value));

    // --- keystone cadence and composition -----------------------------------------------

    private async Task CadenceChecks()
    {
        (Player player, RunBuild build) = await RunPlayer(CharacterData.Create("Cadence", "arcanist"));
        var lesser = new List<int>();
        var great = new List<int>();
        var stray = new List<string>();
        bool everyRowHasSkill = true;
        bool rowsDistinct = true;
        var categories = new Dictionary<OfferCategory, int>();
        for (int level = 1; level <= 40; level++)
        {
            KeystoneTier? tier = OfferRules.KeystoneTierAt(level);
            for (int rep = 0; rep < 12; rep++)
            {
                List<UpgradeOffer> offers = build.RollOffers(level, _rng);
                rowsDistinct &= offers.Count == 3 && offers.Select(o => o.Identity).Distinct().Count() == 3;
                bool allLesser = offers.All(o => o.Kind == UpgradeKind.Keystone && o.KeystoneTier == KeystoneTier.Lesser);
                bool allGreat = offers.All(o => o.Kind == UpgradeKind.Keystone && o.KeystoneTier == KeystoneTier.Great);
                if (tier == KeystoneTier.Lesser && allLesser && rep == 0)
                {
                    lesser.Add(level);
                }
                else if (tier == KeystoneTier.Great && allGreat && rep == 0)
                {
                    great.Add(level);
                }
                else if (tier is null && offers.Any(o => o.Kind == UpgradeKind.Keystone))
                {
                    stray.Add($"{level}:{offers.First(o => o.Kind == UpgradeKind.Keystone).Title}");
                }

                if (tier is null)
                {
                    everyRowHasSkill &= offers.Any(o => o.Category == OfferCategory.Skill);
                    foreach (UpgradeOffer o in offers)
                    {
                        categories[o.Category] = categories.GetValueOrDefault(o.Category) + 1;
                    }
                }
            }
        }

        Check("CADENCE-LESSER-AT-5-15-25", lesser.SequenceEqual(new[] { 5, 15, 25, 35 }), $"lesser-only rows at levels {string.Join(",", lesser)}");
        Check("CADENCE-GREAT-AT-10-20-30", great.SequenceEqual(new[] { 10, 20, 30, 40 }), $"great-only rows at levels {string.Join(",", great)}");
        Check("CADENCE-NO-KEYSTONE-ELSEWHERE", stray.Count == 0, stray.Count == 0 ? "no keystone card on 32 ordinary levels × 12 rolls" : string.Join(", ", stray.Take(5)));
        Check("CADENCE-TIERS", OfferRules.KeystoneTierAt(5) == KeystoneTier.Lesser && OfferRules.KeystoneTierAt(10) == KeystoneTier.Great
            && OfferRules.KeystoneTierAt(25) == KeystoneTier.Lesser && OfferRules.KeystoneTierAt(30) == KeystoneTier.Great && OfferRules.KeystoneTierAt(4) is null && OfferRules.KeystoneTierAt(11) is null,
            "KeystoneTierAt: 5 lesser, 10 great, 25 lesser, 30 great, 4/11 none");
        Check("OFFERS-DISTINCT", rowsDistinct, "every row has three different cards");
        int total = categories.Values.Sum();
        float skillShare = categories.GetValueOrDefault(OfferCategory.Skill) / (float)total;
        float powerShare = categories.GetValueOrDefault(OfferCategory.PowerUp) / (float)total;
        Check("ORDINARY-ROW-HAS-A-SKILL-CARD", everyRowHasSkill, "every ordinary row shows a new skill or a skill level");
        Check("ORDINARY-MOSTLY-SKILLS-AND-POWER-UPS", skillShare > 0.4f && powerShare > 0.25f && skillShare + powerShare > 0.8f,
            $"skills {skillShare:P0}, power-ups {powerShare:P0}, supports {categories.GetValueOrDefault(OfferCategory.Support) / (float)total:P0} of {total} cards");
        bool noMana = Enumerable.Range(0, 200).SelectMany(_ => build.RollOffers(7, _rng)).All(o => o.Id is not ("mana_regen" or "max_mana" or "mana_cost"));
        Check("NO-MANA-CARDS-IN-RIFT", noMana, "no mana power-up in 200 rows (skills cost nothing in the Rift)");
        player.QueueFree();

        // A keystone the character took on its tree is never offered again.
        CharacterData jug = CharacterData.Create("Jugg", "warrior");
        jug.Level = CharacterData.MaxLevel;
        jug.Passives.UnionWith(PassiveTree.PathTo(jug, "ks_juggernaut"));
        (Player p2, RunBuild b2) = await RunPlayer(jug);
        bool neverOffered = Enumerable.Range(0, 60).SelectMany(_ => b2.RollOffers(5, _rng)).All(o => o.Id != "juggernaut");
        Check("TREE-KEYSTONE-NOT-OFFERED", jug.Passives.Contains("ks_juggernaut") && neverOffered && b2.HasKeystone("juggernaut") && !b2.ApplyKey("k:juggernaut"),
            $"allocated={jug.Passives.Contains("ks_juggernaut")}, offered at level 5 in 60 rows={!neverOffered}, replayed key refused={!b2.ApplyKey("k:juggernaut")}");
        p2.QueueFree();
        await Frames(1);
    }

    // --- starting skills ------------------------------------------------------------------

    private async Task StartSkillChecks()
    {
        CharacterData c = CharacterData.Create("Starter", "arcanist");
        GemData gem = c.Gems.First(g => !g.IsSupport);
        gem.Level = 12;
        (Player player, RunBuild build) = await RunPlayer(c);
        SkillInstance? fireball = build.FindSkill("fireball");
        Check("START-SKILLS-AT-LEVEL-1", fireball is { Level: 1 } && fireball.Attached.Count == 0 && gem.Level == 12,
            $"fireball live level {fireball?.Level}, supports {fireball?.Attached.Count}, gem still level {gem.Level}");

        List<UpgradeOffer>[] rows = Enumerable.Range(0, 40).Select(_ => build.RollStartOffers(_rng)).ToArray();
        bool skillCards = rows.All(r => r.Count == 3 && r.All(o => o.Category == OfferCategory.Skill));
        bool coherent = rows.SelectMany(r => r).Where(o => o.Kind == UpgradeKind.NewSkill).All(o => ClassCatalog.Get("arcanist").RiftSkills.Contains(o.Id));
        Check("START-PICK-WITH-SKILLS", skillCards && coherent, $"40 start rows: all skill cards={skillCards}, new skills all Arcanist ones={coherent}");

        // The run owns the skills: a character level-up mid-run must not re-equip the loadout.
        build.AddSkill("spark");
        fireball!.Level = 3;
        CharacterBuild.Of(player)!.Rebuild();
        Check("CHARACTER-LEVELUP-KEEPS-RUN-SKILLS", build.FindSkill("spark") is not null && build.FindSkill("fireball") is { Level: 3 } && CharacterBuild.Of(player)!.RunOwnsSkills,
            $"after a rebuild: spark kept={build.FindSkill("spark") is not null}, fireball level {build.FindSkill("fireball")?.Level}");
        player.QueueFree();

        foreach (string cls in new[] { "warrior", "rogue", "arcanist" })
        {
            (Player p, RunBuild b) = await RunPlayer(SkillLess(cls));
            IReadOnlyList<string> pool = ClassCatalog.Get(cls).RiftSkills;
            List<UpgradeOffer>[] picks = Enumerable.Range(0, 40).Select(_ => b.RollStartOffers(_rng)).ToArray();
            bool allNew = picks.All(r => r.Count == 3 && r.All(o => o.Kind == UpgradeKind.NewSkill));
            bool onClass = picks.SelectMany(r => r).All(o => pool.Contains(o.Id));
            bool signature = picks.All(r => r.Any(o => o.Id == pool[0]));
            Check($"START-PICK-SKILLLESS-{cls.ToUpperInvariant()}", !b.Skills.Any() && allNew && onClass && signature,
                $"no skills={!b.Skills.Any()}; 40 rows: new skills only={allNew}, all from [{string.Join(", ", pool)}]={onClass}, {pool[0]} always offered={signature}");
            p.QueueFree();
        }

        await Frames(1);
    }

    // --- rerolls ---------------------------------------------------------------------------

    private async Task RerollChecks()
    {
        (Player player, RunBuild build) = await RunPlayer(CharacterData.Create("Reroller", "warrior"));
        List<UpgradeOffer> offers = build.RollOffers(6, _rng);
        string[] before = offers.Select(o => o.Identity).ToArray();
        UpgradeOffer? swapped = build.TryReroll(6, offers, 1, _rng);
        bool onlyThat = swapped is not null && !before.Contains(swapped.Identity);
        if (swapped is not null)
        {
            offers[1] = swapped;
        }

        Check("REROLL-REPLACES-ONE-CARD", onlyThat && offers[0].Identity == before[0] && offers[2].Identity == before[2] && build.RerollsLeft == 1,
            $"[{string.Join(", ", before)}] → [{string.Join(", ", offers.Select(o => o.Identity))}], charges {build.RerollsLeft}");
        UpgradeOffer? second = build.TryReroll(6, offers, 0, _rng);
        UpgradeOffer? third = build.TryReroll(6, offers, 2, _rng);
        Check("REROLL-TWO-CHARGES", second is not null && third is null && build.RerollsLeft == 0, $"second={second is not null}, third refused={third is null}, charges {build.RerollsLeft}");

        bool distinct = true;
        for (int i = 0; i < 300; i++)
        {
            build.RerollsLeft = 2;
            int level = i % 3 == 0 ? 10 : i % 3 == 1 ? 15 : 7;
            List<UpgradeOffer> row = build.RollOffers(level, _rng);
            int index = i % row.Count;
            string replaced = row[index].Identity;
            if (build.TryReroll(level, row, index, _rng) is { } next)
            {
                distinct &= next.Identity != replaced && row.Where((_, j) => j != index).All(o => o.Identity != next.Identity)
                    && (level % 5 != 0 || (next.Kind == UpgradeKind.Keystone && next.KeystoneTier == row[(index + 1) % row.Count].KeystoneTier));
            }
        }

        Check("REROLL-NEVER-DUPLICATES", distinct, "300 rerolls (ordinary, great and lesser levels): never a card already on screen, keystones stay in their tier");
        build.RerollsLeft = OfferRules.RerollsPerRun;
        player.QueueFree();
        await Frames(1);
    }

    private async Task RerollUiChecks()
    {
        (Player player, RunBuild build) = await RunPlayer(CharacterData.Create("Keys", "rogue"));
        var state = new RunState { Player = player };
        AddChild(state);
        // The tree keeps running under the cards, which the test needs.
        var screen = new LevelUpScreen { State = state, Build = build, PauseTree = false };
        AddChild(screen);
        await Frames(1);
        state.AddExperience(RunState.ExperienceToNext(1) + 0.1f);
        await Frames(1);
        await WaitArmed(screen);
        UpgradeOffer[] before = screen.Offers.ToArray();
        PressKey(Key.Key2, shift: true);
        await Frames(2);
        UpgradeOffer[] after = screen.Offers.ToArray();
        Check("REROLL-UI-SHIFT-NUMBER", screen.IsOpen && after.Length == 3 && after[0] == before[0] && after[2] == before[2] && after[1] != before[1]
            && build.RerollsLeft == 1 && after.Select(o => o.Identity).Distinct().Count() == 3,
            $"Shift+2: card 2 {before[1].Title} → {after[1].Title}, cards 1 and 3 kept, charges {build.RerollsLeft}");

        await WaitArmed(screen);
        PressKey(Key.R);
        await Frames(1);
        Check("REROLL-UI-R-ALONE-DOES-NOTHING", screen.IsOpen && screen.Offers.SequenceEqual(after) && build.RerollsLeft == 1, "R keeps every card and the charge");

        var button = screen.FindChild("Reroll3", true, false) as Button;
        button?.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(1);
        UpgradeOffer[] third = screen.Offers.ToArray();
        var anyButton = screen.FindChild("Reroll1", true, false) as Button;
        Check("REROLL-UI-BUTTON", button is not null && third[2] != after[2] && third[0] == after[0] && third[1] == after[1] && build.RerollsLeft == 0 && anyButton is { Disabled: true },
            $"button under card 3 replaced it ({after[2].Title} → {third[2].Title}); charges {build.RerollsLeft}; buttons disabled={anyButton?.Disabled}");

        await WaitArmed(screen);
        PressKey(Key.Key1);
        await Frames(2);
        Check("REROLL-UI-PICK-AFTER", !screen.IsOpen && state.PendingLevelUps == 0, $"1 picked {third[0].Title}");
        screen.QueueFree();
        state.QueueFree();
        player.QueueFree();
        await Frames(1);
    }

    /// <summary>Waits for the arming delay by the wall clock the cards use (a long
    /// frame can make a timer fire early).</summary>
    private async Task WaitArmed(LevelUpScreen screen)
    {
        for (int i = 0; i < 600 && !screen.Armed; i++)
        {
            await Frames(1);
        }

        await Frames(1);
    }

    private void PressKey(Key k, bool shift = false)
    {
        foreach (bool down in new[] { true, false })
        {
            GetViewport().PushInput(new InputEventKey { Pressed = down, Keycode = k, PhysicalKeycode = k, ShiftPressed = shift });
        }
    }

    // --- minimum loot ------------------------------------------------------------------------

    private async Task MinimumLootChecks()
    {
        var rng = new RandomNumberGenerator { Seed = 5 };
        RunRewards quit = RunRewards.For(10f, 0, 3, false, rng);
        RunRewards minute = RunRewards.For(RunRewards.MinItemSeconds, 0, 4, false, rng);
        RunRewards killer = RunRewards.For(25f, 0, RunRewards.MinItemKills, false, rng);
        RunRewards almost = RunRewards.For(RunRewards.MinItemSeconds - 1f, 0, RunRewards.MinItemKills - 1, false, rng);
        Check("MIN-LOOT-RULE", !quit.ItemsEarned && quit.Choices == 0 && quit.Keep == 0 && quit.NoItemReason.Length > 0
            && minute.ItemsEarned && minute.Choices == 3 && killer.ItemsEarned && !almost.ItemsEarned && quit.Gold < 10,
            $"0:10/3 kills earned={quit.ItemsEarned} (gold {quit.Gold}); {RunRewards.MinItemSeconds}s={minute.ItemsEarned}; {RunRewards.MinItemKills} kills in 0:25={killer.ItemsEarned}; 0:59/29 kills={almost.ItemsEarned}");

        var state = new RunState();
        AddChild(state);
        state.SkipTo(12f);
        (Player player, RunBuild build) = await RunPlayer(null);
        var results = new ResultsScreen { Name = "QuitResults" };
        AddChild(results);
        await Frames(1);
        results.ShowResults(state, build, quit);
        await Frames(1);
        Check("RESULTS-EXPLAINS-NO-ITEM", results.RewardCount == 0 && results.RewardHint.Contains("No item") && results.RewardHint.Contains("60 s"),
            $"cards {results.RewardCount}; hint: {results.RewardHint}");
        results.Leave(null);
        results.QueueFree();
        state.QueueFree();
        player.QueueFree();
        await Frames(1);
    }

    // --- energy shield (a new power-up stat) ------------------------------------------------

    private async Task EnergyShieldChecks()
    {
        (Player player, RunBuild _) = await RunPlayer(null);
        player.Stats.Get(StatId.EnergyShield).AddModifier(new StatModifier(ModifierType.Flat, 40f, this));
        await Frames(10);
        Combat.Health health = player.Health!;
        float full = health.EnergyShield;
        float life = player.Stats.Life!.Current;
        health.TakeDamage(new Combat.DamagePayload(30f, Combat.DamageType.Physical, null, Combat.Faction.Enemy, Combat.SkillTags.None, Combat.DamageDeliveryKind.Hit));
        float afterSmall = health.EnergyShield;
        bool lifeKept = Mathf.IsEqualApprox(player.Stats.Life.Current, life);
        health.TakeDamage(new Combat.DamagePayload(60f, Combat.DamageType.Physical, null, Combat.Faction.Enemy, Combat.SkillTags.None, Combat.DamageDeliveryKind.Hit));
        bool lifeHit = player.Stats.Life.Current < life && health.EnergyShield == 0f;
        await Seconds(Combat.Health.ShieldRechargeDelay + 2.4f);
        Check("ENERGY-SHIELD", Mathf.IsEqualApprox(full, 40f) && afterSmall < full && lifeKept && lifeHit && health.EnergyShield > 30f,
            $"starts {full:0}; a 30 hit leaves {afterSmall:0} shield and life untouched={lifeKept}; a 60 hit breaks it and reaches life={lifeHit}; recharged to {health.EnergyShield:0}");
        player.QueueFree();
        await Frames(1);
    }

    // --- no regeneration + a max-life card (owner's report 2026-10-24) --------------------------

    private async Task NoRegenMaxLifeChecks()
    {
        (Player player, RunBuild build) = await RunPlayer(null);
        Combat.Health health = player.Health!;
        ResourcePool life = player.Stats.Life!;
        build.ApplyKey("k:vampiric_pact");
        build.ApplyKey("p:max_life:2");
        build.ApplyKey("p:max_life:2");
        await Frames(3);
        bool noRegen = Mathf.IsEqualApprox(life.RegenPerSecond, 0f);
        float maxBefore = life.Max;
        life.Current = life.Max * 0.2f;
        float lifeOnKill = player.Stats.Get(StatId.LifeOnKill).Value;
        for (int i = 0; i < 60; i++)
        {
            health.Heal(lifeOnKill);
        }

        await Frames(3);
        Check("NO-REGEN-MAX-LIFE-REACHABLE", noRegen && Mathf.IsEqualApprox(life.Max, maxBefore) && Mathf.IsEqualApprox(life.Current, life.Max),
            $"no regen={noRegen}, life {life.Current:0.##} / {life.Max:0.##} after healing with {lifeOnKill:0.#} per kill");

        // The card itself: Vitality heals by exactly what it adds to the maximum (flat part and
        // the increased part both). With no regeneration, a card that left you a few points
        // short would leave you short for good until the next kill.
        life.Current = life.Max;
        float before = life.Max;
        build.ApplyKey("p:max_life:2");
        await Frames(3);
        bool full = Mathf.IsEqualApprox(life.Current, life.Max);
        Check("MAX-LIFE-CARD-AT-FULL-STAYS-FULL", full && life.Max > before,
            $"max {before:0.##} -> {life.Max:0.##}, current {life.Current:0.##} straight after the card");
        life.Current = life.Max * 0.5f;
        float half = life.Current;
        float maxNow = life.Max;
        build.ApplyKey("p:max_life:1");
        await Frames(3);
        Check("MAX-LIFE-CARD-KEEPS-MISSING-LIFE", Mathf.IsEqualApprox(life.Current, half + (life.Max - maxNow)) && life.Current < life.Max,
            $"{half:0.##}/{maxNow:0.##} -> {life.Current:0.##}/{life.Max:0.##}: the card adds the new maximum, not a free refill");
        player.QueueFree();
        await Frames(1);
    }

    // --- Pending items --------------------------------------------------------------------------

    private static EquipmentItem Roll(int seed) =>
        ItemFactory.RollRandom(20, DropSource.RiftReward, 2f, new RandomNumberGenerator { Seed = (ulong)seed });

    /// <summary>An account whose active character's bag is full.</summary>
    private static Profile FullBagAccount()
    {
        var account = new Profile { Persist = false };
        CharacterData c = account.Active;
        int seed = 100;
        while (account.AddToStash(Roll(seed++)) && seed < 400)
        {
        }

        // Single cells left by odd shapes: fill with whatever fits until nothing does.
        for (int i = 0; i < 200; i++)
        {
            account.AddToStash(Roll(seed++));
        }

        return account;
    }

    /// <summary>Co-op downed rule (PartyRules.StepRevive): back up alone after 10 s,
    /// sooner with an ally standing by; the ring shows whichever is further.</summary>
    private void DownedRuleChecks()
    {
        float help = 0f, down = 0f, shown = 0f;
        Net.PartyRules.ReviveOutcome outcome = Net.PartyRules.ReviveOutcome.StillDown;
        int steps = 0;
        while (outcome == Net.PartyRules.ReviveOutcome.StillDown && steps < 2000)
        {
            (help, down, outcome, shown) = Net.PartyRules.StepRevive(help, down, false, 0.05f);
            steps++;
        }

        Check("DOWNED-ALONE-RESPAWNS-AT-10S", outcome == Net.PartyRules.ReviveOutcome.Respawned && Mathf.Abs(steps * 0.05f - Net.PartyRules.RespawnSeconds) < 0.06f,
            $"outcome={outcome} after {steps * 0.05f:0.00} s");

        help = 0f; down = 0f; outcome = Net.PartyRules.ReviveOutcome.StillDown; steps = 0;
        while (outcome == Net.PartyRules.ReviveOutcome.StillDown && steps < 2000)
        {
            (help, down, outcome, shown) = Net.PartyRules.StepRevive(help, down, true, 0.05f);
            steps++;
        }

        Check("DOWNED-ALLY-REVIVES-SOONER", outcome == Net.PartyRules.ReviveOutcome.RevivedByAlly && Mathf.Abs(steps * 0.05f - Net.PartyRules.ReviveTime) < 0.06f,
            $"outcome={outcome} after {steps * 0.05f:0.00} s");

        (float h, float d, Net.PartyRules.ReviveOutcome o, float ring) = Net.PartyRules.StepRevive(0.1f, 6f, false, 0.05f);
        Check("DOWNED-RING-SHOWS-FURTHEST", o == Net.PartyRules.ReviveOutcome.StillDown && Mathf.IsEqualApprox(ring, 6.05f / Net.PartyRules.RespawnSeconds) && h < 0.1f,
            $"ring={ring:0.000}, help drained to {h:0.000}, down {d:0.00} s");
    }

    private void OverflowChecks()
    {
        Profile account = FullBagAccount();
        int bag = account.Active.Inventory.Count;
        EquipmentItem reward = Roll(9001);
        string name = reward.Descriptor.DisplayName;
        Profile.RewardClaim claim = account.ClaimReward(reward);
        Check("OVERFLOW-TAKES-REWARD", claim.Stored && claim.ToOverflow && account.Overflow.Count == 1 && account.Active.Inventory.Count == bag,
            $"bag of {bag} full; {name} → overflow={claim.ToOverflow}, pending {account.Overflow.Count}");

        var reread = new Profile { Persist = false };
        reread.LoadJson(account.ToJson());
        Check("OVERFLOW-SURVIVES-SAVE", reread.Overflow.Count == 1 && reread.Overflow[0].Item.Descriptor.DisplayName == name
            && reread.Overflow[0].Item.RolledAffixes.Count == reward.RolledAffixes.Count,
            $"after save/load: {reread.Overflow.Count} pending ({(reread.Overflow.Count > 0 ? reread.Overflow[0].Item.Descriptor.DisplayName : "none")})");

        long gold = account.Active.Gold;
        string oldest = account.Overflow[0].Item.Descriptor.DisplayName;
        Profile.RewardClaim last = default;
        int sold = 0;
        for (int i = 0; i < Profile.OverflowCap + 4; i++)
        {
            last = account.ClaimReward(Roll(9100 + i));
            sold += last.SoldOldest is not null ? 1 : 0;
        }

        Check("OVERFLOW-CAP-SELLS-OLDEST", account.Overflow.Count == Profile.OverflowCap && sold >= 5 && account.Active.Gold > gold
            && account.Overflow.All(e => e.Item != reward) && last.Stored,
            $"{Profile.OverflowCap + 5} rewards into a cap of {Profile.OverflowCap}: {account.Overflow.Count} kept, {sold} times the oldest was sold (first: {oldest}), gold {gold} → {account.Active.Gold}");
        account.Free();
        reread.Free();
    }

    private async Task OverflowUiChecks(Profile? real)
    {
        if (real is null)
        {
            Check("OVERFLOW-UI", false, "no Profile autoload");
            return;
        }

        // Pending items holds three rewards; the bag is empty.
        var account = new Profile { Persist = false };
        _ = account.Active;
        for (int i = 0; i < 3; i++)
        {
            account.AddToOverflow(Roll(7000 + i));
        }

        real.LoadJson(account.ToJson());
        account.Free();
        var player = GD.Load<PackedScene>("res://scenes/Player.tscn").Instantiate<Player>();
        AddChild(player);
        player.GlobalPosition = new Vector2(-30000f, 0f);
        await Frames(1);
        real.ApplyTo(player);
        var panel = new StashPanel { Player = player, Name = "PendingStash" };
        AddChild(panel);
        await Frames(1);
        panel.Open();
        await Frames(1);
        Check("OVERFLOW-UI-OPENS-ON-PENDING", panel.ShowingPending && panel.PendingGrid is { } g && g.Placements.Count == 3,
            $"opened on Pending={panel.ShowingPending}, {panel.PendingGrid?.Placements.Count} items shown");

        EquipmentItem first = panel.PendingGrid!.Placements.Keys.First();
        bool taken = panel.TakePending(first);
        await Frames(1);
        Check("OVERFLOW-MOVE-TO-INVENTORY", taken && player.Inventory.Placements.ContainsKey(first) && real.Overflow.Count == 2,
            $"right-click moved it={taken}, in the bag={player.Inventory.Placements.ContainsKey(first)}, pending left {real.Overflow.Count}");
        int moved = panel.MoveAllPendingToStash();
        await Frames(1);
        Check("OVERFLOW-MOVE-ALL-TO-STASH", moved == 2 && real.Overflow.Count == 0 && real.StashTabs[0].Count >= 2,
            $"moved {moved}, pending {real.Overflow.Count}, stash tab 1 holds {real.StashTabs[0].Count}");
        panel.Close();
        panel.QueueFree();
        player.QueueFree();
        await Frames(1);

        // The results screen with a full bag: the reward goes to Pending items, and says so.
        Profile full = FullBagAccount();
        real.LoadJson(full.ToJson());
        full.Free();
        var state = new RunState();
        AddChild(state);
        state.SkipTo(400f);
        (Player p2, RunBuild b2) = await RunPlayer(null);
        var results = new ResultsScreen { Name = "FullResults" };
        AddChild(results);
        await Frames(1);
        results.ShowResults(state, b2, RunRewards.For(400f, 2, 900, true, _rng));
        await Frames(1);
        results.ClaimIndex(0);
        await Frames(1);
        Check("RESULTS-SENDS-TO-PENDING", results.LastClaim is { Stored: true, ToOverflow: true } && real.Overflow.Count == 1 && results.RewardHint.Contains("Pending items"),
            $"claim → overflow={results.LastClaim?.ToOverflow}, pending {real.Overflow.Count}; hint: {results.RewardHint}");
        results.Leave(null);
        results.QueueFree();
        state.QueueFree();
        p2.QueueFree();
        await Frames(1);
    }

    // --- the character's tree and ascendancy in a real Rift -----------------------------------

    private async Task RiftSceneChecks(Profile? real)
    {
        if (real is null)
        {
            Check("RIFT-SCENE", false, "no Profile autoload");
            return;
        }

        var account = new Profile { Persist = false };
        CharacterData c = account.CreateCharacter("Treebound", "warrior");
        c.Level = CharacterData.MaxLevel;
        c.TutorialDone = true;
        c.Passives.UnionWith(PassiveTree.PathTo(c, "ks_juggernaut"));
        c.Passives.UnionWith(PassiveTree.PathTo(c, "ks_vampiric_pact"));
        c.Ascendancy = "barbarian";
        c.AscendancyPoints = 4;
        c.AscendancyNodes.UnionWith(new[] { "barb_thick", "barb_bloodlust" });
        c.Gems.First(g => !g.IsSupport).Level = 15;
        real.LoadJson(account.ToJson());
        account.Free();

        var run = GD.Load<PackedScene>(Scenes.Run).Instantiate<RunController>();
        AddChild(run);
        await Frames(3);
        Player player = run.Player;
        CharacterBuild? cb = CharacterBuild.Of(player);
        object source = cb?.Source ?? new object();

        // Every stat line of every allocated node is on the run player, once.
        int expected = 0;
        int found = 0;
        foreach (string id in c.Passives)
        {
            foreach (PassiveMod m in PassiveTree.Get(id)?.Mods ?? System.Array.Empty<PassiveMod>())
            {
                expected++;
                found += HasModifier(player, m.Stat, source, m.Type, m.Value) ? 1 : 0;
            }
        }

        Check("RIFT-APPLIES-PASSIVE-TREE", cb is not null && expected > 5 && found == expected,
            $"{c.Passives.Count} nodes allocated: {found}/{expected} of their stat lines on the run player");
        int juggernaut = player.Stats.Armour.Modifiers.Count(m => m.Type == ModifierType.More && Mathf.IsEqualApprox(m.Value, 50f));
        Check("RIFT-TREE-KEYSTONE-ONCE", juggernaut == 1 && Enumerable.Range(0, 40).SelectMany(_ => run.Build.RollOffers(5, _rng)).All(o => o.Id != "juggernaut"),
            $"Juggernaut's 50% more Armour applied {juggernaut} time(s); never offered as a card");
        Check("RIFT-APPLIES-ASCENDANCY", cb?.Ascendancy.Has("barb_thick") == true && HasModifier(player, StatId.MaxLife, source, ModifierType.Increased, 15f)
            && HasModifier(player, StatId.Damage, source, ModifierType.More, 20f),
            $"Barbarian nodes live={cb?.Ascendancy.Has("barb_thick")}, Thick Skin and Bloodlust on the run player");
        float life = player.Stats.Get(StatId.MaxLife).Value;
        bool runBaseline = player.Stats.Get(StatId.MaxLife).Modifiers.Any(m => Mathf.IsEqualApprox(m.Value, RunController.RunBonusLife) && m.Type == ModifierType.Flat);
        Check("RIFT-RUN-BONUSES-ALONGSIDE", runBaseline && life > 200f, $"max life {life:0} with the run's +{RunController.RunBonusLife} on top of the tree");

        SkillInstance? cleave = run.Build.FindSkill("cleave");
        Check("RIFT-CHARACTER-SKILLS-LEVEL-1", cleave is { Level: 1 } && cleave.Attached.Count == 0, $"cleave level {cleave?.Level} (gem 15), supports {cleave?.Attached.Count}");
        Check("RIFT-START-PICK-OPENS", run.LevelUp.IsOpen && run.LevelUp.Offers.Count == 3 && run.LevelUp.Offers.All(o => o.Category == OfferCategory.Skill),
            $"start pick open={run.LevelUp.IsOpen}: {string.Join(", ", run.LevelUp.Offers.Select(o => o.Title))}");

        await WaitArmed(run.LevelUp);
        run.LevelUp.ChooseIndex(0);
        await Frames(2);
        Check("RIFT-STARTS-AFTER-PICK", !run.LevelUp.IsOpen && !GetTree().Paused && run.Director.Spawning, $"open={run.LevelUp.IsOpen}, paused={GetTree().Paused}, spawning={run.Director.Spawning}");
        run.Director.Spawning = false;

        // Vampiric Pact (no regeneration, life on kill) + Vitality in the real Rift: kills
        // must be able to fill the new, larger maximum (owner's report 2026-10-24).
        if (!run.Build.HasKeystone("vampiric_pact")) run.Build.ApplyKey("k:vampiric_pact");
        run.Build.ApplyKey("p:max_life:1");
        run.Build.ApplyKey("p:max_life:2");
        await Frames(3);
        ResourcePool pool = player.Stats.Life!;
        pool.Current = pool.Max * 0.3f;
        for (int i = 0; i < 40 && pool.Current < pool.Max - 0.01f; i++)
        {
            var slime = GD.Load<PackedScene>("res://scenes/enemies/Slime.tscn").Instantiate<Enemies.Enemy>();
            AddChild(slime);
            slime.GlobalPosition = player.GlobalPosition + new Vector2(60f, 0f);
            run.State.RegisterEnemy(slime);
            await Frames(1);
            slime.Health.ApplyResolvedDamage(100000f, player);
            await Frames(1);
        }

        Check("PACT-KILLS-REACH-MAX-LIFE", pool.Current >= pool.Max - 0.01f && pool.RegenPerSecond <= 0f,
            $"life {pool.Current:0.##} / {pool.Max:0.##}, regen {pool.RegenPerSecond:0.##}");
        run.QueueFree();
        await Frames(2);
    }
}
