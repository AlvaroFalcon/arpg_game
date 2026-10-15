#nullable enable
using System;
using Godot;
using ArpgGame.Core;

namespace ArpgGame.UI;

/// <summary>
/// Audio / display / gameplay settings — docs/design/menus.md -> "Settings".
/// One panel used by both the main menu and the pause menu. Every control
/// writes straight through to <see cref="GameSettings"/> (changes apply
/// live); the file is saved when the panel closes.
/// </summary>
public partial class SettingsPanel : PanelContainer
{
    public event Action? Closed;

    public static SettingsPanel Create(Control parent)
    {
        var panel = new SettingsPanel { Theme = UiTheme.Theme, CustomMinimumSize = new Vector2(560f, 0f) };
        parent.AddChild(panel);
        return panel;
    }

    public override void _Ready()
    {
        GameSettings? settings = GameSettings.Get(this);
        VBoxContainer box = UiTheme.VBox(this, 12);
        box.AddChild(UiTheme.Label("Settings", 34, UiTheme.Gold, HorizontalAlignment.Center));

        box.AddChild(UiTheme.Label("Audio", 22, UiTheme.Ember));
        AddSlider(box, "Master volume", settings?.MasterVolume ?? 0.8f, v => settings?.SetMasterVolume(v));
        AddSlider(box, "Music volume", settings?.MusicVolume ?? 0.7f, v => settings?.SetMusicVolume(v));
        AddSlider(box, "Effects volume", settings?.SfxVolume ?? 0.8f, v => settings?.SetSfxVolume(v));

        box.AddChild(UiTheme.Label("Display", 22, UiTheme.Ember));
        AddToggle(box, "Fullscreen", settings?.Fullscreen ?? false, v => settings?.SetFullscreen(v));
        AddToggle(box, "V-Sync", settings?.VSync ?? true, v => settings?.SetVSync(v));
        AddToggle(box, "Voxel graphics (HD-2D) — applies on the next scene", settings?.VoxelGraphics ?? true, v => settings?.SetVoxelGraphics(v));
        AddToggle(box, "Life and mana bars over your character", settings?.OverheadBars ?? true, v => settings?.SetOverheadBars(v));
        AddToggle(box, "Screen shake", settings?.ScreenShake ?? true, v => settings?.SetScreenShake(v));
        AddToggle(box, "Damage numbers", settings?.DamageNumbers ?? true, v => settings?.SetDamageNumbers(v));
        AddToggle(box, "Show FPS (F3)", settings?.ShowFps ?? false, v => settings?.SetShowFps(v));

        box.AddChild(UiTheme.Label("Gameplay", 22, UiTheme.Ember));

        box.AddChild(UiTheme.Label(
            "WASD move · Mouse aim · Space dodge · I inventory · Esc pause",
            15, UiTheme.TextDim, HorizontalAlignment.Center));

        Button back = UiTheme.Button("Back", Close);
        back.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        box.AddChild(back);
        back.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close()
    {
        GameSettings.Get(this)?.Save();
        Closed?.Invoke();
        QueueFree();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    private static void AddSlider(VBoxContainer box, string text, float value, Action<float> onChanged)
    {
        HBoxContainer row = UiTheme.HBox(box);
        Label label = UiTheme.Label(text, 18);
        label.CustomMinimumSize = new Vector2(200f, 0f);
        row.AddChild(label);
        var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = value, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(240f, 24f) };
        Label pct = UiTheme.Label($"{Mathf.RoundToInt(value * 100)}%", 16, UiTheme.TextDim);
        pct.CustomMinimumSize = new Vector2(52f, 0f);
        slider.ValueChanged += v =>
        {
            onChanged((float)v);
            pct.Text = $"{Mathf.RoundToInt((float)v * 100)}%";
        };
        row.AddChild(slider);
        row.AddChild(pct);
    }

    private static void AddToggle(VBoxContainer box, string text, bool value, Action<bool> onChanged)
    {
        var check = new CheckBox { Text = text, ButtonPressed = value };
        check.AddThemeFontSizeOverride("font_size", 18);
        check.Toggled += v => onChanged(v);
        box.AddChild(check);
    }
}
