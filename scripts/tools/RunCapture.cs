#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Enemies;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Run;
using ArpgGame.UI;
using ArpgGame.World;

namespace ArpgGame.Tools;

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
        _ = _only is "endgame" or "panels" ? EndgameScript() : _only == "voxel" ? VoxelScript() : _only == "tutorial" ? TutorialScript() : _only == "facing" ? FacingScript() : _only == "hud" ? HudScript() : _only == "crafting" ? CraftingScript() : _only == "town" ? TownScript() : _only == "update" ? UpdateScript() : Script();
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

        await Shot(ArpgGame.Core.Scenes.Hideout, "hideout.png", 1.2f);

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
        profile.Hideout = new World.HideoutData();
        profile.Currency.Clear();
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
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.MainMenu);
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

        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Hideout);
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

    /// <summary>The merchant, the Artificer's bench and the tooltip with and
    /// without Alt (docs/design/crafting.md).</summary>
    private async Task CraftingScript()
    {
        SetupDemoProfile();
        if (ArpgGame.Core.Profile.Get(this) is { } profile)
        {
            CharacterData k = profile.Active;
            k.HighestTierCleared = 9;
            profile.Hideout.GuideSeen = true;
            k.Gold = 2400;
            foreach (Items.CraftingCurrency c in Items.Crafting.All)
            {
                profile.Currency[c.Id] = c.IsDust ? 12 : 3 + (int)(c.DropWeight / 4f);
            }

            var rng = new RandomNumberGenerator { Seed = 99 };
            k.Inventory.Clear();
            Items.BaseItemDefinition[] picks = Items.ItemFactory.Bases.Take(5).ToArray();
            for (int i = 0; i < picks.Length; i++)
            {
                Items.Rarity r = i == 0 ? Items.Rarity.Rare : i % 2 == 0 ? Items.Rarity.Magic : Items.Rarity.Common;
                k.Inventory.Add((Items.ItemFactory.Roll(picks[i], r, 38, rng), new Vector2I(i * 2, 0)));
            }
        }

        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Hideout);
        await Wait(1.0f);
        Node hideout = GetTree().CurrentScene;
        var player = hideout.GetNode<Player>("Player");
        player.GlobalPosition = World.HideoutTown.World(new Vector2I(25, 19));
        await Wait(0.8f);
        await Snap("hideout-stations.png");

        // Loot on the ground: a few items and orbs in the plaza.
        var lootRng = new RandomNumberGenerator { Seed = 5 };
        Vector2 lootAt = player.GlobalPosition + new Vector2(0f, 60f);
        int n = 0;
        foreach (Items.BaseItemDefinition b in Items.ItemFactory.Bases.Take(6))
        {
            Items.EquipmentItem drop = Items.ItemFactory.NewBlank();
            hideout.AddChild(drop);
            drop.GlobalPosition = lootAt + new Vector2(-150f + n * 60f, (n % 2) * 26f);
            drop.Initialize(b, (Items.Rarity)(n % 3), 30, Items.ItemFactory.Affixes.Entries, lootRng);
            n++;
        }

        foreach ((string id, int i) in new[] { ("tempest_orb", 0), ("kindling_shard", 1), ("crown_orb", 2) })
        {
            Pickup orb = Pickup.Spawn(hideout, PickupKind.Currency, 1f, lootAt + new Vector2(-60f + i * 60f, 80f));
            orb.CurrencyKey = id;
            orb.QueueRedraw();
        }

        await Wait(0.6f);
        await Snap("items-ground.png");
        var inventoryPanel = player.GetNode<InventoryUI>("InventoryUI");
        inventoryPanel.SetOpen(true);
        await Wait(0.3f);
        await Snap("items-inventory.png");
        inventoryPanel.SetOpen(false);

        var merchant = hideout.GetNode<MerchantPanel>("MerchantPanel");
        merchant.Open();
        await Wait(0.3f);
        await HoverFirstItem(merchant, player);
        await Snap("panel-merchant.png");
        merchant.Close();

        var inventoryUi = player.GetNode<InventoryUI>("InventoryUI");
        inventoryUi.SetOpen(true);
        inventoryUi.ShowCrafting(true);
        await Wait(0.3f);
        await HoverFirstItem(inventoryUi, player);
        await Snap("craft-tooltip.png");
        ItemTooltipControl.ForceDetail = true;
        await Wait(0.2f);
        await Snap("craft-tooltip-alt.png");
        ItemTooltipControl.ForceDetail = false;
        inventoryUi.Board.Arm(Items.Crafting.Orbs.First(o => o.Action == Items.CraftAction.Tempest));
        await Wait(0.2f);
        await Snap("craft-orbs.png");
        inventoryUi.Board.ArmSeal();
        await Wait(0.2f);
        await Snap("craft-seal.png");
        inventoryUi.ShowCrafting(false);
        inventoryUi.SetOpen(false);

        var bench = hideout.GetNode<ArtificerPanel>("ArtificerPanel");
        bench.Open();
        bench.Show(ArtificerPanel.Tab.Disenchant);
        await Wait(0.2f);
        await HoverFirstItem(bench, player);
        await Snap("craft-disenchant.png");
        bench.Show(ArtificerPanel.Tab.Forge);
        await Wait(0.3f);
        await Snap("craft-forge.png");
        bench.Close();

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }

    /// <summary>The hideout town: views of its quarters, an overview, Ysolde's
    /// shop and the three edit-mode tools (docs/design/hideout.md).</summary>
    private async Task TownScript()
    {
        SetupDemoProfile();
        if (ArpgGame.Core.Profile.Get(this) is { } profile)
        {
            profile.Active.Gold = 3000;
        }

        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Hideout);
        await Wait(1.2f);
        var hideout = (Hideout)GetTree().CurrentScene;
        Player player = hideout.Player;
        player.Health!.IgnoreDamage = true;
        // First visit: the guide opens by itself.
        await Snap("hideout-guide.png");
        hideout.GetNode<TownGuidePanel>("TownGuide").Close();
        player.GlobalPosition = hideout.Data.Find("mirror")!.Position + new Vector2(0f, 46f);
        await Wait(0.6f);
        await Snap("hideout-hud.png");
        hideout.Hud.ShowBanner("Hideout", "Your town between realities — ? opens the guide");
        player.GlobalPosition = hideout.Data.Find("forge")!.Position + new Vector2(30f, 50f);
        await Wait(0.6f);
        await Snap("hideout-prompt-forge.png");
        foreach ((string file, Vector2I cell) in new[] { ("town-plaza.png", new Vector2I(25, 20)), ("town-smithy.png", new Vector2I(11, 16)), ("town-south.png", new Vector2I(25, 28)), ("town-gate.png", new Vector2I(25, 7)), ("town-yard.png", new Vector2I(40, 16)) })
        {
            player.GlobalPosition = World.HideoutTown.World(cell);
            await Wait(0.6f);
            await Snap(file);
        }

        var camera = player.GetNode<Camera2D>("Camera2D");
        player.GlobalPosition = World.HideoutTown.World(new Vector2I(25, 17));
        camera.Zoom = Vector2.One * 0.62f;
        await Wait(0.8f);
        await Snap("town-overview.png");
        camera.Zoom = Vector2.One * Player.CloseZoom;

        var shop = hideout.GetNode<DecorShopPanel>("DecorShopPanel");
        shop.Open();
        await Wait(0.3f);
        shop.BuyFloor(World.HideoutCatalog.Floor("carpet"));
        shop.BuyFloor(World.HideoutCatalog.Floor("grass"));
        shop.BuyDecor(World.HideoutCatalog.Prop("statue")!);
        shop.BuyDecor(World.HideoutCatalog.Prop("tree")!);
        await Wait(0.2f);
        await Snap("town-shop.png");
        shop.Close();

        player.GlobalPosition = World.HideoutTown.World(new Vector2I(25, 20));
        await Wait(0.4f);
        hideout.OpenEditor();
        await Wait(0.3f);
        HideoutEditor editor = hideout.Editor!;
        Vector2 stall = hideout.Data.Find("merchant")!.Position;
        GetViewport().WarpMouse(hideout.GetGlobalTransformWithCanvas() * (stall + new Vector2(0f, -24f)));
        await Wait(0.3f);
        await Snap("town-edit-move.png");
        editor.Use(HideoutEditor.Tool.Floors);
        editor.SelectFloor("carpet");
        for (int x = 23; x <= 27; x++)
        {
            for (int y = 21; y <= 22; y++)
            {
                hideout.Town.Paint(new Vector2I(x, y), "carpet");
            }
        }

        editor.SelectFloor("carpet");
        await Wait(0.8f);
        await Snap("town-edit-floors.png");
        editor.Use(HideoutEditor.Tool.Decorations);
        editor.SelectDecor("statue");
        hideout.PlaceFromStock("tree", World.HideoutTown.World(new Vector2I(21, 22)));
        await Wait(0.4f);
        await Snap("town-edit-decor.png");
        editor.Close();
        await Wait(0.3f);

        // Esc menu → Send feedback.
        if (hideout.GetChildren().OfType<PauseMenu>().FirstOrDefault() is { } pause)
        {
            pause.Open();
            FeedbackPanel feedback = pause.OpenFeedback();
            await Wait(0.2f);
            feedback.OpenUrl = _ => { };
            feedback.Fill("The statue blocks the bridge", "I placed a hero statue on the south bridge and now I can't walk to the temple.", "1. H to edit the town\n2. Place a statue on a bridge");
            await Wait(0.3f);
            await Snap("feedback-panel.png");
            feedback.Close();
            pause.Close();
        }

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }

    /// <summary>
    /// The 2026-10-14 update: the Rift's life-only HUD and danger gauge, the two
    /// power-ups, the endless phase and its reward, Arehucaz and his panel, the
    /// Mirror's jewel tree, and the liquid shader in the town canal and a reality.
    /// </summary>
    private async Task UpdateScript()
    {
        SetupDemoProfile();
        if (ArpgGame.Core.Profile.Get(this) is { } profile)
        {
            profile.Active.Gold = 4200;
            profile.Hideout.GuideSeen = true;
            profile.Jewels = new World.JewelData { Found = 26 };
            foreach (string id in new[] { "b0s1", "b0s2", "b0s3", "b0a1", "b0a2", "b0m", "bridge0", "b1s2", "b1s1", "b1s3", "b1s4",
                         "b2s1", "b2s2", "b2s3", "b2s4", "b2s5", "b2s6", "b2n", "b4s1", "b4s2", "b4s3", "b5s1", "b5s2", "b3s1" })
            {
                World.JewelTree.Socket(profile.Jewels, id);
            }
        }

        // The Rift: mid-run with a few skills, so the timers and the danger gauge read.
        RunLaunchOptions.Reset();
        RunLaunchOptions.StartTime = 200f;
        RunLaunchOptions.StartSkill = "spark";
        RunLaunchOptions.AutoLevels = 14;
        await Launch();
        Run()!.Player.Health!.IgnoreDamage = true;
        await Wait(8f);
        await Snap("rift-hud.png");

        RunController run = Run()!;
        Vector2 at = run.Player.GlobalPosition;
        // Out of the (levelled-up) pickup radius so they stay on the floor for the shot.
        float radius = run.Player.Stats.Get(Stats.StatId.PickupRadius).Value + 40f;
        Pickup.Spawn(run, PickupKind.Nuke, 1f, at + new Vector2(-radius, -20f));
        Pickup.Spawn(run, PickupKind.Magnet, 1f, at + new Vector2(radius, -20f));
        Pickup orb = Pickup.Spawn(run, PickupKind.Currency, 1f, at + new Vector2(0f, -radius));
        orb.CurrencyKey = "tempest_orb";
        await Wait(0.3f);
        await Snap("rift-powerups.png");
        run.Player.GlobalPosition = at + new Vector2(-radius, -20f);
        await Wait(0.12f);
        await Snap("rift-cataclysm.png");

        // The Colossus falls: the portal opens and the waves come back.
        RunLaunchOptions.Reset();
        RunLaunchOptions.StartTime = 418f;
        RunLaunchOptions.StartSkill = "meteor";
        RunLaunchOptions.AutoLevels = 20;
        await Launch();
        run = Run()!;
        run.Player.Health!.IgnoreDamage = true;
        await Wait(4f);
        if (run.Director.ActiveBoss is { } colossus)
        {
            colossus.Health.ApplyResolvedDamage(float.MaxValue / 4f, run.Player);
        }

        // Stand still: the capture's steering would walk into the portal.
        bool steer = _steer;
        _steer = false;
        await Wait(9f);
        if (run.Director.ExitPortal is { } portal)
        {
            run.Player.GlobalPosition = portal.GlobalPosition + new Vector2(0f, 120f);
        }

        await Wait(2f);
        await Snap("rift-endless.png");
        _steer = steer;
        run.State.SkipTo(16f * 60f + 12f);
        run.Director.TakeExitPortal();
        await Wait(1.5f);
        await Snap("rift-results.png");
        GetTree().Paused = false;

        // Arehucaz in the north alley, and his cloth of wares.
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Hideout);
        await Wait(1.2f);
        var hideout = (Hideout)GetTree().CurrentScene;
        Player player = hideout.Player;
        player.Health!.IgnoreDamage = true;
        player.GlobalPosition = hideout.Data.Find("arehucaz")!.Position + new Vector2(-40f, 70f);
        await Wait(0.8f);
        await Snap("town-arehucaz.png");
        var dealer = hideout.GetNode<ShardVendorPanel>("ShardVendorPanel");
        dealer.Open();
        dealer.Buy(4);
        dealer.Buy(3);
        await Wait(0.3f);
        await Snap("arehucaz-panel.png");
        dealer.Close();

        var mirror = hideout.GetNode<MirrorPanel>("MirrorPanel");
        mirror.Open();
        mirror.ShowTab(true);
        await Wait(0.3f);
        GetViewport().WarpMouse(mirror.JewelView.GetGlobalTransformWithCanvas() * mirror.JewelView.ScreenOf("b2n"));
        await Wait(0.4f);
        await Snap("mirror-jewels.png");
        mirror.Close();

        // The canal, now real water.
        player.GlobalPosition = World.HideoutTown.World(new Vector2I(10, 23));
        await Wait(1.2f);
        await Snap("town-canal.png");

        // Liquid in the realities: lava, ooze and water.
        foreach ((string layout, string name) in new[] { ("ashen_halls", "lava"), ("bone_crypt", "ooze"), ("drowned_mirror", "water") })
        {
            RealityLaunch.Tier = 14;
            RealityLaunch.LayoutId = layout;
            GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Reality);
            await Wait(0.8f);
            if (GetTree().CurrentScene is not RealityController reality)
            {
                continue;
            }

            reality.Player.Health!.IgnoreDamage = true;
            // Stand on the floor beside the first pool of liquid.
            Vector2I? liquid = null;
            for (int y = 0; y < reality.Raster.Height && liquid is null; y++)
            {
                for (int x = 0; x < reality.Raster.Width && liquid is null; x++)
                {
                    if (reality.Raster.Cells[x, y] != CellKind.Hazard)
                    {
                        continue;
                    }

                    foreach (Vector2I d in new[] { new Vector2I(0, 3), new Vector2I(0, -3), new Vector2I(3, 0), new Vector2I(-3, 0), new Vector2I(0, 2), new Vector2I(2, 0) })
                    {
                        if (reality.Raster.Walkable(new Vector2I(x, y) + d))
                        {
                            liquid = new Vector2I(x, y) + d;
                            break;
                        }
                    }
                }
            }

            GD.Print($"[CAPTURE] {name}: liquid near {liquid}");
            if (liquid is { } cell)
            {
                reality.Player.GlobalPosition = RasterLayout.World(cell);
            }

            await Wait(1.5f);
            await Snap($"reality-liquid-{name}.png");
        }

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }

    /// <summary>Puts the mouse over the first (top-left) item in a panel's
    /// inventory grid so its tooltip shows.</summary>
    private async Task HoverFirstItem(Node panel, Player player)
    {
        if (FindChild<InventoryGridControl>(panel) is not { } grid || player.Inventory.Placements.Count == 0)
        {
            return;
        }

        Vector2I cell = player.Inventory.Placements.Values.OrderBy(c => c.Y).ThenBy(c => c.X).First();
        Vector2 at = grid.GetGlobalTransformWithCanvas() * ((new Vector2(cell.X, cell.Y) + new Vector2(0.5f, 0.5f)) * InventoryGridControl.CellPx);
        GetViewport().WarpMouse(at);
        await Wait(0.25f);
    }

    /// <summary>The Tab overlay map and the HUD editor, in a reality.</summary>
    private async Task HudScript()
    {
        SetupDemoProfile();
        RealityLaunch.Tier = 5;
        RealityLaunch.LayoutId = "ashen_halls";
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Reality);
        await Wait(1.2f);
        if (GetTree().CurrentScene is not RealityController reality)
        {
            GetTree().Quit();
            return;
        }

        reality.Player.Health!.IgnoreDamage = true;
        reality.Player.GetNode<PlayerSkillInput>("PlayerSkillInput").ForceAutoCast = true;
        // Explore: walk the room graph so the map has something to show.
        foreach (PackSpot spot in reality.Raster.Packs.Take(6))
        {
            reality.Player.GlobalPosition = RasterLayout.World(spot.Cell);
            await Wait(0.5f);
        }

        reality.Player.GlobalPosition = RasterLayout.World(reality.Raster.Packs[2].Cell);
        await Wait(1f);
        // The bars over the character, with some life missing so they read.
        if (reality.Player.Stats.Life is { } life)
        {
            life.Current = life.Max * 0.55f;
        }

        await Wait(0.4f);
        await Snap("overhead-bars.png");

        var map = reality.GetNode("Hud").FindChild("MapOverlay", true, false) as MapOverlay;
        map?.Toggle();
        await Wait(0.5f);
        await Snap("map-overlay.png");
        map?.Toggle();

        // The editor, as the pause menu opens it, with the life cluster dragged up-left.
        GetTree().Paused = true;
        var editor = new HudEditor();
        GetTree().CurrentScene.AddChild(editor);
        await Wait(0.3f);
        GetTree().GetNodesInGroup(HudLayout.Group).OfType<HudMovable>().FirstOrDefault(m => m.Id == "skills")?.MoveBy(new Vector2(380f, 0f), GetViewport().GetVisibleRect().Size);
        GetTree().GetNodesInGroup(HudLayout.Group).OfType<HudMovable>().FirstOrDefault(m => m.Id == "vitals")?.SetScale(0.8f);
        GetTree().GetNodesInGroup(HudLayout.Group).OfType<HudMovable>().FirstOrDefault(m => m.Id == "reality_info")?.SetScale(1.25f);
        await Wait(0.3f);
        await Snap("hud-editor.png");
        HudLayout.Reset();
        GetTree().Quit();
    }

    /// <summary>The four facings: the player walks up, left, down and right in the hideout.</summary>
    private async Task FacingScript()
    {
        SetupDemoProfile();
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Hideout);
        await Wait(1.2f);
        var player = GetTree().CurrentScene.GetNode<Player>("Player");
        player.GlobalPosition = new Vector2(640f, 420f);
        foreach ((string action, string name) in new[] { ("move_up", "up"), ("move_left", "left"), ("move_down", "down"), ("move_right", "right") })
        {
            Input.ActionPress(action);
            await Wait(0.45f);
            await Snap($"facing-{name}.png");
            Input.ActionRelease(action);
            await Wait(0.2f);
        }

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }

    /// <summary>The introduction reality: its start, the first fight and the guardian.</summary>
    private async Task TutorialScript()
    {
        SetupDemoProfile();
        RealityLaunch.Tutorial = true;
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Reality);
        await Wait(1.5f);
        if (GetTree().CurrentScene is not RealityController reality)
        {
            GetTree().Quit();
            return;
        }

        reality.Player.Health!.IgnoreDamage = true;
        await Wait(1.5f);
        await Snap("tutorial-start.png");
        reality.Player.GlobalPosition += new Vector2(520f, 0f);
        await Wait(0.6f);
        reality.Player.Dodge!.EmitSignal(Dodge.SignalName.Dodged);
        reality.Player.GlobalPosition = RasterLayout.World(reality.Raster.Packs.First(p => p.Room == "yard").Cell) + new Vector2(-260f, 0f);
        reality.Player.GetNode<PlayerSkillInput>("PlayerSkillInput").ForceAutoCast = true;
        await Wait(2.5f);
        await Snap("tutorial-fight.png");
        await Wait(4f);
        await Snap("tutorial-loot.png");
        reality.Player.GlobalPosition = RasterLayout.World(reality.Raster.BossCell) + new Vector2(0f, 220f);
        await Wait(4f);
        await Snap("tutorial-guardian.png");
        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }

    /// <summary>Quick look at the voxel view: hideout and one reality per theme.</summary>
    private async Task VoxelScript()
    {
        SetupDemoProfile();
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Hideout);
        await Wait(1.2f);
        Node hideout = GetTree().CurrentScene;
        hideout.GetNode<Player>("Player").GlobalPosition = World.HideoutTown.World(new Vector2I(25, 19));
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

    /// <summary>Puts the player on the walkable cell nearest to them whose
    /// neighbour in <paramref name="toward"/> is wall, and takes a shot.</summary>
    private async Task SnapNextToWall(RealityController reality, Vector2I toward, string file)
    {
        FlowField field = reality.Field;
        Vector2I from = field.CellOf(reality.Player.GlobalPosition);
        Vector2I? best = null;
        int bestDistance = int.MaxValue;
        for (int x = 0; x < field.Width; x++)
        {
            for (int y = 0; y < field.Height; y++)
            {
                var c = new Vector2I(x, y);
                bool open = field.IsWalkable(c) && field.IsWalkable(c - toward) && field.IsWalkable(c + new Vector2I(toward.Y, toward.X)) && field.IsWalkable(c - new Vector2I(toward.Y, toward.X));
                int d = Mathf.Abs(x - from.X) + Mathf.Abs(y - from.Y);
                if (open && !field.IsWalkable(c + toward) && !field.IsWalkable(c + toward * 2) && d < bestDistance)
                {
                    best = c;
                    bestDistance = d;
                }
            }
        }

        if (best is { } cell)
        {
            reality.Player.GlobalPosition = field.CenterOf(cell);
            await Wait(0.8f);
            await Snap(file);
        }
    }

    private async Task CaptureReality(string layout, int tier, string name)
    {
        RealityLaunch.Tier = tier;
        RealityLaunch.LayoutId = layout;
        RealityLaunch.Modifiers = new List<string> { "elite", "hordes" };
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Reality);
        await Wait(0.5f);
        if (GetTree().CurrentScene is not RealityController reality)
        {
            return;
        }

        reality.Player.Health!.IgnoreDamage = true;
        reality.Player.GetNode<PlayerSkillInput>("PlayerSkillInput").ForceAutoCast = true;
        await Wait(1.0f);
        await Snap($"reality-{name}-start.png");

        if (_only == "voxel")
        {
            // Wall dithering: standing beside a wall must leave it solid; standing
            // just north of one (it hides the player) must dither it.
            await SnapNextToWall(reality, new Vector2I(1, 0), $"voxel-wall-side-{name}.png");
            await SnapNextToWall(reality, new Vector2I(0, 1), $"voxel-wall-front-{name}.png");
        }

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

    private static string Scenes(bool main) => ArpgGame.Core.Scenes.MainMenu;

    private async Task Shot(string scene, string file, float wait)
    {
        GetTree().ChangeSceneToFile(scene);
        await Wait(wait);
        await Snap(file);
    }

    private async Task Launch()
    {
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Run);
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
