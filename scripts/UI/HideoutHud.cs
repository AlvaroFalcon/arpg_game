#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Items;
using ArpgGame.Quests;
using ArpgGame.Skills;
using ArpgGame.World;

namespace ArpgGame.UI;

/// <summary>
/// The hideout's HUD — docs/design/menus.md -> "Hideout HUD". Replaces the old
/// two-line wall of key hints with pieces that explain themselves:
/// <list type="bullet">
/// <item>a <b>character plate</b> (portrait, level, XP, gold, unspent points);</item>
/// <item>a <b>menu bar</b> of key caps (I, C, K, P, H, ?, Esc) that also work as buttons;</item>
/// <item>the <b>town directory</b>: every station, what quarter it's in, which way and
/// how far — click one to walk there;</item>
/// <item>a floating <b>E prompt</b> over the station you're next to, saying what it does;</item>
/// <item>the same life/mana cluster and skill row as in realities.</item>
/// </list>
/// </summary>
public partial class HideoutHud : CanvasLayer
{
    public Hideout Hideout { get; set; } = null!;

    private Player Player => Hideout.Player;

    private Control _root = null!;
    private VBoxContainer _directory = null!;
    private Control _directoryHost = null!;

    /// <summary>The town directory shows (only with the big map; tests).</summary>
    public bool DirectoryShowing => _directoryHost.Visible;
    private readonly List<DirectoryRow> _rows = new();
    private readonly List<KeyChip> _chips = new();
    private PanelContainer _prompt = null!;
    private Label _promptName = null!;
    private Label _promptVerb = null!;
    private Label _banner = null!;
    private Label _bannerSub = null!;
    private float _bannerTime;
    private SkillBar _skillRow = null!;
    private QuestTracker _tracker = null!;
    private QuestBadge _promptBadge = null!;
    private string _promptQuestSuffix = "";

    /// <summary>The prompt's text while it shows (tests).</summary>
    public string PromptText => _prompt.Visible ? $"{_promptName.Text} — {_promptVerb.Text}" : "";

    /// <summary>The marker on the E prompt's card (tests).</summary>
    public QuestMarker PromptMarker => _prompt.Visible ? _promptBadge.Marker : QuestMarker.None;

    public IReadOnlyList<string> DirectoryEntries => _rows.Select(r => r.Kind).ToList();

    /// <summary>The quest marker each directory row shows (tests).</summary>
    public IReadOnlyDictionary<string, QuestMarker> DirectoryMarkers => _rows.ToDictionary(r => r.Kind, r => r.Quest);

    /// <summary>The small town map above the directory (tests, captures).</summary>
    public TownMap Minimap => (TownMap)_corner.Map!;

    /// <summary>The big map (M).</summary>
    public TownMap BigMap => _bigMap!;

    /// <summary>The arrival banner's second line (tests).</summary>
    public string BannerSub => _bannerSub.Text;

    /// <summary>The badge on a menu-bar chip by its key ("J"), 0 when it has none (tests).</summary>
    public int ChipBadge(string key) => _chips.FirstOrDefault(c => c.Key == key)?.Badge?.Invoke() ?? 0;

    public override void _Ready()
    {
        Layer = 4;
        _root = UiTheme.FullRect(this);
        _root.MouseFilter = Control.MouseFilterEnum.Ignore;
        BuildTracker();
        BuildBigMap();
        BuildMenuBar();
        BuildDirectory();
        BuildVitals();
        BuildPrompt();
        BuildBanner();
        BuildParty();
    }

    private static StyleBoxFlat Glass(Color accent, int top = 2)
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.04f, 0.08f, 0.82f),
            BorderColor = accent,
        };
        box.SetBorderWidthAll(1);
        box.BorderWidthTop = top;
        box.SetCornerRadiusAll(6);
        box.SetContentMarginAll(10f);
        box.ShadowColor = new Color(0f, 0f, 0f, 0.35f);
        box.ShadowSize = 6;
        return box;
    }

    private CharacterPanels? Panels => Hideout.GetNodeOrNull<CharacterPanels>("CharacterPanels");

    // --- quest tracker (under the plate) -------------------------------------------

    /// <summary>The quest tracker (tests, captures).</summary>
    public QuestTracker Tracker => _tracker;

    private HudCorner _corner = null!;

    /// <summary>The top-right corner: the town minimap, the local time, the quests (tests).</summary>
    public HudCorner Corner => _corner;

    /// <summary>The top-right corner (docs/design/menus.md -> "HUD: top-right corner"): the
    /// square town minimap, the clock and the quest tracker under it.</summary>
    private void BuildTracker()
    {
        _tracker = new QuestTracker { OpenLog = id => Panels?.Quests.OpenOn(id) };
        _corner = new HudCorner
        {
            Map = new TownMap { Hideout = Hideout, Name = "Minimap" },
            Tracker = _tracker,
            BigMapOpen = () => BigMapOpen,
        };
        _root.AddChild(_corner);
        _corner.Dock();
        HudLayout.Register(_corner, "corner", "Minimap, clock & quests", _tracker.SetPreview, HudAvoid.Hide);
    }

    /// <summary>Where the quest toast's card goes: 8 px under the top-right corner, right-aligned with it.</summary>
    public Vector2 ToastAnchor()
    {
        Rect2 corner = _corner.GetGlobalRect();
        return new Vector2(corner.End.X - QuestToast.CardWidth, corner.End.Y + 8f);
    }

    // --- menu bar (bottom right) -----------------------------------------------------

    private void BuildMenuBar()
    {
        var bar = new PanelContainer { Name = "MenuBar" };
        // Bottom right, a little off the edges, a touch shorter than the flask boxes (owner, 2026-10-29).
        StyleBoxFlat glass = Glass(UiTheme.PanelBorder, 1);
        glass.SetContentMarginAll(3f);
        bar.AddThemeStyleboxOverride("panel", glass);
        _root.AddChild(bar);
        bar.SetAnchorsPreset(Control.LayoutPreset.BottomRight, true);
        bar.GrowHorizontal = Control.GrowDirection.Begin;
        bar.GrowVertical = Control.GrowDirection.Begin;
        bar.OffsetRight = -12f;
        bar.OffsetBottom = -12f;
        HBoxContainer row = UiTheme.HBox(bar, 2);
        AddChip(row, "I", "Inventory", () => ToggleInventory());
        AddChip(row, "C", "Character", () => Panels?.Sheet.Toggle());
        AddChip(row, "K", "Skills", () => Panels?.Skills.Toggle());
        AddChip(row, "P", "Passives", () => Panels?.Tree.Toggle(), () => CharacterBuild.Of(Player)?.Data.PassivePointsFree ?? 0);
        AddChip(row, "J", "Quests", () => Panels?.Quests.Toggle(), () => QuestService.Attention(Hideout));
        AddChip(row, "H", "Edit town", Hideout.OpenEditor, blocked: () => Hideout.EditBlockedReason);
        AddChip(row, "?", "Guide", Hideout.OpenGuide);
        AddChip(row, "Esc", "Menu", OpenPauseMenu);
        HudLayout.Register(bar, "hideout_menu", "Menu bar", avoid: HudAvoid.Slide);
    }

    private void AddChip(HBoxContainer row, string key, string label, Action press, Func<int>? badge = null, Func<string?>? blocked = null)
    {
        var chip = new KeyChip { Key = key, Caption = label, Pressed = press, Badge = badge, Blocked = blocked };
        row.AddChild(chip);
        _chips.Add(chip);
    }

    private void ToggleInventory()
    {
        if (Player.GetNodeOrNull<InventoryUI>("InventoryUI") is { } inv)
        {
            inv.SetOpen(!inv.IsOpen);
        }
    }

    private void OpenPauseMenu()
    {
        if (Hideout.GetChildren().OfType<PauseMenu>().FirstOrDefault() is { } menu)
        {
            menu.Open();
        }
    }

    // --- town directory (top right) ------------------------------------------------

    private void BuildDirectory()
    {
        // Top left, and only while the big map is open (owner, 2026-10-29): it is the map's legend.
        _directoryHost = new Control { Name = "TownDirectoryHost", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _root.AddChild(_directoryHost);
        var panel = new PanelContainer { Name = "TownDirectory", Position = new Vector2(14f, 14f) };
        panel.AddThemeStyleboxOverride("panel", Glass(new Color(0.55f, 0.75f, 1f)));
        _directoryHost.AddChild(panel);
        VBoxContainer column = UiTheme.VBox(panel, 2);
        HBoxContainer head = UiTheme.HBox(column, 6);
        Label title = UiTheme.Label("TOWN", 13, new Color(0.65f, 0.8f, 1f));
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(title);
        head.AddChild(UiTheme.Label("click to walk there", 11, UiTheme.TextDim));
        _directory = UiTheme.VBox(column, 2);
        foreach (PropDef def in HideoutCatalog.Props.Where(p => p.Station))
        {
            var row = new DirectoryRow { Kind = def.Id, Def = def, Hud = this };
            _directory.AddChild(row);
            _rows.Add(row);
        }

        HudLayout.Register(panel, "town", "Town directory", avoid: HudAvoid.Hide);
    }

    // --- big map (M / Tab) -----------------------------------------------------------

    private TownMap? _bigMap;

    /// <summary>Whether the big town map is showing.</summary>
    public bool BigMapOpen => _bigMap is { Visible: true };

    private void BuildBigMap()
    {
        _bigMap = new TownMap { Hideout = Hideout, Big = true, Name = "BigMap", Visible = false };
        _root.AddChild(_bigMap);
        _bigMap.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    public void ToggleBigMap()
    {
        if (_bigMap is not null && !Hideout.Editing)
        {
            _bigMap.Visible = !_bigMap.Visible;
            _directoryHost.Visible = _bigMap.Visible;
        }
    }

    public void CloseBigMap()
    {
        if (_bigMap is not null)
        {
            _bigMap.Visible = false;
            _directoryHost.Visible = false;
        }
    }

    /// <summary>M or Tab toggles the big map; Esc closes it before anything else.</summary>
    public override void _Input(InputEvent e)
    {
        if (GetTree().Paused || Hideout.Editing || e.IsEcho())
        {
            return;
        }

        if (InputMap.HasAction(MapOverlay.Action) && e.IsActionPressed(MapOverlay.Action) && !PanelStack.HasOpen)
        {
            ToggleBigMap();
            GetViewport().SetInputAsHandled();
        }
        else if (BigMapOpen && e.IsActionPressed("ui_cancel"))
        {
            CloseBigMap();
            PanelStack.MarkClosedThisFrame();
            GetViewport().SetInputAsHandled();
        }
    }

    // --- vitals and skills (bottom centre, as in realities) ------------------------

    private void BuildVitals()
    {
        CharacterBuild? build = CharacterBuild.Of(Player);
        var bar = new PlayerHudBar
        {
            Player = Player,
            Progress = () => CharacterBuild.Of(Player) is { } b ? (b.Data.Level, b.ExperienceFraction) : (Profile.Get(this)?.Active.Level ?? 1, 0f),
            Resource = () => CharacterBuild.Of(Player)?.Resource,
        };
        _root.AddChild(bar);
        bar.Dock();
        _skillRow = PlayerHudBar.CreateSkillRow(_root);
        HudLayout.Register(bar, "vitals", "Life, mana & flasks");
        HudLayout.Register(_skillRow, "skills", "Skills");
        _ = build;
    }

    private void UpdateSkills()
    {
        _skillRow.Show(CharacterBuild.Slots.Select(slot => (SkillSlotView.LabelOf(slot), Player.SkillUser.GetSkill(slot))).ToList());
        _skillRow.Cooldowns(Player.SkillUser.CooldownFraction);
    }

    // --- F prompt (over the nearest station) ---------------------------------------

    private void BuildPrompt()
    {
        _prompt = new PanelContainer { Name = "Prompt", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_prompt);
        HBoxContainer row = UiTheme.HBox(_prompt, 10);
        PanelContainer key = UiTheme.KeyCap("F", 20);
        key.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(key);
        VBoxContainer text = UiTheme.VBox(row, 0);
        _promptName = UiTheme.Label("", 17, UiTheme.Gold);
        _promptVerb = UiTheme.Label("", 13, UiTheme.Text);
        text.AddChild(_promptName);
        text.AddChild(_promptVerb);
        _promptBadge = new QuestBadge { Visible = false };
        row.AddChild(_promptBadge);
    }

    private void UpdatePrompt()
    {
        if (Hideout.Editing || GetTree().Paused || PanelStack.HasOpen || Hideout.NearestStation() is not { } near)
        {
            _prompt.Visible = false;
            Hideout.HighlightStation(null);
            return;
        }

        (PlacedProp prop, PropDef def) = near;
        _promptName.Text = def.Name;
        _promptName.AddThemeColorOverride("font_color", def.Accent);
        QuestMarker marker = QuestService.MarkerAt(Hideout, prop.Uid);
        _promptVerb.Text = (def.Id == "portal" ? $"{def.Verb} — {def.Blurb}" : def.Verb) + marker switch
        {
            QuestMarker.New => " · New quest",
            QuestMarker.Ready => " · Quest ready to turn in",
            QuestMarker.Active => " · Quest in progress",
            _ => "",
        };
        _promptBadge.Marker = marker;
        _promptBadge.Visible = marker != QuestMarker.None;
        _prompt.AddThemeStyleboxOverride("panel", Glass(def.Accent));
        _prompt.Visible = true;
        _prompt.ResetSize();
        Vector2 anchor = Hideout.GetGlobalTransformWithCanvas() * (prop.Position - new Vector2(0f, Hideout.LabelHeightOf(def) + 6f));
        Vector2 at = anchor - new Vector2(_prompt.Size.X / 2f, _prompt.Size.Y);
        Vector2 screen = _root.Size;
        _prompt.Position = new Vector2(Mathf.Clamp(at.X, 8f, screen.X - _prompt.Size.X - 8f), Mathf.Clamp(at.Y, 80f, screen.Y - _prompt.Size.Y - 230f));
        Hideout.HighlightStation(prop.Uid);
    }

    // --- banner ----------------------------------------------------------------------

    private void BuildBanner()
    {
        _banner = UiTheme.Label("", 44, UiTheme.Gold, HorizontalAlignment.Center);
        _root.AddChild(_banner);
        _banner.SetAnchorsPreset(Control.LayoutPreset.Center, true);
        _banner.OffsetLeft = -500f;
        _banner.OffsetRight = 500f;
        _banner.OffsetTop = -190f;
        _bannerSub = UiTheme.Label("", 18, UiTheme.Text, HorizontalAlignment.Center);
        _root.AddChild(_bannerSub);
        _bannerSub.SetAnchorsPreset(Control.LayoutPreset.Center, true);
        _bannerSub.OffsetLeft = -500f;
        _bannerSub.OffsetRight = 500f;
        _bannerSub.OffsetTop = -130f;
        foreach (Label l in new[] { _banner, _bannerSub })
        {
            l.AddThemeConstantOverride("outline_size", 6);
            l.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
        }
    }

    public void ShowBanner(string title, string sub)
    {
        _banner.Text = title;
        _bannerSub.Text = sub;
        _bannerTime = 3.2f;
    }

    // --- frame -------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool editing = Hideout.Editing;
        _root.Visible = !editing;
        if (editing)
        {
            return;
        }

        UpdateSkills();
        UpdatePrompt();
        _directoryHost.Visible = BigMapOpen;
        foreach (DirectoryRow row in _rows)
        {
            row.Refresh();
        }

        foreach (KeyChip chip in _chips)
        {
            chip.Modulate = chip.Blocked?.Invoke() is null ? Colors.White : new Color(1f, 1f, 1f, 0.45f);
            chip.QueueRedraw();
        }

        if (_bannerTime > 0f)
        {
            _bannerTime -= dt;
            float a = Mathf.Clamp(_bannerTime / 0.8f, 0f, 1f);
            _banner.Modulate = new Color(1f, 1f, 1f, a);
            _bannerSub.Modulate = new Color(1f, 1f, 1f, a);
        }
        else
        {
            _banner.Text = "";
            _bannerSub.Text = "";
        }

        if (_toastTime > 0f)
        {
            _toastTime -= dt;
            _toast.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(_toastTime / 0.6f, 0f, 1f));
            _toast.Visible = true;
        }
        else
        {
            _toast.Visible = false;
        }

        _partyBox.Visible = _partyLine.Text.Length > 0;
    }

    // --- party (docs/design/multiplayer.md -> "Shared hideout") ----------------------------

    private Label _toast = null!;
    private float _toastTime;
    private PanelContainer _partyBox = null!;
    private Label _partyLine = null!;

    /// <summary>The last party toast while it shows (tests).</summary>
    public string ToastText => _toastTime > 0f ? _toast.Text : "";

    /// <summary>The standing party line ("Ana's party is in the Survival Rift · 3:12"), "" when hidden.</summary>
    public string PartyLine
    {
        get => _partyLine.Text;
        set => _partyLine.Text = value;
    }

    private void BuildParty()
    {
        _toast = UiTheme.Label("", 18, UiTheme.Text, HorizontalAlignment.Center);
        _toast.Name = "PartyToast";
        _toast.MouseFilter = Control.MouseFilterEnum.Ignore;
        _toast.AddThemeConstantOverride("outline_size", 5);
        _toast.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _root.AddChild(_toast);
        _toast.SetAnchorsPreset(Control.LayoutPreset.CenterTop, true);
        _toast.OffsetLeft = -460f;
        _toast.OffsetRight = 460f;
        _toast.OffsetTop = 112f;
        _toast.Visible = false;

        _partyBox = new PanelContainer { Name = "PartyLine", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _partyBox.AddThemeStyleboxOverride("panel", Glass(UiTheme.Gold, 1));
        _root.AddChild(_partyBox);
        _partyBox.SetAnchorsPreset(Control.LayoutPreset.CenterTop, true);
        _partyBox.GrowHorizontal = Control.GrowDirection.Both;
        _partyBox.OffsetTop = 66f;
        _partyLine = UiTheme.Label("", 16, UiTheme.Gold, HorizontalAlignment.Center);
        _partyBox.AddChild(_partyLine);
    }

    /// <summary>A short party message under the menu bar ("Bo is here").</summary>
    public void ShowToast(string text, Color colour)
    {
        _toast.Text = text;
        _toast.AddThemeColorOverride("font_color", colour);
        _toastTime = 3.5f;
    }

    /// <summary>A key cap with its action underneath; clicking it does the same as the key.</summary>
    private sealed partial class KeyChip : Control
    {
        public string Key = "";
        public string Caption = "";
        public Action Pressed = () => { };
        public Func<int>? Badge;

        /// <summary>Why the chip can't be used now (greyed, said in its tooltip), or null.</summary>
        public Func<string?>? Blocked;
        private bool _hover;

        public override void _Ready()
        {
            // A key cap alone; what it does is its tooltip.
            CustomMinimumSize = new Vector2(Mathf.Max(34f, Key.Length * 9f + 16f), 34f);
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += () => { _hover = true; QueueRedraw(); };
            MouseExited += () => { _hover = false; QueueRedraw(); };
            UiTheme.InstantTooltip(this, () => Blocked?.Invoke() ?? $"{Caption} ({Key})");
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                Pressed();
                AcceptEvent();
            }
        }

        public override void _Draw()
        {
            Font font = ThemeDB.FallbackFont;
            var cap = new Rect2(2f, 2f, Size.X - 4f, Size.Y - 7f);
            Color face = _hover ? new Color(1f, 0.95f, 0.82f) : new Color(0.88f, 0.84f, 0.75f);
            DrawRect(new Rect2(cap.Position + new Vector2(0f, 3f), cap.Size), new Color(0.35f, 0.3f, 0.25f));
            DrawRect(cap, face);
            DrawString(font, new Vector2(cap.Position.X, cap.Position.Y + 18f), Key, HorizontalAlignment.Center, cap.Size.X, 14, new Color(0.12f, 0.1f, 0.08f));
            if (Badge?.Invoke() is > 0 and var n)
            {
                Vector2 c = new(cap.End.X - 3f, cap.Position.Y + 3f);
                DrawCircle(c, 8f, UiTheme.Ember);
                DrawString(font, c + new Vector2(-8f, 4.5f), n.ToString(), HorizontalAlignment.Center, 16f, 11, Colors.White);
            }
        }
    }

    /// <summary>One station in the town directory: accent, name, quarter, distance and
    /// an arrow pointing at it from the player. Clicking walks there.</summary>
    private sealed partial class DirectoryRow : Control
    {
        public string Kind = "";
        public PropDef Def = null!;
        public HideoutHud Hud = null!;
        private bool _hover;
        private string _where = "";
        private float _angle;
        private bool _alert;
        private QuestMarker _quest;

        /// <summary>The quest marker the row shows (tests).</summary>
        public QuestMarker Quest => _quest;

        public override void _Ready()
        {
            CustomMinimumSize = new Vector2(250f, 30f);
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            UiTheme.InstantTooltip(this, () => $"{Def.Name}\n{Def.Blurb}");
            MouseEntered += () => { _hover = true; QueueRedraw(); };
            MouseExited += () => { _hover = false; QueueRedraw(); };
        }

        public void Refresh()
        {
            Hideout h = Hud.Hideout;
            if (h.Data.Find(Kind) is not { } prop)
            {
                return;
            }

            Vector2 d = prop.Position - h.Player.GlobalPosition;
            _angle = d.Angle();
            _where = $"{HideoutTown.QuarterOf(prop.Position)} · {Mathf.RoundToInt(d.Length() / 32f)} m";
            CharacterData? c = CharacterBuild.Of(h.Player)?.Data;
            _alert = Kind == "altar" && c is not null && (c.AscendancyPointsFree > 0 || c is { AscendancyPoints: > 0, Ascendancy: null });
            _quest = QuestService.MarkerAt(h, Kind);
            QueueRedraw();
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                Hud.Hideout.WalkTo(Kind);
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

            DrawRect(new Rect2(0f, 5f, 3f, Size.Y - 10f), Def.Accent);
            DrawString(font, new Vector2(10f, 14f), Def.Name, HorizontalAlignment.Left, Size.X - 50f, 14, _hover ? Colors.White : UiTheme.Text);
            DrawString(font, new Vector2(10f, 27f), _where, HorizontalAlignment.Left, Size.X - 50f, 11, UiTheme.TextDim);
            // A quest's marker wins over the ascendancy-points dot (the altar has both).
            if (_quest != QuestMarker.None)
            {
                QuestGlyphs.DrawMarker(this, new Vector2(Size.X - 46f, Size.Y / 2f), 8f, _quest, 11f);
            }
            else if (_alert)
            {
                DrawCircle(new Vector2(Size.X - 46f, 10f), 5f, UiTheme.Ember);
            }

            // The arrow: which way to walk (gold and a fifth larger when a quest waits here).
            bool waiting = _quest is QuestMarker.New or QuestMarker.Ready;
            float k = waiting ? 1.2f : 1f;
            Vector2 c = new(Size.X - 16f, Size.Y / 2f);
            Vector2 f = Vector2.FromAngle(_angle);
            Vector2 s = new(-f.Y, f.X);
            DrawCircle(c, 11f * k, new Color(1f, 1f, 1f, 0.06f));
            DrawColoredPolygon(new[] { c + f * 8f * k, c - f * 5f * k + s * 5f * k, c - f * 2f * k, c - f * 5f * k - s * 5f * k }, waiting ? UiTheme.Gold : Def.Accent);
        }
    }

    /// <summary>The 22 px quest marker on the E prompt's card.</summary>
    private sealed partial class QuestBadge : Control
    {
        private QuestMarker _marker;

        public QuestMarker Marker
        {
            get => _marker;
            set
            {
                _marker = value;
                QueueRedraw();
            }
        }

        public override void _Ready()
        {
            CustomMinimumSize = new Vector2(24f, 24f);
            SizeFlagsVertical = SizeFlags.ShrinkCenter;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw() => QuestGlyphs.DrawMarker(this, new Vector2(12f, 12f), 11f, _marker, 16f);
    }
}
