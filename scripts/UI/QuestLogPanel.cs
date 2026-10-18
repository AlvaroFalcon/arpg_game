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
/// The quest log (J) — docs/design/quests.md -> "The quest log". Left: Ready to turn in, Active
/// (new quests first) and Completed (collapsed). Right: the selected quest — what it teaches,
/// the objective as a checkbox, the rewards, and where to turn it in on a map of the town with
/// a "Walk there" button. Turning in is never done from here: you claim at the station,
/// which is what makes the "?" on the map mean something.
/// </summary>
public partial class QuestLogPanel : ModalPanel
{
    protected override string Title => "Quests";

    protected override Vector2 FrameSize => new(1010f, 600f);

    /// <summary>The hideout, when the log is opened there (its map and "Walk there"); null in realities.</summary>
    public Hideout? Hideout { get; set; }

    /// <summary>Set by whoever hosts the log: true keeps J from opening it (the town's edit mode).</summary>
    public Func<bool>? Blocked { get; set; }

    /// <summary>The quest whose detail is showing.</summary>
    public string? SelectedId { get; private set; }

    /// <summary>The Completed section is expanded.</summary>
    public bool CompletedOpen { get; private set; }

    /// <summary>The list as shown: section captions and quest ids in order (tests).</summary>
    public IReadOnlyList<string> Listing => _listing;

    /// <summary>The empty-state line is showing (tests).</summary>
    public bool ShowingEmpty { get; private set; }

    /// <summary>Text of the empty state.</summary>
    public const string EmptyText = "No quests yet. A quest appears when something new opens up in your town, and teaches you how it works.";

    private readonly List<string> _listing = new();
    private VBoxContainer _list = null!;
    private VBoxContainer _detail = null!;
    private Button? _walk;
    private string? _walkStation;

    /// <summary>The "Walk there" button is enabled (tests).</summary>
    public bool CanWalk => _walk is { Disabled: false };

    protected override void Build(VBoxContainer body)
    {
        HBoxContainer columns = UiTheme.HBox(body, 14);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var listBox = new PanelContainer { CustomMinimumSize = new Vector2(320f, 510f) };
        listBox.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.06f, 0.05f, 0.09f), UiTheme.PanelBorder, 1, 4, 4));
        columns.AddChild(listBox);
        var listScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        listBox.AddChild(listScroll);
        _list = UiTheme.VBox(listScroll, 2);
        _list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var detailScroll = new ScrollContainer { CustomMinimumSize = new Vector2(640f, 510f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        columns.AddChild(detailScroll);
        _detail = UiTheme.VBox(detailScroll, 8);
        _detail.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
    }

    /// <summary>Opens the log on a quest (a toast or a tracker row was clicked).</summary>
    public void OpenOn(string id)
    {
        SelectedId = id;
        if (IsOpen)
        {
            Refresh();
        }
        else if (!GetTree().Paused)
        {
            Open();
        }
    }

    protected override void OnClosed() => SelectedId = null;

    protected override void OnQuestsChanged() => Refresh();

    public override void Refresh()
    {
        ClearChildren(_list);
        ClearChildren(_detail);
        _listing.Clear();
        _walk = null;
        _walkStation = null;
        var log = QuestService.Log(this).ToList();
        ShowingEmpty = log.Count == 0;
        if (log.Count == 0)
        {
            Label empty = UiTheme.Label(EmptyText, 16, UiTheme.TextDim);
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            empty.CustomMinimumSize = new Vector2(300f, 0f);
            _list.AddChild(empty);
            _detail.AddChild(UiTheme.Label("Nothing to show", 20, UiTheme.TextDim));
            return;
        }

        List<(QuestDef Def, QuestProgress Progress)> ready = log.Where(q => q.Progress.Status == QuestStatus.Ready).ToList();
        List<(QuestDef Def, QuestProgress Progress)> active = log.Where(q => q.Progress.Status is QuestStatus.New or QuestStatus.Active)
            .OrderBy(q => q.Progress.Status == QuestStatus.New ? 0 : 1).ToList();
        List<(QuestDef Def, QuestProgress Progress)> done = log.Where(q => q.Progress.Status == QuestStatus.Done).ToList();

        if (SelectedId is null || log.All(q => q.Def.Id != SelectedId))
        {
            SelectedId = ready.Concat(active).Concat(done).First().Def.Id;
        }

        AddSection($"READY TO TURN IN ({ready.Count})", ready, null);
        AddSection($"ACTIVE ({active.Count})", active, null);
        AddSection($"COMPLETED ({done.Count})", done, () =>
        {
            CompletedOpen = !CompletedOpen;
            Refresh();
        });

        (QuestDef def, QuestProgress progress) = log.First(q => q.Def.Id == SelectedId);
        BuildDetail(def, progress);
    }

    private void AddSection(string caption, List<(QuestDef Def, QuestProgress Progress)> quests, Action? toggle)
    {
        if (quests.Count == 0 && toggle is null)
        {
            return;
        }

        _listing.Add(caption);
        _list.AddChild(new SectionHeader { Caption = caption, Collapsible = toggle is not null, Open = CompletedOpen, Toggle = toggle ?? (() => { }) });
        if (toggle is not null && !CompletedOpen)
        {
            return;
        }

        foreach ((QuestDef def, QuestProgress progress) in quests)
        {
            _listing.Add(def.Id);
            _list.AddChild(new ListRow
            {
                Quest = def,
                Status = progress.Status,
                Selected = def.Id == SelectedId,
                Pick = id =>
                {
                    SelectedId = id;
                    Refresh();
                },
            });
        }
    }

    // --- detail ---------------------------------------------------------------------------

    private void BuildDetail(QuestDef q, QuestProgress progress)
    {
        PropDef? station = HideoutCatalog.Prop(q.Station);
        Color accent = station?.Accent ?? UiTheme.Gold;
        Profile? profile = Profile.Get(this);
        PlacedProp? placed = profile?.Hideout.Find(q.Station);
        string quarter = placed is null ? "" : HideoutTown.QuarterOf(placed.Position);

        _detail.AddChild(UiTheme.Label(q.Title, 24, UiTheme.Gold));
        _detail.AddChild(UiTheme.Label(quarter.Length > 0 ? $"From {station?.Name ?? q.Speaker} · {quarter}" : $"From {station?.Name ?? q.Speaker}", 14, accent));

        QuestContext? ctx = profile is null ? null : QuestService.ContextOf(profile);
        string explanation = ctx is null ? "" : q.Explain(ctx);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(620f, 130f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _detail.AddChild(scroll);
        var rich = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            CustomMinimumSize = new Vector2(600f, 0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = QuestGlyphs.Rich(explanation),
        };
        rich.AddThemeFontSizeOverride("normal_font_size", 14);
        scroll.AddChild(rich);

        // Objective: a drawn checkbox.
        bool met = progress.Status is QuestStatus.Ready or QuestStatus.Done;
        HBoxContainer objective = UiTheme.HBox(_detail, 12);
        objective.AddChild(Caps("OBJECTIVE"));
        objective.AddChild(new CheckBox2 { Ticked = met });
        VBoxContainer lines = UiTheme.VBox(objective, 0);
        string stationName = station?.Name ?? q.Speaker;
        string count = q.Count > 1 ? $" ({Math.Min(progress.Count, q.Count)}/{q.Count})" : "";
        lines.AddChild(UiTheme.Label(progress.Status == QuestStatus.Ready ? $"Done: turn it in at {stationName}" : q.Objective + count, 15, met ? UiTheme.Experience : UiTheme.Text));
        Label hint = UiTheme.Label(q.Hint, 13, UiTheme.TextDim);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.CustomMinimumSize = new Vector2(480f, 0f);
        lines.AddChild(hint);

        HBoxContainer rewards = UiTheme.HBox(_detail, 12);
        Label rewardCaps = Caps("REWARD");
        rewardCaps.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        rewards.AddChild(rewardCaps);
        QuestRewardRow.Build(rewards, q, ctx, 24f, 15, progress.Status == QuestStatus.Done ? UiTheme.TextDim : UiTheme.Text);
        if (progress.Status == QuestStatus.Done)
        {
            rewards.AddChild(UiTheme.Label("Claimed", 14, UiTheme.TextDim));
        }

        if (progress.Status == QuestStatus.Done)
        {
            HBoxContainer line = UiTheme.HBox(_detail, 12);
            line.AddChild(Caps("TURNED IN"));
            Label said = UiTheme.Label(q.TurnIn, 14, UiTheme.TextDim);
            said.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            said.CustomMinimumSize = new Vector2(480f, 0f);
            line.AddChild(said);
            return;
        }

        // Turn in: the town map with the station ringed, and a button that walks there.
        HBoxContainer turnIn = UiTheme.HBox(_detail, 12);
        Label turnInCaps = Caps("TURN IN");
        turnInCaps.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        turnIn.AddChild(turnInCaps);
        if (Hideout is { } hideout && IsInstanceValid(hideout) && hideout.Data.Find(q.Station) is { } prop)
        {
            turnIn.AddChild(new TownMap { Hideout = hideout, Highlight = q.Station, Interactive = false, CustomMinimumSize = new Vector2(300f, 190f) });
            VBoxContainer side = UiTheme.VBox(turnIn, 6);
            side.AddChild(UiTheme.Label(stationName, 15, accent));
            side.AddChild(UiTheme.Label($"{HideoutTown.QuarterOf(prop.Position)} · {Mathf.RoundToInt(prop.Position.DistanceTo(hideout.Player.GlobalPosition) / 32f)} m", 13, UiTheme.TextDim));
            _walkStation = q.Station;
            _walk = UiTheme.PrimaryButton("Walk there", WalkThere, 16, 160f);
            _walk.FocusMode = Control.FocusModeEnum.None;
            side.AddChild(_walk);
        }
        else
        {
            VBoxContainer side = UiTheme.VBox(turnIn, 6);
            Label where = UiTheme.Label(quarter.Length > 0 ? $"In your hideout: the {quarter}" : $"In your hideout: {stationName}", 16, accent);
            where.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            where.CustomMinimumSize = new Vector2(400f, 0f);
            side.AddChild(where);
            _walk = UiTheme.Button("Back in your hideout", () => { }, 16, 220f);
            _walk.Disabled = true;
            _walk.FocusMode = Control.FocusModeEnum.None;
            side.AddChild(_walk);
        }
    }

    private static Label Caps(string text)
    {
        Label l = UiTheme.Label(text, 12, UiTheme.TextDim);
        l.CustomMinimumSize = new Vector2(78f, 0f);
        return l;
    }

    /// <summary>Closes the log and walks to the turn-in station (the directory's walk).</summary>
    public void WalkThere()
    {
        if (_walkStation is not { } station || Hideout is not { } hideout || _walk is null || _walk.Disabled)
        {
            return;
        }

        Close();
        hideout.WalkTo(station);
    }

    // --- keys ---------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!IsOpen)
        {
            return;
        }

        int step = Input.IsActionJustPressed("ui_down") ? 1 : Input.IsActionJustPressed("ui_up") ? -1 : 0;
        if (step != 0)
        {
            Move(step);
        }

        if (Input.IsActionJustPressed("ui_accept"))
        {
            WalkThere();
        }
    }

    /// <summary>Moves the selection through the quests the list shows.</summary>
    public void Move(int step)
    {
        List<string> ids = _listing.Where(l => QuestCatalog.Exists(l)).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        int at = Math.Max(0, ids.IndexOf(SelectedId ?? ""));
        SelectedId = ids[Math.Clamp(at + step, 0, ids.Count - 1)];
        Refresh();
    }

    // --- controls -------------------------------------------------------------------------------

    private sealed partial class SectionHeader : Control
    {
        public string Caption = "";
        public bool Collapsible;
        public bool Open;
        public Action Toggle = () => { };

        public override void _Ready()
        {
            CustomMinimumSize = new Vector2(296f, 28f);
            MouseFilter = Collapsible ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
            if (Collapsible)
            {
                MouseDefaultCursorShape = CursorShape.PointingHand;
            }
        }

        public override void _GuiInput(InputEvent e)
        {
            if (Collapsible && e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                Toggle();
                AcceptEvent();
            }
        }

        public override void _Draw()
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(8f, 19f), Caption, HorizontalAlignment.Left, -1, 12, UiTheme.TextDim);
            if (Collapsible)
            {
                QuestGlyphs.DrawDisclosure(this, new Vector2(Size.X - 16f, Size.Y / 2f), Open, UiTheme.TextDim);
            }
        }
    }

    private sealed partial class ListRow : Control
    {
        public QuestDef Quest = null!;
        public QuestStatus Status;
        public bool Selected;
        public Action<string> Pick = _ => { };
        private bool _hover;

        public override void _Ready()
        {
            CustomMinimumSize = new Vector2(296f, 48f);
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += () => { _hover = true; QueueRedraw(); };
            MouseExited += () => { _hover = false; QueueRedraw(); };
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                Pick(Quest.Id);
                AcceptEvent();
            }
        }

        public override void _Draw()
        {
            Font font = ThemeDB.FallbackFont;
            PropDef? station = HideoutCatalog.Prop(Quest.Station);
            if (Selected || _hover)
            {
                DrawRect(new Rect2(Vector2.Zero, Size), Selected ? new Color(0.91f, 0.72f, 0.29f, 0.14f) : new Color(1f, 1f, 1f, 0.05f));
            }

            DrawRect(new Rect2(0f, 5f, 3f, Size.Y - 10f), station?.Accent ?? UiTheme.Gold);
            var glyph = new Vector2(24f, Size.Y / 2f);
            if (Status == QuestStatus.Done)
            {
                QuestGlyphs.DrawCheck(this, new Rect2(glyph - new Vector2(9f, 9f), new Vector2(18f, 18f)), UiTheme.Experience);
            }
            else
            {
                QuestGlyphs.DrawMarker(this, glyph, 11f, Status switch { QuestStatus.Ready => QuestMarker.Ready, QuestStatus.New => QuestMarker.New, _ => QuestMarker.Active }, 15f);
            }

            bool dim = Status == QuestStatus.Done;
            DrawString(font, new Vector2(44f, 21f), Quest.Title, HorizontalAlignment.Left, Size.X - 52f, 16, dim ? UiTheme.TextDim : Selected ? UiTheme.Gold : UiTheme.Text);
            DrawString(font, new Vector2(44f, 38f), station?.Name ?? Quest.Speaker, HorizontalAlignment.Left, Size.X - 52f, 12, UiTheme.TextDim);
        }
    }

    /// <summary>The objective's checkbox: drawn, because the theme's font has no ballot-box glyphs.</summary>
    private sealed partial class CheckBox2 : Control
    {
        public bool Ticked;

        public override void _Ready()
        {
            CustomMinimumSize = new Vector2(20f, 20f);
            SizeFlagsVertical = SizeFlags.ShrinkBegin;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw() =>
            QuestGlyphs.DrawCheckbox(this, new Rect2(1f, 1f, 17f, 17f), Ticked, Ticked ? UiTheme.Experience : UiTheme.TextDim);
    }
}
