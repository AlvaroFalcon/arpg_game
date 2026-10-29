#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Core;
using ArpgGame.Enemies;
using ArpgGame.World;

namespace ArpgGame.Tools;

/// <summary>`--only=look -- --look=voxel|2d|billboard`: the same Ashen spot with the
/// Arcanist casting, for comparing presentation styles (docs/design/art-pipeline.md).</summary>
public partial class RunCapture
{
    private Vector2 _lookHome;

    private async Task LookScript()
    {
        string look = "voxel";
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--look="))
            {
                look = arg[7..];
            }
        }

        GameSettings.Get(this)?.SetVoxelGraphics(look != "2d");
        View3D.VoxelActor.Billboard = look == "billboard";
        SetupDemoProfile();
        if (Profile.Get(this) is { } profile)
        {
            profile.Select(profile.Characters.FindIndex(c => c.ClassId == "arcanist"));
        }

        ulong seed = 1;
        while (RealityLaunch.LayoutFor(seed).Id != "ashen_depths")
        {
            seed++;
        }

        RealityLaunch.Seed = seed;
        RealityLaunch.Tier = 3;
        RealityLaunch.LayoutId = null;
        RealityLaunch.Modifiers = new List<string>();
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Reality);
        await Wait(0.6f);
        if (GetTree().CurrentScene is not RealityController reality)
        {
            GetTree().Quit();
            return;
        }

        reality.Player.Health!.IgnoreDamage = true;
        await Wait(1.2f);
        _lookHome = reality.Player.GlobalPosition;
        await Snap($"look-{look}-1-start.png");
        await SnapNextToWall(reality, new Vector2I(0, -1), $"look-{look}-2-wall.png");
        // Same spot every time: back at the start with the three nearest monsters brought in.
        Vector2 home = _lookHome;
        reality.Player.GlobalPosition = home;
        int k = 0;
        foreach (Enemy e in reality.GetChildren().OfType<Enemy>().Where(e => !e.IsBoss)
                     .OrderBy(e => e.GlobalPosition.DistanceTo(home)).Take(3))
        {
            e.GlobalPosition = home + new Vector2(230f, -60f + 60f * k++);
        }

        reality.Player.GetNode<PlayerSkillInput>("PlayerSkillInput").ForceAutoCast = true;
        await Wait(1.3f);
        await Snap($"look-{look}-3-fight.png");

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }
}
