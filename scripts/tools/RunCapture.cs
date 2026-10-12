#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PathToExile.Enemies;
using PathToExile.Characters;
using PathToExile.Core;
using PathToExile.Run;
using PathToExile.UI;
using PathToExile.World;

namespace PathToExile.Tools;

/// <summary>
/// Plays scripted runs in a real (windowed) session and saves screenshots for
/// the devlog / PR — run with scripts/capture_run_screenshots.sh. It moves
/// itself under the scene-tree root so it survives scene changes, then
/// launches the run several times with different <see cref="RunLaunchOptions"/>
/// (early swarm, mid-run build, each boss, phase mechanics). The player is
/// made immune to damage and steered in slow circles so the shots show a
/// living fight rather than a corpse; level-up screens are auto-picked (one is
/// photographed).
/// </summary>
public partial class RunCapture : Node
{
    private string _outDir = "res://docs/devlog/screenshots";
    private bool _autoPick = true;
    private bool _steer = true;
    private float _steerAngle;
    private string _only = "";

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--out="))
            {
                _outDir = arg[6..];
            }
            else if (arg.StartsWith("--only="))
            {
                _only = arg[7..];
            }
        }

        ProcessMode = ProcessModeEnum.Always;
        // Aim at enemies: there is no mouse in a scripted run.
        Core.GameSettings.Get(this)?.SetAutoAim(true);
        CallDeferred(nameof(Detach));
    }

    private void Detach()
    {
        Node root = GetTree().Root;
        GetParent().RemoveChild(this);
        root.AddChild(this);
        _ = _only is "endgame" or "panels" ? EndgameScript() : _only == "voxel" ? VoxelScript() : Script();
    }

    private async Task Script()
    {
        await Shot(Scenes(main: true), "menu-main.png", 1.0f);
        if (GetTree().CurrentScene is UI.MainMenu menu)
        {
            menu.OpenSettingsForCapture();
            await Wait(0.4f);
            await Snap("menu-settings.png");
        }

        await Shot(PathToExile.Core.Scenes.Hideout, "hideout.png", 1.2f);

        // Start-of-run skill pick.
        RunLaunchOptions.Reset();
        _autoPick = false;
        await Launch();
        await Wait(1.0f);
        await Snap("run-skill-pick.png");
        PickFirstCard();
        _autoPick = true;

        // Early run.
        await Wait(14f);
        await Snap("run-early.png");

        // Mid run with a built-up character.
        RunLaunchOptions.Reset();
        RunLaunchOptions.StartTime = 150f;
        RunLaunchOptions.StartSkill = "spark";
        RunLaunchOptions.AutoLevels = 12;
        await Launch();
        await Wait(16f);
        await Snap("run-midgame.png");

        // A level-up screen.
        _autoPick = false;
        if (Run() is { } r1)
        {
            r1.State.AddExperience(RunState.ExperienceToNext(r1.State.Level));
        }

        await Wait(0.6f);
        await Snap("run-levelup.png");
        PickFirstCard();
        _autoPick = true;

        // Pause menu.
        await Wait(0.5f);
        Run()?.Pause.Open();
        await Wait(0.3f);
        await Snap("run-pause.png");
        Run()?.Pause.Close();

        // The Bone Herald.
        RunLaunchOptions.Reset();
        RunLaunchOptions.StartTime = 176f;
        RunLaunchOptions.StartSkill = "fireball";
        RunLaunchOptions.AutoLevels = 10;
        await Launch();
        await Wait(9f);
        await Snap("boss-herald.png");
        await WaitForAttack("Grave Circles", 45f);
        await Wait(0.7f);
        await Snap("boss-herald-circles.png");

        // The Ember Colossus, each phase.
        RunLaunchOptions.Reset();
        RunLaunchOptions.StartTime = 417f;
        RunLaunchOptions.StartSkill = "meteor";
        RunLaunchOptions.AutoLevels = 18;
        await Launch();
        await Wait(6f);
        await Snap("boss-colossus.png");
        await WaitForAttack("Seismic Slam", 25f);
        await Wait(0.6f);
        await Snap("boss-colossus-slam.png");
        SetBossLife(0.6f, immune: true);
        await WaitForAttack("Inferno Ring", 40f);
        await Wait(1.0f);
        await Snap("boss-colossus-inferno.png");
        SetBossLife(0.25f, immune: true);
        await WaitForAttack("Flame Beams", 40f);
        await Wait(0.8f);
        await Snap("boss-colossus-beams.png");

        // Results.
        if (Run()?.Director.ActiveBoss is { } finalBoss)
        {
            finalBoss.Health.IgnoreDamage = false;
        }

        Run()?.State.EndRun(true);
        await Wait(1.5f);
        await Snap("run-results.png");

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }

    // --- endgame ------------------------------------------------------------------

    /// <summary>Demo characters, in memory only (the real profile is never touched).</summary>
    private void SetupDemoProfile()
    {
        if (Profile.Get(this) is not { } profile)
        {
            return;
        }

        profile.Persist = false;
        while (profile.Characters.Count > 0)
        {
            profile.DeleteCharacter(0);
        }

        CharacterData nyx = profile.CreateCharacter("Nyx", "rogue");
        nyx.Level = 9;
        nyx.HighestTierCleared = 2;
        CharacterData ilva = profile.CreateCharacter("Ilva", "arcanist");
        ilva.Level = 3;

        CharacterData k = profile.CreateCharacter("Kaelen", "warrior");
        k.Level = 24;
        k.Gold = 900;
        k.HighestTierCleared = 5;
        k.RealitiesCleared = 11;
        k.Gems[0].Level = 9;
        k.AddGem("ground_slam", false, 6);
        k.AddGem("sunder", false, 4);
        k.AddGem("throwing_knives", false, 2);
        foreach (string sup in new[] { "melee_splash", "brutality", "multistrike", "increased_area", "pierce", "faster_casting" })
        {
            k.AddGem(sup, true);
        }

        GemData slam = k.Gems.First(g => g.Id == "ground_slam");
        k.Loadout[1].SkillUid = slam.Uid;
        k.Loadout[1].SupportUids.Add(k.Gems.First(g => g.Id == "brutality").Uid);
        k.Loadout[0].SupportUids.Add(k.Gems.First(g => g.Id == "melee_splash").Uid);
        foreach ((int tier, int n) in new[] { (2, 1), (3, 2), (4, 1), (5, 3), (6, 1), (7, 1) })
        {
            k.AddShards(tier, n);
        }

        // Spend the passive points by walking outward from the start.
        var frontier = new Queue<string>(new[] { k.Class.TreeStart });
        while (k.PassivePointsFree > 0 && frontier.Count > 0)
        {
            foreach (string next in PassiveTree.Get(frontier.Dequeue())!.Links)
            {
                if (k.PassivePointsFree > 0 && PassiveTree.CanAllocate(k, next) && PassiveTree.Get(next)!.Kind != PassiveKind.Keystone)
                {
                    k.Passives.Add(next);
                    frontier.Enqueue(next);
                }
            }
        }

        // Demo gear: a full paper doll plus a few items for the inventory and stash.
        var rng = new RandomNumberGenerator { Seed = 7 };
        foreach ((string id, Items.EquipmentSlot slot, Items.Rarity rarity) in new[]
        {
            ("iron_axe", Items.EquipmentSlot.Weapon, Items.Rarity.Rare), ("oak_buckler", Items.EquipmentSlot.Offhand, Items.Rarity.Magic),
            ("leather_cap", Items.EquipmentSlot.Helmet, Items.Rarity.Magic), ("ringmail_vest", Items.EquipmentSlot.BodyArmour, Items.Rarity.Rare),
            ("rawhide_gloves", Items.EquipmentSlot.Gloves, Items.Rarity.Common), ("rawhide_boots", Items.EquipmentSlot.Boots, Items.Rarity.Magic),
            ("rope_belt", Items.EquipmentSlot.Belt, Items.Rarity.Common), ("copper_amulet", Items.EquipmentSlot.Amulet, Items.Rarity.Rare),
            ("copper_ring", Items.EquipmentSlot.Ring1, Items.Rarity.Magic), ("linen_trousers", Items.EquipmentSlot.Pants, Items.Rarity.Magic),
        })
        {
            if (Items.ItemFactory.FindBase(id) is { } b)
            {
                k.Equipped[slot] = Items.ItemFactory.Roll(b, rarity, 22, rng);
            }
        }

        for (int i = 0; i < 5; i++)
        {
            k.Inventory.Add((Items.ItemFactory.RollRandom(22, 0.5f, rng), new Vector2I(i * 2, 0)));
        }

        for (int i = 0; i < 6; i++)
        {
            profile.StashTabs[0].Add((Items.ItemFactory.RollRandom(18, 0.4f, rng), new Vector2I((i % 4) * 3, (i / 4) * 3)));
        }

        k.Ascendancy = "samurai";
        k.AscendancyPoints = 4;
        foreach (AscendancyNode node in AscendancyCatalog.For("samurai").Take(3))
        {
            k.AscendancyNodes.Add(node.Id);
        }
    }

    private async Task EndgameScript()
    {
        SetupDemoProfile();
        GetTree().ChangeSceneToFile(PathToExile.Core.Scenes.MainMenu);
        await Wait(0.8f);
        if (GetTree().CurrentScene is MainMenu menu)
        {
            menu.OpenCharactersForCapture();
            await Wait(0.4f);
            await Snap("menu-characters.png");
            if (FindChild<CharacterSelect>(menu) is { } select)
            {
                select.ShowCreate();
                await Wait(0.3f);
                await Snap("menu-create.png");
            }
        }

        GetTree().ChangeSceneToFile(PathToExile.Core.Scenes.Hideout);
        await Wait(1.0f);
        Node hideout = GetTree().CurrentScene;
        var player = hideout.GetNode<Player>("Player");
        player.GlobalPosition = hideout.GetNode<Node2D>("Mirror").GlobalPosition + new Vector2(30f, 60f);
        await Wait(0.6f);
        await Snap("hideout.png");

        var mirror = hideout.GetNode<MirrorPanel>("MirrorPanel");
        mirror.Open();
        await Wait(0.3f);
        mirror.Select(5);
        mirror.Empower();
        await Wait(0.3f);
        await Snap("mirror-panel.png");
        mirror.Close();

        var panels = hideout.GetNode<CharacterPanels>("CharacterPanels");
        panels.Skills.Open();
        await Wait(0.3f);
        await Snap("panel-skills.png");
        panels.Skills.Close();
        panels.Tree.Open();
        await Wait(0.3f);
        await Snap("panel-passives.png");
        panels.Tree.Close();
        var altar = hideout.GetNode<AscendancyPanel>("AscendancyPanel");
        altar.Open();
        await Wait(0.3f);
        await Snap("panel-ascendancy.png");
        altar.Close();
        await Wait(0.2f);

        panels.Sheet.Open();
        await Wait(0.3f);
        await Snap("panel-character.png");
        panels.Sheet.Close();

        var inventoryUi = player.GetNode<InventoryUI>("InventoryUI");
        inventoryUi.SetOpen(true);
        await Wait(0.3f);
        await Snap("panel-inventory.png");
        inventoryUi.SetOpen(false);

        var stash = hideout.GetNode<StashPanel>("StashPanel");
        stash.Open();
        await Wait(0.3f);
        await Snap("panel-stash.png");
        stash.Close();
        await Wait(0.2f);

        foreach ((string layout, int tier, string name) in new[] { ("ashen_halls", 5, "ashen"), ("bone_crypt", 8, "crypt"), ("drowned_mirror", 12, "drowned") })
        {
            if (_only == "panels")
            {
                break;
            }

            if (!ResourceLoader.Exists(RealityTheme.Get(LayoutCatalog.Find(layout)!.ThemeId).TilesetPath))
            {
                GD.Print($"[CAPTURE] skipping {layout}: tileset missing");
                continue;
            }

            await CaptureReality(layout, tier, name);
        }

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }

    /// <summary>Quick look at the voxel view: hideout and one reality per theme.</summary>
    private async Task VoxelScript()
    {
        SetupDemoProfile();
        GetTree().ChangeSceneToFile(PathToExile.Core.Scenes.Hideout);
        await Wait(1.2f);
        Node hideout = GetTree().CurrentScene;
        hideout.GetNode<Player>("Player").GlobalPosition = new Vector2(420f, 330f);
        await Wait(0.6f);
        await Snap("voxel-hideout.png");
        foreach ((string layout, int tier, string name) in new[] { ("ashen_halls", 5, "ashen"), ("bone_crypt", 8, "crypt"), ("drowned_mirror", 12, "drowned") })
        {
            if (_only == "voxel" && name != "ashen" && OS.GetCmdlineUserArgs().Length < 3)
            {
                break;
            }

            await CaptureReality(layout, tier, name);
        }

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }

    private async Task CaptureReality(string layout, int tier, string name)
    {
        RealityLaunch.Tier = tier;
        RealityLaunch.LayoutId = layout;
        RealityLaunch.Modifiers = new List<string> { "elite", "hordes" };
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(PathToExile.Core.Scenes.Reality);
        await Wait(0.5f);
        if (GetTree().CurrentScene is not RealityController reality)
        {
            return;
        }

        reality.Player.Health!.IgnoreDamage = true;
        reality.Player.GetNode<PlayerSkillInput>("PlayerSkillInput").ForceAutoCast = true;
        await Wait(1.0f);
        await Snap($"reality-{name}-start.png");

        // The richest pack: a rare or unique-rare if the layout rolled one.
        Enemy? best = reality.GetChildren().OfType<Enemy>().Where(e => !e.IsBoss).OrderByDescending(e => (int)e.Rarity).FirstOrDefault();
        if (best is not null)
        {
            reality.Player.GlobalPosition = best.GlobalPosition + new Vector2(-220f, 40f);
            await Wait(3.5f);
            await Snap($"reality-{name}-pack.png");
        }

        reality.Player.GlobalPosition = RasterLayout.World(reality.Raster.BossCell) + new Vector2(0f, 200f);
        await Wait(5f);
        await Snap($"reality-{name}-boss.png");
        if (name == "drowned" && reality.Boss is { Stats.Life: { } life } nereth)
        {
            nereth.Health.IgnoreDamage = true;
            life.Current = life.Max * 0.5f;
            await WaitForRealityAttack(reality, "Mirror Images", 40f);
            await Wait(0.9f);
            await Snap("boss-nereth-mirror.png");
            life.Current = life.Max * 0.2f;
            await WaitForRealityAttack(reality, "Rising Tide", 50f);
            await Wait(1.4f);
            await Snap("boss-nereth-tide.png");
            nereth.Health.IgnoreDamage = false;
        }

        if (reality.Boss is { } boss)
        {
            reality.Player.GlobalPosition = boss.GlobalPosition + new Vector2(0f, -200f);
            boss.Health.ApplyResolvedDamage(1e9f, reality.Player);
        }

        await Wait(1.6f);
        await Snap($"reality-{name}-cleared.png");
    }

    private async Task WaitForRealityAttack(RealityController reality, string attack, float timeout)
    {
        for (float waited = 0f; waited < timeout; waited += 0.1f)
        {
            if (IsInstanceValid(reality) && reality.Boss is { } b && IsInstanceValid(b) && b.CurrentAttack == attack)
            {
                return;
            }

            await Wait(0.1f);
        }

        GD.Print($"[CAPTURE] timed out waiting for {attack}");
    }

    private static T? FindChild<T>(Node node) where T : Node
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is T t)
            {
                return t;
            }

            if (FindChild<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string Scenes(bool main) => PathToExile.Core.Scenes.MainMenu;

    private async Task Shot(string scene, string file, float wait)
    {
        GetTree().ChangeSceneToFile(scene);
        await Wait(wait);
        await Snap(file);
    }

    private async Task Launch()
    {
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(PathToExile.Core.Scenes.Run);
        await Wait(0.3f);
        if (Run() is { } run)
        {
            run.Player.Health!.IgnoreDamage = true;
        }
    }

    private RunController? Run() => GetTree().CurrentScene as RunController;

    public override void _PhysicsProcess(double delta)
    {
        if (Run() is not { } run || run.State.Ended)
        {
            return;
        }

        if (_autoPick && run.LevelUp.IsOpen)
        {
            PickFirstCard();
        }

        if (_steer && !GetTree().Paused)
        {
            // Slow circles around the arena centre: keeps the camera moving
            // through the fight without needing input.
            _steerAngle += (float)delta * 0.5f;
            Vector2 centre = run.Director.ActiveBoss?.GlobalPosition ?? run.Arena.Center;
            Vector2 goal = centre + Vector2.Right.Rotated(_steerAngle) * 230f;
            Vector2 dir = run.Player.GlobalPosition.DirectionTo(goal);
            run.Player.Velocity = dir * run.Player.Stats.MovementSpeed.Value;
            run.Player.MoveAndSlide();
        }
    }

    private void PickFirstCard() => Run()?.LevelUp.ChooseIndex(0);

    private void SetBossLife(float fraction, bool immune = false)
    {
        if (Run()?.Director.ActiveBoss is { Stats.Life: { } life } boss)
        {
            life.Current = life.Max * fraction;
            boss.Health.IgnoreDamage = immune;
        }
    }

    private async Task WaitForAttack(string attack, float timeout)
    {
        float waited = 0f;
        while (waited < timeout)
        {
            if (Run()?.Director.ActiveBoss is BossEnemy boss && boss.CurrentAttack == attack)
            {
                return;
            }

            await Wait(0.1f);
            waited += 0.1f;
        }

        GD.Print($"[CAPTURE] timed out waiting for {attack}");
    }

    private async Task Wait(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds, processAlways: true), SceneTreeTimer.SignalName.Timeout);
    }

    private async Task Snap(string file)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        string path = _outDir.StartsWith("res://") || _outDir.StartsWith("user://")
            ? ProjectSettings.GlobalizePath($"{_outDir}/{file}")
            : $"{_outDir}/{file}";
        Error err = image.SavePng(path);
        GD.Print(err == Error.Ok ? $"[CAPTURE] saved {path}" : $"[CAPTURE] FAILED {path}: {err}");
    }
}
