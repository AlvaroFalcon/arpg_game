#nullable enable
using System;
using System.Linq;
using Godot;
using ArpgGame.Net;
using ArpgGame.World;

namespace ArpgGame.UI;

/// <summary>
/// The rift proposal popup — docs/design/menus.md -> "Party: proposals". A card
/// at the top of the hideout over a game that keeps running:
/// <list type="bullet">
/// <item><b>Asked</b>: "Ana is opening the Survival Rift", what it is, and
/// [Join] / [Stay in the hideout] with the 30 s countdown (red from 5 s, a tick
/// every second). The host's decline reads "Decline (cancels it for everyone)".</item>
/// <item><b>Waiting</b> (the proposer, or after answering): "Waiting for the party
/// — 1/3 joined · 0:24", and [Cancel] for the proposer.</item>
/// <item><b>Confirm</b>: a yes/no question before proposing (walking into the portal).</item>
/// </list>
/// Buttons take no keyboard focus: Space dodges in the hideout, and must never
/// press Join by accident.
/// </summary>
public partial class RiftProposalPopup : CanvasLayer
{
    public NetSession? Session { get; set; }

    public bool Showing => _card.Visible;

    public enum Mode
    {
        Hidden,
        Asked,
        Waiting,
        Confirm,
    }

    public Mode Current { get; private set; } = Mode.Hidden;

    public string TitleText => _title.Text;
    public string InfoText => _info.Text;
    public string StatusText => _status.Text;
    public string YesText => _yes.Text;
    public string NoText => _no.Text;

    /// <summary>The whole seconds the countdown shows, and whether it is red (tests).</summary>
    public int SecondsShown { get; private set; }
    public bool Urgent { get; private set; }
    public int TicksPlayed { get; private set; }

    private PanelContainer _card = null!;
    private StyleBoxFlat _style = null!;
    private Label _title = null!;
    private Label _info = null!;
    private Label _status = null!;
    private Label _clock = null!;
    private Button _yes = null!;
    private Button _no = null!;
    private Action? _confirmYes;
    private int _lastTick = -1;

    public override void _Ready()
    {
        Layer = 40;
        ProcessMode = ProcessModeEnum.Always;
        Control root = UiTheme.FullRect(this);
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        _card = new PanelContainer { Name = "ProposalCard", CustomMinimumSize = new Vector2(560f, 0f) };
        _style = UiTheme.Box(new Color(0.08f, 0.065f, 0.12f, 0.96f), UiTheme.Gold, 2, 8, 16);
        _card.AddThemeStyleboxOverride("panel", _style);
        root.AddChild(_card);
        _card.SetAnchorsPreset(Control.LayoutPreset.CenterTop, true);
        _card.GrowHorizontal = Control.GrowDirection.Both;
        _card.OffsetTop = 104f;
        VBoxContainer box = UiTheme.VBox(_card, 8);
        HBoxContainer head = UiTheme.HBox(box, 12);
        _title = UiTheme.Label("", 22, UiTheme.Gold);
        _title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _title.CustomMinimumSize = new Vector2(420f, 0f);
        head.AddChild(_title);
        _clock = UiTheme.Label("", 26, UiTheme.Text, HorizontalAlignment.Right);
        _clock.CustomMinimumSize = new Vector2(80f, 0f);
        head.AddChild(_clock);
        _info = UiTheme.Label("", 15, UiTheme.Text);
        _info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _info.CustomMinimumSize = new Vector2(520f, 0f);
        box.AddChild(_info);
        _status = UiTheme.Label("", 15, UiTheme.TextDim);
        box.AddChild(_status);
        HBoxContainer buttons = UiTheme.HBox(box, 12);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        _yes = UiTheme.PrimaryButton("Join", PressYes, 18, 200f);
        _no = UiTheme.Button("Stay in the hideout", PressNo, 18, 260f);
        foreach (Button b in new[] { _yes, _no })
        {
            b.FocusMode = Control.FocusModeEnum.None;
            buttons.AddChild(b);
        }

        _card.Visible = false;
        if (Session is not null)
        {
            Session.ProposalChanged += Refresh;
            Session.ProposalClosed += OnClosed;
        }

        Refresh();
    }

    public override void _ExitTree()
    {
        if (Session is not null)
        {
            Session.ProposalChanged -= Refresh;
            Session.ProposalClosed -= OnClosed;
        }
    }

    // --- actions (public for tests and captures) -------------------------------------------

    /// <summary>Asks before proposing (the portal): <paramref name="yes"/> runs on the first button.</summary>
    public void Confirm(string title, string body, string yesText, Action yes)
    {
        if (Session?.OpenProposal is not null)
        {
            return; // a proposal owns the card
        }

        _confirmYes = yes;
        Current = Mode.Confirm;
        _title.Text = title;
        _info.Text = body;
        _status.Text = "";
        _clock.Text = "";
        _yes.Text = yesText;
        _no.Text = "Not now";
        _yes.Visible = _no.Visible = true;
        Accent(UiTheme.Gold);
        _card.Visible = true;
    }

    public void PressYes()
    {
        if (Current == Mode.Confirm)
        {
            Action? yes = _confirmYes;
            HideCard();
            yes?.Invoke();
            return;
        }

        if (Current == Mode.Asked)
        {
            Session?.AnswerProposal(true);
        }
    }

    public void PressNo()
    {
        switch (Current)
        {
            case Mode.Confirm:
                HideCard();
                break;
            case Mode.Asked:
                Session?.AnswerProposal(false);
                break;
            case Mode.Waiting when Session?.OpenProposal is { } view && view.Proposer == Session.LocalPeerId:
                Session.CancelProposal();
                break;
        }
    }

    private void HideCard()
    {
        _confirmYes = null;
        Current = Mode.Hidden;
        _card.Visible = false;
    }

    // --- state ------------------------------------------------------------------------------

    private void OnClosed(ProposalView view, bool started, string reason) => Refresh();

    private void Refresh()
    {
        if (Session?.OpenProposal is not { } view)
        {
            if (Current != Mode.Confirm)
            {
                HideCard();
            }

            return;
        }

        bool mine = view.Proposer == Session.LocalPeerId;
        bool host = Session.IsHost;
        Current = view.Mine is null && !mine ? Mode.Asked : Mode.Waiting;
        _confirmYes = null;
        _title.Text = mine ? $"You are opening {view.Spec.Describe()}" : $"{view.ProposerName} is opening {view.Spec.Describe()}";
        _info.Text = Describe(view);
        if (Current == Mode.Asked)
        {
            _yes.Text = "Join";
            _no.Text = host ? "Decline (cancels it for everyone)" : "Stay in the hideout";
            _status.Text = host
                ? "You are the host: the rift runs on your game, so it only opens if you join."
                : "Not answering in time counts as staying.";
            _yes.Visible = _no.Visible = true;
            Accent(UiTheme.Gold);
        }
        else
        {
            _status.Text = $"Waiting for the party — {view.Accepted}/{view.Asked} joined{(view.Declined > 0 ? $" · {view.Declined} staying" : "")}";
            _yes.Visible = false;
            _no.Text = "Cancel";
            _no.Visible = mine;
            Accent(UiTheme.PanelBorder);
        }

        _card.Visible = true;
        UpdateClock(view);
    }

    /// <summary>What the rift is, in one or two lines.</summary>
    private string Describe(ProposalView view)
    {
        int players = Math.Max(2, view.Accepted + view.Pending);
        string party = $"Monsters get tougher with each player (life ×{PartyRules.MonsterLife(players):0.0} for {players})";
        if (view.Spec.Kind == RiftKind.Reality)
        {
            int tier = view.Spec.Tier;
            string mods = view.Spec.Modifiers.Count == 0 ? "not empowered"
                : string.Join(", ", view.Spec.Modifiers.Select(m => RealityModifier.Find(m)?.Text ?? m));
            return $"Monster level {RealityTiers.MonsterLevel(tier)} · {RealityTiers.Portals(tier)} portals · {mods}\n{party}";
        }

        return $"The endless arena — survive, break the Ember Colossus, take the reward home.\n{party}";
    }

    private void Accent(Color colour)
    {
        _style.BorderColor = colour;
    }

    public override void _Process(double delta)
    {
        if (Current is Mode.Asked or Mode.Waiting && Session?.OpenProposal is { } view)
        {
            UpdateClock(view);
        }
    }

    private void UpdateClock(ProposalView view)
    {
        double left = view.SecondsLeft;
        int shown = (int)Math.Ceiling(left);
        SecondsShown = shown;
        Urgent = shown <= PartyRules.ProposalUrgentSeconds;
        _clock.Text = $"0:{shown:00}";
        _clock.AddThemeColorOverride("font_color", Urgent ? UiTheme.Life : UiTheme.Text);
        // A tick each second in the red, only for someone who still has to answer.
        if (Urgent && Current == Mode.Asked && shown != _lastTick && shown > 0)
        {
            _lastTick = shown;
            TicksPlayed++;
            Core.Sfx.Play(this, shown <= 2 ? "tick_urgent" : "tick");
        }
    }
}
