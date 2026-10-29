#nullable enable
using System;
using Godot;
using ArpgGame.Items;
using ArpgGame.Quests;
using ArpgGame.World;

namespace ArpgGame.UI;

/// <summary>
/// The quest dialogue — docs/design/quests.md -> "The quest dialogue". Opens when you press F
/// at a station that has something to say: the explanation of a new quest (primary: open the
/// station on the tab that teaches it) or, with the objective met, the turn-in (primary:
/// claim the reward). Enter or, after a short guard so the E that opened it cannot also
/// confirm it, E presses the primary button; Esc is "Later".
/// </summary>
public partial class QuestDialog : ModalPanel
{
    /// <summary>The E that opened the dialogue is ignored for this long.</summary>
    public const double KeyGuardSeconds = 0.35;

    protected override string Title => "Quest";

    protected override bool ShowHeader => false;

    // 640 wide; the height is whatever the text needs (a turn-in is short, an explanation fills the screen).
    protected override Vector2 FrameSize => new(640f, 0f);

    /// <summary>The quest shown (null when closed).</summary>
    public QuestDef? Quest { get; private set; }

    /// <summary>Turn-in mode (objective met) rather than the explanation of a new quest.</summary>
    public bool TurnInMode { get; private set; }

    /// <summary>The primary button's text, for tests and captures.</summary>
    public string PrimaryText { get; private set; } = "";

    private Action? _primary;
    private ulong _openedAt;
    private VBoxContainer _content = null!;
    private bool _acting;

    protected override void Build(VBoxContainer body)
    {
        _content = UiTheme.VBox(body, 10);
        _content.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
    }

    /// <summary>
    /// Shows <paramref name="quest"/>; <paramref name="primary"/> runs when the primary button
    /// is pressed (after the dialogue has closed itself).
    /// </summary>
    public void Show(QuestDef quest, bool turnIn, QuestContext? ctx, Action primary)
    {
        Quest = quest;
        TurnInMode = turnIn;
        _primary = primary;
        _acting = false;
        _context = ctx;
        if (IsOpen)
        {
            Refresh();
        }
        else
        {
            Open();
        }

        _openedAt = Time.GetTicksMsec();
    }

    private QuestContext? _context;

    /// <summary>Presses the primary button (Enter, E after the guard, or a click).</summary>
    public void PressPrimary()
    {
        if (!IsOpen || _acting)
        {
            return;
        }

        _acting = true;
        Action? action = _primary;
        Close();
        action?.Invoke();
    }

    public override void Refresh()
    {
        ClearChildren(_content);
        if (Quest is not { } q)
        {
            return;
        }

        Color accent = HideoutCatalog.Prop(q.Station)?.Accent ?? UiTheme.Gold;

        HBoxContainer head = UiTheme.HBox(_content, 14);
        head.AddChild(Portrait(q));
        VBoxContainer titles = UiTheme.VBox(head, 2);
        titles.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        titles.AddChild(UiTheme.Label(TurnInMode ? "QUEST COMPLETE" : "NEW QUEST", 12, TurnInMode ? UiTheme.Experience : UiTheme.Gold));
        titles.AddChild(UiTheme.Label(q.Title, 26, UiTheme.Gold));
        titles.AddChild(UiTheme.Label(HideoutCatalog.Prop(q.Station)?.Name ?? q.Speaker, 14, accent));

        _content.AddChild(new HSeparator());

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(600f, TurnInMode ? 70f : 270f), SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _content.AddChild(scroll);
        string text = TurnInMode ? q.TurnIn : q.Explain(_context ?? new QuestContext { Character = Characters.CharacterData.Create("", "warrior") });
        var rich = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            CustomMinimumSize = new Vector2(580f, 0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Text = QuestGlyphs.Rich(text),
        };
        rich.AddThemeFontSizeOverride("normal_font_size", 15);
        rich.AddThemeConstantOverride("line_separation", 2);
        scroll.AddChild(rich);

        if (!TurnInMode)
        {
            HBoxContainer objective = UiTheme.HBox(_content, 12);
            Label caps = UiTheme.Label("OBJECTIVE", 12, UiTheme.TextDim);
            caps.CustomMinimumSize = new Vector2(78f, 0f);
            objective.AddChild(caps);
            VBoxContainer lines = UiTheme.VBox(objective, 0);
            lines.AddChild(UiTheme.Label(q.Objective, 15, UiTheme.Text));
            lines.AddChild(UiTheme.Label(q.Hint, 13, UiTheme.TextDim));
        }

        HBoxContainer reward = UiTheme.HBox(_content, 12);
        Label rewardCaps = UiTheme.Label("REWARD", 12, UiTheme.TextDim);
        rewardCaps.CustomMinimumSize = new Vector2(78f, 0f);
        rewardCaps.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        reward.AddChild(rewardCaps);
        QuestRewardRow.Build(reward, q, _context, TurnInMode ? 32f : 24f, TurnInMode ? 18 : 16);

        HBoxContainer buttons = UiTheme.HBox(_content, 10);
        buttons.Alignment = BoxContainer.AlignmentMode.End;
        PrimaryText = TurnInMode ? "Claim reward" : q.OpenText;
        Button later = UiTheme.Button("Later (Esc)", Close, 16, 140f);
        later.FocusMode = Control.FocusModeEnum.None;
        buttons.AddChild(later);
        Button primary = UiTheme.PrimaryButton(PrimaryText, PressPrimary, 16, 260f);
        primary.FocusMode = Control.FocusModeEnum.None;
        buttons.AddChild(primary);
    }

    private static Control Portrait(QuestDef q)
    {
        switch (q.Station)
        {
            case "merchant":
                return UiTheme.Portrait("paladin", 72f);
            case "forge":
                return UiTheme.Portrait("sage", 72f);
        }

        Texture2D? tex = q.Station == "arehucaz"
            ? ItemArt.ForProp("props/arehucaz")
            : ResourceLoader.Exists($"res://assets/ui/mapicons/map_{q.Station}.png") ? GD.Load<Texture2D>($"res://assets/ui/mapicons/map_{q.Station}.png") : null;
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(72f, 72f) };
        frame.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.1f, 0.08f, 0.14f), HideoutCatalog.Prop(q.Station)?.Accent ?? UiTheme.Gold, 2, 4, 2));
        frame.AddChild(UiTheme.PixelIcon(tex, 64f));
        return frame;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!IsOpen)
        {
            return;
        }

        bool guarded = Time.GetTicksMsec() - _openedAt < (ulong)(KeyGuardSeconds * 1000.0);
        if (Input.IsActionJustPressed("ui_accept") || (!guarded && Input.IsActionJustPressed("interact")))
        {
            PressPrimary();
        }
    }

    protected override void OnClosed()
    {
        _primary = null;
    }
}
