#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Items;
using ArpgGame.Run;
using ArpgGame.UI;

namespace ArpgGame.Tests;

/// <summary>The Rift's cash-out offer, tiers and the infinite phase — docs/design/run.md -> "Cash-out offer and the infinite Rift" (CASHOUT-*).</summary>
public partial class RiftProgressionTest
{
    private async Task CashOutChecks(Profile? real)
    {
        TierChecks();
        if (real is null)
        {
            Check("CASHOUT-SCENE", false, "no Profile autoload to run the Rift scene with");
            return;
        }

        await OfferTakeChecks(real);
        await StayPortalPauseChecks(real);
        await DeathChecks(real);
        await AutoplayChecks(real);
    }

    private void TierChecks()
    {
        RunRewards at(float t, int bosses = 3, int kills = 400) => RunRewards.Preview(t, bosses, kills, true);
        bool tiers = at(9 * 60f + 59f).Tier == 1 && at(600f).Tier == 2 && at(719f).Tier == 2 && at(720f).Tier == 3 && at(900f).Tier == 4
            && at(1200f).Tier == 5 && at(1200f).GuaranteedRare == 1 && at(1500f).GuaranteedRare == 2 && at(2400f).GuaranteedRare == 5
            && at(2700f).Tier == 10 && at(2700f).BonusAscendant == 1 && at(3000f).BonusAscendant == 2
            && RunRewards.Preview(600f, 3, 400, false).Tier == 0;
        RunRewards one = at(540f);
        bool text = one.NextTierText == "Tier II at 10:00: keep 2" && at(1100f).NextTierText.StartsWith("Tier V at 20:00");
        bool keep = at(599f).Keep == 1 && at(600f).Keep == 2 && at(900f).Keep == 3 && at(719f).Choices == 4 && at(720f).Choices == 5;
        Check("CASHOUT-TIERS", tiers && text && keep, $"I at 9:59, II 10:00, III 12:00, IV 15:00, V 20:00 with {at(1200f).GuaranteedRare} Rare, X 45:00 with {at(2700f).BonusAscendant} Ascendant; next-tier text '{one.NextTierText}'");

        // Preview and For agree at random moments (the panel shows what the results pay).
        var rng = new RandomNumberGenerator { Seed = 77 };
        bool agree = true;
        string worst = "";
        for (int i = 0; i < 60; i++)
        {
            float t = rng.RandfRange(30f, 3300f);
            int bosses = rng.RandiRange(0, 12);
            int kills = rng.RandiRange(0, 2500);
            bool victory = rng.Randf() < 0.8f;
            RunRewards a = RunRewards.Preview(t, bosses, kills, victory);
            RunRewards b = RunRewards.For(t, bosses, kills, victory, rng);
            bool same = a.Gold == b.Gold && a.OrbCount == b.Orbs.Count && a.Choices == b.Choices && a.Keep == b.Keep && a.ItemLevel == b.ItemLevel
                && Mathf.IsEqualApprox(a.RarityMultiplier, b.RarityMultiplier) && a.Tier == b.Tier && a.GuaranteedRare == b.GuaranteedRare && a.ItemsEarned == b.ItemsEarned;
            if (!same)
            {
                agree = false;
                worst = $"t={t:0} bosses={bosses} kills={kills} victory={victory}: gold {a.Gold}/{b.Gold}, orbs {a.OrbCount}/{b.Orbs.Count}, cards {a.Choices}/{b.Choices}";
                break;
            }
        }

        Check("CASHOUT-PREVIEW-MATCHES", agree, agree ? "60 random moments: Preview and For agree on gold, orbs, cards, keep, item level, rarity, tier" : worst);
    }

    /// <summary>A real Rift scene for a fresh character with a preset first skill, invulnerable.</summary>
    private async Task<RunController> NewRift(Profile real, bool autoCashOut = false, bool invulnerable = true)
    {
        var account = new Profile { Persist = false };
        CharacterData c = account.CreateCharacter("Cashier", "warrior");
        c.TutorialDone = true;
        c.Level = 20;
        real.LoadJson(account.ToJson());
        account.Free();
        RunLaunchOptions.StartSkill = "cleave";
        RunLaunchOptions.AutoCashOut = autoCashOut;
        // The test node never pauses; the run must, or the panel's pause means nothing.
        var holder = new Node { ProcessMode = ProcessModeEnum.Pausable, Name = "RiftHolder" };
        AddChild(holder);
        var run = GD.Load<PackedScene>(Scenes.Run).Instantiate<RunController>();
        holder.AddChild(run);
        await Frames(4);
        if (invulnerable)
        {
            run.Player.Health!.IgnoreDamage = true;
        }

        return run;
    }

    /// <summary>Waits up to <paramref name="seconds"/> for <paramref name="done"/>, answering any level-up cards on the way.</summary>
    private async Task<bool> WaitFor(RunController run, System.Func<bool> done, float seconds)
    {
        ulong end = Time.GetTicksMsec() + (ulong)(seconds * 1000f);
        while (Time.GetTicksMsec() < end)
        {
            if (done())
            {
                return true;
            }

            if (run.LevelUp.IsOpen && run.LevelUp.Armed)
            {
                run.LevelUp.ChooseIndex(0);
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        return done();
    }

    /// <summary>Kills the Colossus the way a fight would and returns once the director has registered the victory.</summary>
    private async Task<bool> KillColossus(RunController run)
    {
        run.Director.StartFinalBoss();
        await WaitFor(run, () => run.Director.ActiveBoss is not null, 3f);
        if (run.Director.ActiveBoss is { } boss)
        {
            boss.Health.ApplyResolvedDamage(float.MaxValue / 4f, run.Player);
        }

        return await WaitFor(run, () => run.Director.ColossusDefeated, 5f);
    }

    private void PressKey(Key k)
    {
        foreach (bool down in new[] { true, false })
        {
            GetViewport().PushInput(new InputEventKey { Pressed = down, Keycode = k, PhysicalKeycode = k });
        }
    }

    private static long Orbs(Player p) => Crafting.Orbs.Sum(o => p.Wallet.GetBalance(o.Id));

    private async Task OfferTakeChecks(Profile real)
    {
        RunController run = await NewRift(real);
        run.State.SkipTo(425f);
        run.Director.SyncTimers();
        bool killed = await KillColossus(run);
        float t0 = Time.GetTicksMsec() / 1000f;
        int enemiesAtKill = GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup).Count(n => n is Enemies.Enemy { IsBoss: false });
        bool held = run.Director.AwaitingCashOut && !run.CashOut.IsOpen;
        bool opened = await WaitFor(run, () => run.CashOut.IsOpen, 2.5f);
        float openedAfter = Time.GetTicksMsec() / 1000f - t0;

        int enemiesWhenOpen = GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup).Count(n => n is Enemies.Enemy { IsBoss: false });
        Check("CASHOUT-OFFER", killed && held && opened && openedAfter < 1.5f && GetTree().Paused && run.CashOut.Mode == CashOutPanel.PanelMode.Offer
            && enemiesWhenOpen <= enemiesAtKill && !run.Director.EndlessStarted && run.Director.ExitPortal is null && run.CashOut.TitleText == "The Colossus falls",
            $"victory registered={killed}, held before the panel={held}, panel open={opened} {openedAfter:0.00} s after the kill, paused={GetTree().Paused}, normal enemies {enemiesAtKill} → {enemiesWhenOpen}, no portal yet={run.Director.ExitPortal is null}");

        // The first 0.45 s ignore everything; Space and Enter never pick.
        PressKey(Key.Key1);
        PressKey(Key.Key2);
        bool guarded = run.CashOut.IsOpen && !run.State.Ended && !run.Director.EndlessStarted && !run.CashOut.Armed;
        for (int i = 0; i < 600 && !run.CashOut.Armed; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        string pausedNow = $"still paused after arming={GetTree().Paused}, t={run.State.Elapsed:0.000}";
        PressKey(Key.Space);
        PressKey(Key.Enter);
        PressKey(Key.Escape);
        bool noPick = run.CashOut.IsOpen && !run.State.Ended && !run.Director.EndlessStarted;
        Check("CASHOUT-INPUT-GUARD", guarded && noPick, $"{pausedNow}; a 1 and a 2 in the first {CashOutPanel.ArmDelay} s changed nothing={guarded}; Space, Enter and Esc never pick (offer stays open)={noPick}");

        // Key 1 takes the reward home: the results pay exactly what the panel showed.
        Player p = run.Player;
        long gold0 = p.Wallet.GetBalance(Profile.GoldId);
        long orbs0 = Orbs(p);
        RunRewards shown = run.CashOut.Shown;
        string diag = $"panel computed at {run.CashOut.ShownInputs}";
        PressKey(Key.Key1);
        await WaitFor(run, () => run.State.Ended, 2f);
        diag += $"; at the end t={run.State.Elapsed:0.000} bosses={run.State.BossesKilled} kills={run.State.Kills}";
        await Frames(3);
        long paidGold = p.Wallet.GetBalance(Profile.GoldId) - gold0;
        long paidOrbs = Orbs(p) - orbs0;
        string keepText = shown.Keep == 1 ? "Choose one item to keep" : $"Choose {shown.Keep} items to keep";
        Check("CASHOUT-TAKE", run.State.Ended && run.State.Victory && !run.State.Fell && paidGold == shown.Gold && paidOrbs == shown.OrbCount
            && run.Results.RewardCount == shown.Choices && run.Results.RewardHint == keepText && !run.CashOut.IsOpen,
            $"{diag}; ended as a victory={run.State.Victory}; gold {paidGold} (panel {shown.Gold}), orbs {paidOrbs} (panel {shown.OrbCount}), cards {run.Results.RewardCount} (panel {shown.Choices}), '{run.Results.RewardHint}'");
        GetTree().Paused = false;
        run.GetParent().QueueFree();
        await Frames(3);
    }

    private async Task StayPortalPauseChecks(Profile real)
    {
        RunController run = await NewRift(real);
        run.State.SkipTo(425f);
        run.Director.SyncTimers();
        run.Pause.Open();
        string before = run.Pause.LeaveButtonText;
        run.Pause.Close();
        await KillColossus(run);
        await WaitFor(run, () => run.CashOut.IsOpen && run.CashOut.Armed, 3f);
        PressKey(Key.Key2);
        await Frames(2);
        // Keep going: the game runs again, the portal is open, spawning is back, the reward panel shows tier I.
        await WaitFor(run, () => run.Director.EndlessStarted, 1f);
        int nonBoss = 0;
        await WaitFor(run, () =>
        {
            nonBoss = GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup).Count(n => n is Enemies.Enemy { IsBoss: false });
            return nonBoss > 0;
        }, 6f);
        await Seconds(0.35f);
        Check("CASHOUT-STAY", !run.CashOut.IsOpen && !GetTree().Paused && run.Director.EndlessStarted && run.Director.ExitPortal is not null && nonBoss > 0
            && run.Hud.RewardPanelVisible && run.Hud.RewardTitle == "REWARD · TIER I" && run.Hud.RewardNextText.StartsWith("Tier II at 10:00"),
            $"unpaused={!GetTree().Paused}, portal={run.Director.ExitPortal is not null}, {nonBoss} normal enemies back, panel '{run.Hud.RewardTitle}' / '{run.Hud.RewardNextText}'");

        // A new tier: toast and the gauge line.
        run.State.SkipTo(601f);
        await Seconds(0.6f);
        Check("CASHOUT-TIER-TOAST", run.Hud.ToastText.StartsWith("Reward tier II") && run.Hud.RewardTitle == "REWARD · TIER II" && run.Hud.DangerGauge()!.Value.Detail.StartsWith("Reward tier II · next at 12:00"),
            $"toast '{run.Hud.ToastText}', title '{run.Hud.RewardTitle}', gauge '{run.Hud.DangerGauge()?.Detail}'");

        // The pause menu offers "Leave with reward" now, and it opens the panel in leave mode.
        run.Pause.Open();
        string after = run.Pause.LeaveButtonText;
        run.Pause.PressLeave();
        await Frames(2);
        bool leaveOpen = run.CashOut.IsOpen && run.CashOut.Mode == CashOutPanel.PanelMode.Leave && !run.State.Ended && run.CashOut.StayLabel == "Not yet";
        Check("CASHOUT-PAUSE", before == "Abandon run" && after == "Leave with reward" && leaveOpen, $"label before the Colossus '{before}', after '{after}', leave panel open={leaveOpen}");
        for (int i = 0; i < 600 && !run.CashOut.Armed; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        PressKey(Key.Escape); // Esc means Not yet in leave mode
        await Frames(3);
        bool notYet = !run.CashOut.IsOpen && !run.State.Ended && !run.Pause.IsOpen && !GetTree().Paused;

        // The portal: walking in opens the panel (it never ends the run by itself), Not yet closes it,
        // and stepping out and in again reopens it.
        Node2D portal = run.Director.ExitPortal!;
        run.Player.GlobalPosition = portal.GlobalPosition + new Vector2(0f, 220f);
        await Seconds(0.2f);
        run.Player.GlobalPosition = portal.GlobalPosition;
        bool entered = await WaitFor(run, () => run.CashOut.IsOpen, 1.5f);
        bool leaveMode = entered && run.CashOut.Mode == CashOutPanel.PanelMode.Leave && !run.State.Ended;
        for (int i = 0; i < 600 && !run.CashOut.Armed; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        PressKey(Key.Key2);
        await Frames(2);
        await Seconds(0.5f);
        bool stayedClosed = !run.CashOut.IsOpen && !run.State.Ended; // still standing in the portal: no re-trigger
        run.Player.GlobalPosition = portal.GlobalPosition + new Vector2(0f, 100f);
        await Seconds(0.3f);
        run.Player.GlobalPosition = portal.GlobalPosition;
        bool reopened = await WaitFor(run, () => run.CashOut.IsOpen, 1.5f);
        Check("CASHOUT-PORTAL", notYet && leaveMode && stayedClosed && reopened, $"Esc = Not yet={notYet}; portal opens leave mode without ending the run={leaveMode}; Not yet closes it and standing in it doesn't reopen={stayedClosed}; re-entering reopens={reopened}");

        // Leaving from the panel pays what it showed (still a victory).
        for (int i = 0; i < 600 && !run.CashOut.Armed; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        RunRewards shown = run.CashOut.Shown;
        long gold0 = run.Player.Wallet.GetBalance(Profile.GoldId);
        PressKey(Key.Key1);
        await WaitFor(run, () => run.State.Ended, 2f);
        await Frames(2);
        Check("CASHOUT-LEAVE-PAYS", run.State.Ended && run.State.Victory && run.Player.Wallet.GetBalance(Profile.GoldId) - gold0 == shown.Gold, $"gold paid {run.Player.Wallet.GetBalance(Profile.GoldId) - gold0} of {shown.Gold}");
        GetTree().Paused = false;
        run.GetParent().QueueFree();
        await Frames(3);
    }

    private async Task DeathChecks(Profile real)
    {
        RunController run = await NewRift(real, invulnerable: false);
        run.State.SkipTo(425f);
        run.Director.SyncTimers();
        run.Player.Health!.IgnoreDamage = true;
        await KillColossus(run);
        await WaitFor(run, () => run.CashOut.IsOpen && run.CashOut.Armed, 3f);
        PressKey(Key.Key2);
        await Frames(2);
        await Seconds(PartyRulesGrace() + 0.3f); // keep going gives a second of invulnerability
        run.State.SkipTo(13f * 60f + 40f);
        await Frames(2);
        run.Player.Health.IgnoreDamage = false;
        RunRewards expected = RunRewards.Preview(run.State.Elapsed, run.State.BossesKilled, run.State.Kills, true);
        long gold0 = run.Player.Wallet.GetBalance(Profile.GoldId);
        run.Player.Health.ApplyResolvedDamage(1e9f, null);
        await WaitFor(run, () => run.State.Ended, 2f);
        await Frames(3);
        long paid = run.Player.Wallet.GetBalance(Profile.GoldId) - gold0;
        var subtitle = run.Results.FindChild("FellSubtitle", true, false) as Label;
        Check("CASHOUT-DEATH", run.State.Ended && run.State.Victory && run.State.Fell && paid >= expected.Gold && paid <= expected.Gold + 2 && subtitle is not null && subtitle.Text.Contains("13:40") && subtitle.Text.Contains("The reward is yours"),
            $"victory={run.State.Victory}, fell={run.State.Fell}, gold {paid} (reward at 13:40 {expected.Gold}), subtitle '{subtitle?.Text}'");
        GetTree().Paused = false;
        run.GetParent().QueueFree();
        await Frames(3);
    }

    private static float PartyRulesGrace() => Net.PartyRules.ResumeGraceSeconds;

    private async Task AutoplayChecks(Profile real)
    {
        RunController run = await NewRift(real, autoCashOut: true);
        run.State.SkipTo(425f);
        run.Director.SyncTimers();
        await KillColossus(run);
        await WaitFor(run, () => run.State.Ended, 3f);
        Check("CASHOUT-AUTOPLAY", run.State.Ended && run.State.Victory && !run.CashOut.IsOpen && run.Director.ExitPortal is null, $"the bot's run ended at the Colossus: victory={run.State.Victory}, no panel={!run.CashOut.IsOpen}");
        GetTree().Paused = false;
        run.GetParent().QueueFree();
        await Frames(3);
        RunLaunchOptions.Reset();
    }
}
