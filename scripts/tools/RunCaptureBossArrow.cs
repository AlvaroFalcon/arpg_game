#nullable enable
using System.Threading.Tasks;
using Godot;
using ArpgGame.World;

namespace ArpgGame.Tools;

/// <summary>`--only=bossarrow` for <see cref="RunCapture"/> (docs/design/bosses.md ->
/// "Off-screen indicator"): a reality's boss fight with the boss beyond the right edge,
/// then beyond the top-left corner, then back on screen (no arrow).</summary>
public partial class RunCapture
{
    private async Task BossArrowScript()
    {
        SetupDemoProfile();
        RealityLaunch.Tier = 5;
        RealityLaunch.LayoutId = "ashen_halls";
        GetTree().ChangeSceneToFile(ArpgGame.Core.Scenes.Reality);
        await Wait(1.4f);
        if (GetTree().CurrentScene is RealityController reality && reality.Boss is { } boss)
        {
            reality.Player.Health!.IgnoreDamage = true;
            reality.Player.GlobalPosition = RasterLayout.World(reality.Raster.BossCell) + new Vector2(0f, 120f);
            await Wait(1.2f);
            Vector2 view = GetViewport().GetVisibleRect().Size;
            Vector2 Centre() => GetViewport().CanvasTransform.AffineInverse() * (view / 2f);

            foreach ((Vector2 offset, string file) in new[]
            {
                (new Vector2(1100f, -120f), "boss-arrow-right.png"),
                (new Vector2(-900f, -650f), "boss-arrow-top-left.png"),
                (new Vector2(160f, -60f), "boss-arrow-on-screen.png"),
            })
            {
                for (int i = 0; i < 4; i++)
                {
                    boss.GlobalPosition = Centre() + offset;
                    await Wait(0.05f);
                }

                await Snap(file);
            }
        }

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }
}
