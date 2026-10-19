#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Asked each time the menu opens: the leave button's label right now
    /// (the Rift's "Abandon run" becomes "Leave with reward" once the Colossus is down).</summary>
    public Func<string>? LeaveTextNow { get; set; }

    /// <summary>One extra entry above "Send feedback" (the hideout's "Invite friends", "Edit hideout").</summary>
    public sealed class Entry
    {
        public string Text { get; init; } = "";
        public Action? OnPress { get; init; }

        /// <summary>Asked each time the menu opens: the label now, or null to hide the entry.</summary>
        public Func<string?>? LiveText { get; init; }
    }

    /// <summary>Extra entries, in order (set before adding).</summary>
    public List<Entry> Extras { get; } = new();

    /// <summary>Compatibility: a single extra entry (added first to <see cref="Extras"/>).</summary>
    public string? ExtraText { get; set; }

    public Action? OnExtra { get; set; }

    /// <summary>The extra entries' labels as last shown, hidden ones left out (tests).</summary>
    public IEnumerable<string> ExtraTexts => _extras.Where(e => e.Button.Visible).Select(e => e.Button.Text);

    /// <summary>Presses the extra entry showing <paramref name="text"/> (tests).</summary>
    public bool PressExtra(string text)
    {
        foreach ((Entry _, Button button) in _extras)
        {
            if (button.Visible && button.Text == text)
            {
                button.EmitSignal(BaseButton.SignalName.Pressed);
                return true;
            }
        }

        return false;
    }

    private readonly List<(Entry Entry, Button Button)> _extras = new();

    public bool IsOpen { get; private set; }

    /// <summary>The leave button's label (tests, captures).</summary>
    public string LeaveButtonText => _leave.Text;

    /// <summary>Presses the leave button as a click would (tests).</summary>
    public void PressLeave() => _leave.EmitSignal(BaseButton.SignalName.Pressed);

    /// <summary>Co-op: the menu opens over a game that keeps running
    /// (docs/design/multiplayer.md -> "Pausing").</summary>
    public bool NoPause { get; set; }

    private Control _root = null!;
    private VBoxContainer _menu = null!;
    private Label _summary = null!;
    private Control _center = null!;
    private ulong _settingsClosedFrame = ulong.MaxValue;
    private Button _editHud = null!;
    private Button _resume = null!;
    private Button _settings = null!;
    private Button _leave = null!;

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
        _resume = UiTheme.Button("Resume", Close);
        _menu.AddChild(_resume);
        _settings = UiTheme.Button("Settings", OpenSettings);
        _menu.AddChild(_settings);
        _editHud = UiTheme.Button("Edit HUD", OpenHudEditor);
        _menu.AddChild(_editHud);
        if (ExtraText is not null)
        {
            Extras.Insert(0, new Entry { Text = ExtraText, OnPress = OnExtra });
        }

        foreach (Entry entry in Extras)
        {
            Entry e = entry;
            Button b = UiTheme.Button(e.Text, () => { Close(); e.OnPress?.Invoke(); });
            _menu.AddChild(b);
            _extras.Add((e, b));
        }

        _menu.AddChild(UiTheme.Button("Send feedback", () => OpenFeedback()));
        _leave = UiTheme.Button(LeaveText, () => { Close(); OnLeave?.Invoke(); });
        _menu.AddChild(_leave);
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
        if (PanelStack.ClosedThisFrame || PanelStack.HasOpen || CashOutPanel.Blocks)
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

        if (!GetTree().Paused && !(NoPause && GetTree().Root.GetNodeOrNull<LevelUpScreen>("Run/LevelUp") is { IsOpen: true }))
        {
            Open();
        }
    }

    public void Open()
    {
        IsOpen = true;
        GetTree().Paused = !NoPause;
        if (LeaveTextNow is not null)
        {
            _leave.Text = LeaveTextNow();
        }

        foreach ((Entry entry, Button button) in _extras)
        {
            string? text = entry.LiveText is null ? entry.Text : entry.LiveText();
            button.Visible = text is not null;
            button.Text = text ?? entry.Text;
        }

        _summary.Text = Summary?.Invoke() ?? "";
        _summary.Visible = _summary.Text.Length > 0;
        _editHud.Visible = GetTree().GetNodesInGroup(HudLayout.Group).Count > 0;
        _menu.Visible = true;
        _root.Visible = true;
        _resume.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close()
    {
        IsOpen = false;
        _root.Visible = false;
        if (!NoPause)
        {
            GetTree().Paused = false;
        }
    }

    /// <summary>HUD edit mode (docs/design/menus.md -> "HUD layout editor"):
    /// the pause menu steps aside while the panels are dragged around.</summary>
    private void OpenHudEditor()
    {
        _root.Visible = false;
        var editor = new HudEditor();
        AddSibling(editor);
        editor.Closed += () =>
        {
            // The Esc that closed the editor must not also close the pause menu.
            _settingsClosedFrame = Engine.GetProcessFrames();
            _root.Visible = true;
            _editHud.CallDeferred(Control.MethodName.GrabFocus);
        };
    }

    /// <summary>Bug reports, ideas and feature requests → GitHub (docs/design/menus.md -> "Feedback").</summary>
    public FeedbackPanel OpenFeedback()
    {
        _menu.GetParent<Control>().Visible = false;
        FeedbackPanel feedback = FeedbackPanel.Create(_center);
        feedback.Closed += () =>
        {
            _settingsClosedFrame = Engine.GetProcessFrames();
            _menu.GetParent<Control>().Visible = true;
        };
        return feedback;
    }

    private void OpenSettings()
    {
        _menu.GetParent<Control>().Visible = false;
        SettingsPanel settings = SettingsPanel.Create(_center);
        settings.Closed += () =>
        {
            _settingsClosedFrame = Engine.GetProcessFrames();
            _menu.GetParent<Control>().Visible = true;
            _settings.CallDeferred(Control.MethodName.GrabFocus);
        };
    }
}
