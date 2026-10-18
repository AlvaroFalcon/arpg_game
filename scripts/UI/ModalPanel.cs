#nullable enable
using System;
using Godot;
using ArpgGame.Core;
using ArpgGame.Quests;

namespace ArpgGame.UI;

/// <summary>
/// Base for the full-screen character panels (Mirror, skills, passive tree,
/// ascendancy) — docs/design/menus.md -> "Panels". Opening pauses the game (not in a
/// co-op party, where nothing pauses) and joins <see cref="PanelStack"/>; Escape or the close button closes it. The
/// panel itself polls Escape because the usual poller (the inventory) lives on
/// the player and is paused while a modal is up.
/// </summary>
public abstract partial class ModalPanel : CanvasLayer, IClosablePanel
{
    public event Action? Closed;

    public bool IsOpen { get; private set; }

    protected Control Root { get; private set; } = null!;
    protected VBoxContainer Body { get; private set; } = null!;
    protected Label TitleLabel { get; private set; } = null!;

    protected abstract string Title { get; }

    /// <summary>False for panels that draw their own header (the quest dialogue).</summary>
    protected virtual bool ShowHeader => true;

    /// <summary>The hideout station this panel belongs to ("forge"), or null. A station's panel shows
    /// its quest in a slim strip under the title (docs/design/quests.md -> "E prompt and station panels").</summary>
    public virtual string? StationUid => null;

    /// <summary>Size of the panel frame.</summary>
    protected virtual Vector2 FrameSize => new(980f, 620f);

    private PanelContainer _strip = null!;
    private Label _stripLabel = null!;
    private Profile? _questProfile;

    /// <summary>The quest strip is showing (tests, captures).</summary>
    public bool QuestStripVisible => _strip is { Visible: true };

    public string QuestStripText => _stripLabel?.Text ?? "";

    /// <summary>The strip says the quest is complete.</summary>
    public bool QuestStripDone { get; private set; }

    public override void _EnterTree()
    {
        _questProfile = Profile.Get(this);
        if (_questProfile is not null)
        {
            _questProfile.QuestChanged += OnQuestChanged;
        }
    }

    public override void _ExitTree()
    {
        if (_questProfile is not null && IsInstanceValid(_questProfile))
        {
            _questProfile.QuestChanged -= OnQuestChanged;
        }

        _questProfile = null;
    }

    private void OnQuestChanged(string id, int status)
    {
        if (IsOpen)
        {
            RefreshQuestStrip();
            OnQuestsChanged();
        }
    }

    /// <summary>A quest changed while this panel is open (the quest log redraws its list).</summary>
    protected virtual void OnQuestsChanged()
    {
    }

    public override void _Ready()
    {
        Layer = 20;
        ProcessMode = ProcessModeEnum.Always;
        Root = UiTheme.FullRect(this);
        UiTheme.Dim(Root, 0.75f);
        CenterContainer center = UiTheme.Center(Root);
        var frame = new PanelContainer { CustomMinimumSize = FrameSize };
        center.AddChild(frame);
        Body = UiTheme.VBox(frame, 10);
        HBoxContainer header = UiTheme.HBox(Body, 10);
        TitleLabel = UiTheme.Label(Title, 30, UiTheme.Gold);
        TitleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(TitleLabel);
        header.AddChild(UiTheme.Button("Close (Esc)", Close, 16, 140f));
        header.Visible = ShowHeader;
        _strip = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(0f, 28f), MouseFilter = Control.MouseFilterEnum.Ignore };
        Body.AddChild(_strip);
        _stripLabel = UiTheme.Label("", 14, UiTheme.Gold);
        _stripLabel.ClipText = true;
        _stripLabel.VerticalAlignment = VerticalAlignment.Center;
        _strip.AddChild(_stripLabel);
        SetQuestStrip(null, false);
        Build(Body);
        Root.Visible = false;
    }

    protected abstract void Build(VBoxContainer body);

    /// <summary>Re-reads the character and redraws (called on open and after every change).</summary>
    public virtual void Refresh()
    {
    }

    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;
        Root.Visible = true;
        // In a party nothing pauses (docs/design/multiplayer.md -> "Pausing"): the hideout
        // holds the local player still while a panel is open instead.
        GetTree().Paused = Net.NetSession.Get(this) is not { Active: true };
        PanelStack.Push(this);
        Refresh();
        RefreshQuestStrip();
    }

    /// <summary>
    /// The quest line under the title: gold on a dark gold band while the quest runs, green once
    /// its objective is met ("close the forge to claim your reward"). Null hides it.
    /// </summary>
    public void SetQuestStrip(string? text, bool done)
    {
        QuestStripDone = done && text is not null;
        _strip.Visible = text is not null;
        _stripLabel.Text = text ?? "";
        Color accent = done ? UiTheme.Experience : UiTheme.Gold;
        _stripLabel.AddThemeColorOverride("font_color", accent);
        var box = new StyleBoxFlat { BgColor = accent with { A = 0.12f }, BorderColor = accent with { A = 0.6f } };
        box.SetBorderWidthAll(0);
        box.BorderWidthLeft = 3;
        box.SetCornerRadiusAll(3);
        box.ContentMarginLeft = 10f;
        box.ContentMarginRight = 10f;
        _strip.AddThemeStyleboxOverride("panel", box);
    }

    /// <summary>Reads the station's quest from the book and shows or hides the strip.</summary>
    public void RefreshQuestStrip()
    {
        if (StationUid is not { } station || QuestService.FirstAt(this, station) is not { } quest)
        {
            SetQuestStrip(null, false);
            return;
        }

        if (QuestService.StatusOf(this, quest.Id) == QuestStatus.Ready)
        {
            SetQuestStrip($"Quest complete: close {CloseNoun(station)} to claim your reward", true);
        }
        else
        {
            SetQuestStrip($"Quest: {quest.Objective} · {quest.Hint}", false);
        }
    }

    private static string CloseNoun(string station) => station switch
    {
        "forge" => "the forge",
        "altar" => "the altar",
        "mirror" => "the Mirror",
        "merchant" => "Odran's stall",
        _ => "this window",
    };

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Root.Visible = false;
        PanelStack.Remove(this);
        GetTree().Paused = false;
        OnClosed();
        Closed?.Invoke();
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else if (!GetTree().Paused)
        {
            Open();
        }
    }

    protected virtual void OnClosed()
    {
    }

    public override void _Process(double delta)
    {
        if (IsOpen && Input.IsActionJustPressed("ui_cancel") && !PanelStack.ClosedThisFrame)
        {
            PanelStack.CloseTopmost();
        }
    }

    /// <summary>Removes and frees every child of <paramref name="node"/>.</summary>
    protected static void ClearChildren(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
