#nullable enable
using Godot;

namespace ArpgGame.Core;

/// <summary>
/// Player-facing settings — docs/design/menus.md -> "Settings". An autoload
/// (scenes/core/GameSettings.tscn) persisted to <c>user://settings.cfg</c> via
/// <see cref="ConfigFile"/>. Every setter applies immediately; <see cref="Save"/>
/// is called by the settings panel when it closes, not on every slider tick.
///
/// Code that needs a setting goes through the static helpers
/// (<see cref="Get"/>, <see cref="ShakeEnabled"/>) which fall back to defaults
/// when the autoload is absent — headless test scenes run without autoloads
/// being meaningful, and must not crash or silently change behaviour.
/// </summary>
[GlobalClass]
public partial class GameSettings : Node
{
    public const string SavePath = "user://settings.cfg";
    public const string MusicBus = "Music";
    public const string SfxBus = "SFX";

    [Signal]
    public delegate void ChangedEventHandler();

    public float MasterVolume { get; private set; } = 0.8f;
    public float MusicVolume { get; private set; } = 0.7f;
    public float SfxVolume { get; private set; } = 0.8f;
    /// <summary>docs/design/menus.md -> "Settings": windowed, borderless (a window
    /// covering the screen — the default, quick to alt-tab) or exclusive fullscreen.</summary>
    public WindowModeKind WindowMode { get; private set; } = WindowModeKind.Borderless;

    /// <summary>Window size in windowed mode (the game renders at any size: the
    /// 1280×720 layout scales up to fill it).</summary>
    public Vector2I WindowSize { get; private set; } = new(1600, 900);

    /// <summary>16:9 sizes, then ultrawide 21:9 (2560×1080, 3440×1440, 3840×1600) and 32:9 (5120×1440).</summary>
    public static readonly Vector2I[] WindowSizes =
    {
        new(1280, 720), new(1600, 900), new(1920, 1080), new(2560, 1440),
        new(2560, 1080), new(3440, 1440), new(3840, 1600), new(5120, 1440),
    };

    /// <summary>Wide screens (21:9, 32:9) show more of the world to the sides by
    /// default; off keeps the classic 16:9 view with bars at the sides.</summary>
    public bool FillWideScreens { get; private set; } = true;

    public bool Fullscreen => WindowMode != WindowModeKind.Windowed;
    public bool VSync { get; private set; } = true;
    public bool ScreenShake { get; private set; } = true;

    /// <summary>Damage numbers (docs/design/menus.md -> "Damage numbers"): Full (columns
    /// with tags, skill icons and the big-hit flash), Compact (numbers only, shorter
    /// columns) or Off.</summary>
    public DamageNumberMode DamageNumberMode { get; private set; } = DamageNumberMode.Full;

    public bool DamageNumbers => DamageNumberMode != DamageNumberMode.Off;

    /// <summary>Small life/mana bars over the player (docs/design/menus.md -> "Overhead bars").</summary>
    public bool OverheadBars { get; private set; } = true;

    /// <summary>Skills fire on their own while off cooldown (survivor style).
    /// Off: hold left mouse (all skills) or 1-4 (one skill).</summary>
    public bool AutoCast { get; private set; } = true;

    /// <summary>Aim at the nearest enemy instead of the mouse cursor.</summary>
    public bool AutoAim { get; private set; }

    /// <summary>Frame-rate readout in the corner (<see cref="UI.FpsOverlay"/>); F3 toggles it.</summary>
    public bool ShowFps { get; private set; }

    /// <summary>Testing tools (docs/design/menus.md -> "Admin mode"): a "test character" button on character creation.</summary>
    public bool AdminMode { get; private set; }

    private UI.FpsOverlay? _fps;

    public static GameSettings? Get(Node context) =>
        context.IsInsideTree() ? context.GetTree().Root.GetNodeOrNull<GameSettings>("GameSettings") : null;

    public static bool ShakeEnabled(Node context) => Get(context)?.ScreenShake ?? true;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        EnsureBus(MusicBus);
        EnsureBus(SfxBus);
        Load();
        ApplyAll();
        AddChild(new UI.GameCursor { Name = "GameCursor" });
    }

    private static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0)
        {
            return;
        }

        AudioServer.AddBus();
        int index = AudioServer.BusCount - 1;
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, "Master");
    }

    public void SetMasterVolume(float v) { MasterVolume = Mathf.Clamp(v, 0f, 1f); ApplyAudio(); NotifyChanged(); }
    public void SetMusicVolume(float v) { MusicVolume = Mathf.Clamp(v, 0f, 1f); ApplyAudio(); NotifyChanged(); }
    public void SetSfxVolume(float v) { SfxVolume = Mathf.Clamp(v, 0f, 1f); ApplyAudio(); NotifyChanged(); }
    public void SetWindowMode(WindowModeKind v) { WindowMode = v; ApplyDisplay(); NotifyChanged(); }
    public void SetWindowSize(Vector2I v) { WindowSize = v; ApplyDisplay(); NotifyChanged(); }
    public void SetFillWideScreens(bool v) { FillWideScreens = v; ApplyDisplay(); NotifyChanged(); }
    public void SetVSync(bool v) { VSync = v; ApplyDisplay(); NotifyChanged(); }
    public void SetScreenShake(bool v) { ScreenShake = v; NotifyChanged(); }
    public void SetDamageNumbers(bool v) => SetDamageNumberMode(v ? DamageNumberMode.Full : DamageNumberMode.Off);
    public void SetDamageNumberMode(DamageNumberMode v) { DamageNumberMode = v; NotifyChanged(); }

    public void SetOverheadBars(bool v) { OverheadBars = v; NotifyChanged(); }
    public void SetAutoCast(bool v) { AutoCast = v; NotifyChanged(); }
    public void SetAutoAim(bool v) { AutoAim = v; NotifyChanged(); }
    public void SetShowFps(bool v) { ShowFps = v; ApplyFps(); NotifyChanged(); }
    public void SetAdminMode(bool v) { AdminMode = v; NotifyChanged(); }

    private void ApplyFps()
    {
        if (ShowFps && _fps is null)
        {
            _fps = new UI.FpsOverlay { Name = "FpsOverlay" };
            AddChild(_fps);
        }
        else if (!ShowFps && _fps is not null)
        {
            _fps.QueueFree();
            _fps = null;
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
        {
            SetShowFps(!ShowFps);
            Save();
            GetViewport().SetInputAsHandled();
        }
    }

    private void NotifyChanged() => EmitSignal(SignalName.Changed);

    public void ApplyAll()
    {
        ApplyAudio();
        ApplyDisplay();
        ApplyFps();
    }

    private void ApplyAudio()
    {
        SetBus("Master", MasterVolume);
        SetBus(MusicBus, MusicVolume);
        SetBus(SfxBus, SfxVolume);
    }

    private static void SetBus(string name, float linear)
    {
        int index = AudioServer.GetBusIndex(name);
        if (index < 0)
        {
            return;
        }

        AudioServer.SetBusMute(index, linear <= 0.001f);
        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(linear, 0.001f)));
    }

    private void ApplyDisplay()
    {
        // Headless runs have no real window; changing modes there only spams errors.
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        // The layout is 1280×720: "expand" widens the view on screens wider than
        // 16:9 (anchored HUD pieces follow their corners); "keep" letterboxes.
        if (GetTree()?.Root is { } root)
        {
            root.ContentScaleAspect = FillWideScreens ? Window.ContentScaleAspectEnum.Expand : Window.ContentScaleAspectEnum.Keep;
        }

        switch (WindowMode)
        {
            case WindowModeKind.Fullscreen:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
                break;
            case WindowModeKind.Borderless:
                // Godot's non-exclusive fullscreen is a borderless window covering the screen.
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                break;
            default:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
                Vector2I screen = DisplayServer.ScreenGetUsableRect().Size;
                Vector2I size = new(Mathf.Min(WindowSize.X, screen.X), Mathf.Min(WindowSize.Y, screen.Y));
                DisplayServer.WindowSetSize(size);
                Vector2I origin = DisplayServer.ScreenGetUsableRect().Position;
                DisplayServer.WindowSetPosition(origin + (screen - size) / 2);
                break;
        }

        DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
    }

    public void Load()
    {
        LegacySave.Migrate();
        var config = new ConfigFile();
        if (config.Load(SavePath) != Error.Ok)
        {
            return;
        }

        MasterVolume = (float)config.GetValue("audio", "master", MasterVolume);
        MusicVolume = (float)config.GetValue("audio", "music", MusicVolume);
        SfxVolume = (float)config.GetValue("audio", "sfx", SfxVolume);
        // Older files only had a fullscreen flag: on = exclusive fullscreen; off was simply
        // the old default, so it becomes the new default (borderless).
        WindowMode = config.HasSectionKey("display", "window_mode")
            ? (WindowModeKind)(int)config.GetValue("display", "window_mode", (int)WindowMode)
            : config.HasSectionKey("display", "fullscreen")
                ? ((bool)config.GetValue("display", "fullscreen", false) ? WindowModeKind.Fullscreen : WindowModeKind.Borderless)
                : WindowMode;
        WindowSize = (Vector2I)config.GetValue("display", "window_size", WindowSize);
        FillWideScreens = (bool)config.GetValue("display", "fill_wide_screens", FillWideScreens);
        VSync = (bool)config.GetValue("display", "vsync", VSync);
        ScreenShake = (bool)config.GetValue("gameplay", "screen_shake", ScreenShake);
        // Older files only had an on/off flag: on = Full.
        DamageNumberMode = config.HasSectionKey("gameplay", "damage_number_mode")
            ? (DamageNumberMode)Mathf.Clamp((int)config.GetValue("gameplay", "damage_number_mode", (int)DamageNumberMode), 0, 2)
            : (bool)config.GetValue("gameplay", "damage_numbers", true) ? DamageNumberMode.Full : DamageNumberMode.Off;
        OverheadBars = (bool)config.GetValue("display", "overhead_bars", OverheadBars);
        AutoCast = (bool)config.GetValue("gameplay", "auto_cast", AutoCast);
        AutoAim = (bool)config.GetValue("gameplay", "auto_aim", AutoAim);
        ShowFps = (bool)config.GetValue("display", "show_fps", ShowFps);
        AdminMode = (bool)config.GetValue("gameplay", "admin_mode", AdminMode);
    }

    public void Save()
    {
        var config = new ConfigFile();
        config.SetValue("audio", "master", MasterVolume);
        config.SetValue("audio", "music", MusicVolume);
        config.SetValue("audio", "sfx", SfxVolume);
        config.SetValue("display", "window_mode", (int)WindowMode);
        config.SetValue("display", "window_size", WindowSize);
        config.SetValue("display", "fill_wide_screens", FillWideScreens);
        config.SetValue("display", "vsync", VSync);
        config.SetValue("gameplay", "screen_shake", ScreenShake);
        config.SetValue("gameplay", "damage_numbers", DamageNumbers);
        config.SetValue("gameplay", "damage_number_mode", (int)DamageNumberMode);
        config.SetValue("display", "overhead_bars", OverheadBars);
        config.SetValue("gameplay", "auto_cast", AutoCast);
        config.SetValue("gameplay", "auto_aim", AutoAim);
        config.SetValue("display", "show_fps", ShowFps);
        config.SetValue("gameplay", "admin_mode", AdminMode);
        config.Save(SavePath);
    }
}

public enum DamageNumberMode
{
    Full,
    Compact,
    Off,
}

public enum WindowModeKind
{
    Windowed,
    Borderless,
    Fullscreen,
}
