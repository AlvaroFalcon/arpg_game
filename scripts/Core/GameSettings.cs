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
    public bool Fullscreen { get; private set; }
    public bool VSync { get; private set; } = true;
    public bool ScreenShake { get; private set; } = true;
    public bool DamageNumbers { get; private set; } = true;

    /// <summary>HD-2D voxel view (docs/design/art-pipeline.md -> "Voxels"); off = classic 2D sprites.</summary>
    public bool VoxelGraphics { get; private set; } = true;

    /// <summary>Small life/mana bars over the player (docs/design/menus.md -> "Overhead bars").</summary>
    public bool OverheadBars { get; private set; } = true;

    /// <summary>Skills fire on their own while off cooldown (survivor style).
    /// Off: hold left mouse (all skills) or 1-4 (one skill).</summary>
    public bool AutoCast { get; private set; } = true;

    /// <summary>Aim at the nearest enemy instead of the mouse cursor.</summary>
    public bool AutoAim { get; private set; }

    /// <summary>Frame-rate readout in the corner (<see cref="UI.FpsOverlay"/>); F3 toggles it.</summary>
    public bool ShowFps { get; private set; }

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
    public void SetFullscreen(bool v) { Fullscreen = v; ApplyDisplay(); NotifyChanged(); }
    public void SetVSync(bool v) { VSync = v; ApplyDisplay(); NotifyChanged(); }
    public void SetScreenShake(bool v) { ScreenShake = v; NotifyChanged(); }
    public void SetDamageNumbers(bool v) { DamageNumbers = v; NotifyChanged(); }
    public void SetVoxelGraphics(bool v) { VoxelGraphics = v; NotifyChanged(); }

    public void SetOverheadBars(bool v) { OverheadBars = v; NotifyChanged(); }
    public void SetAutoCast(bool v) { AutoCast = v; NotifyChanged(); }
    public void SetAutoAim(bool v) { AutoAim = v; NotifyChanged(); }
    public void SetShowFps(bool v) { ShowFps = v; ApplyFps(); NotifyChanged(); }

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

        DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
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
        Fullscreen = (bool)config.GetValue("display", "fullscreen", Fullscreen);
        VSync = (bool)config.GetValue("display", "vsync", VSync);
        ScreenShake = (bool)config.GetValue("gameplay", "screen_shake", ScreenShake);
        DamageNumbers = (bool)config.GetValue("gameplay", "damage_numbers", DamageNumbers);
        VoxelGraphics = (bool)config.GetValue("display", "voxel_graphics", VoxelGraphics);
        OverheadBars = (bool)config.GetValue("display", "overhead_bars", OverheadBars);
        AutoCast = (bool)config.GetValue("gameplay", "auto_cast", AutoCast);
        AutoAim = (bool)config.GetValue("gameplay", "auto_aim", AutoAim);
        ShowFps = (bool)config.GetValue("display", "show_fps", ShowFps);
    }

    public void Save()
    {
        var config = new ConfigFile();
        config.SetValue("audio", "master", MasterVolume);
        config.SetValue("audio", "music", MusicVolume);
        config.SetValue("audio", "sfx", SfxVolume);
        config.SetValue("display", "fullscreen", Fullscreen);
        config.SetValue("display", "vsync", VSync);
        config.SetValue("gameplay", "screen_shake", ScreenShake);
        config.SetValue("gameplay", "damage_numbers", DamageNumbers);
        config.SetValue("display", "voxel_graphics", VoxelGraphics);
        config.SetValue("display", "overhead_bars", OverheadBars);
        config.SetValue("gameplay", "auto_cast", AutoCast);
        config.SetValue("gameplay", "auto_aim", AutoAim);
        config.SetValue("display", "show_fps", ShowFps);
        config.Save(SavePath);
    }
}
