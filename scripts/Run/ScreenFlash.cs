#nullable enable
using Godot;

namespace ArpgGame.Run;

/// <summary>A full-screen colour flash that fades out and frees itself (power-ups).</summary>
public partial class ScreenFlash : CanvasLayer
{
    public static ScreenFlash? Spawn(Node context, Color color, float strength = 0.45f, float seconds = 0.5f)
    {
        if (!context.IsInsideTree())
        {
            return null;
        }

        var flash = new ScreenFlash { Layer = 9 };
        var rect = new ColorRect { Color = color with { A = strength }, MouseFilter = Control.MouseFilterEnum.Ignore };
        flash.AddChild(rect);
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        context.GetTree().CurrentScene.AddChild(flash);
        Tween tween = flash.CreateTween();
        tween.TweenProperty(rect, "color:a", 0f, seconds);
        tween.TweenCallback(Callable.From(flash.QueueFree));
        return flash;
    }
}
