#nullable enable
using System;
using Godot;
using ArpgGame.Core;

namespace ArpgGame.UI;

/// <summary>
/// Esc menu → Send feedback (docs/design/menus.md -> "Feedback"). Pick a kind (bug,
/// idea, feature request), write a title and a description, optionally include the
/// game info, and "Open on GitHub" opens the project's new-issue page with all of it
/// filled in and the right label applied by the issue form; the player only presses
/// Submit there. "Copy text" puts the same report on the clipboard.
/// </summary>
public partial class FeedbackPanel : PanelContainer
{
    public event Action? Closed;

    public FeedbackKind Kind { get; private set; } = FeedbackKind.Bug;

    /// <summary>The last URL opened (tests and the status line).</summary>
    public string LastUrl { get; private set; } = "";

    /// <summary>Tests set this so nothing really opens a browser.</summary>
    public Action<string> OpenUrl { get; set; } = url => OS.ShellOpen(url);

    private readonly Button[] _kinds = new Button[3];
    private LineEdit _title = null!;
    private TextEdit _description = null!;
    private Label _extraLabel = null!;
    private TextEdit _extra = null!;
    private CheckBox _includeInfo = null!;
    private Label _info = null!;
    private Button _send = null!;
    private Label _status = null!;

    private static readonly (FeedbackKind Kind, string Text, Color Color, string Hint)[] Kinds =
    {
        (FeedbackKind.Bug, "Bug", new Color(1f, 0.45f, 0.4f), "Something broken: what happened, and what did you expect?"),
        (FeedbackKind.Idea, "Idea", new Color(1f, 0.85f, 0.4f), "A thought about the game: balance, feel, art, anything."),
        (FeedbackKind.FeatureRequest, "Feature request", new Color(0.5f, 0.85f, 1f), "Something you'd like the game to have."),
    };

    public static FeedbackPanel Create(Control parent)
    {
        var panel = new FeedbackPanel { Theme = UiTheme.Theme, CustomMinimumSize = new Vector2(640f, 0f) };
        parent.AddChild(panel);
        return panel;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        VBoxContainer box = UiTheme.VBox(this, 10);
        box.AddChild(UiTheme.Label("Send feedback", 32, UiTheme.Gold, HorizontalAlignment.Center));

        HBoxContainer kinds = UiTheme.HBox(box, 6);
        kinds.Alignment = BoxContainer.AlignmentMode.Center;
        for (int i = 0; i < Kinds.Length; i++)
        {
            FeedbackKind k = Kinds[i].Kind;
            _kinds[i] = UiTheme.Button(Kinds[i].Text, () => SetKind(k), 16, 170f);
            kinds.AddChild(_kinds[i]);
        }

        _title = new LineEdit { PlaceholderText = "Title — one line", MaxLength = 120 };
        _title.TextChanged += _ => Validate();
        box.AddChild(_title);
        _description = new TextEdit { CustomMinimumSize = new Vector2(0f, 120f), WrapMode = TextEdit.LineWrappingMode.Boundary };
        _description.TextChanged += Validate;
        box.AddChild(_description);
        _extraLabel = UiTheme.Label("", 14, UiTheme.TextDim);
        box.AddChild(_extraLabel);
        _extra = new TextEdit { CustomMinimumSize = new Vector2(0f, 70f), WrapMode = TextEdit.LineWrappingMode.Boundary };
        box.AddChild(_extra);

        _includeInfo = new CheckBox { Text = "Include game info (version, system, where you are — nothing personal)", ButtonPressed = true };
        _includeInfo.Toggled += on => _info.Visible = on;
        box.AddChild(_includeInfo);
        _info = UiTheme.Label(Feedback.GameInfo(this), 12, UiTheme.TextDim);
        box.AddChild(_info);

        Label how = UiTheme.Label("Opens the project's GitHub in your browser with everything filled in and tagged — you only press Submit there (a free GitHub account is needed).", 13, UiTheme.TextDim);
        how.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        how.CustomMinimumSize = new Vector2(600f, 0f);
        box.AddChild(how);

        HBoxContainer buttons = UiTheme.HBox(box, 8);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        _send = UiTheme.Button("Open on GitHub", Send, 17, 200f);
        buttons.AddChild(_send);
        buttons.AddChild(UiTheme.Button("Copy text", Copy, 17, 140f));
        buttons.AddChild(UiTheme.Button("Back", Close, 17, 120f));
        _status = UiTheme.Label("", 13, UiTheme.Experience, HorizontalAlignment.Center);
        box.AddChild(_status);

        SetKind(FeedbackKind.Bug);
        UiTheme.FocusLater(_title);
    }

    public void SetKind(FeedbackKind kind)
    {
        Kind = kind;
        for (int i = 0; i < Kinds.Length; i++)
        {
            bool on = Kinds[i].Kind == kind;
            _kinds[i].AddThemeStyleboxOverride("normal", UiTheme.Box(on ? new Color(0.16f, 0.12f, 0.22f) : new Color(0.08f, 0.07f, 0.11f), on ? Kinds[i].Color : UiTheme.PanelBorder, on ? 2 : 1));
            _kinds[i].AddThemeColorOverride("font_color", on ? Kinds[i].Color : UiTheme.TextDim);
            if (on)
            {
                _description.PlaceholderText = Kinds[i].Hint;
            }
        }

        _extraLabel.Text = kind switch
        {
            FeedbackKind.Bug => "Steps to reproduce (optional)",
            FeedbackKind.FeatureRequest => "Why would it help? (optional)",
            _ => "",
        };
        _extraLabel.Visible = _extra.Visible = kind != FeedbackKind.Idea;
        Validate();
    }

    /// <summary>Fills the form (tests).</summary>
    public void Fill(string title, string description, string extra = "")
    {
        _title.Text = title;
        _description.Text = description;
        _extra.Text = extra;
        Validate();
    }

    private void Validate() => _send.Disabled = _title.Text.Trim().Length == 0 || _description.Text.Trim().Length == 0;

    private string Info => _includeInfo.ButtonPressed ? Feedback.GameInfo(this) : "";

    private string Extra => Kind == FeedbackKind.Idea ? "" : _extra.Text;

    public void Send()
    {
        if (_send.Disabled)
        {
            return;
        }

        LastUrl = Feedback.BuildUrl(Kind, _title.Text, _description.Text, Extra, Info);
        OpenUrl(LastUrl);
        _status.Text = "Opened in your browser — press Submit there. Thank you!";
    }

    private void Copy()
    {
        DisplayServer.ClipboardSet(Feedback.AsMarkdown(Kind, _title.Text, _description.Text, Extra, Info));
        _status.Text = "Copied to the clipboard.";
    }

    public void Close()
    {
        Closed?.Invoke();
        QueueFree();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }
}
