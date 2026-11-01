#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.UI;
using ArpgGame.World;

namespace ArpgGame.Tools;

/// <summary>`--only=playground` for <see cref="RunCapture"/> (docs/design/playground.md): Escaldón's
/// Buy skills tab in the hideout, then the hidden playground — the camp with its two NPCs and the
/// spawner, a tier-12 horde, a boss, and Escaldón's panel inside it.</summary>
public partial class RunCapture
{
    private async Task PlaygroundScript()
    {
        SetupDemoProfile();
        Profile profile = Profile.Get(this)!;
        bool late = OS.GetCmdlineUserArgs().Contains("--late");
        CharacterData k = profile.CreateCharacter("Kaela", late ? "arcanist" : "warrior");
        k.Level = late ? 70 : 30;
        if (late)
        {
            k.Ascendancy = "elementalist";
            k.AscendancyPoints = 6;
            k.AddGem("spark", false, 12);
            k.AddGem("immolation", false, 5);
            k.Loadout[1].SkillUid = k.Gems.First(g => g.Id == "spark").Uid;
            k.Loadout[4].SkillUid = k.Gems.First(g => g.Id == "immolation").Uid;
        }

        k.Gold = 12_000;
        k.TutorialDone = true;
        k.GemShopSeenLevel = 8;
        profile.Select(profile.Characters.IndexOf(k));
        profile.Hideout.GuideSeen = true;

        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Hideout);
        await Wait(1.6f);
        if (GetTree().CurrentScene is not Hideout hideout)
        {
            return;
        }

        hideout.Escaldon.Open();
        hideout.Escaldon.ShowTab(EscaldonTab.BuySkills);
        await Wait(0.4f);
        await Snap("escaldon-buy-skills.png");
        hideout.Escaldon.Close();
        await Wait(0.2f);

        hideout.EnterPlayground();
        await Wait(3.5f);
        if (GetTree().CurrentScene is not RealityController ground || ground.GetNodeOrNull<Playground>("Playground") is not { } pg)
        {
            return;
        }

        await Snap("playground-camp.png");
        ground.Player.GlobalPosition = RasterLayout.World(ground.Raster.BossCell) + new Vector2(-260f, 0f);
        await Wait(0.6f);
        ground.SetPlaygroundTier(12);
        ground.SpawnPlaygroundHorde(24, null, null, ground.Player.GlobalPosition + new Vector2(240f, 0f));
        await Wait(1.2f);
        await Snap("playground-horde.png");
        pg.ClearMonsters();
        await Wait(0.3f);
        ground.SpawnPlaygroundBoss(RealityController.AllBosses.Last(), ground.Player.GlobalPosition + new Vector2(260f, 0f));
        await Wait(1.5f);
        await Snap("playground-boss.png");
        pg.ClearMonsters();
        await Wait(0.2f);
        // The DPS meter against a training dummy: a fire build with ignite stacks.
        Combat.TargetDummy dummy = pg.SpawnDummy();
        ground.Player.GlobalPosition = dummy.GlobalPosition + new Vector2(-140f, 0f);
        var fireball = new Skills.SkillInstance(Progression.UpgradeCatalog.FindSkill("fireball")!.Definition) { Level = 20 };
        fireball.TreeModifiers.AddRange(Skills.SkillTrees.ModifiersFor("fireball", new System.Collections.Generic.Dictionary<string, int> { ["power"] = 3, ["blast"] = 3, ["firestorm"] = 1, ["kindle"] = 3, ["searing"] = 3 }));
        ground.Player.SkillUser.EquipSkill(Skills.SkillSlotId.Slot1, fireball);
        for (int i = 0; i < 14; i++)
        {
            if (ground.Player.Stats.Mana is { } mana)
            {
                mana.Current = mana.Max;
            }

            ground.Player.SkillUser.TryCast(Skills.SkillSlotId.Slot1, Vector2.Right, dummy.GlobalPosition);
            await Wait(0.45f);
        }

        await Snap("playground-dps.png");
        pg.ClearMonsters();
        ground.GetNode<SkillTreePanel>("SkillTreePanel").Open();
        await Wait(0.4f);
        await Snap("playground-escaldon.png");
        GetTree().Quit();
    }
}
