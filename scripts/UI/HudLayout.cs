#nullable enable
using System;
using System.Collections.Generic;
using Godot;

namespace ArpgGame.UI;

/// <summary>
/// The player's HUD layout — docs/design/menus.md -> "HUD layout editor". Every
/// movable HUD panel registers here with a stable id; the layout stores, per id,
/// how far the player dragged it from its default spot (in the 1280×720 canvas
/// the UI is authored in; the stretch mode scales that to any window). Ids are
/// shared between screens, so moving the life cluster or the boss bar in a
/// reality moves it in the Survival Rift too.
/// </summary>
public static class HudLayout
{
    public const string Group = "hud_movable";

    /// <summary>Where the layout is saved (tests point this elsewhere).</summary>
    public static string SavePath = "user://hud_layout.cfg";

    public const float MinScale = 0.5f;
    public const float MaxScale = 2f;

    private static Dictionary<string, Vector2>? _offsets;
    private static Dictionary<string, float>? _scales;

    private static Dictionary<string, Vector2> Offsets
    {
        get
        {
            if (_offsets is null)
            {
                (_offsets, _scales) = Load();
            }

            return _offsets;
        }
    }

    private static Dictionary<string, float> Scales
    {
        get
        {
            _ = Offsets;
            return _scales!;
        }
    }

    /// <summary>
    /// Makes <paramref name="target"/> movable in the HUD editor and applies any
    /// saved offset once its layout has settled. <paramref name="preview"/> is
    /// called with true while the editor is open, so panels that are usually
    /// hidden (the boss bar, notifications) can show a placeholder.
    /// </summary>
    public static HudMovable Register(Control target, string id, string label, Action<bool>? preview = null)
    {
        var movable = new HudMovable { Id = id, Label = label, Target = target, Preview = preview, Name = $"HudMovable_{id}" };
        target.AddChild(movable);
        return movable;
    }

    public static Vector2 OffsetOf(string id) => Offsets.TryGetValue(id, out Vector2 o) ? o : Vector2.Zero;

    public static void SetOffset(string id, Vector2 offset)
    {
        if (offset.LengthSquared() < 0.25f)
        {
            Offsets.Remove(id);
        }
        else
        {
            Offsets[id] = offset;
        }
    }

    /// <summary>Size multiplier of a panel (1 = as designed).</summary>
    public static float ScaleOf(string id) => Scales.TryGetValue(id, out float s) ? s : 1f;

    public static void SetScale(string id, float scale)
    {
        scale = Mathf.Clamp(scale, MinScale, MaxScale);
        if (Mathf.Abs(scale - 1f) < 0.01f)
        {
            Scales.Remove(id);
        }
        else
        {
            Scales[id] = scale;
        }
    }

    public static void Reset()
    {
        Offsets.Clear();
        Scales.Clear();
    }

    public static void Save()
    {
        var config = new ConfigFile();
        foreach ((string id, Vector2 offset) in Offsets)
        {
            config.SetValue("offsets", id, offset);
        }

        foreach ((string id, float scale) in Scales)
        {
            config.SetValue("scales", id, scale);
        }

        config.Save(SavePath);
    }

    /// <summary>Forgets the cached layout (tests, or after switching <see cref="SavePath"/>).</summary>
    public static void Reload()
    {
        _offsets = null;
        _scales = null;
    }

    private static (Dictionary<string, Vector2>, Dictionary<string, float>) Load()
    {
        var offsets = new Dictionary<string, Vector2>();
        var scales = new Dictionary<string, float>();
        var config = new ConfigFile();
        if (config.Load(SavePath) != Error.Ok)
        {
            return (offsets, scales);
        }

        if (config.HasSection("offsets"))
        {
            foreach (string id in config.GetSectionKeys("offsets"))
            {
                if (config.GetValue("offsets", id).Obj is Vector2 v)
                {
                    offsets[id] = v;
                }
            }
        }

        if (config.HasSection("scales"))
        {
            foreach (string id in config.GetSectionKeys("scales"))
            {
                scales[id] = Mathf.Clamp((float)config.GetValue("scales", id).AsDouble(), MinScale, MaxScale);
            }
        }

        return (offsets, scales);
    }
}

/// <summary>
/// The bookkeeping node a movable HUD panel carries (see <see cref="HudLayout"/>):
/// its id, its default position (captured once the HUD has laid itself out) and
/// how to move it.
/// </summary>
public partial class HudMovable : Node
{
    public string Id = "";
    public string Label = "";
    public Control Target = null!;
    public Action<bool>? Preview;

    public Vector2 Default { get; private set; }
    public bool IsSettled { get; private set; }

    public override void _Ready()
    {
        AddToGroup(HudLayout.Group);
        ProcessMode = ProcessModeEnum.Always;
        // Anchored controls only know their position once their parent has a size.
        CallDeferred(nameof(Settle));
    }

    private void Settle()
    {
        Default = Target.Position;
        IsSettled = true;
        Target.Position = Default + HudLayout.OffsetOf(Id);
        ApplyScale(HudLayout.ScaleOf(Id));
        KeepOnScreen();
    }

    public float Scale => Target.Scale.X;

    /// <summary>Resizes the panel around its centre and records the size.</summary>
    public void SetScale(float scale)
    {
        scale = Mathf.Clamp(scale, HudLayout.MinScale, HudLayout.MaxScale);
        ApplyScale(scale);
        HudLayout.SetScale(Id, scale);
        KeepOnScreen();
    }

    /// <summary>A panel grown from its centre near an edge would poke off screen:
    /// nudge it back in (and remember the nudge as part of its offset).</summary>
    private void KeepOnScreen()
    {
        Vector2 screen = Target.GetViewportRect().Size;
        Rect2 r = Target.GetGlobalRect();
        Vector2 shift = Vector2.Zero;
        if (r.Size.X <= screen.X)
        {
            shift.X = r.Position.X < 0f ? -r.Position.X : r.End.X > screen.X ? screen.X - r.End.X : 0f;
        }

        if (r.Size.Y <= screen.Y)
        {
            shift.Y = r.Position.Y < 0f ? -r.Position.Y : r.End.Y > screen.Y ? screen.Y - r.End.Y : 0f;
        }

        if (shift != Vector2.Zero)
        {
            Target.Position += shift;
            HudLayout.SetOffset(Id, Target.Position - Default);
        }
    }

    private void ApplyScale(float scale)
    {
        Target.PivotOffset = Target.Size / 2f;
        Target.Scale = new Vector2(scale, scale);
    }

    /// <summary>Moves the panel by <paramref name="delta"/>, keeping it on screen, and records the offset.</summary>
    public void MoveBy(Vector2 delta, Vector2 screen)
    {
        Rect2 rect = Target.GetGlobalRect();
        Vector2 size = rect.Size;
        Vector2 to = rect.Position + delta;
        to = new Vector2(Mathf.Clamp(to.X, -size.X * 0.5f, screen.X - size.X * 0.5f), Mathf.Clamp(to.Y, -size.Y * 0.5f, screen.Y - size.Y * 0.5f));
        Target.Position += to - rect.Position;
        HudLayout.SetOffset(Id, Target.Position - Default);
    }

    public void ResetPosition()
    {
        if (IsSettled)
        {
            Target.Position = Default;
            ApplyScale(1f);
        }
    }
}
