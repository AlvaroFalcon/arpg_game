#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Run;
using ArpgGame.UI;

namespace ArpgGame.Tests;

/// <summary>
/// The Survival Rift's arena systems (docs/design/survival-roadmap.md): shrines (M3), Rift tiers
/// and Fear cards at launch (M4), the Onslaught and the endless milestones (M6), and the Rift
/// challenges (M7) — in a real Rift scene. Run:
///   ./scripts/run_headless.sh res://scenes/tests/RiftShrineTest.tscn --quit-after 6000
/// </summary>
public partial class RiftShrineTest : Node2D
{
    private const string ResultPath = "user://test_results/RiftShrineTest.txt";
    private readonly System.Text.StringBuilder _log = new();
    private bool _failed;

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
        Log($"[RIFTSHRINE] {name}: {(ok ? "PASS" : "FAIL")} — {detail}");
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
            if (real is null)
            {
                Check("PROFILE", false, "needs the Profile autoload");
            }
            else
            {
                await ShrineChecks(real);
                await TierLaunchChecks(real);
                await OnslaughtChecks(real);
                ChallengeChecks(real);
                await EntryPanelChecks(real);
            }
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

        Log($"[RIFTSHRINE] === RESULT: {(_failed ? "FAIL" : "PASS")} ===");
        GetTree().Quit(_failed ? 1 : 0);
    }

    private Node? _holder;

    private async Task<RunController> NewRift(Profile real, int tier = 1, params string[] fears)
    {
        var account = new Profile { Persist = false };
        CharacterData c = account.CreateCharacter("Shrine", "warrior");
        c.TutorialDone = true;
        c.Level = 20;
        real.LoadJson(account.ToJson());
        account.Free();
        RunLaunchOptions.StartSkill = "cleave";
        RunLaunchOptions.RiftTier = tier;
        RunLaunchOptions.Fears = fears.ToList();
        _holder = new Node { ProcessMode = ProcessModeEnum.Pausable, Name = "RiftHolder" };
        AddChild(_holder);
        var run = GD.Load<PackedScene>(Scenes.Run).Instantiate<RunController>();
        _holder.AddChild(run);
        await Frames(4);
        run.Player.Health!.IgnoreDamage = true;
        return run;
    }

    private async Task EndRift()
    {
        _holder?.QueueFree();
        _holder = null;
        GetTree().Paused = false;
        await Frames(3);
    }

    private async Task WaitSeconds(RunController run, float seconds)
    {
        ulong end = Time.GetTicksMsec() + (ulong)(seconds * 1000f);
        while (Time.GetTicksMsec() < end)
        {
            if (run.LevelUp.IsOpen && run.LevelUp.Armed)
            {
                run.LevelUp.ChooseIndex(0);
            }

            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    // --- M3: shrines ------------------------------------------------------------------------

    private async Task ShrineChecks(Profile real)
    {
        RunController run = await NewRift(real);
        await WaitSeconds(run, 1.6f);
        List<Shrine> first = run.Director.Shrines.ToList();
        float minGap = float.MaxValue;
        foreach (Shrine a in first)
        {
            minGap = Mathf.Min(minGap, a.GlobalPosition.DistanceTo(run.Arena.Center));
            foreach (Shrine b in first.Where(b => b != a))
            {
                minGap = Mathf.Min(minGap, a.GlobalPosition.DistanceTo(b.GlobalPosition));
            }
        }

        Check("SHRINES-PLACED", first.Count == RunDirector.ShrinesAtOnce && minGap > 300f,
            $"{first.Count} shrines after a second ({string.Join(", ", first.Select(s => s.Kind))}), closest gap {minGap:0} px");

        run.Director.Spawning = false;
        Player p = run.Player;
        Shrine greed = run.Director.PlaceShrine(ShrineKind.Greed, p.GlobalPosition + new Vector2(400f, 0f));
        run.Director.TickShrines(0.1f);
        bool notYet = !greed.Used;
        p.GlobalPosition = greed.GlobalPosition;
        run.Director.TickShrines(0.1f);
        run.Director.TickShrines(0.1f);
        Check("GREED-SHRINE", notYet && greed.Used && run.State.Difficulty.Greed == 1 && run.State.Difficulty.Life > 1f,
            $"untouched at 400 px={notYet}; touched: used={greed.Used}, Greed {run.State.Difficulty.Greed}, monster life ×{run.State.Difficulty.Life:0.00}; used once");

        Shrine charge = run.Director.PlaceShrine(ShrineKind.Charge, p.GlobalPosition + new Vector2(0f, 300f));
        p.GlobalPosition = charge.GlobalPosition;
        run.Director.TickShrines(1.5f);
        p.GlobalPosition += new Vector2(400f, 0f);
        run.Director.TickShrines(0.1f);
        bool reset = charge.Progress == 0f && !charge.Used;
        p.GlobalPosition = charge.GlobalPosition;
        int chestsBefore = Chests();
        for (int i = 0; i < 4; i++)
        {
            run.Director.TickShrines(1f);
        }

        await Frames(3);
        Check("CHARGE-SHRINE", reset && charge.Used && Chests() > chestsBefore,
            $"leaving at 1.5 s reset it={reset}; 3 s inside: used={charge.Used}, a chest dropped ({chestsBefore} → {Chests()})");

        int enemiesBefore = GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup).Count;
        Shrine challenge = run.Director.PlaceShrine(ShrineKind.Challenge, p.GlobalPosition + new Vector2(0f, -400f));
        run.Director.UseShrine(challenge, p);
        await Frames(2);
        var challengers = GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup).OfType<Enemies.Enemy>().Where(e => e.GuaranteedChest > 0).ToList();
        Check("CHALLENGE-SHRINE", challengers.Count == 1 && GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup).Count >= enemiesBefore + 3 && challengers[0].IsElite,
            $"an elite with a guaranteed chest and two guards rose ({enemiesBefore} → {GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup).Count} monsters)");
        if (challengers.Count == 1)
        {
            challengers[0].Health.ApplyResolvedDamage(float.MaxValue / 4f, p);
            await Frames(6);
        }

        Check("CHALLENGE-DROPS-CHEST", challengers.Count == 1 && Chests() > chestsBefore + 1, $"the challenger's chest is on the floor ({Chests()} chests)");

        Shrine magnet = run.Director.PlaceShrine(ShrineKind.Magnet, p.GlobalPosition + new Vector2(-200f, 0f));
        run.State.SpawnExperience(p.GlobalPosition + new Vector2(1500f, 0f), 3f);
        await Frames(1);
        run.Director.UseShrine(magnet, p);
        Check("MAGNET-SHRINE", magnet.Used, "the magnet shrine pulls every gem (Lodestone effect)");
        await EndRift();
    }

    private int Chests() => GetTree().GetNodesInGroup(RunState.PickupGroup).OfType<Pickup>().Count(x => x.Kind == PickupKind.Chest && !x.IsQueuedForDeletion());

    // --- M4: tiers --------------------------------------------------------------------------

    private async Task TierLaunchChecks(Profile real)
    {
        RunController run = await NewRift(real, 4, "thick", "swift", "bogus");
        RiftDifficulty d = run.State.Difficulty;
        Check("TIER-FROM-LAUNCH", d.Tier == 4 && d.Fears.Count == 2 && d.Fears.Any(f => f.Id == "thick") && d.MagicChance > 0f,
            $"launched on tier {d.Tier} with [{string.Join(", ", d.Fears.Select(f => f.Id))}] (an unknown id dropped)");
        run.Director.Spawning = false;
        Enemies.Enemy e = run.Director.SpawnAt(run.Director.Slime!, run.Player.GlobalPosition + new Vector2(600f, 0f));
        float life = e.Stats.Life?.Max ?? 0f;
        await EndRift();
        RunController plain = await NewRift(real);
        plain.Director.Spawning = false;
        Enemies.Enemy f0 = plain.Director.SpawnAt(plain.Director.Slime!, plain.Player.GlobalPosition + new Vector2(600f, 0f));
        float plainLife = f0.Stats.Life?.Max ?? 0f;
        Check("TIER-SCALES-MONSTERS", life > plainLife * 3f, $"a slime has {plainLife:0} life on tier 1, {life:0} on tier 4 with Thick");

        real.RiftTierCleared = 2;
        real.RecordRiftClear(1);
        bool kept = real.RiftTierCleared == 2;
        real.RecordRiftClear(5);
        Check("TIER-UNLOCK-NEVER-LOWERS", kept && real.RiftTierCleared == 5 && real.RiftTierUnlocked == 6, $"a tier-1 clear keeps 2; a tier-5 clear opens tier {real.RiftTierUnlocked}");
        string json = real.ToJson();
        real.LoadJson(json);
        Check("TIER-SAVED", real.RiftTierCleared == 5, "the cleared tier survives a save and load");
        await EndRift();
    }

    // --- M6: the Onslaught and the endless milestones ------------------------------------------

    private async Task OnslaughtChecks(Profile real)
    {
        RunController run = await NewRift(real);
        run.State.SkipTo(run.Director.OnslaughtStart - 5f);
        run.Director.SyncTimers();
        bool before = !run.Director.InOnslaught;
        run.State.SkipTo(run.Director.OnslaughtStart + 1f);
        await Frames(2);
        Check("ONSLAUGHT-WINDOW", before && run.Director.InOnslaught && RunDirector.OnslaughtSpawn > 1f,
            $"off 5 s before, on at {run.Director.OnslaughtStart + 1f:0} s (spawns ×{RunDirector.OnslaughtSpawn})");

        run.Director.StartFinalBoss();
        await Frames(2);
        Check("ONSLAUGHT-ENDS-ON-COLOSSUS", !run.Director.InOnslaught, "the Colossus rising ends it");
        if (run.Director.ActiveBoss is { } boss)
        {
            boss.Health.ApplyResolvedDamage(float.MaxValue / 4f, run.Player);
        }

        await WaitSeconds(run, 2f);
        run.CashOut.Visible = false;
        run.Director.BeginEndless();
        GetTree().Paused = false;
        int chests = Chests();
        run.State.SkipTo(run.Director.EndlessSince + 5f * 60f + 1f);
        await WaitSeconds(run, 0.5f);
        Check("ENDLESS-MILESTONE-CHEST", run.Director.MilestonesReached == 1 && Chests() > chests && run.Director.NextMilestone == 10,
            $"5 min in the endless phase: milestone {run.Director.MilestonesReached}, chests {chests} → {Chests()}, next at {run.Director.NextMilestone} min");
        await EndRift();
    }

    // --- M7: challenges ---------------------------------------------------------------------

    private void ChallengeChecks(Profile real)
    {
        real.LoadJson(new Profile { Persist = false }.ToJson());
        bool locked = !Unlocks.Has(RiftChallenges.Starfall) && Progression.EvolutionCatalog.ForSkill("meteor") is { } star && !Progression.EvolutionCatalog.Known(star);
        GameEvents.Raise(GameEvents.RiftCleared, 3, "tier=3;fears=0;greed=0;coop=0;class=warrior;melee=0");
        bool win = RiftChallenges.IsDone(real.RiftChallenges, RiftChallenges.Get("rift_win")!);
        bool tier3 = RiftChallenges.IsDone(real.RiftChallenges, RiftChallenges.Get("rift_tier3")!);
        bool tier5 = RiftChallenges.IsDone(real.RiftChallenges, RiftChallenges.Get("rift_tier5")!);
        Check("CHALLENGE-UNLOCKS", locked && win && tier3 && !tier5 && Unlocks.Has(RiftChallenges.Starfall) && Unlocks.Has(RiftChallenges.ExtraBanish),
            $"Starfall locked at first={locked}; a tier-3 clear completes \"Heart of embers\" and \"Deeper still\" (not tier 5), unlocking Starfall and +1 Banish");

        for (int i = 0; i < 5; i++)
        {
            GameEvents.Raise(GameEvents.RiftEvolved, 1, "fireball");
        }

        int count = RiftChallenges.ProgressOf(real.RiftChallenges, RiftChallenges.Get("rift_evolve5")!);
        long sunder = real.Currency.GetValueOrDefault("sundering_orb");
        Check("CHALLENGE-COUNTS-AND-PAYS", count == 5 && sunder >= 2 && Unlocks.Has(RiftChallenges.Hailstorm),
            $"five evolutions: {count}/5, {sunder} Sundering Orbs paid, Hailstorm unlocked");

        string json = real.ToJson();
        real.LoadJson(new Profile { Persist = false }.ToJson());
        bool cleared = !Unlocks.Has(RiftChallenges.Starfall);
        real.LoadJson(json);
        Check("CHALLENGE-SAVED", cleared && Unlocks.Has(RiftChallenges.Starfall) && RiftChallenges.IsDone(real.RiftChallenges, RiftChallenges.Get("rift_win")!),
            "unlocks and progress survive a save and load (and a fresh account has none)");

        var build = new Progression.RunBuild();
        Check("UNLOCK-GATES-POOL", build.BanishesLeft == Progression.OfferRules.BanishesPerRun + 1,
            $"with \"Heart of embers\" done a run starts with {build.BanishesLeft} banishes");
        build.Free();
    }

    // --- the entry panel --------------------------------------------------------------------

    private async Task EntryPanelChecks(Profile real)
    {
        real.RiftTierCleared = 2;
        var panel = new RiftEntryPanel { Name = "RiftEntryPanel" };
        Net.RiftSpec? sent = null;
        panel.Enter = spec => sent = spec;
        AddChild(panel);
        await Frames(1);
        panel.Open();
        await Frames(1);
        bool lockedTier = panel.FindChild("Tier4", true, false) is Button { Disabled: true } && panel.FindChild("Tier3", true, false) is Button { Disabled: false };
        panel.Choose(3, new[] { "swift", "famine", "hordes", "thick" });
        (panel.FindChild("EnterRift", true, false) as Button)?.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(1);
        bool sane = sent is not null && sent.Sanitize();
        Check("ENTRY-PANEL", lockedTier && sent is { Kind: Net.RiftKind.SurvivalRift, Tier: 3 } && sent.Modifiers.Count == 3 && sane && !panel.IsOpen,
            $"tiers above 3 locked={lockedTier}; Enter sent tier {sent?.Tier} with [{string.Join(", ", sent?.Modifiers ?? new List<string>())}] (three at most), sane={sane}");
        var bad = Net.RiftSpec.SurvivalRift(3, new[] { "nope" });
        Check("SPEC-SANITIZES-FEARS", bad.Sanitize() && bad.Modifiers.Count == 0 && bad.Tier == 3, "an unknown Fear id is dropped, the tier kept");
        GetTree().Paused = false;
        panel.QueueFree();
        await Frames(1);
    }
}
