#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Core;
using ArpgGame.Progression;
using ArpgGame.Run;

namespace ArpgGame.UI;

/// <summary>
/// docs/design/menus.md -> "Level-up": pauses the run and offers three cards
/// (lesser keystones at 5, 15, 25…, great ones at 10, 20, 30… —
/// <see cref="OfferRules"/>). Queued level-ups are offered back to back. Two
/// rerolls per run, each replacing one card. Also hosts the run-start skill pick
/// (<see cref="RunBuild.RollStartOffers"/>), the same card row.
///
/// While open it owns the keyboard (owner feedback: a stray key closed the
/// cards): every key is swallowed in <see cref="_Input"/>, before the GUI and
/// gameplay see it, and only 1-9 (pick) and Shift+1-9 (reroll that card) do
/// anything — and only once the cards are armed, <see cref="ArmDelay"/> after
/// they appear, so a key or click already on its way when they pop up is not
/// taken as a choice. R alone only points at Shift+number.
///
/// Co-op (docs/design/multiplayer.md -> "Level-ups") never opens on its own: the
/// host runs <b>rounds</b> (the run-start skill pick, then one per party level) and
/// tells every machine through <see cref="OpenPartyRound"/> / <see cref="SetPartyStatus"/>
/// / <see cref="ClosePartyRound"/>. The whole game is paused by the host meanwhile
/// (not by this screen). A choice is not applied here: it is raised as
/// <see cref="PartyChosen"/>, the host applies it (and on a client tells it back),
/// and the screen shows the party's "1/2 chosen" line and the host's countdown —
/// red from <see cref="Net.PartyRules.LevelUpUrgentSeconds"/>, a tick every second.
/// </summary>
public partial class LevelUpScreen : CanvasLayer
{
    public RunState State { get; set; } = null!;
    public RunBuild Build { get; set; } = null!;

    /// <summary>True while this screen owns the pause.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>True while a level-up screen is open anywhere: things that poll
    /// keys directly (dodge, flasks, the inventory key) stand down (tests run the
    /// cards over an unpaused tree; co-op keeps the screen up while waiting).</summary>
    public static bool BlocksInput => _blocks || CashOutPanel.Blocks;

    private static bool _blocks;

    /// <summary>Co-op: the screen is driven by the host's rounds (see the class summary).</summary>
    public bool Coop { get; set; }

    /// <summary>Offline the open screen pauses the scene tree. Tests that drive the
    /// cards from a node that must keep running turn this off.</summary>
    public bool PauseTree { get; set; } = true;

    /// <summary>Raised when the cards open or close.</summary>
    public event System.Action<bool>? OpenChanged;

    /// <summary>Raised after a card is applied (offline).</summary>
    public event System.Action<UpgradeOffer>? Picked;

    /// <summary>Co-op: the local player chose <c>offer</c> in round <c>round</c> (not applied yet).</summary>
    public event System.Action<int, UpgradeOffer>? PartyChosen;

    /// <summary>Co-op: the row on screen changed (opened or one card rerolled): the
    /// host keeps it, so that its timeout takes a card this player was really shown.</summary>
    public event System.Action<int, IReadOnlyList<UpgradeOffer>>? PartyOffersChanged;

    /// <summary>Seconds after the cards appear before keys or clicks pick one.</summary>
    public const float ArmDelay = 0.45f;

    private string _hintText = "";

    // --- co-op round state ------------------------------------------------------------
    private int _partyRound;
    private bool _partyChosen;
    private ulong _partyDeadlineMsec;
    private int _partyTotal;
    private int _lastShownSecond = -1;
    private Control _partyBar = null!;
    private Label _partyCount = null!;
    private Label _partyClock = null!;
    private Label _waiting = null!;

    /// <summary>Co-op: the host round shown (0 = none).</summary>
    public int PartyRound => _partyRound;

    /// <summary>Co-op: the round shown is the run-start skill pick.</summary>
    public bool PartyRoundIsStart { get; private set; }

    /// <summary>Co-op: the local player has chosen in this round (waiting for the others).</summary>
    public bool PartyChosenThisRound => _partyRound != 0 && _partyChosen;

    /// <summary>Co-op: the "1/2 chosen" text on screen (tests, captures).</summary>
    public string PartyCountText => _partyCount.Text;

    /// <summary>Co-op: whole seconds left on the countdown shown (-1 when none).</summary>
    public int PartySecondsShown => _partyRound == 0 ? -1 : _lastShownSecond;

    /// <summary>Co-op: true while the countdown is drawn red.</summary>
    public bool PartyClockUrgent => _partyRound != 0 && _lastShownSecond <= Net.PartyRules.LevelUpUrgentSeconds;

    /// <summary>Co-op: countdown ticks played this run (tests).</summary>
    public int TicksPlayed { get; private set; }

    /// <summary>The red of the last seconds.</summary>
    public static readonly Color Urgent = new(1f, 0.32f, 0.28f);

    private Control _root = null!;
    private Label _title = null!;
    private Label _hint = null!;
    private UpgradeCards _cards = null!;
    private Label _rerollInfo = null!;
    private Label _buildSummary = null!;
    private readonly RandomNumberGenerator _rng = new();
    private int _offeringLevel;
    private bool _startPick;
    private System.Action? _onStartPicked;

    public override void _Ready()
    {
        Layer = 20;
        ProcessMode = ProcessModeEnum.Always;
        _rng.Randomize();
        _root = UiTheme.FullRect(this);
        UiTheme.Dim(_root, 0.72f);
        CenterContainer center = UiTheme.Center(_root);
        VBoxContainer box = UiTheme.VBox(center, 18);
        BuildPartyBar(box);
        _title = UiTheme.Label("", 40, UiTheme.Gold, HorizontalAlignment.Center);
        box.AddChild(_title);
        _hint = UiTheme.Label("", 18, UiTheme.TextDim, HorizontalAlignment.Center);
        box.AddChild(_hint);
        _cards = new UpgradeCards();
        box.AddChild(_cards);
        _cards.Chosen += OnChosen;
        _cards.RerollRequested += Reroll;
        _waiting = UiTheme.Label("", 22, UiTheme.Text, HorizontalAlignment.Center);
        _waiting.CustomMinimumSize = new Vector2(820f, 120f);
        _waiting.VerticalAlignment = VerticalAlignment.Center;
        _waiting.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _waiting.Visible = false;
        box.AddChild(_waiting);
        _rerollInfo = UiTheme.Label("", 15, UiTheme.TextDim, HorizontalAlignment.Center);
        box.AddChild(_rerollInfo);
        _buildSummary = UiTheme.Label("", 15, UiTheme.TextDim, HorizontalAlignment.Center);
        _buildSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _buildSummary.CustomMinimumSize = new Vector2(820f, 0f);
        box.AddChild(_buildSummary);
        _root.Visible = false;

        State.LevelUpReady += _ => TryOpen();
        State.RunEnded += _ => ClosePartyRound();
    }

    /// <summary>Co-op: "1 / 2 chosen" and the countdown, at the top of the screen.</summary>
    private void BuildPartyBar(VBoxContainer box)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, Name = "PartyBar" };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, UiTheme.PanelBorder, 2, 8, 12));
        box.AddChild(panel);
        HBoxContainer row = UiTheme.HBox(panel, 28);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        _partyCount = UiTheme.Label("", 26, UiTheme.Text, HorizontalAlignment.Center);
        _partyCount.Name = "PartyCount";
        row.AddChild(_partyCount);
        _partyClock = UiTheme.Label("", 30, UiTheme.Gold, HorizontalAlignment.Center);
        _partyClock.Name = "PartyClock";
        _partyClock.CustomMinimumSize = new Vector2(90f, 0f);
        row.AddChild(_partyClock);
        _partyBar = panel;
        _partyBar.Visible = false;
    }

    /// <summary>The run-start pick (docs/design/progression.md -> "The start pick"):
    /// class skills for a skill-less character, otherwise skill cards for the
    /// skills it brought. Rerolls work here too (same two charges).</summary>
    public void OpenStartPick(System.Action onPicked)
    {
        _startPick = true;
        _onStartPicked = onPicked;
        SetOpen(true);
        ShowStart();
    }

    private void ShowStart()
    {
        _offeringLevel = 0;
        bool skillLess = !Build.Skills.Any();
        _title.Text = skillLess ? "Choose your first skill" : "Start of the run — choose a skill card";
        SetHint(skillLess
            ? (Features.SupportGems ? "Your build grows from here: every level adds a skill, a support or a power-up." : "Your build grows from here: every level adds a skill or a power-up.")
            : "Your skills start at level 1 here. Learn a new one or level one up.  (1 / 2 / 3)");
        _buildSummary.Text = skillLess ? "" : BuildSummary(Build);
        ShowRow(true);
        _cards.SetOffers(Build.RollStartOffers(_rng));
        RefreshReroll();
        Arm();
        _root.Visible = true;
    }

    /// <summary>Starts the arming delay for a fresh set of cards.</summary>
    private void Arm()
    {
        _cards.ArmedAtMsec = Time.GetTicksMsec() + (ulong)(ArmDelay * 1000f);
        _cards.Modulate = new Color(1f, 1f, 1f, 0.55f);
    }

    private void TryOpen()
    {
        // Co-op: only the host's rounds open the cards.
        if (Coop || IsOpen || State.PendingLevelUps <= 0 || State.Ended)
        {
            return;
        }

        SetOpen(true);
        ShowNext();
    }

    private void ShowNext() => ShowLevel(State.Level - State.PendingLevelUps + 1);

    private void ShowLevel(int level)
    {
        _offeringLevel = level;
        List<UpgradeOffer> offers = Build.RollOffers(_offeringLevel, _rng);
        KeystoneTier? tier = offers.Count > 0 && offers.All(o => o.Kind == UpgradeKind.Keystone) ? OfferRules.KeystoneTierAt(_offeringLevel) : null;
        _title.Text = tier switch
        {
            KeystoneTier.Great => $"Level {_offeringLevel} — choose a Great Keystone",
            KeystoneTier.Lesser => $"Level {_offeringLevel} — choose a Lesser Keystone",
            _ => $"Level {_offeringLevel}",
        };
        SetHint(tier switch
        {
            KeystoneTier.Great => "Great keystones change how your whole build works. Choose carefully.",
            KeystoneTier.Lesser => "A lesser keystone: a smaller trade that bends one part of your build.",
            _ => "Choose an upgrade  (1 / 2 / 3)",
        });
        _buildSummary.Text = BuildSummary(Build);
        ShowRow(true);
        _cards.SetOffers(offers);
        RefreshReroll();
        Arm();
        _root.Visible = true;
        Core.Sfx.Play(this, "levelup");
    }

    /// <summary>Picks a card programmatically (autoplay bot, capture tool).</summary>
    public void ChooseIndex(int index) => _cards.ChooseIndex(Mathf.Clamp(index, 0, Mathf.Max(0, _cards.OfferCount - 1)));

    private void RefreshReroll()
    {
        int left = Build.RerollsLeft;
        _rerollInfo.Text = left > 0
            ? $"Rerolls: {left} left this run — replaces one card  (Shift + its number, or the button under it)"
            : "No rerolls left this run";
        _cards.SetRerollState(left > 0, left > 0 ? "Replace this card only" : "No rerolls left this run");
    }

    /// <summary>Rerolls one card (docs/design/progression.md -> "Rerolls"). Public for
    /// tools and tests; returns true if the card changed.</summary>
    public bool RerollCard(int index)
    {
        if (!IsOpen || index < 0 || index >= _cards.OfferCount)
        {
            return false;
        }

        if (Build.TryReroll(_offeringLevel, _cards.Offers, index, _rng) is not { } offer)
        {
            if (Build.RerollsLeft > 0)
            {
                _rerollInfo.Text = "Nothing else to offer for that card.";
            }

            return false;
        }

        _cards.ReplaceOffer(index, offer);
        RefreshReroll();
        Arm();
        if (_partyRound != 0)
        {
            PartyOffersChanged?.Invoke(_partyRound, _cards.Offers);
        }

        return true;
    }

    private void Reroll(int index) => RerollCard(index);

    /// <summary>True once the cards accept keys and clicks (<see cref="ArmDelay"/>).</summary>
    public bool Armed => _cards.Armed;

    /// <summary>The offers on screen (tests, tools).</summary>
    public IReadOnlyList<UpgradeOffer> Offers => _cards.Offers;

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen || @event is not InputEventKey key)
        {
            return;
        }

        // Nothing else hears the keyboard while the cards are up: no dodge, no
        // panels, no pause, and no Space/Enter "pressing" a button.
        GetViewport().SetInputAsHandled();
        if (!key.Pressed || key.Echo || !_cards.Armed)
        {
            return;
        }

        if (key.PhysicalKeycode == Key.R)
        {
            // The old "reroll everything" key: rerolls are per card now.
            _rerollInfo.Text = Build.RerollsLeft > 0 ? "To reroll one card, press Shift + its number (or the button under it)." : "No rerolls left this run";
        }
        else if (key.PhysicalKeycode is >= Key.Key1 and <= Key.Key9)
        {
            int index = (int)(key.PhysicalKeycode - Key.Key1);
            if (index >= _cards.OfferCount)
            {
                return;
            }

            if (key.ShiftPressed)
            {
                RerollCard(index);
            }
            else
            {
                _cards.ChooseIndex(index);
            }
        }
    }

    private void OnChosen(UpgradeOffer offer)
    {
        if (_partyRound != 0)
        {
            // Co-op: the host applies it (and tells a client back); wait for the others.
            _partyChosen = true;
            ShowWaiting($"You chose {offer.Title}.");
            PartyChosen?.Invoke(_partyRound, offer);
            return;
        }

        offer.Apply();
        Picked?.Invoke(offer);
        if (_startPick)
        {
            _startPick = false;
            Close();
            _onStartPicked?.Invoke();
            TryOpen(); // a level gained while the start pick was open (co-op) is next
            return;
        }

        State.ConsumeLevelUp();
        if (State.PendingLevelUps > 0)
        {
            ShowNext();
            return;
        }

        Close();
    }

    private void Close()
    {
        _root.Visible = false;
        SetOpen(false);
    }

    private void SetOpen(bool open)
    {
        IsOpen = open;
        _blocks = open;
        if (!Coop && PauseTree)
        {
            GetTree().Paused = open;
            if (!open)
            {
                State.Player?.Health?.GrantInvulnerability(Net.PartyRules.ResumeGraceSeconds);
            }
        }

        OpenChanged?.Invoke(open);
    }

    public override void _ExitTree() => _blocks = false;

    private void SetHint(string text)
    {
        _hintText = text;
        _hint.Text = text;
    }

    public override void _Process(double delta)
    {
        if (IsOpen && _cards.Modulate.A < 1f && _cards.Armed)
        {
            _cards.Modulate = Colors.White;
        }

        if (_partyRound != 0)
        {
            UpdatePartyClock();
        }
    }

    // --- co-op rounds ---------------------------------------------------------------------

    /// <summary>
    /// Co-op: the host opened round <paramref name="round"/> — the run-start skill
    /// pick (<paramref name="start"/>) or the cards for party level
    /// <paramref name="level"/>. Rolls this player's own row (and reports it,
    /// <see cref="PartyOffersChanged"/>). <paramref name="remaining"/> counts this
    /// round and those queued behind it (a burst of levels: rounds back to back).
    /// </summary>
    public void OpenPartyRound(int round, bool start, int level, int remaining)
    {
        _partyRound = round;
        PartyRoundIsStart = start;
        _partyChosen = false;
        _lastShownSecond = -1;
        _partyDeadlineMsec = 0;
        if (!IsOpen)
        {
            SetOpen(true);
        }

        if (start)
        {
            ShowStart();
        }
        else
        {
            ShowLevel(level);
            if (remaining > 1)
            {
                _title.Text += $"   ({remaining - 1} more after this)";
            }
        }

        SetHint(_hintText + "\nWhen the time runs out, the first card is taken for you.");
        _partyBar.Visible = true;
        if (start)
        {
            Core.Sfx.Play(this, "levelup");
        }

        PartyOffersChanged?.Invoke(round, _cards.Offers);
    }

    /// <summary>Co-op: who has chosen and when the host's time runs out (local msec).</summary>
    public void SetPartyStatus(int picked, int total, ulong deadlineMsec, bool mineChosen)
    {
        if (_partyRound == 0)
        {
            return;
        }

        _partyTotal = Mathf.Max(1, total);
        _partyDeadlineMsec = deadlineMsec;
        _partyCount.Text = $"{Mathf.Clamp(picked, 0, _partyTotal)} / {_partyTotal} chosen";
        if (mineChosen && !_partyChosen)
        {
            // The host took a card for this player (the timer) before a choice reached it.
            _partyChosen = true;
            ShowWaiting("Time's up — a card was taken for you.");
        }

        UpdatePartyClock();
    }

    /// <summary>Co-op: the host applied <paramref name="title"/> for this player.</summary>
    public void ShowPartyApplied(string title, bool forced)
    {
        if (_partyRound == 0)
        {
            return;
        }

        _partyChosen = true;
        ShowWaiting(forced ? $"Time's up — {title} was taken for you." : $"You chose {title}.");
    }

    /// <summary>Co-op: the round is over (everyone chose, the timer ran out, or the run ended).</summary>
    public void ClosePartyRound()
    {
        if (_partyRound == 0)
        {
            return;
        }

        _partyRound = 0;
        _partyChosen = false;
        _partyBar.Visible = false;
        Close();
    }

    /// <summary>Co-op timeout for this machine's player (the host's own): the first card.</summary>
    public UpgradeOffer? FirstOffer => _cards.OfferCount > 0 ? _cards.Offers[0] : null;

    private void ShowWaiting(string chosen)
    {
        // The line takes the row's place at the row's height, so nothing jumps.
        float rowHeight = _cards.Size.Y + (_rerollInfo.Visible ? _rerollInfo.Size.Y + 18f : 0f);
        if (_cards.Visible && rowHeight > 0f)
        {
            _waiting.CustomMinimumSize = new Vector2(820f, rowHeight);
        }

        ShowRow(false);
        _hint.Text = "The game resumes when everyone has chosen.";
        _waiting.Text = $"{chosen}\nWaiting for the others…";
    }

    /// <summary>The card row, or (co-op, after choosing) the waiting line.</summary>
    private void ShowRow(bool cards)
    {
        _cards.Visible = cards;
        _rerollInfo.Visible = cards;
        _waiting.Visible = !cards;
        if (cards)
        {
            _waiting.Text = "";
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
        if (_lastShownSecond >= 0 && seconds >= 0 && seconds < _lastShownSecond)
        {
            // One tick per second of the countdown, sharper in the red — only for a
            // player still choosing (owner decision: silent once you've picked).
            if (!_partyChosen)
            {
                TicksPlayed++;
                Core.Sfx.Play(this, urgent ? "tick_urgent" : "tick");
            }
        }

        _lastShownSecond = seconds;
        _partyClock.Text = seconds < 0 ? "" : $"{seconds} s";
        _partyClock.AddThemeColorOverride("font_color", urgent ? Urgent : UiTheme.Gold);
    }

    /// <summary>One-paragraph description of the build so far.</summary>
    public static string BuildSummary(RunBuild build)
    {
        var parts = new List<string>();
        foreach (Skills.SkillInstance skill in build.Skills)
        {
            string supports = string.Join(", ", skill.Attached.OfType<SupportGem>().Select(g => g.Definition.Name));
            parts.Add(supports.Length > 0 ? $"{skill.Definition.DisplayName} Lv{skill.Level} [{supports}]" : $"{skill.Definition.DisplayName} Lv{skill.Level}");
        }

        string text = string.Join("   ·   ", parts);
        if (build.Keystones.Count > 0)
        {
            text += "\nKeystones: " + string.Join(", ", build.Keystones.Select(id => UpgradeCatalog.Keystones.First(k => k.Id == id).Name));
        }

        int passives = build.PassiveRanks.Values.Sum();
        if (passives > 0)
        {
            text += $"\n{passives} passive{(passives == 1 ? "" : "s")} allocated";
        }

        return text;
    }
}
