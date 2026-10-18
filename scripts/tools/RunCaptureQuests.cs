#nullable enable
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Quests;
using ArpgGame.UI;
using ArpgGame.World;

namespace ArpgGame.Tools;

/// <summary>
/// `--only=quests`: the quests (docs/design/quests.md). The tier-5 boss falling in a reality
/// (the toast), the log opened there, then the hideout with every kind of marker — in the world,
/// on the directory, the minimap, the big map and the E prompt — the tracker, the dialogue
/// (Odran's long text and the altar's), the altar with its quest strip, the turn-in, the
/// claimed reward, the forge's green strip, and the log (selected, ready, empty).
/// </summary>
public partial class RunCapture
{
    private async Task QuestsScript()
    {
        SetupDemoProfile();
        if (Profile.Get(this) is not { } profile)
        {
            GetTree().Quit();
            return;
        }

        profile.Hideout.GuideSeen = true;
        CharacterData mira = profile.CreateCharacter("Mira", "warrior");
        mira.Level = 20;
        mira.TutorialDone = true;
        mira.HighestTierCleared = 4;
        mira.RealitiesCleared = 6;
        mira.Gold = 1500;
        mira.Quests[QuestCatalog.OdranId] = new QuestProgress { Status = QuestStatus.Done };
        mira.Quests[QuestCatalog.ArehucazId] = new QuestProgress { Status = QuestStatus.Done };
        profile.Quests[QuestCatalog.DisenchantId] = new QuestProgress { Status = QuestStatus.Done };
        profile.Quests[QuestCatalog.ForgeId] = new QuestProgress { Status = QuestStatus.Done };
        profile.Quests[QuestCatalog.JewelId] = new QuestProgress { Status = QuestStatus.Skipped };

        // The tier-5 guardian falls: the quest toast, and the log from inside a reality.
        RealityLaunch.Tier = AscendancyCatalog.Tier1Unlock;
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Reality);
        await Wait(1.6f);
        if (GetTree().CurrentScene is World.RealityController reality)
        {
            reality.Player.Health!.IgnoreDamage = true;
            reality.Boss!.Health.ApplyResolvedDamage(1e9f, reality.Player);
            await Wait(0.9f);
            await Snap("quests-toast-reality.png");
            await Wait(6.5f);
            reality.GetNode<CharacterPanels>("CharacterPanels").Quests.Open();
            await Wait(0.4f);
            await Snap("quests-log-reality.png");
            reality.GetNode<CharacterPanels>("CharacterPanels").Quests.Close();
        }

        // Back home: every kind of marker at once.
        mira.Quests.Remove(QuestCatalog.OdranId); // unlocks on arrival: the toast
        mira.Quests[QuestCatalog.ArehucazId] = new QuestProgress { Status = QuestStatus.Active };
        profile.Quests[QuestCatalog.JewelId] = new QuestProgress { Status = QuestStatus.Ready };
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Hideout);
        await Wait(1.2f);
        var hideout = (Hideout)GetTree().CurrentScene;
        Player player = hideout.Player;
        player.Health!.IgnoreDamage = true;
        await Snap("quests-toast-hideout.png");
        var camera = player.GetNode<Camera2D>("Camera2D");
        player.GlobalPosition = HideoutTown.World(new Vector2I(25, 17));
        camera.Zoom = Vector2.One * 0.62f;
        await Wait(0.9f);
        await Snap("quests-town-overview.png");
        camera.Zoom = Vector2.One * Player.CloseZoom;

        Vector2 altar = hideout.Data.Find("altar")!.Position;
        player.GlobalPosition = altar + new Vector2(110f, 20f);
        await Wait(0.7f);
        await Snap("quests-marker-altar.png");
        player.GlobalPosition = altar + new Vector2(0f, 40f);
        await Wait(0.7f);
        await Snap("quests-prompt-altar.png");

        hideout.Hud.ToggleBigMap();
        await Wait(0.5f);
        await Snap("quests-bigmap.png");
        hideout.Hud.ToggleBigMap();

        // Odran's explanation is the longest; then the altar's.
        player.GlobalPosition = hideout.Data.Find("merchant")!.Position + new Vector2(0f, 46f);
        await Wait(0.4f);
        hideout.Interact("merchant");
        await Wait(0.5f);
        await Snap("quests-dialog-odran.png");
        hideout.QuestDialog.Close();
        await Wait(0.2f);
        player.GlobalPosition = hideout.Data.Find("arehucaz")!.Position + new Vector2(0f, 46f);
        mira.Quests[QuestCatalog.ArehucazId] = new QuestProgress { Status = QuestStatus.New };
        QuestService.Evaluate(hideout);
        hideout.Interact("arehucaz");
        await Wait(0.5f);
        await Snap("quests-dialog-arehucaz.png");
        hideout.QuestDialog.Close();
        mira.Quests[QuestCatalog.ArehucazId] = new QuestProgress { Status = QuestStatus.Active };
        await Wait(0.2f);

        player.GlobalPosition = altar + new Vector2(0f, 40f);
        await Wait(0.4f);
        hideout.Interact("altar");
        await Wait(0.5f);
        await Snap("quests-dialog-altar.png");
        hideout.QuestDialog.PressPrimary();
        await Wait(0.5f);
        await Snap("quests-altar-strip.png");
        var altarPanel = hideout.GetNode<AscendancyPanel>("AscendancyPanel");
        altarPanel.Choose("samurai");
        await Wait(0.2f);
        await Snap("quests-altar-armed.png");
        altarPanel.Choose("samurai");
        await Wait(0.5f);
        await Snap("quests-altar-ready.png");
        altarPanel.Close();
        await Wait(0.6f);
        await Snap("quests-turnin.png");
        hideout.QuestDialog.PressPrimary();
        await Wait(0.7f);
        await Snap("quests-claimed.png");

        // The forge: the objective met inside the panel.
        profile.Quests[QuestCatalog.DisenchantId] = new QuestProgress { Status = QuestStatus.Active };
        player.GlobalPosition = hideout.Data.Find("forge")!.Position + new Vector2(30f, 50f);
        var forge = hideout.GetNode<ArtificerPanel>("ArtificerPanel");
        forge.Open();
        var rng = new RandomNumberGenerator { Seed = 3 };
        var scrap = Items.ItemFactory.RollRandom(20, Items.DropSource.NormalMonster, 1f, rng);
        player.Inventory.TryAdd(scrap);
        await Wait(0.4f);
        await Snap("quests-forge-strip.png");
        forge.Disenchant(scrap);
        await Wait(0.4f);
        await Snap("quests-forge-ready.png");
        forge.Close();
        await Wait(0.6f);
        await Snap("quests-turnin-forge.png");
        hideout.QuestDialog.PressPrimary();
        await Wait(0.5f);

        // The log: a selected quest with the town map, a ready one, and an empty book.
        QuestLogPanel log = hideout.GetNode<CharacterPanels>("CharacterPanels").Quests;
        mira.Quests[QuestCatalog.OdranId] = new QuestProgress { Status = QuestStatus.Done };
        mira.Quests[QuestCatalog.AscendId] = new QuestProgress { Status = QuestStatus.New };
        mira.Ascendancy = null;
        player.GlobalPosition = HideoutTown.World(new Vector2I(25, 20));
        log.OpenOn(QuestCatalog.AscendId);
        await Wait(0.5f);
        await Snap("quests-log.png");
        log.OpenOn(QuestCatalog.JewelId);
        await Wait(0.4f);
        await Snap("quests-log-ready.png");
        log.Close();
        await Wait(0.2f);
        mira.Quests.Clear();
        profile.Quests.Clear();
        mira.TutorialDone = false;
        log.Open();
        await Wait(0.4f);
        await Snap("quests-log-empty.png");
        log.Close();

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }
}
