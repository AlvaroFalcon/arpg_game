#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Run;
using ArpgGame.UI;

namespace ArpgGame.Tools;

/// <summary>
/// `--only=rift`: the Survival Rift's progression screens (docs/design/progression.md,
/// run.md -> "Rewards", items.md -> "Pending items") with a demo character whose
/// bag is full — the start pick with the character's skills, a lesser and a great
/// keystone row, an ordinary row after one per-card reroll, the results screen
/// sending the reward to Pending items, a run left at once (no item, and why),
/// then the hideout's banner and the stash's Pending tab. Also the co-op card
/// screen (<see cref="CoopCardShots"/>): counter, countdown, waiting.
/// </summary>
public partial class RunCapture
{
    private async Task RiftScript()
    {
        SetupDemoProfile();
        if (Profile.Get(this) is { } profile)
        {
            profile.Hideout.GuideSeen = true;
            CharacterData k = profile.Active;
            k.Inventory.Clear();
            var rng = new RandomNumberGenerator { Seed = 77 };
            int guard = 0;
            while (guard++ < 300)
            {
                profile.AddToStash(Items.ItemFactory.RollRandom(30, Items.DropSource.RiftReward, 2f, rng));
            }

            for (int i = 0; i < 2; i++)
            {
                profile.AddToOverflow(Items.ItemFactory.RollRandom(34, Items.DropSource.RiftReward, 3f, rng));
            }
        }

        // A run with the character's skills: the start pick, then the keystone levels.
        RunLaunchOptions.Reset();
        RunLaunchOptions.AutoLevels = 3;
        _autoPick = false;
        await Launch();
        await Wait(1.0f);
        await Snap("rift-start-pick.png");
        PickFirstCard();
        await Wait(0.4f);

        await LevelTo(5);
        await Snap("rift-levelup-lesser.png");
        PickFirstCard();

        await LevelTo(6);
        if (Run() is { } run)
        {
            run.LevelUp.RerollCard(1);
            await Wait(LevelUpScreen.ArmDelay + 0.2f);
        }

        await Snap("rift-levelup-reroll.png");
        PickFirstCard();
        for (int level = 7; level <= 9; level++)
        {
            await LevelTo(level);
            PickFirstCard();
        }

        await LevelTo(10);
        await Snap("rift-levelup-great.png");
        PickFirstCard();
        await Wait(1f);
        await CoopCardShots();
        await Wait(2f);

        // The end: the bag is full, so the kept item goes to Pending items.
        if (Run() is { } ending)
        {
            ending.State.SkipTo(420f);
            ending.State.EndRun(false);
            await Wait(2.0f);
            ending.Results.ClaimIndex(0);
            await Wait(0.4f);
            await Snap("rift-results-pending.png");
            ending.Results.Leave(null);
        }

        // A run left at once: gold and orbs by the formula, no item, and the reason.
        RunLaunchOptions.Reset();
        await Launch();
        await Wait(0.8f);
        PickFirstCard();
        await Wait(1.5f);
        if (Run() is { } quit)
        {
            quit.State.EndRun(false);
            await Wait(2.0f);
            await Snap("rift-results-no-item.png");
            quit.Results.Leave(null);
        }

        _autoPick = true;
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Hideout);
        await Wait(1.2f);
        await Snap("rift-hideout-pending.png");
        if (GetTree().CurrentScene.GetNodeOrNull<StashPanel>("StashPanel") is { } stash)
        {
            stash.Open();
            await Wait(0.4f);
            await Snap("rift-stash-pending.png");
            stash.Close();
        }

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }

    /// <summary>
    /// The co-op card screen (docs/design/multiplayer.md -> "Level-ups", menus.md ->
    /// "Level-up and results"): the same screen and the same calls a host's round
    /// makes (<see cref="LevelUpScreen.OpenPartyRound"/>, <see cref="LevelUpScreen.SetPartyStatus"/>),
    /// driven here with a party of three: the start pick, "1 / 3 chosen" with time
    /// left, the last five seconds in red, and the waiting line after choosing.
    /// </summary>
    private async Task CoopCardShots()
    {
        if (Run() is not { } run)
        {
            return;
        }

        LevelUpScreen screen = run.LevelUp;
        screen.Coop = true;
        GetTree().Paused = true; // what the host's round does to every machine

        // The status is set again right before each shot: captures run slowly under Xvfb.
        screen.OpenPartyRound(1, true, 0, 1);
        await Wait(LevelUpScreen.ArmDelay + 0.3f);
        screen.SetPartyStatus(0, 3, Time.GetTicksMsec() + 9400, false);
        await Wait(0.05f);
        await Snap("coop-start-pick.png");
        screen.ClosePartyRound();

        screen.OpenPartyRound(2, false, run.State.Level + 1, 1);
        await Wait(LevelUpScreen.ArmDelay + 0.3f);
        screen.SetPartyStatus(1, 3, Time.GetTicksMsec() + 7400, false);
        await Wait(0.05f);
        await Snap("coop-levelup-counter.png");
        screen.SetPartyStatus(1, 3, Time.GetTicksMsec() + 3400, false);
        await Wait(0.05f);
        await Snap("coop-levelup-red.png");
        screen.ChooseIndex(0);
        screen.SetPartyStatus(2, 3, Time.GetTicksMsec() + 2400, true);
        await Wait(0.05f);
        await Snap("coop-levelup-waiting.png");
        screen.ClosePartyRound();

        screen.Coop = false;
        GetTree().Paused = false;
    }

    /// <summary>Gives the experience for the next level and waits until its cards are armed.</summary>
    private async Task LevelTo(int level)
    {
        if (Run() is not { } run)
        {
            return;
        }

        while (run.State.Level < level)
        {
            run.State.AddExperience(RunState.ExperienceToNext(run.State.Level) + 0.1f);
        }

        await Wait(LevelUpScreen.ArmDelay + 0.3f);
    }
}
