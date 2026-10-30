#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Skills;
using ArpgGame.UI;
using ArpgGame.World;

namespace ArpgGame.Tools;

/// <summary>`--only=rework` for <see cref="RunCapture"/> (docs/design/classes.md, skills.md, the
/// rework of 2026-10-30): Escaldón's panel on Fireball and on Corruption, the Witch at the altar,
/// the Thaumaturge at character creation, and Fireball, Spark and the curses in a reality.</summary>
public partial class RunCapture
{
    private async Task ReworkScript()
    {
        SetupDemoProfile();
        Profile profile = Profile.Get(this)!;
        CharacterData v = profile.CreateCharacter("Vesna", "arcanist");
        v.Level = 22;
        v.Gold = 40_000;
        v.TutorialDone = true;
        v.HighestTierCleared = 6;
        v.AscendancyPoints = 4;
        v.Ascendancy = "witch";
        v.AscendancyNodes.UnionWith(new[] { "war_hex", "witch_malediction" });
        GemData fireball = v.Gems.First(g => g.Id == "fireball");
        fireball.Level = 9;
        fireball.Tree = new() { ["power"] = 3, ["blast"] = 2, ["firestorm"] = 1, ["kindle"] = 2 };
        v.AddGem("spark", false, 4);
        v.AddGem("corruption", false, 6);
        v.AddGem("curse_elements", false, 1);
        GemData corruption = v.Gems.First(g => g.Id == "corruption");
        corruption.Tree = new() { ["efficiency"] = 2, ["virulence"] = 2, ["power"] = 1 };
        v.Loadout[1].SkillUid = v.Gems.First(g => g.Id == "spark").Uid;
        v.Loadout[2].SkillUid = corruption.Uid;
        v.Loadout[3].SkillUid = v.Gems.First(g => g.Id == "curse_elements").Uid;
        profile.Select(profile.Characters.IndexOf(v));
        profile.Hideout.GuideSeen = true;

        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Hideout);
        await Wait(1.6f);
        if (GetTree().CurrentScene is Hideout hideout)
        {
            hideout.Escaldon.Open();
            await Wait(0.3f);
            hideout.Escaldon.Select(CharacterBuild.Of(hideout.Player)!.Data.Gems.First(g => g.Id == "fireball"));
            await Wait(0.3f);
            await Snap("rework-escaldon-fireball.png");
            hideout.Escaldon.Select(CharacterBuild.Of(hideout.Player)!.Data.Gems.First(g => g.Id == "corruption"));
            await Wait(0.3f);
            await Snap("rework-escaldon-corruption.png");
            hideout.Escaldon.Close();
            await Wait(0.2f);
            hideout.Player.GlobalPosition = hideout.Data.Find("escaldon")!.Position + new Vector2(0f, 50f);
            await Wait(0.8f);
            await Snap("rework-escaldon-station.png");
            hideout.Interact("altar");
            await Wait(0.5f);
            await Snap("rework-altar-witch.png");
        }

        // The altar's panel paused the tree; a direct scene change doesn't undo that.
        PanelStack.Clear();
        GetTree().Paused = false;

        RealityLaunch.Tier = 4;
        RealityLaunch.LayoutId = "ashen_halls";
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Reality);
        await Wait(4f);
        if (GetTree().CurrentScene is RealityController reality)
        {
            reality.Player.Health!.IgnoreDamage = true;
            reality.Player.GetNode<PlayerSkillInput>("PlayerSkillInput").ForceAutoCast = true;
            reality.Player.GlobalPosition = RasterLayout.World(reality.Raster.Packs[1].Cell);
            await Wait(2.2f);
            await Snap("rework-combat-1.png");
            await Wait(0.9f);
            await Snap("rework-combat-2.png");
        }

        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.MainMenu);
        await Wait(0.8f);
        if (GetTree().CurrentScene is MainMenu menu)
        {
            menu.OpenCharactersForCapture();
            await Wait(0.4f);
            if (FindChild<CharacterSelect>(menu) is { } select)
            {
                select.ShowCreate();
                await Wait(0.3f);
                await Snap("rework-create.png");
            }
        }

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }
}
