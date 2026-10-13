#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArpgGame.UI;

/// <summary>
/// HUD edit mode — docs/design/menus.md -> "HUD layout editor". Opened from the
/// pause menu: the game stays paused, every movable HUD panel gets a labelled
/// frame that can be dragged anywhere on screen, and "Done" (or Esc) saves the
/// layout. "Reset layout" puts everything back where it started.
/// </summary>
public partial class HudEditor : CanvasLayer
{
    public event Action? Closed;

    private readonly List<(HudMovable Movable, Panel Frame, Label Name)> _frames = new();
    private HudMovable? _dragging;
    private HudMovable? _resizing;
    private float _resizeStartScale;
    private float _resizeStartDistance;
    private Vector2 _resizeCenter;
    private Control _root = null!;
    private StyleBoxFlat _idle = null!;
    private StyleBoxFlat _hot = null!;

    public override void _Ready()
    {
        Layer = 110;
        ProcessMode = ProcessModeEnum.Always;
        _root = UiTheme.FullRect(this);
        _root.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(new ColorRect { Color = new Color(0.02f, 0.01f, 0.05f, 0.35f), MouseFilter = Control.MouseFilterEnum.Ignore, AnchorRight = 1f, AnchorBottom = 1f });

        _idle = Frame(new Color(1f, 0.82f, 0.45f, 0.10f), new Color(1f, 0.82f, 0.45f, 0.85f));
        _hot = Frame(new Color(1f, 0.82f, 0.45f, 0.22f), new Color(1f, 0.95f, 0.75f, 1f));

        foreach (HudMovable m in GetTree().GetNodesInGroup(HudLayout.Group).OfType<HudMovable>())
        {
            if (!IsInstanceValid(m.Target) || !m.IsSettled)
            {
                continue;
            }

            m.Preview?.Invoke(true);
            var frame = new Panel { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.Move, TooltipText = "" };
            frame.AddThemeStyleboxOverride("panel", _idle);
            Label name = UiTheme.Label(m.Label, 13, new Color(1f, 0.9f, 0.6f));
            name.MouseFilter = Control.MouseFilterEnum.Ignore;
            name.Position = new Vector2(6f, 2f);
            frame.AddChild(name);
            HudMovable movable = m;

            // Resize grip in the bottom-right corner (the mouse wheel works too).
            var grip = new Panel { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.Fdiagsize, CustomMinimumSize = new Vector2(14f, 14f), Size = new Vector2(14f, 14f) };
            var gripStyle = new StyleBoxFlat { BgColor = new Color(1f, 0.85f, 0.5f, 0.95f) };
            gripStyle.SetCornerRadiusAll(2);
            grip.AddThemeStyleboxOverride("panel", gripStyle);
            grip.SetAnchorsPreset(Control.LayoutPreset.BottomRight, true);
            grip.OffsetLeft = -16f;
            grip.OffsetTop = -16f;
            grip.OffsetRight = -2f;
            grip.OffsetBottom = -2f;
            grip.GuiInput += e => OnGripInput(movable, grip, e);
            frame.AddChild(grip);
            frame.GuiInput += e => OnFrameInput(movable, frame, e);
            frame.MouseEntered += () => frame.AddThemeStyleboxOverride("panel", _hot);
            frame.MouseExited += () =>
            {
                if (_dragging != movable)
                {
                    frame.AddThemeStyleboxOverride("panel", _idle);
                }
            };
            _root.AddChild(frame);
            _frames.Add((m, frame, name));
        }

        // The instructions bar.
        var bar = new PanelContainer();
        _root.AddChild(bar);
        // In the middle of the screen, where no HUD panel lives by default.
        bar.SetAnchorsPreset(Control.LayoutPreset.Center, true);
        bar.OffsetLeft = -380f;
        bar.OffsetRight = 380f;
        bar.OffsetTop = -150f;
        bar.OffsetBottom = -90f;
        HBoxContainer row = UiTheme.HBox(bar, 14);
        VBoxContainer text = UiTheme.VBox(row, 0);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        text.AddChild(UiTheme.Label("Edit HUD", 22, UiTheme.Gold));
        text.AddChild(UiTheme.Label("Drag a panel to move it; drag its corner or use the wheel to resize. Esc or Done saves.", 14, UiTheme.TextDim));
        row.AddChild(UiTheme.Button("Reset layout", ResetLayout, 15, 150f));
        Button done = UiTheme.Button("Done", Finish, 15, 110f);
        row.AddChild(done);
        done.CallDeferred(Control.MethodName.GrabFocus);
        PlaceFrames();
    }

    private static StyleBoxFlat Frame(Color fill, Color border)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        box.SetBorderWidthAll(2);
        box.SetCornerRadiusAll(4);
        return box;
    }

    public override void _Process(double delta)
    {
        PlaceFrames();
        if (Input.IsActionJustPressed("ui_cancel"))
        {
            Finish();
        }
    }

    /// <summary>Frames follow their panels (which can change size, e.g. the skill row).</summary>
    private void PlaceFrames()
    {
        foreach ((HudMovable m, Panel frame, Label name) in _frames)
        {
            if (!IsInstanceValid(m) || !IsInstanceValid(m.Target))
            {
                frame.Visible = false;
                continue;
            }

            name.Text = Mathf.Abs(m.Scale - 1f) < 0.01f ? m.Label : $"{m.Label}  ·  {Mathf.RoundToInt(m.Scale * 100f)}%";

            Rect2 r = m.Target.GetGlobalRect();
            if (r.Size.X < 60f || r.Size.Y < 24f)
            {
                // Tiny or empty panels still get a grabbable frame.
                r = new Rect2(r.Position, new Vector2(Mathf.Max(r.Size.X, 160f), Mathf.Max(r.Size.Y, 28f)));
            }

            frame.Position = r.Position - Vector2.One * 4f;
            frame.Size = r.Size + Vector2.One * 8f;
        }
    }

    private void OnFrameInput(HudMovable movable, Panel frame, InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _dragging = button.Pressed ? movable : null;
                frame.AddThemeStyleboxOverride("panel", button.Pressed ? _hot : _idle);
                frame.AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                movable.SetScale(movable.Scale + (wheel.ButtonIndex == MouseButton.WheelUp ? 0.05f : -0.05f));
                PlaceFrames();
                frame.AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging == movable:
                movable.MoveBy(motion.Relative, _root.Size);
                PlaceFrames();
                frame.AcceptEvent();
                break;
        }
    }

    /// <summary>Corner drag: panels scale around their centre, so the size follows the
    /// mouse's distance from the centre and the corner stays under the cursor.</summary>
    private void OnGripInput(HudMovable movable, Panel grip, InputEvent e)
    {
        Vector2 mouse = _root.GetGlobalMousePosition();
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    _resizing = movable;
                    _resizeCenter = movable.Target.GetGlobalRect().GetCenter();
                    _resizeStartScale = movable.Scale;
                    _resizeStartDistance = Mathf.Max(8f, mouse.DistanceTo(_resizeCenter));
                }
                else
                {
                    _resizing = null;
                }

                grip.AcceptEvent();
                break;
            case InputEventMouseMotion when _resizing == movable:
                movable.SetScale(_resizeStartScale * mouse.DistanceTo(_resizeCenter) / _resizeStartDistance);
                PlaceFrames();
                grip.AcceptEvent();
                break;
        }
    }

    private void ResetLayout()
    {
        HudLayout.Reset();
        foreach ((HudMovable m, Panel _, Label _) in _frames)
        {
            if (IsInstanceValid(m))
            {
                m.ResetPosition();
            }
        }

        PlaceFrames();
    }

    private void Finish()
    {
        HudLayout.Save();
        foreach ((HudMovable m, Panel _, Label _) in _frames)
        {
            if (IsInstanceValid(m))
            {
                m.Preview?.Invoke(false);
            }
        }

        Closed?.Invoke();
        QueueFree();
    }
}
