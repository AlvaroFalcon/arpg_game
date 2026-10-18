#nullable enable
using System;
using Godot;
using ArpgGame.Run;

namespace ArpgGame.UI;

/// <summary>
/// The Rift's cash-out panel — docs/design/run.md -> "Cash-out offer and the infinite Rift".
/// Two cards of equal weight: <b>[1] Take it and leave</b> with the reward as it stands, and
/// <b>[2] Keep going</b> (the endless phase, a better reward later). Three ways to open it:
/// <list type="bullet">
/// <item><see cref="OpenOffer"/>: the Colossus fell (solo; the tree pauses).</item>
/// <item><see cref="OpenLeave"/>: leaving later (portal, Esc → Leave with reward). The second
/// card reads <b>Not yet</b> and Esc means Not yet.</item>
/// <item><see cref="OpenPartyOffer"/>: the co-op vote, a host round like the level-up cards
/// (RiftHost.Rounds): "1 / 3 chosen" and a countdown; the host decides what the votes mean.</item>
/// </list>
/// It owns the keyboard while open (the level-up cards' rule): every key is swallowed, only 1 and
/// 2 (and Esc in leave mode) do anything, and only <see cref="ArmDelay"/> after the cards appear,
/// so a dodge pressed as the boss died can't pick. Numbers come from
/// <see cref="RunRewards.Preview"/>, so the run ends with exactly what the panel showed.
/// </summary>
public partial class CashOutPanel : CanvasLayer
{
    public enum PanelMode
    {
        Offer,
        Leave,
        Party,
    }

    public RunState State { get; set; } = null!;

    /// <summary>True while any cash-out panel is open: dodge, flasks and the inventory key stand down.</summary>
    public static bool Blocks { get; private set; }

    public bool IsOpen { get; private set; }
    public PanelMode Mode { get; private set; }

    /// <summary>Seconds after the cards appear before keys or clicks choose.</summary>
    public const float ArmDelay = LevelUpScreen.ArmDelay;

    /// <summary>Raised when the panel opens or closes.</summary>
    public event Action<bool>? OpenChanged;

    /// <summary>Co-op: the local player voted in round <c>round</c> (true = leave); the host decides.</summary>
    public event Action<int, bool>? PartyChosen;

    public bool Armed => Time.GetTicksMsec() >= _armedAt;

    /// <summary>The reward the panel showed last (tests).</summary>
    public RunRewards Shown { get; private set; } = RunRewards.Preview(0f, 0, 0, false);

    /// <summary>The run numbers <see cref="Shown"/> was computed from (tests).</summary>
    public string ShownInputs { get; private set; } = "";

    public string TitleText => _title.Text;
    public string IntroText => _intro.Text;
    public string TakeLabel => _takeTitle.Text;
    public string StayLabel => _stayTitle.Text;

    /// <summary>Co-op: the "1 / 3 chosen" text on screen (tests, captures).</summary>
    public string PartyCountText => _partyCount.Text;

    /// <summary>Co-op: whole seconds left on the countdown (-1 when none).</summary>
    public int PartySecondsShown => Mode == PanelMode.Party ? _lastShownSecond : -1;

    public bool PartyClockUrgent => Mode == PanelMode.Party && _lastShownSecond >= 0 && _lastShownSecond <= Net.PartyRules.LevelUpUrgentSeconds;

    /// <summary>Co-op: the round shown (0 = none).</summary>
    public int PartyRound => _partyRound;

    public bool PartyChosenThisRound => _partyRound != 0 && _partyChosen;

    /// <summary>Co-op: countdown ticks played (tests).</summary>
    public int TicksPlayed { get; private set; }

    private Control _root = null!;
    private Label _title = null!;
    private Label _intro = null!;
    private Label _footer = null!;
    private Button _takeCard = null!;
    private Button _stayCard = null!;
    private Label _takeTitle = null!;
    private Label _stayTitle = null!;
    private VBoxContainer _takeBody = null!;
    private VBoxContainer _stayBody = null!;
    private Label _note = null!;
    private Control _partyBar = null!;
    private Label _partyCount = null!;
    private Label _partyClock = null!;
    private ulong _armedAt;
    private Action? _onTake;
    private Action? _onStay;
    private string _takeText = "Take it and leave";
    private int _partyRound;
    private bool _partyChosen;
    private ulong _partyDeadlineMsec;
    private int _lastShownSecond = -1;
    private float _refreshIn;
    private bool _pausedByUs;

    public override void _Ready()
    {
        Layer = 22;
        ProcessMode = ProcessModeEnum.Always;
        _root = UiTheme.FullRect(this);
        UiTheme.Dim(_root, 0.7f);
        CenterContainer center = UiTheme.Center(_root);
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(760f, 430f) };
        center.AddChild(frame);
        VBoxContainer box = UiTheme.VBox(frame, 10);
        BuildPartyBar(box);
        _title = UiTheme.Label("", 34, UiTheme.Gold, HorizontalAlignment.Center);
        box.AddChild(_title);
        _intro = UiTheme.Label("", 15, UiTheme.Text, HorizontalAlignment.Center);
        _intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _intro.CustomMinimumSize = new Vector2(720f, 0f);
        box.AddChild(_intro);
        HBoxContainer cards = UiTheme.HBox(box, 20);
        cards.Alignment = BoxContainer.AlignmentMode.Center;
        (_takeCard, _takeTitle, _takeBody) = BuildCard(cards, "1", UiTheme.Gold, () => TryChoose(true));
        (_stayCard, _stayTitle, _stayBody) = BuildCard(cards, "2", UiTheme.Ember, () => TryChoose(false));
        _note = UiTheme.Label("", 15, UiTheme.Text, HorizontalAlignment.Center);
        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _note.CustomMinimumSize = new Vector2(720f, 0f);
        box.AddChild(_note);
        _footer = UiTheme.Label("", 13, UiTheme.TextDim, HorizontalAlignment.Center);
        box.AddChild(_footer);
        _root.Visible = false;
        State.RunEnded += _ => ForceClose();
    }

    private void BuildPartyBar(VBoxContainer box)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, Name = "PartyBar" };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, UiTheme.PanelBorder, 2, 8, 10));
        box.AddChild(panel);
        HBoxContainer row = UiTheme.HBox(panel, 28);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        _partyCount = UiTheme.Label("", 22, UiTheme.Text, HorizontalAlignment.Center);
        row.AddChild(_partyCount);
        _partyClock = UiTheme.Label("", 26, UiTheme.Gold, HorizontalAlignment.Center);
        _partyClock.CustomMinimumSize = new Vector2(80f, 0f);
        row.AddChild(_partyClock);
        _partyBar = panel;
        _partyBar.Visible = false;
    }

    private (Button Card, Label Title, VBoxContainer Body) BuildCard(Control parent, string key, Color accent, Action onPressed)
    {
        var card = new Button { CustomMinimumSize = new Vector2(350f, 250f), FocusMode = Control.FocusModeEnum.None, ClipContents = true };
        card.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.Panel, accent with { A = 0.6f }, 2, 8, 14));
        card.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(0.16f, 0.13f, 0.22f), accent, 3, 8, 14));
        card.AddThemeStyleboxOverride("pressed", UiTheme.Box(new Color(0.22f, 0.18f, 0.3f), accent, 3, 8, 14));
        card.AddThemeStyleboxOverride("disabled", UiTheme.Box(UiTheme.Panel with { A = 0.6f }, accent with { A = 0.25f }, 2, 8, 14));
        card.Pressed += onPressed;
        parent.AddChild(card);
        var body = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        body.AddThemeConstantOverride("separation", 6);
        card.AddChild(body);
        body.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        body.OffsetLeft = 16f;
        body.OffsetRight = -16f;
        body.OffsetTop = 12f;
        body.OffsetBottom = -12f;
        HBoxContainer head = UiTheme.HBox(body, 8);
        head.MouseFilter = Control.MouseFilterEnum.Ignore;
        PanelContainer cap = UiTheme.KeyCap(key, 15, 26f);
        head.AddChild(cap);
        Label title = UiTheme.Label("", 22, accent);
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(title);
        var lines = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        lines.AddThemeConstantOverride("separation", 6);
        body.AddChild(lines);
        return (card, title, lines);
    }

    // --- opening ----------------------------------------------------------------------------

    /// <summary>The Colossus fell (solo): pauses the tree until a card is chosen.</summary>
    public void OpenOffer(Action onTake, Action onStay)
    {
        _onTake = onTake;
        _onStay = onStay;
        _takeText = "Take it and leave";
        Show(PanelMode.Offer, "The Colossus falls",
            "Take your reward home now, or stay: the Rift keeps getting harder, and what it pays keeps growing.", "Keep going", pause: true);
    }

    /// <summary>Leaving later: the same panel with the reward as it is now. <paramref name="pause"/>
    /// is false in co-op, where nothing can pause.</summary>
    public void OpenLeave(string title, string intro, string takeText, Action onTake, bool pause)
    {
        _onTake = onTake;
        _onStay = null;
        _takeText = takeText;
        Show(PanelMode.Leave, title, intro, "Not yet", pause);
    }

    /// <summary>Co-op: the host's cash-out round (the host paused everyone).</summary>
    public void OpenPartyOffer(int round)
    {
        _partyRound = round;
        _partyChosen = false;
        _lastShownSecond = -1;
        _partyDeadlineMsec = 0;
        _onTake = null;
        _onStay = null;
        _takeText = "Take it and leave";
        Show(PanelMode.Party, "The Colossus falls",
            "The party leaves only if everyone takes the reward. Nobody answering counts as leaving.", "Keep going", pause: false);
        _partyBar.Visible = true;
        _partyCount.Text = "";
        _partyClock.Text = "";
    }

    private void Show(PanelMode mode, string title, string intro, string stayText, bool pause)
    {
        Mode = mode;
        _title.Text = title;
        _intro.Text = intro;
        _stayTitle.Text = stayText;
        _takeTitle.Text = _takeText;
        _note.Text = "";
        _partyBar.Visible = mode == PanelMode.Party;
        _takeCard.Disabled = false;
        _stayCard.Disabled = false;
        _armedAt = Time.GetTicksMsec() + (ulong)(ArmDelay * 1000f);
        _takeCard.Modulate = _stayCard.Modulate = new Color(1f, 1f, 1f, 0.55f);
        Rebuild();
        _root.Visible = true;
        IsOpen = true;
        Blocks = true;
        _pausedByUs = pause;
        if (pause)
        {
            GetTree().Paused = true;
        }

        Core.Sfx.Play(this, "ui");
        OpenChanged?.Invoke(true);
    }

    /// <summary>The cards' contents from the reward as it stands.</summary>
    private void Rebuild()
    {
        RunRewards r = RunRewards.Preview(State.Elapsed, State.BossesKilled, State.Kills, true);
        Shown = r;
        ShownInputs = $"t={State.Elapsed:0.000} bosses={State.BossesKilled} kills={State.Kills}";
        foreach (VBoxContainer lines in new[] { _takeBody, _stayBody })
        {
            foreach (Node child in lines.GetChildren())
            {
                lines.RemoveChild(child);
                child.QueueFree();
            }
        }

        _takeBody.AddChild(Line(Items.ItemArt.ForCurrency("gold"), $"{r.Gold:N0} gold", 17, UiTheme.Gold));
        _takeBody.AddChild(Line(Items.ItemArt.ForCurrency("tempest_orb"), r.OrbCount == 1 ? "1 crafting orb" : $"{r.OrbCount} crafting orbs", 16, UiTheme.Text));
        _takeBody.AddChild(Line(Icon("icon_keystone"), r.ItemsEarned ? $"{r.Choices} item cards, keep {r.Keep}" : "No item cards yet", 16, UiTheme.Text));
        _takeBody.AddChild(Line(Icon("icon_passive"), $"item level {r.ItemLevel} · rarity ×{r.RarityMultiplier:0.0}", 16, UiTheme.Text));
        if (r.GuaranteedRare > 0)
        {
            _takeBody.AddChild(Plain($"{r.GuaranteedRare} {(r.GuaranteedRare == 1 ? "card" : "cards")} guaranteed Rare or better", 14, new Color(1f, 0.95f, 0.5f)));
        }

        if (r.BonusAscendant > 0)
        {
            _takeBody.AddChild(Plain($"+{r.BonusAscendant} Ascendant {(r.BonusAscendant == 1 ? "Orb" : "Orbs")}", 14, new Color(0.9f, 0.9f, 1f)));
        }

        string next = r.NextTierText;
        int colon = next.IndexOf(": ", System.StringComparison.Ordinal);
        _stayBody.AddChild(Plain(next.Length > 0 ? "Next: " + (colon > 0 ? next[..colon].Replace("Tier", "tier") : next) : "", 16, UiTheme.Ember));
        if (colon > 0)
        {
            _stayBody.AddChild(Plain("   " + next[(colon + 2)..], 14, UiTheme.TextDim));
        }

        _stayBody.AddChild(Plain("Every minute: more gold, orbs, item level and rarity", 14, UiTheme.Text));
        _stayBody.AddChild(Plain("A boss every 2 minutes", 14, UiTheme.Text));
        _stayBody.AddChild(Plain("Leave any time: the portal or Esc", 14, UiTheme.Text));
        _stayBody.AddChild(Plain("If you fall, you keep the reward you had.", 14, UiTheme.Text));
        _footer.Text = $"Survived {UiTheme.FormatTime(State.Elapsed)} · {State.BossesKilled} {(State.BossesKilled == 1 ? "boss" : "bosses")} · {State.Kills} kills · Reward tier {RunRewards.Roman(Mathf.Max(1, r.Tier))}";
    }

    private static Texture2D? Icon(string name) => GD.Load<Texture2D>($"res://assets/sprites/icons/{name}.png");

    private static Control Line(Texture2D? icon, string text, int size, Color color)
    {
        HBoxContainer row = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        TextureRect pic = UiTheme.PixelIcon(icon, 24f);
        pic.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(pic);
        Label label = UiTheme.Label(text, size, color);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(label);
        return row;
    }

    private static Label Plain(string text, int size, Color color)
    {
        Label l = UiTheme.Label(text, size, color);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(300f, 0f);
        l.MouseFilter = Control.MouseFilterEnum.Ignore;
        return l;
    }

    // --- choosing ----------------------------------------------------------------------------

    /// <summary>A card chosen by key or click; ignored until <see cref="Armed"/>. Returns true if it counted.</summary>
    public bool TryChoose(bool take)
    {
        if (!IsOpen || !Armed)
        {
            return false;
        }

        if (Mode == PanelMode.Party)
        {
            if (_partyChosen)
            {
                return false;
            }

            _partyChosen = true;
            _takeCard.Disabled = true;
            _stayCard.Disabled = true;
            (take ? _takeCard : _stayCard).Modulate = Colors.White;
            (take ? _stayCard : _takeCard).Modulate = new Color(1f, 1f, 1f, 0.45f);
            _note.Text = take
                ? "You chose to leave. The party goes home only if everyone does."
                : "You chose to keep going. Waiting for the others…";
            PartyChosen?.Invoke(_partyRound, take);
            return true;
        }

        Action? next = take ? _onTake : _onStay;
        Close();
        if (!take)
        {
            // Back in the action: nobody is hit in the frame it restarts.
            State.Player?.Health?.GrantInvulnerability(Net.PartyRules.ResumeGraceSeconds);
        }

        next?.Invoke();
        return true;
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen || @event is not InputEventKey key)
        {
            return;
        }

        // Nothing else hears the keyboard while the cards are up.
        GetViewport().SetInputAsHandled();
        if (!key.Pressed || key.Echo || !Armed)
        {
            return;
        }

        if (key.PhysicalKeycode == Key.Key1)
        {
            TryChoose(true);
        }
        else if (key.PhysicalKeycode == Key.Key2)
        {
            TryChoose(false);
        }
        else if (key.PhysicalKeycode == Key.Escape && Mode == PanelMode.Leave)
        {
            PanelStack.MarkClosedThisFrame();
            TryChoose(false);
        }
    }

    /// <summary>Closes without choosing (the run ended under it).</summary>
    private void ForceClose()
    {
        if (IsOpen)
        {
            Close(unpause: false);
        }

        _partyRound = 0;
    }

    private void Close(bool unpause = true)
    {
        _root.Visible = false;
        _partyBar.Visible = false;
        IsOpen = false;
        Blocks = false;
        if (_pausedByUs && unpause)
        {
            GetTree().Paused = false;
        }

        _pausedByUs = false;
        OpenChanged?.Invoke(false);
    }

    public override void _ExitTree() => Blocks = false;

    public override void _Process(double delta)
    {
        if (!IsOpen)
        {
            return;
        }

        if (_takeCard.Modulate.A < 1f && Armed && !_partyChosen)
        {
            _takeCard.Modulate = _stayCard.Modulate = Colors.White;
        }

        if (Mode == PanelMode.Party)
        {
            UpdatePartyClock();
        }
        else
        {
            // Leave mode over a running co-op game: the numbers move.
            _refreshIn -= (float)delta;
            if (_refreshIn <= 0f && !GetTree().Paused)
            {
                _refreshIn = 0.25f;
                Rebuild();
            }
        }
    }

    // --- co-op round -------------------------------------------------------------------------

    /// <summary>Co-op: who has voted and when the host's time runs out (local msec).</summary>
    public void SetPartyStatus(int picked, int total, ulong deadlineMsec, bool mineChosen)
    {
        if (_partyRound == 0 || Mode != PanelMode.Party)
        {
            return;
        }

        int count = Mathf.Max(1, total);
        _partyDeadlineMsec = deadlineMsec;
        _partyCount.Text = $"{Mathf.Clamp(picked, 0, count)} / {count} chosen";
        if (mineChosen && !_partyChosen)
        {
            // The host's timer answered for this player ("leave").
            _partyChosen = true;
            _takeCard.Disabled = true;
            _stayCard.Disabled = true;
            _note.Text = "Time's up: that counts as leaving. The party goes home only if everyone does.";
        }

        UpdatePartyClock();
    }

    /// <summary>The host's own timeout: answers for the local player as "leave".</summary>
    public void AnswerLeaveForTimeout()
    {
        if (Mode != PanelMode.Party || _partyChosen || _partyRound == 0)
        {
            return;
        }

        _partyChosen = true;
        _takeCard.Disabled = true;
        _stayCard.Disabled = true;
        _note.Text = "Time's up: that counts as leaving. The party goes home only if everyone does.";
        PartyChosen?.Invoke(_partyRound, true);
    }

    /// <summary>Co-op: the round is over.</summary>
    public void ClosePartyRound()
    {
        if (_partyRound == 0)
        {
            return;
        }

        _partyRound = 0;
        _partyChosen = false;
        if (IsOpen)
        {
            Close(unpause: false);
        }
    }

    private void UpdatePartyClock()
    {
        ulong now = Time.GetTicksMsec();
        float left = _partyDeadlineMsec > now ? (_partyDeadlineMsec - now) / 1000f : 0f;
        int seconds = _partyDeadlineMsec == 0 ? -1 : Mathf.CeilToInt(left);
        if (seconds == _lastShownSecond)
        {
            return;
        }

        bool urgent = seconds >= 0 && seconds <= Net.PartyRules.LevelUpUrgentSeconds;
        if (_lastShownSecond >= 0 && seconds >= 0 && seconds < _lastShownSecond && !_partyChosen)
        {
            TicksPlayed++;
            Core.Sfx.Play(this, urgent ? "tick_urgent" : "tick");
        }

        _lastShownSecond = seconds;
        _partyClock.Text = seconds < 0 ? "" : $"{seconds} s";
        _partyClock.AddThemeColorOverride("font_color", urgent ? LevelUpScreen.Urgent : UiTheme.Gold);
    }
}
