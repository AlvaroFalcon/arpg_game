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
        var panel = new SettingsPanel { Theme = UiTheme.Theme, CustomMinimumSize = new Vector2(600f, 0f) };
        parent.AddChild(panel);
        return panel;
    }

    public override void _Ready()
    {
        GameSettings? settings = GameSettings.Get(this);
        VBoxContainer root = UiTheme.VBox(this, 10);
        root.AddChild(UiTheme.Label("Settings", 34, UiTheme.Gold, HorizontalAlignment.Center));

        // Three tabs, each scrolling on its own: the panel never grows past the
        // screen (it used to, and the Gameplay section and Back fell off the bottom).
        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0f, 420f) };
        tabs.AddThemeFontSizeOverride("font_size", 18);
        root.AddChild(tabs);

        VBoxContainer audio = Tab(tabs, "Audio");
        AddSlider(audio, "Master volume", settings?.MasterVolume ?? 0.8f, v => settings?.SetMasterVolume(v));
        AddSlider(audio, "Music volume", settings?.MusicVolume ?? 0.7f, v => settings?.SetMusicVolume(v));
        AddSlider(audio, "Effects volume", settings?.SfxVolume ?? 0.8f, v => settings?.SetSfxVolume(v));

        VBoxContainer display = Tab(tabs, "Display");
        OptionButton? size = null;
        AddOptions(display, "Window mode", new[] { "Windowed", "Borderless", "Fullscreen" }, (int)(settings?.WindowMode ?? WindowModeKind.Borderless), i =>
        {
            settings?.SetWindowMode((WindowModeKind)i);
            if (size is not null)
            {
                size.Disabled = i != (int)WindowModeKind.Windowed;
            }
        });
        Vector2I current = settings?.WindowSize ?? new Vector2I(1600, 900);
        size = AddOptions(display, "Window size", Array.ConvertAll(GameSettings.WindowSizes, v => $"{v.X} × {v.Y}"),
            Math.Max(0, Array.IndexOf(GameSettings.WindowSizes, current)), i => settings?.SetWindowSize(GameSettings.WindowSizes[i]));
        size.Disabled = settings?.WindowMode != WindowModeKind.Windowed;
        AddToggle(display, "V-Sync", settings?.VSync ?? true, v => settings?.SetVSync(v));
        AddToggle(display, "Voxel graphics (HD-2D) — applies on the next scene", settings?.VoxelGraphics ?? true, v => settings?.SetVoxelGraphics(v));
        AddToggle(display, "Show FPS (F3)", settings?.ShowFps ?? false, v => settings?.SetShowFps(v));

        VBoxContainer gameplay = Tab(tabs, "Gameplay");
        AddToggle(gameplay, "Life and mana bars over your character", settings?.OverheadBars ?? true, v => settings?.SetOverheadBars(v));
        AddToggle(gameplay, "Screen shake", settings?.ScreenShake ?? true, v => settings?.SetScreenShake(v));
        AddToggle(gameplay, "Damage numbers", settings?.DamageNumbers ?? true, v => settings?.SetDamageNumbers(v));
        AddToggle(gameplay, "Admin mode (testing: max-level test characters)", settings?.AdminMode ?? false, v => settings?.SetAdminMode(v));
        Label controls = UiTheme.Label(
            "WASD move · Mouse aim · LMB/RMB/1-4 skills · Space dodge · Shift sprint · Q/R flasks\nI inventory · C character · K skills · P passive tree · Esc menu",
            15, UiTheme.TextDim, HorizontalAlignment.Center);
        controls.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        gameplay.AddChild(controls);

        Button back = UiTheme.Button("Back", Close);
        back.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        root.AddChild(back);
        back.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>A tab page that scrolls vertically if its rows don't fit.</summary>
    private static VBoxContainer Tab(TabContainer tabs, string title)
    {
        var scroll = new ScrollContainer { Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        tabs.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 10);
        var pad = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (string side in new[] { "left", "right", "top", "bottom" })
        {
            pad.AddThemeConstantOverride($"margin_{side}", 12);
        }

        scroll.AddChild(pad);
        pad.AddChild(box);
        return box;
    }

    private static OptionButton AddOptions(VBoxContainer box, string text, string[] items, int selected, Action<int> onChanged)
    {
        HBoxContainer row = UiTheme.HBox(box);
        Label label = UiTheme.Label(text, 18);
        label.CustomMinimumSize = new Vector2(200f, 0f);
        row.AddChild(label);
        var options = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        options.AddThemeFontSizeOverride("font_size", 18);
        foreach (string item in items)
        {
            options.AddItem(item);
        }

        options.Selected = Mathf.Clamp(selected, 0, items.Length - 1);
        options.ItemSelected += i => onChanged((int)i);
        row.AddChild(options);
        return options;
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
