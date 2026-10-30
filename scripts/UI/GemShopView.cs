#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Combat;
using ArpgGame.Core;
using ArpgGame.Skills;

namespace ArpgGame.UI;

/// <summary>
/// The Skill gems tab of Odran's panel — docs/design/hideout.md -> "Odran's Skill gems
/// tab". Left: a Skills / Supports toggle, filter chips, "Only what I can buy" and a
/// list of rows; right: the selected gem's details and its Buy button. The rules live
/// in <see cref="GemShop"/>; this only draws them and forwards clicks.
/// Keys: Up / Down select, Enter buys (the owner panel's Esc closes).
/// </summary>
public partial class GemShopView : VBoxContainer
{
    public static readonly Color SkillColor = new(1f, 0.6f, 0.35f);
    public static readonly Color SupportColor = new(0.45f, 1f, 0.55f);
    private static readonly Color OwnedColor = new(0.5f, 0.75f, 0.55f);

    private static readonly (string Id, string Label, SkillTags Tag)[] SkillFilters =
    {
        ("all", "All", SkillTags.None), ("attack", "Attack", SkillTags.Attack), ("spell", "Spell", SkillTags.Spell),
        ("projectile", "Projectile", SkillTags.Projectile), ("melee", "Melee", SkillTags.Melee), ("area", "Area", SkillTags.Area),
        ("fire", "Fire", SkillTags.Fire), ("cold", "Cold", SkillTags.Cold), ("lightning", "Lightning", SkillTags.Lightning),
    };

    private static readonly (string Id, string Label, SkillTags Tag)[] SupportFilters =
    {
        ("all", "All", SkillTags.None), ("fits", "Fits my skills", SkillTags.None), ("projectile", "Projectile", SkillTags.Projectile),
        ("area", "Area", SkillTags.Area), ("melee", "Melee", SkillTags.Melee), ("spell", "Spell", SkillTags.Spell),
        ("attack", "Attack", SkillTags.Attack),
    };

    /// <summary>The character to sell to (the panel supplies it).</summary>
    public Func<CharacterData?> CharacterSource { get; set; } = () => null;

    public Player Player { get; set; } = null!;

    /// <summary>Closes Odran and opens the skills panel (K); set by the owning panel.</summary>
    public Action? OpenSkills { get; set; }

    public bool ShowingSupports { get; private set; }
    public string Filter { get; private set; } = "all";
    public bool OnlyBuyable { get; private set; }
    public GemOffer? Selected { get; private set; }

    /// <summary>The offers the list shows now, in row order.</summary>
    public IReadOnlyList<GemOffer> Offered => _visible;

    public Button BuyButton => _buy;
    public string ReasonText => _reason.Text;
    public string DetailTitle => _detailName.Text;
    public Button? OpenSkillsButton => _openSkills;

    private readonly List<GemOffer> _visible = new();
    private readonly Dictionary<GemOffer, Control> _rows = new();
    private Button _skillsKind = null!;
    private Button _supportsKind = null!;
    private CheckBox _only = null!;
    private HFlowContainer _chips = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _list = null!;
    private TextureRect _detailIcon = null!;
    private Label _detailName = null!;
    private Label _detailKind = null!;
    private Label _detailFavoured = null!;
    private Label _detailText = null!;
    private Label _detailNumbers = null!;
    private Label _detailFits = null!;
    private Label _detailUnlock = null!;
    private Button _buy = null!;
    private Label _reason = null!;
    private Button _openSkills = null!;
    private Label _footer = null!;
    private int _newSince;
    private GemOffer? _justBought;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 6);
        HBoxContainer columns = UiTheme.HBox(this, 14);
        columns.SizeFlagsVertical = SizeFlags.ExpandFill;

        VBoxContainer left = UiTheme.VBox(columns, 6);
        left.CustomMinimumSize = new Vector2(600f, 0f);
        HBoxContainer top = UiTheme.HBox(left, 6);
        _skillsKind = UiTheme.Button("", () => SetKind(false), 14, 130f);
        _supportsKind = UiTheme.Button("", () => SetKind(true), 14, 130f);
        foreach (Button b in new[] { _skillsKind, _supportsKind })
        {
            b.CustomMinimumSize = new Vector2(130f, 30f);
            b.FocusMode = FocusModeEnum.None;
            top.AddChild(b);
        }

        top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _only = new CheckBox { Text = "Only what I can buy", FocusMode = FocusModeEnum.None };
        _only.AddThemeFontSizeOverride("font_size", 14);
        _only.Toggled += on => { OnlyBuyable = on; Rebuild(); };
        top.AddChild(_only);

        _chips = new HFlowContainer();
        _chips.AddThemeConstantOverride("h_separation", 4);
        _chips.AddThemeConstantOverride("v_separation", 4);
        left.AddChild(_chips);

        _scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(600f, 250f) };
        left.AddChild(_scroll);
        _list = UiTheme.VBox(_scroll, 4);
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        VBoxContainer right = UiTheme.VBox(columns, 6);
        right.CustomMinimumSize = new Vector2(340f, 0f);
        HBoxContainer head = UiTheme.HBox(right, 10);
        _detailIcon = UiTheme.PixelIcon(null, 48f);
        head.AddChild(_detailIcon);
        VBoxContainer titles = UiTheme.VBox(head, 0);
        _detailName = UiTheme.Label("", 22, SkillColor);
        titles.AddChild(_detailName);
        _detailKind = UiTheme.Label("", 12, UiTheme.TextDim);
        _detailKind.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailKind.CustomMinimumSize = new Vector2(270f, 0f);
        titles.AddChild(_detailKind);
        _detailFavoured = Wrapped(right, 13, UiTheme.TextDim);
        _detailText = Wrapped(right, 14, UiTheme.Text);
        _detailNumbers = Wrapped(right, 13, UiTheme.TextDim);
        _detailFits = Wrapped(right, 13, UiTheme.TextDim);
        _detailUnlock = Wrapped(right, 14, UiTheme.Text);
        right.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        _buy = UiTheme.PrimaryButton("Buy", () => Buy(), 20, 300f);
        _buy.FocusMode = FocusModeEnum.None;
        right.AddChild(_buy);
        _reason = Wrapped(right, 14, new Color(1f, 0.55f, 0.4f));
        _openSkills = UiTheme.Button("Open skills (K)", () => OpenSkills?.Invoke(), 15, 300f);
        _openSkills.FocusMode = FocusModeEnum.None;
        _openSkills.Visible = false;
        right.AddChild(_openSkills);

        _footer = UiTheme.Label("", 14, UiTheme.Gold);
        AddChild(_footer);
    }

    private static Label Wrapped(Control parent, int size, Color color)
    {
        Label l = UiTheme.Label("", size, color);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(330f, 0f);
        parent.AddChild(l);
        return l;
    }

    /// <summary>Called when the tab is shown: remembers what was new, marks it seen, selects the first row.</summary>
    public void OnShown(bool firstSinceOpen)
    {
        if (CharacterSource() is { } c)
        {
            if (firstSinceOpen)
            {
                _newSince = c.GemShopSeenLevel;
            }

            c.GemShopSeenLevel = c.Level;
        }

        _justBought = null;
        Rebuild();
    }

    public void SetKind(bool supports)
    {
        ShowingSupports = supports;
        Filter = "all";
        Selected = null;
        _justBought = null;
        Rebuild();
    }

    public void SetFilter(string id)
    {
        Filter = id;
        Rebuild();
    }

    public void SetOnlyBuyable(bool on)
    {
        OnlyBuyable = on;
        _only.SetPressedNoSignal(on);
        Rebuild();
    }

    public void Select(GemOffer offer)
    {
        if (Selected != offer)
        {
            _justBought = null;
        }

        Selected = offer;
        Rebuild();
        Callable.From(() => { if (IsInstanceValid(this) && Selected is { } s && _rows.TryGetValue(s, out Control? row) && IsInstanceValid(row)) { _scroll.EnsureControlVisible(row); } }).CallDeferred();
    }

    private bool Matches(CharacterData c, GemOffer o)
    {
        (string Id, string Label, SkillTags Tag)[] filters = ShowingSupports ? SupportFilters : SkillFilters;
        (string Id, string Label, SkillTags Tag) f = filters.FirstOrDefault(x => x.Id == Filter);
        if (f.Id is null || f.Id == "all")
        {
            return true;
        }

        if (f.Id == "fits")
        {
            return GemShop.FitsAny(c, o);
        }

        return (GemShop.TagsOf(o) & f.Tag) != 0;
    }

    public void Rebuild()
    {
        if (!IsNodeReady() || CharacterSource() is not { } c || Player is null)
        {
            return;
        }

        int skillCount = GemShop.Offers.Count(o => !o.Support);
        int supportCount = GemShop.Offers.Count(o => o.Support);
        _skillsKind.Text = $"Skills {skillCount}";
        _supportsKind.Text = $"Supports {supportCount}";
        StyleToggle(_skillsKind, !ShowingSupports);
        StyleToggle(_supportsKind, ShowingSupports);

        // Chips.
        foreach (Node n in _chips.GetChildren())
        {
            _chips.RemoveChild(n);
            n.QueueFree();
        }

        foreach ((string id, string label, SkillTags _) in ShowingSupports ? SupportFilters : SkillFilters)
        {
            string chipId = id;
            var chip = new Button { Text = label, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0f, 26f), Name = $"Chip_{id}" };
            chip.AddThemeFontSizeOverride("font_size", 12);
            bool on = Filter == id;
            chip.AddThemeStyleboxOverride("normal", UiTheme.Box(on ? new Color(0.24f, 0.18f, 0.32f) : new Color(0.1f, 0.085f, 0.14f), on ? UiTheme.Gold : UiTheme.PanelBorder, on ? 2 : 1, 4, 6));
            chip.AddThemeColorOverride("font_color", on ? UiTheme.Gold : UiTheme.TextDim);
            chip.Pressed += () => { Core.Sfx.Play(this, "ui", -6f); SetFilter(chipId); };
            _chips.AddChild(chip);
        }

        // Rows.
        foreach (Node n in _list.GetChildren())
        {
            _list.RemoveChild(n);
            n.QueueFree();
        }

        _rows.Clear();
        _visible.Clear();
        long gold = Player.Wallet.GetBalance(Profile.GoldId);
        foreach (GemOffer o in GemShop.Offers.Where(o => o.Support == ShowingSupports && Matches(c, o)))
        {
            if (OnlyBuyable && GemShop.CannotBuy(c, Player.Wallet, o) is not null)
            {
                continue;
            }

            _visible.Add(o);
        }

        if (Selected is null || !_visible.Contains(Selected))
        {
            Selected = _visible.FirstOrDefault();
        }

        foreach (GemOffer o in _visible)
        {
            Control row = BuildRow(c, o, gold);
            _rows[o] = row;
            _list.AddChild(row);
        }

        if (_visible.Count == 0)
        {
            Label none = UiTheme.Label(OnlyBuyable ? "Nothing here you can buy right now. Untick \"Only what I can buy\" to see the whole shelf." : "Nothing matches this filter.", 15, UiTheme.TextDim);
            none.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            none.CustomMinimumSize = new Vector2(560f, 0f);
            _list.AddChild(none);
        }

        RefreshDetail(c, gold);
        _footer.Text = $"Gold {gold:N0}  ·  Gems go to your skills (K) at level 1, not to your bag.";
    }

    private static void StyleToggle(Button b, bool on)
    {
        b.AddThemeStyleboxOverride("normal", UiTheme.Box(on ? new Color(0.2f, 0.15f, 0.3f) : new Color(0.08f, 0.07f, 0.11f), on ? UiTheme.Gold : UiTheme.PanelBorder, on ? 2 : 1, 4, 4));
        b.AddThemeColorOverride("font_color", on ? UiTheme.Gold : UiTheme.TextDim);
    }

    private static Texture2D? IconOf(GemOffer o) => o.Support
        ? GD.Load<Texture2D>("res://assets/sprites/icons/icon_support.png")
        : GemShop.SkillOf(o)?.Icon;

    private static string TagText(GemOffer o)
    {
        SkillTags t = GemShop.TagsOf(o);
        var names = new List<string>();
        foreach (SkillTags tag in Enum.GetValues<SkillTags>())
        {
            if (tag != SkillTags.None && (t & tag) == tag && tag != SkillTags.Triggered)
            {
                names.Add(tag.ToString());
            }
        }

        // Elements last, like the plan's tables.
        names = names.OrderBy(n => n is "Fire" or "Cold" or "Lightning" ? 1 : 0).ToList();
        return string.Join(" · ", names);
    }

    private Control BuildRow(CharacterData c, GemOffer o, long gold)
    {
        bool locked = GemShop.Locked(c, o);
        int owned = GemShop.Owned(c, o);
        bool selected = o == Selected;
        var row = new Button { CustomMinimumSize = new Vector2(0f, 52f), FocusMode = FocusModeEnum.None, Name = $"Row_{o.Id}", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeStyleboxOverride("normal", UiTheme.Box(selected ? new Color(0.2f, 0.15f, 0.3f) : new Color(0.08f, 0.07f, 0.11f), selected ? UiTheme.Gold : UiTheme.PanelBorder with { A = 0.5f }, selected ? 2 : 1, 4, 4));
        row.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(0.16f, 0.13f, 0.22f), UiTheme.Gold, 1, 4, 4));
        row.AddThemeStyleboxOverride("pressed", UiTheme.Box(new Color(0.2f, 0.15f, 0.3f), UiTheme.Gold, 2, 4, 4));
        if (locked)
        {
            row.Modulate = new Color(1f, 1f, 1f, 0.55f);
        }

        row.Pressed += () => { Core.Sfx.Play(this, "ui", -8f); Select(o); };

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 10);
        row.AddChild(line);
        line.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 6);
        TextureRect icon = UiTheme.PixelIcon(IconOf(o), 40f);
        icon.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        line.AddChild(icon);

        VBoxContainer text = UiTheme.VBox(line, 0);
        text.MouseFilter = MouseFilterEnum.Ignore;
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        HBoxContainer nameLine = UiTheme.HBox(text, 6);
        nameLine.MouseFilter = MouseFilterEnum.Ignore;
        nameLine.AddChild(Ignore(UiTheme.Label(GemShop.NameOf(o), 16, o.Support ? SupportColor : SkillColor)));
        if (!locked && owned == 0 && o.Level > _newSince)
        {
            nameLine.AddChild(Ignore(UiTheme.Label("NEW", 11, UiTheme.Ember)));
        }

        text.AddChild(Ignore(UiTheme.Label(TagText(o), 12, UiTheme.TextDim)));

        HBoxContainer state = UiTheme.HBox(line, 4);
        state.MouseFilter = MouseFilterEnum.Ignore;
        state.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        state.CustomMinimumSize = new Vector2(124f, 0f);
        state.Alignment = BoxContainer.AlignmentMode.End;
        string stateText;
        Color stateColor;
        if (locked)
        {
            state.AddChild(new LockGlyph { MouseFilter = MouseFilterEnum.Ignore });
            stateText = $"Level {o.Level}";
            stateColor = UiTheme.TextDim;
        }
        else if (!o.Support && owned > 0)
        {
            stateText = $"Owned · Lv {GemShop.OwnedLevel(c, o)}";
            stateColor = OwnedColor;
        }
        else if (o.Support && owned >= GemShop.MaxCopies(o))
        {
            stateText = $"{owned} / {GemShop.MaxCopies(o)} owned";
            stateColor = OwnedColor;
        }
        else
        {
            string price = $"{o.Price:N0} g";
            stateText = o.Support && owned > 0 ? $"{owned} / {GemShop.MaxCopies(o)} owned · {price}" : price;
            stateColor = gold >= o.Price ? UiTheme.Gold : UiTheme.Ember;
        }

        state.AddChild(Ignore(UiTheme.Label(stateText, 14, stateColor, HorizontalAlignment.Right)));
        return row;
    }

    private static Label Ignore(Label l)
    {
        l.MouseFilter = MouseFilterEnum.Ignore;
        return l;
    }

    /// <summary>The state text of a row (tests).</summary>
    public string RowStateText(string id)
    {
        if (GemShop.Offers.FirstOrDefault(o => o.Id == id && o.Support == ShowingSupports) is not { } offer || !_rows.TryGetValue(offer, out Control? row))
        {
            return "";
        }

        return string.Join(" ", row.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
    }

    public bool RowLocked(string id) =>
        GemShop.Offers.FirstOrDefault(o => o.Id == id && o.Support == ShowingSupports) is { } offer && _rows.TryGetValue(offer, out Control? row) && row.Modulate.A < 0.9f;

    private void RefreshDetail(CharacterData c, long gold)
    {
        if (Selected is not { } o)
        {
            _detailIcon.Texture = null;
            _detailName.Text = "";
            _detailKind.Text = "";
            foreach (Label l in new[] { _detailFavoured, _detailText, _detailNumbers, _detailFits, _detailUnlock, _reason })
            {
                l.Text = "";
            }

            _buy.Visible = false;
            _openSkills.Visible = false;
            return;
        }

        _buy.Visible = true;
        _detailIcon.Texture = IconOf(o);
        _detailName.Text = GemShop.NameOf(o);
        _detailName.AddThemeColorOverride("font_color", o.Support ? SupportColor : SkillColor);
        _detailKind.Text = (o.Support ? "Support gem" : "Skill gem") + (o.Support ? $" · fits {(TagText(o).Length > 0 ? TagText(o) : "any skill")}" : $" · {TagText(o)}");
        if (o.Support)
        {
            _detailFavoured.Text = "";
            _detailText.Text = GemShop.SupportOf(o)?.Description.Replace("\n", " ") ?? "";
            _detailNumbers.Text = "";
            string[] fits = GemShop.FitNames(c, o).ToArray();
            _detailFits.Text = fits.Length > 0 ? $"Fits your: {string.Join(", ", fits)}" : "None of your skills can use this yet";
            _detailFits.AddThemeColorOverride("font_color", fits.Length > 0 ? SupportColor : UiTheme.Ember);
        }
        else
        {
            // Skills belong to no class (owner, 2026-10-30): no "favoured by" line.
            _detailFavoured.Text = "";
            SkillDefinition? def = GemShop.SkillOf(o);
            _detailText.Text = def?.Description.Replace("\n", " ") ?? "";
            _detailNumbers.Text = def is null ? "" : $"Damage {def.BaseDamage:0.#} · use {def.BaseUseTime:0.00} s" + (def.Cooldown > 0f ? $" · cooldown {def.Cooldown:0.#} s" : "");
            _detailFits.Text = "";
        }

        _detailUnlock.Text = GemShop.Locked(c, o) ? $"Unlocks at character level {o.Level} · you are {c.Level}" : $"Unlocked at level {o.Level}";
        _buy.Text = $"Buy · {o.Price:N0} g";
        string? why = GemShop.CannotBuy(c, Player.Wallet, o);
        _buy.Disabled = why is not null;
        bool boughtNow = _justBought == o;
        if (boughtNow)
        {
            _reason.Text = $"Bought {GemShop.NameOf(o)}. It's in your skills (K) at level 1.";
            _reason.AddThemeColorOverride("font_color", SupportColor);
        }
        else
        {
            _reason.Text = why ?? "";
            _reason.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.4f));
        }

        _openSkills.Visible = boughtNow;
    }

    /// <summary>Buys the selected gem (the Buy button, Enter).</summary>
    public bool Buy()
    {
        if (Selected is not { } o || CharacterSource() is not { } c || Player is null)
        {
            return false;
        }

        if (!GemShop.TryBuy(c, Player.Wallet, o, out string message))
        {
            _justBought = null;
            Rebuild();
            return false;
        }

        _justBought = o;
        Core.Sfx.Play(this, "pickup", -4f);
        _ = message;
        Rebuild();
        return true;
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsVisibleInTree() || @event is not InputEventKey { Pressed: true, Echo: false } key || _visible.Count == 0)
        {
            return;
        }

        int step = key.Keycode switch { Key.Down => 1, Key.Up => -1, _ => 0 };
        if (step != 0)
        {
            int at = Mathf.Clamp(Selected is null ? 0 : _visible.IndexOf(Selected) + step, 0, _visible.Count - 1);
            Select(_visible[at]);
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode is Key.Enter or Key.KpEnter)
        {
            Buy();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>A small padlock for locked rows.</summary>
    private sealed partial class LockGlyph : Control
    {
        public LockGlyph() => CustomMinimumSize = new Vector2(12f, 16f);

        public override void _Draw()
        {
            Color c = UiTheme.TextDim;
            DrawRect(new Rect2(1f, 7f, 10f, 8f), c);
            DrawArc(new Vector2(6f, 7f), 3.5f, Mathf.Pi, Mathf.Tau, 10, c, 1.6f);
        }
    }
}
