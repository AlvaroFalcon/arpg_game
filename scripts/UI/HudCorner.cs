#nullable enable
using System;
using Godot;

namespace ArpgGame.UI;

/// <summary>
/// The top-right corner of every HUD — docs/design/menus.md -> "HUD: top-right corner"
/// (the owner's sketch, 2026-10-29): a square minimap held a little off the edges, the
/// local time under it, and under that, a little apart, the quest tracker when a quest is
/// open. The minimap hides while the big map (Tab) is up; the clock and quests move up.
/// One movable panel in the HUD editor ("Minimap, clock & quests").
/// </summary>
public partial class HudCorner : VBoxContainer
{
    public const float MapSize = 196f;
    public const float Margin = 18f;

    /// <summary>The minimap control (any size; it is drawn into a square frame).</summary>
    public Control? Map { get; init; }

    /// <summary>The quest tracker, if this screen shows quests.</summary>
    public QuestTracker? Tracker { get; init; }

    /// <summary>True while the big map is open: the minimap steps aside.</summary>
    public Func<bool> BigMapOpen { get; init; } = () => false;

    private Label _clock = null!;
    private PanelContainer? _frame;
    private int _minute = -1;

    /// <summary>The clock's text (tests).</summary>
    public string ClockText => _clock.Text;

    /// <summary>Whether the minimap shows now (tests).</summary>
    public bool MapShowing => _frame?.Visible ?? false;

    public override void _Ready()
    {
        Name = "HudCorner";
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 4);
        if (Map is not null)
        {
            _frame = new PanelContainer { Name = "MinimapFrame", MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
            var box = new StyleBoxFlat { BgColor = new Color(0.02f, 0.01f, 0.04f, 0.55f), BorderColor = new Color(0.62f, 0.52f, 0.9f, 0.9f) };
            box.SetBorderWidthAll(2);
            box.SetContentMarginAll(3f);
            box.ShadowColor = new Color(0f, 0f, 0f, 0.4f);
            box.ShadowSize = 6;
            _frame.AddThemeStyleboxOverride("panel", box);
            AddChild(_frame);
            Map.CustomMinimumSize = new Vector2(MapSize, MapSize);
            Map.SizeFlagsHorizontal = SizeFlags.Fill;
            _frame.AddChild(Map);
        }

        _clock = UiTheme.Label("", 18, UiTheme.Text, HorizontalAlignment.Center);
        _clock.Name = "Clock";
        _clock.AddThemeConstantOverride("outline_size", 5);
        _clock.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
        _clock.CustomMinimumSize = new Vector2(MapSize + 10f, 0f);
        _clock.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        AddChild(_clock);

        if (Tracker is not null)
        {
            AddChild(new Control { CustomMinimumSize = new Vector2(0f, 10f), MouseFilter = MouseFilterEnum.Ignore });
            Tracker.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            // As wide as the minimap's frame (owner, 2026-10-29).
            if (Map is not null)
            {
                Tracker.PanelWidth = MapSize + 2f * 3f + 2f * 2f;
            }
            AddChild(Tracker);
        }

        UpdateClock();
    }

    /// <summary>Anchors the corner to the top right of a full-rect parent, a little off both edges.</summary>
    public void Dock()
    {
        SetAnchorsPreset(LayoutPreset.TopRight, true);
        GrowHorizontal = GrowDirection.Begin;
        OffsetRight = -Margin;
        OffsetTop = Margin;
    }

    public override void _Process(double delta)
    {
        if (_frame is not null)
        {
            _frame.Visible = !BigMapOpen();
        }

        UpdateClock();
    }

    private void UpdateClock()
    {
        var now = Time.GetTimeDictFromSystem();
        int hour = now["hour"].AsInt32();
        int minute = now["minute"].AsInt32();
        if (minute != _minute)
        {
            _minute = minute;
            _clock.Text = $"{hour:00}:{minute:00}";
        }
    }
}
