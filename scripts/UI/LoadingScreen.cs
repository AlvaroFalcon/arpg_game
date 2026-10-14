#nullable enable
using Godot;

namespace ArpgGame.UI;

/// <summary>
/// The loading screen between scenes — docs/design/menus.md -> "Loading screen".
/// Shown before the scene change starts (so the frozen frame of a heavy load — a
/// reality generating its cavern and building the voxel world — is never seen),
/// held until the new scene has run a few frames, then faded out. It lives on the
/// tree root, so it survives the change.
/// </summary>
public partial class LoadingScreen : CanvasLayer
{
    private const float MinimumSeconds = 0.45f;
    private const float FadeSeconds = 0.35f;

    private Control _root = null!;
    private Spinner _spinner = null!;

    public static void Go(SceneTree tree, string path, string title, string subtitle)
    {
        var screen = new LoadingScreen { Name = "LoadingScreen" };
        tree.Root.AddChild(screen);
        screen.Build(title, subtitle);
        screen.Run(path);
    }

    private void Build(string title, string subtitle)
    {
        Layer = 128;
        ProcessMode = ProcessModeEnum.Always;
        _root = UiTheme.FullRect(this);
        _root.MouseFilter = Control.MouseFilterEnum.Stop;
        var back = new ColorRect { Color = new Color(0.025f, 0.02f, 0.04f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(back);
        back.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        CenterContainer center = UiTheme.Center(_root);
        VBoxContainer box = UiTheme.VBox(center, 14);
        _spinner = new Spinner { CustomMinimumSize = new Vector2(72f, 72f), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        box.AddChild(_spinner);
        box.AddChild(UiTheme.Label(title, 34, UiTheme.Gold, HorizontalAlignment.Center));
        box.AddChild(UiTheme.Label(subtitle, 17, UiTheme.TextDim, HorizontalAlignment.Center));
    }

    private async void Run(string path)
    {
        SceneTree tree = GetTree();
        ulong started = Time.GetTicksMsec();
        // Let the screen draw before the (blocking) load starts.
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        tree.ChangeSceneToFile(path);
        // The new scene builds in its _Ready; give it a few frames to settle.
        for (int i = 0; i < 4; i++)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        float waited = (Time.GetTicksMsec() - started) / 1000f;
        if (waited < MinimumSeconds)
        {
            await ToSignal(tree.CreateTimer(MinimumSeconds - waited, processAlways: true, ignoreTimeScale: true), SceneTreeTimer.SignalName.Timeout);
        }

        Tween fade = CreateTween();
        fade.TweenProperty(_root, "modulate:a", 0f, FadeSeconds);
        fade.TweenCallback(Callable.From(QueueFree));
    }

    /// <summary>A ring of eight dots, the bright one walking round.</summary>
    private sealed partial class Spinner : Control
    {
        private float _t;

        public override void _Process(double delta)
        {
            _t += (float)delta;
            QueueRedraw();
        }

        public override void _Draw()
        {
            Vector2 c = Size / 2f;
            float r = Mathf.Min(Size.X, Size.Y) * 0.36f;
            for (int i = 0; i < 8; i++)
            {
                float lag = Mathf.PosMod(_t * 8f - i, 8f) / 8f;
                Vector2 at = c + Vector2.Right.Rotated(Mathf.Tau * i / 8f - Mathf.Pi / 2f) * r;
                DrawCircle(at, 3f + 3f * (1f - lag), UiTheme.Gold with { A = 0.25f + 0.75f * (1f - lag) });
            }
        }
    }
}
