#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Core;
using ArpgGame.Items;
using ArpgGame.Quests;

namespace ArpgGame.UI;

/// <summary>
/// The quest toast — docs/design/quests.md -> "The quest toast". One card at a time on layer 15
/// (above the HUDs and the inventory, under modal panels): "NEW QUEST" with its station when one
/// unlocks, "QUEST COMPLETE" when its objective is met and "REWARD" when it is claimed. It slides
/// in, stays six seconds (longer while hovered) and fades; clicking it opens the quest log.
/// With a station's panel open there is no card for that station's quest: the panel's own quest
/// strip turns green (and the sound plays) where the player acted. Anything else that covers
/// the screen (the guide, another panel, the pause menu, a level-up) holds the card back until
/// it is gone.
/// </summary>
public partial class QuestToast : CanvasLayer
{
    public const float CardWidth = 360f;
    public const float CardHeight = 76f;
    public const float Stay = 6f;
    private const float SlideTime = 0.18f;
    private const float FadeTime = 0.4f;
    private const float Gap = 0.3f;

    public enum Kind
    {
        New,
        Done,
        Reward,
    }

    private sealed record Entry(Kind Kind, QuestDef Quest, string Line);

    /// <summary>Top-left of the card on the 1280×720 canvas; the host knows where its HUD leaves room.</summary>
    public Func<Vector2>? Anchor { get; set; }

    /// <summary>Opens the quest log on a quest (default: the log next to this node).</summary>
    public Action<string>? OpenLog { get; set; }

    /// <summary>The card on screen: its kind and quest title (tests).</summary>
    public (Kind Kind, string Title)? Showing => _card is null ? null : (_entry!.Kind, _entry.Quest.Title);

    /// <summary>The text lines of the card showing (tests, captures).</summary>
    public string CardLine => _lineLabel?.Text ?? "";

    /// <summary>A "quest complete" card for this quest is showing or waiting (tests).</summary>
    public bool AnnouncesDone(string questId) =>
        (_entry is { Kind: Kind.Done } e && e.Quest.Id == questId) || _queue.Any(q => q.Kind == Kind.Done && q.Quest.Id == questId);

    /// <summary>Cards waiting their turn.</summary>
    public int Queued => _queue.Count;

    private readonly Queue<Entry> _queue = new();
    private Profile? _profile;
    private PanelContainer? _card;
    private Entry? _entry;
    private Label? _lineLabel;
    private float _age;
    private float _gap;
    private bool _hover;

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

    public override void _Ready()
    {
        Layer = 15;
        ProcessMode = ProcessModeEnum.Always;
    }

    private void OnChanged(string id, int status)
    {
        if (QuestCatalog.Get(id) is not { } quest)
        {
            return;
        }

        switch ((QuestStatus)status)
        {
            case QuestStatus.Ready when InsideStationPanel(quest):
                // The objective was met inside the station's own panel: its strip says so there.
                return;
            case QuestStatus.New:
                // No card for a new quest (owner, 2026-10-29): it appears in the tracker and the
                // quest log (J), with the quest_new sound QuestService plays.
                return;
            case QuestStatus.Ready:
                _queue.Enqueue(new Entry(Kind.Done, quest, $"Turn it in at {HideoutWorldName(quest)}"));
                break;
            case QuestStatus.Done:
                string reward = QuestService.LastClaim is { } last && last.Id == id ? last.Text : "";
                _queue.Enqueue(new Entry(Kind.Reward, quest, reward));
                break;
            default:
                return;
        }

        TryShow();
    }

    /// <summary>The station's own panel is the topmost one open.</summary>
    private static bool InsideStationPanel(QuestDef quest) =>
        PanelStack.Topmost is ModalPanel { IsOpen: true } panel && panel.StationUid == quest.Station;

    /// <summary>A card still tells the truth: the quest is still in the state it announces.</summary>
    private bool Current(Entry entry)
    {
        QuestStatus now = QuestService.StatusOf(this, entry.Quest.Id);
        return entry.Kind switch
        {
            Kind.New => now == QuestStatus.New,
            Kind.Done => now == QuestStatus.Ready,
            _ => true,
        };
    }

    private static string HideoutWorldName(QuestDef q) => World.HideoutCatalog.Prop(q.Station)?.Name ?? q.Speaker;

    /// <summary>Whether something that covers the screen is up (so a card would be hidden or in the way).</summary>
    private bool Covered => GetTree().Paused || PanelStack.HasOpen || LevelUpScreen.BlocksInput;

    private void TryShow()
    {
        if (_card is not null || _gap > 0f || !IsInsideTree())
        {
            return;
        }

        while (_queue.Count > 0)
        {
            Entry next = _queue.Peek();
            if (!Current(next))
            {
                _queue.Dequeue();
                continue;
            }

            if (Covered)
            {
                return;
            }

            Show(_queue.Dequeue());
            return;
        }
    }

    private void Show(Entry entry)
    {
        _entry = entry;
        _age = 0f;
        _hover = false;
        _card = new PanelContainer { CustomMinimumSize = new Vector2(CardWidth, CardHeight), Size = new Vector2(CardWidth, CardHeight), MouseFilter = Control.MouseFilterEnum.Stop };
        _card.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        var box = new StyleBoxFlat { BgColor = new Color(0.05f, 0.04f, 0.08f, 0.92f), BorderColor = entry.Kind == Kind.Done ? UiTheme.Experience : UiTheme.Gold };
        box.SetBorderWidthAll(1);
        box.BorderWidthLeft = 4;
        box.SetCornerRadiusAll(6);
        box.SetContentMarginAll(10f);
        box.ShadowColor = new Color(0f, 0f, 0f, 0.4f);
        box.ShadowSize = 8;
        _card.AddThemeStyleboxOverride("panel", box);
        _card.MouseEntered += () => _hover = true;
        _card.MouseExited += () => _hover = false;
        _card.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                Open(entry.Quest.Id);
                _card?.AcceptEvent();
            }
        };

        HBoxContainer row = UiTheme.HBox(_card, 10);
        row.AddChild(new Disc { Kind = entry.Kind });
        VBoxContainer text = UiTheme.VBox(row, 0);
        text.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        text.AddChild(UiTheme.Label(entry.Kind switch { Kind.New => "NEW QUEST", Kind.Done => "QUEST COMPLETE", _ => "REWARD" }, 12, entry.Kind == Kind.Done ? UiTheme.Experience : UiTheme.Gold));
        Label title = UiTheme.Label(entry.Quest.Title, 17, UiTheme.Text);
        title.ClipText = true;
        title.CustomMinimumSize = new Vector2(CardWidth - 90f, 0f);
        text.AddChild(title);
        // A long reward line wraps rather than being cut: the card grows by a line.
        _lineLabel = UiTheme.Label(entry.Line, 13, UiTheme.TextDim);
        _lineLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _lineLabel.CustomMinimumSize = new Vector2(CardWidth - 90f, 0f);
        text.AddChild(_lineLabel);
        AddChild(_card);
        Place();
    }

    private void Open(string id)
    {
        if (OpenLog is not null)
        {
            OpenLog(id);
        }
        else
        {
            GetParent()?.GetNodeOrNull<CharacterPanels>("CharacterPanels")?.Quests.OpenOn(id);
        }
    }

    private void Place()
    {
        if (_card is null)
        {
            return;
        }

        Vector2 at = Anchor?.Invoke() ?? new Vector2(14f, 170f);
        float slide = Mathf.Clamp(_age / SlideTime, 0f, 1f);
        slide = 1f - (1f - slide) * (1f - slide);
        _card.Position = at + new Vector2(-(CardWidth + 30f) * (1f - slide), 0f);
        float remaining = Stay - _age;
        _card.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(remaining / FadeTime, 0f, 1f));
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_card is null)
        {
            if (_gap > 0f)
            {
                _gap -= dt;
            }

            if (_queue.Count > 0)
            {
                TryShow();
            }

            return;
        }

        if (!_hover)
        {
            _age += dt;
        }

        Place();
        if (_age >= Stay)
        {
            _card.QueueFree();
            _card = null;
            _entry = null;
            _lineLabel = null;
            _gap = Gap;
        }
    }

    /// <summary>The glyph disc: a gold "!" for a new quest, a green check mark for a done one, the coin for a reward.</summary>
    private sealed partial class Disc : Control
    {
        public Kind Kind;

        public override void _Ready()
        {
            CustomMinimumSize = new Vector2(36f, 36f);
            SizeFlagsVertical = SizeFlags.ShrinkCenter;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            var c = new Vector2(18f, 18f);
            switch (Kind)
            {
                case Kind.New:
                    QuestGlyphs.DrawMarker(this, c, 17f, QuestMarker.New, 24f);
                    break;
                case Kind.Done:
                    DrawCircle(c, 17f, QuestGlyphs.Disc);
                    DrawArc(c, 16f, 0f, Mathf.Tau, 32, UiTheme.Experience, 2f, true);
                    QuestGlyphs.DrawCheck(this, new Rect2(c - new Vector2(10f, 10f), new Vector2(20f, 20f)), UiTheme.Experience, 3f);
                    break;
                default:
                    DrawCircle(c, 17f, QuestGlyphs.Disc);
                    DrawArc(c, 16f, 0f, Mathf.Tau, 32, UiTheme.Gold, 2f, true);
                    if (ItemArt.ForCurrency("gold") is { } coin)
                    {
                        DrawTextureRect(coin, new Rect2(c - new Vector2(12f, 12f), new Vector2(24f, 24f)), false);
                    }

                    break;
            }
        }
    }
}
