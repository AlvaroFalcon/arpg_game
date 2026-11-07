#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Core;
using ArpgGame.Run;
using ArpgGame.Stats;
using ArpgGame.UI;

namespace ArpgGame.Tools;

/// <summary>
/// `--only=riftloop`: the Survival Rift's 2026-11 loop (docs/design/survival-roadmap.md) — the
/// entry panel with a tier and Fear cards, a level-up row with Luck's 4th card, Skip and Banish,
/// shrines and chests in the arena, a golden chest opening, an evolution and the Onslaught.
/// </summary>
public partial class RunCapture
{
    private async Task RiftLoopScript()
    {
        SetupDemoProfile();
        if (Profile.Get(this) is { } profile)
        {
            profile.Hideout.GuideSeen = true;
            profile.RiftTierCleared = 3;
            profile.RiftChallenges["rift_win"] = 1;
            profile.RiftChallenges["rift_evolve5"] = 2;
        }

        // The entry panel, over a dark backdrop.
        var panel = new RiftEntryPanel { Name = "RiftEntryPanel" };
        GetTree().Root.AddChild(panel);
        await Wait(0.3f);
        panel.Open();
        panel.Choose(3, new[] { "swift", "hordes" });
        await Wait(0.4f);
        await Snap("riftloop-entry-panel.png");
        panel.Close();
        panel.QueueFree();
        GetTree().Paused = false;

        RunLaunchOptions.Reset();
        RunLaunchOptions.StartSkill = "fireball";
        RunLaunchOptions.RiftTier = 3;
        RunLaunchOptions.Fears = new System.Collections.Generic.List<string> { "swift", "hordes" };
        _autoPick = false;
        _steer = false;
        await Launch();
        await Wait(1.5f);
        if (Run() is not { } run)
        {
            return;
        }

        // A lucky level-up: four cards, Skip and Banish.
        run.Player.Stats.Get(StatId.Luck).AddModifier(new StatModifier(ModifierType.Flat, 5f, "capture"));
        await LevelTo(2);
        int guard = 0;
        while (run.LevelUp.Offers.Count < 4 && guard++ < 20)
        {
            PickFirstCard();
            await LevelTo(run.State.Level + 1);
        }

        await Snap("riftloop-levelup-four-cards.png");
        run.LevelUp.BanishCard(1);
        await Wait(0.5f);
        await Snap("riftloop-levelup-banished.png");
        PickFirstCard();
        await Wait(0.3f);

        // Shrines and chests around the player.
        run.Director.Spawning = false;
        Vector2 at = run.Player.GlobalPosition;
        run.Director.PlaceShrine(ShrineKind.Greed, at + new Vector2(-260f, -120f));
        run.Director.PlaceShrine(ShrineKind.Charge, at + new Vector2(260f, -60f));
        run.Director.PlaceShrine(ShrineKind.Challenge, at + new Vector2(-40f, 190f));
        run.State.DropChest(at + new Vector2(110f, 120f), 1);
        run.State.DropChest(at + new Vector2(170f, 150f), 3);
        run.State.DropChest(at + new Vector2(230f, 110f), 5);
        await Wait(0.8f);
        await Snap("riftloop-shrines-and-chests.png");

        // Charge the shrine, then open the golden chest.
        Shrine charge = run.Director.Shrines.First(s => s.Kind == ShrineKind.Charge);
        run.Player.GlobalPosition = charge.GlobalPosition;
        await Wait(1.6f);
        await Snap("riftloop-charge-shrine.png");
        await Wait(2f);
        run.Player.GlobalPosition = at + new Vector2(230f, 110f);
        await Wait(0.6f);
        await Snap("riftloop-golden-chest.png");

        // An evolution: Fireball at its cap with Expanse, from a boss-sized chest.
        if (run.Build.FindSkill("fireball") is { } fireball)
        {
            fireball.Level = Progression.RunBuild.MaxSkillLevel;
            run.Build.ApplyKey("p:area:1:1");
            await Wait(0.4f);
            await Snap("riftloop-evolution-ready.png");
            Pickup chest = run.State.DropChest(run.Player.GlobalPosition + new Vector2(60f, 0f), 3);
            run.Player.GlobalPosition = chest.GlobalPosition;
            await Wait(0.8f);
            run.Director.Spawning = true;
            await Wait(1.5f);
            await Snap("riftloop-evolved-firestorm.png");
        }

        // The Onslaught.
        run.State.SkipTo(run.Director.OnslaughtStart + 1f);
        run.Director.SyncTimers();
        _steer = true;
        await Wait(4f);
        await Snap("riftloop-onslaught.png");
        GetTree().Quit();
    }
}
