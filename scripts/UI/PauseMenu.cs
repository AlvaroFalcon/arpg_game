#nullable enable
using System;
using Godot;

namespace ArpgGame.UI;

/// <summary>
/// Escape pauses — docs/design/menus.md -> "Pause". Works in the run and the
/// hideout. Escape first closes an open panel (the inventory), per the
/// existing <see cref="PanelStack"/> rule; only with nothing to close does it
/// pause. It never opens over another screen that already owns the pause
/// (level-up, results).
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    /// <summary>Optional extra text (the run's build summary).</summary>
    public Func<string>? Summary { get; set; }

    /// <summary>Label + action for the "leave" button ("Abandon run", "Main menu").</summary>
    public string LeaveText { get; set; } = "Main menu";

    public Action? OnLeave { get; set; }

    public bool IsOpen { get; private set; }

    private Control _root = null!;
    private VBoxContainer _menu = null!;
    private Label _summary = null!;
    private Control _center = null!;
    private ulong _settingsClosedFrame = ulong.MaxValue;

    public override void _Ready()
    {
        Layer = 30;
        ProcessMode = ProcessModeEnum.Always;
        _root = UiTheme.FullRect(this);
        UiTheme.Dim(_root, 0.6f);
        _center = UiTheme.Center(_root);
        var panel = new PanelContainer();
        _center.AddChild(panel);
        _menu = UiTheme.VBox(panel, 12);
        _menu.AddChild(UiTheme.Label("Paused", 40, UiTheme.Gold, HorizontalAlignment.Center));
        _summary = UiTheme.Label("", 15, UiTheme.TextDim, HorizontalAlignment.Center);
        _summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _summary.CustomMinimumSize = new Vector2(420f, 0f);
        _menu.AddChild(_summary);
        _menu.AddChild(UiTheme.Button("Resume", Close));
        _menu.AddChild(UiTheme.Button("Settings", OpenSettings));
        _menu.AddChild(UiTheme.Button(LeaveText, () => { Close(); OnLeave?.Invoke(); }));
        _menu.AddChild(UiTheme.Button("Quit to desktop", () => GetTree().Quit()));
        _root.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!Input.IsActionJustPressed("ui_cancel"))
        {
            return;
        }

        // The inventory (or any PanelStack panel) gets Escape first, in either
        // processing order: if it already closed something this frame, or still
        // has something open, this press is not ours.
        if (PanelStack.ClosedThisFrame || PanelStack.HasOpen)
        {
            return;
        }

        if (IsOpen)
        {
            // Escape inside the settings panel closes the panel (it handles
            // that itself), not the whole pause menu.
            if (_menu.IsVisibleInTree() && _settingsClosedFrame != Engine.GetProcessFrames())
            {
                Close();
            }

            return;
        }

        if (!GetTree().Paused)
        {
            Open();
        }
    }

    public void Open()
    {
        IsOpen = true;
        GetTree().Paused = true;
        _summary.Text = Summary?.Invoke() ?? "";
        _summary.Visible = _summary.Text.Length > 0;
        _menu.Visible = true;
        _root.Visible = true;
        ((Button)_menu.GetChild(2)).CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close()
    {
        IsOpen = false;
        _root.Visible = false;
        GetTree().Paused = false;
    }

    private void OpenSettings()
    {
        _menu.GetParent<Control>().Visible = false;
        SettingsPanel settings = SettingsPanel.Create(_center);
        settings.Closed += () =>
        {
            _settingsClosedFrame = Engine.GetProcessFrames();
            _menu.GetParent<Control>().Visible = true;
            ((Button)_menu.GetChild(3)).CallDeferred(Control.MethodName.GrabFocus);
        };
    }
}
