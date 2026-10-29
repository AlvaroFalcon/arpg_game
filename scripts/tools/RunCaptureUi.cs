#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.UI;
using ArpgGame.World;

namespace ArpgGame.Tools;

/// <summary>`--only=ui` for <see cref="RunCapture"/> (docs/design/menus.md -> "Title screen",
/// "HUD: the mirror"): the title and its menu, the reality HUD with the mirror, the skill
/// bar, the loot feed and the top-right corner, the Tab map, the hideout and the Rift.</summary>
public partial class RunCapture
{
    private async Task UiScript()
    {
        SetupDemoProfile();
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.MainMenu);
        await Wait(1.2f);
        await Snap("ui-title.png");
        if (GetTree().CurrentScene is MainMenu menu)
        {
            menu.SkipTitle();
            await Wait(0.6f);
            await Snap("ui-menu-start.png");
            if (menu.FindChild("Settings", true, false) is Control settings)
            {
                settings.GrabFocus();
                await Wait(0.5f);
                await Snap("ui-menu-settings.png");
            }
        }

        // A reality: the mirror half-drained, a Q skill, loot coming in.
        if (Profile.Get(this)?.Active is { } k)
        {
            k.Loadout[2].SkillUid = k.Gems.First(g => g.Id == "sunder").Uid;
            k.Experience = 0f;
        }

        RealityLaunch.Tier = 5;
        RealityLaunch.LayoutId = "ashen_halls";
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Reality);
        await Wait(1.4f);
        if (GetTree().CurrentScene is RealityController reality)
        {
            reality.Player.Health!.IgnoreDamage = true;
            foreach (PackSpot spot in reality.Raster.Packs.Take(4))
            {
                reality.Player.GlobalPosition = RasterLayout.World(spot.Cell);
                await Wait(0.3f);
            }

            reality.Player.GlobalPosition = RasterLayout.World(reality.Raster.Entrance);
            await Wait(0.6f);
            if (reality.Player.Stats.Life is { } life)
            {
                life.Current = life.Max * 0.62f;
            }

            if (reality.Player.Stats.Mana is { } mana)
            {
                mana.Current = mana.Max * 0.38f;
            }

            if (CharacterBuild.Of(reality.Player) is { } build)
            {
                build.Data.Experience = CharacterData.ExperienceToNext(build.Data.Level) * 0.64f;
            }

            LootFeed.Gold(reality, 137);
            await Wait(0.25f);
            LootFeed.Currency(reality, "ash_orb", 1);
            await Wait(0.25f);
            LootFeed.Currency(reality, "kindling_shard", 2);
            await Wait(0.5f);
            await Snap("ui-hud-reality.png");

            var map = reality.GetNode("Hud").FindChild("MapOverlay", true, false) as MapOverlay;
            map?.Toggle();
            await Wait(0.5f);
            await Snap("ui-map-open.png");
            map?.Toggle();
        }

        if (Profile.Get(this) is { } seen)
        {
            seen.Hideout.GuideSeen = true;
        }

        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Hideout);
        await Wait(1.6f);
        await Snap("ui-hud-hideout.png");

        await Launch();
        await Wait(2.5f);
        await Snap("ui-hud-rift.png");

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }
}
