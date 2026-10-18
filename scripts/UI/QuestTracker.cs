#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Core;
using ArpgGame.Quests;
using ArpgGame.World;

namespace ArpgGame.UI;

/// <summary>
/// The hideout HUD's quest tracker — docs/design/quests.md -> "The quest tracker". Under the
/// character plate: up to three rows (the glyph, the title, the station), a "+N more · J" line,
/// each a click away from the quest log. Hidden when nothing is New, Active or Ready. Rebuilt
/// when a quest changes, not every frame.
/// </summary>
public partial class QuestTracker : PanelContainer
{
    public const int MaxRows = 3;
    public const float Width = 286f;

    /// <summary>Opens the quest log on a quest.</summary>
    public Action<string>? OpenLog { get; set; }

    /// <summary>Quest ids of the rows showing, top to bottom (tests).</summary>
    public IReadOnlyList<string> RowIds => _ids;

    /// <summary>The "+N more" line, or "" (tests).</summary>
    public string MoreText { get; private set; } = "";

    private VBoxContainer _rows = null!;
    private readonly List<string> _ids = new();
    private Profile? _profile;
    private bool _preview;

    public override void _EnterTree()
    {
        _profile = Profile.Get(this);
        if (_profile is not null)
        {
            _profile.QuestChanged += OnChanged;
        }
    }

    public override void _ExitTree()
    {
        if (_profile is not null && IsInstanceValid(_profile))
        {
            _profile.QuestChanged -= OnChanged;
        }

        _profile = null;
    }

    private void OnChanged(string id, int status) => Rebuild();

    public override void _Ready()
    {
        Name = "QuestTracker";
        CustomMinimumSize = new Vector2(Width, 0f);
        MouseFilter = MouseFilterEnum.Pass;
        var box = new StyleBoxFlat { BgColor = new Color(0.05f, 0.04f, 0.08f, 0.82f), BorderColor = UiTheme.Gold };
        box.SetBorderWidthAll(1);
        box.BorderWidthTop = 2;
        box.SetCornerRadiusAll(6);
        box.SetContentMarginAll(8f);
        box.ShadowColor = new Color(0f, 0f, 0f, 0.35f);
        box.ShadowSize = 6;
        AddThemeStyleboxOverride("panel", box);
        VBoxContainer column = UiTheme.VBox(this, 2);
        HBoxContainer head = UiTheme.HBox(column, 6);
        Label title = UiTheme.Label("QUESTS", 12, UiTheme.TextDim);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        PanelContainer key = UiTheme.KeyCap("J", 11);
        key.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(key);
        _rows = UiTheme.VBox(column, 0);
        Rebuild();
    }

    /// <summary>The HUD editor shows a sample row while it is open and the tracker is empty.</summary>
    public void SetPreview(bool on)
    {
        _preview = on;
        Rebuild();
    }

    public void Rebuild()
    {
        if (_rows is null)
        {
            return;
        }

        foreach (Node child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }

        _ids.Clear();
        MoreText = "";
        List<(QuestDef Def, QuestProgress Progress)> all = _profile is not null && QuestService.BookOf(_profile) is { } book ? book.Log().ToList() : new();
        List<(QuestDef Def, QuestProgress Progress)> open = all
            .Where(q => q.Progress.Status is QuestStatus.New or QuestStatus.Active or QuestStatus.Ready)
            .OrderByDescending(q => Rank(q.Progress.Status))
            .ToList();
        if (open.Count == 0 && _preview)
        {
            open.Add((QuestCatalog.Get(QuestCatalog.AscendId)!, new QuestProgress { Status = QuestStatus.New }));
        }

        Visible = open.Count > 0;
        foreach ((QuestDef def, QuestProgress progress) in open.Take(MaxRows))
        {
            _ids.Add(def.Id);
            var row = new Row { Quest = def, Marker = MarkerOf(progress.Status), Open = id => OpenLog?.Invoke(id) };
            _rows.AddChild(row);
        }

        if (open.Count > MaxRows)
        {
            MoreText = $"+{open.Count - MaxRows} more · J";
            _rows.AddChild(UiTheme.Label(MoreText, 11, UiTheme.TextDim));
        }

        ResetSize();
    }

    private static int Rank(QuestStatus s) => s switch { QuestStatus.Ready => 3, QuestStatus.New => 2, _ => 1 };

    private static QuestMarker MarkerOf(QuestStatus s) => s switch
    {
        QuestStatus.Ready => QuestMarker.Ready,
        QuestStatus.New => QuestMarker.New,
        _ => QuestMarker.Active,
    };

    private sealed partial class Row : Control
    {
        public QuestDef Quest = null!;
        public QuestMarker Marker;
        public Action<string> Open = _ => { };
        private bool _hover;

        public override void _Ready()
        {
            CustomMinimumSize = new Vector2(Width - 16f, 26f);
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += () => { _hover = true; QueueRedraw(); };
            MouseExited += () => { _hover = false; QueueRedraw(); };
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                Open(Quest.Id);
                AcceptEvent();
            }
        }

        public override void _Draw()
        {
            Font font = ThemeDB.FallbackFont;
            if (_hover)
            {
                DrawRect(new Rect2(Vector2.Zero, Size), new Color(1f, 1f, 1f, 0.06f));
            }

            QuestGlyphs.DrawMarker(this, new Vector2(10f, Size.Y / 2f), 9f, Marker, 13f);
            string station = HideoutCatalog.Prop(Quest.Station)?.Name ?? "";
            float stationWidth = font.GetStringSize(station, HorizontalAlignment.Left, -1, 11).X;
            float titleRoom = Size.X - 26f - stationWidth - 6f;
            DrawString(font, new Vector2(26f, 17f), Quest.Title, HorizontalAlignment.Left, titleRoom, 13, _hover ? Colors.White : UiTheme.Text);
            DrawString(font, new Vector2(Size.X - stationWidth, 17f), station, HorizontalAlignment.Left, -1, 11, UiTheme.TextDim);
        }
    }
}
