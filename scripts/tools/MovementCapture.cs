using Godot;

namespace PathToExile.Tools;

/// <summary>
/// Drives scripted movement sequences in a real (windowed) run and saves screenshots to
/// docs/devlog/screenshots/. Not a test — this exists to produce devlog imagery that
/// actually shows the mechanics, since a static shot of the placeholder art conveys
/// nothing about movement. Run with:
///   ./scripts/capture_devlog_screenshot.sh
/// Must run windowed: --headless uses the dummy renderer and captures an empty image.
///
/// Produces two images:
///   movement-trail.png  — the two input modes side by side (WASD vs pathfound).
///   movement-cancel.png — WASD interrupting a path, and the destination being dropped.
/// </summary>
public partial class MovementCapture : Node2D
{
    private static readonly Vector2 StartPos = new(200f, 550f);
    private static readonly Vector2 ClickTarget = new(850f, 160f);

    private Player _player = null!;
    private NavigationAgent2D _agent = null!;
    private Line2D _wasdTrail = null!;
    private Line2D _pathTrail = null!;
    private Label _wasdLabel = null!;
    private Label _pathLabel = null!;

    private int _phase = 1;
    private int _frame;
    private bool _busy;

    public override void _Ready()
    {
        _player = GetNode<Player>("Player");
        _agent = _player.GetNode<NavigationAgent2D>("NavigationAgent2D");

        // The player carries its own follow-camera; disable it so the shot frames the
        // whole play area instead of tracking the character.
        _player.GetNode<Camera2D>("Camera2D").Enabled = false;

        _wasdTrail = GetNode<Line2D>("WasdTrail");
        _pathTrail = GetNode<Line2D>("PathTrail");
        _wasdLabel = GetNode<Label>("WasdLabel");
        _pathLabel = GetNode<Label>("PathLabel");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_busy)
        {
            return;
        }

        _frame++;

        if (_phase == 1)
        {
            RunTwoModesSequence();
        }
        else
        {
            RunCancelSequence();
        }
    }

    /// <summary>Phase 1: a WASD segment, then a right-click destination walked via the navmesh.</summary>
    private void RunTwoModesSequence()
    {
        if (_frame == 5)
        {
            Input.ActionPress("move_right");
        }

        if (_frame > 5 && _frame <= 65)
        {
            _wasdTrail.AddPoint(_player.GlobalPosition);
        }

        if (_frame == 65)
        {
            Input.ActionRelease("move_right");
        }

        if (_frame == 70)
        {
            _pathTrail.AddPoint(_player.GlobalPosition);
            _agent.TargetPosition = ClickTarget;
        }

        if (_frame > 70)
        {
            _pathTrail.AddPoint(_player.GlobalPosition);

            if (_agent.IsNavigationFinished() || _frame > 500)
            {
                Capture("res://docs/devlog/screenshots/movement-trail.png", StartPhase2);
            }
        }
    }

    /// <summary>
    /// Phase 2: start walking to a destination, interrupt with WASD, then release
    /// everything. The point of the image is that nothing happens after the release —
    /// the destination was cancelled, not paused.
    /// </summary>
    private void RunCancelSequence()
    {
        // Walk toward the destination for a bit.
        if (_frame == 5)
        {
            _pathTrail.AddPoint(_player.GlobalPosition);
            _agent.TargetPosition = ClickTarget;
        }

        if (_frame > 5 && _frame <= 90)
        {
            _pathTrail.AddPoint(_player.GlobalPosition);
        }

        // Interrupt with WASD — this cancels the destination outright.
        if (_frame == 90)
        {
            _wasdTrail.AddPoint(_player.GlobalPosition);
            Input.ActionPress("move_up");
        }

        if (_frame > 90 && _frame <= 125)
        {
            _wasdTrail.AddPoint(_player.GlobalPosition);
        }

        if (_frame == 125)
        {
            Input.ActionRelease("move_up");
        }

        // No input at all from here. The player must stay put.
        if (_frame > 190)
        {
            GD.Print($"[CAPTURE] Phase 2 final pos={_player.GlobalPosition} (destination {ClickTarget} abandoned)");
            Capture("res://docs/devlog/screenshots/movement-cancel.png", () => GetTree().Quit());
        }
    }

    private void StartPhase2()
    {
        _phase = 2;
        _frame = 0;

        _player.GlobalPosition = StartPos;
        _player.Velocity = Vector2.Zero;
        _wasdTrail.ClearPoints();
        _pathTrail.ClearPoints();

        _pathLabel.Position = new Vector2(150f, 570f);
        _pathLabel.Text = "1. walking to right-click destination";
        _wasdLabel.Position = new Vector2(375f, 400f);
        _wasdLabel.Text = "2. WASD interrupts";

        AddAbandonedTargetMarker();
        _busy = false;
    }

    private void AddAbandonedTargetMarker()
    {
        var marker = new Label
        {
            Position = ClickTarget + new Vector2(-70f, 20f),
            Text = "3. destination dropped\n   (not resumed)",
        };
        marker.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.7f));
        AddChild(marker);

        var cross = new Line2D
        {
            Width = 3f,
            DefaultColor = new Color(0.5f, 0.5f, 0.55f),
            Points = new[]
            {
                ClickTarget + new Vector2(-10f, -10f),
                ClickTarget + new Vector2(10f, 10f),
            },
        };
        AddChild(cross);
    }

    private async void Capture(string resPath, System.Action onDone)
    {
        _busy = true;

        // Let a frame finish drawing, otherwise the viewport texture is empty.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        Image image = GetViewport().GetTexture().GetImage();
        string absolutePath = ProjectSettings.GlobalizePath(resPath);
        Error err = image.SavePng(absolutePath);
        GD.Print(err == Error.Ok
            ? $"[CAPTURE] Saved {absolutePath}"
            : $"[CAPTURE] FAILED to save {absolutePath}: {err}");

        // _busy stays set — the callback clears it if there's more to do, so a quit
        // doesn't get a stray extra frame that captures the same image twice.
        onDone();
    }
}
