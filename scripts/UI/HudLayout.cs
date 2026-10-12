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

    private static Dictionary<string, Vector2>? _offsets;

    private static Dictionary<string, Vector2> Offsets => _offsets ??= Load();

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

    public static void Reset() => Offsets.Clear();

    public static void Save()
    {
        var config = new ConfigFile();
        foreach ((string id, Vector2 offset) in Offsets)
        {
            config.SetValue("offsets", id, offset);
        }

        config.Save(SavePath);
    }

    /// <summary>Forgets the cached layout (tests, or after switching <see cref="SavePath"/>).</summary>
    public static void Reload() => _offsets = null;

    private static Dictionary<string, Vector2> Load()
    {
        var offsets = new Dictionary<string, Vector2>();
        var config = new ConfigFile();
        if (config.Load(SavePath) != Error.Ok || !config.HasSection("offsets"))
        {
            return offsets;
        }

        foreach (string id in config.GetSectionKeys("offsets"))
        {
            if (config.GetValue("offsets", id).Obj is Vector2 v)
            {
                offsets[id] = v;
            }
        }

        return offsets;
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
    public bool Ready { get; private set; }

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
        Ready = true;
        Target.Position = Default + HudLayout.OffsetOf(Id);
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
        if (Ready)
        {
            Target.Position = Default;
        }
    }
}
