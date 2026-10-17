#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
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
/// </summary>
public partial class LevelUpScreen : CanvasLayer
{
    public RunState State { get; set; } = null!;
    public RunBuild Build { get; set; } = null!;

    /// <summary>True while this screen owns the pause.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>True while a level-up screen is open anywhere: things that poll
    /// keys directly (dodge, flasks, the inventory key) stand down. Matters in
    /// co-op, where the run is not paused under the cards.</summary>
    public static bool BlocksInput { get; private set; }

    /// <summary>
    /// Co-op (docs/design/multiplayer.md -> "Level-ups"): the run never pauses.
    /// The chooser is warded while the cards are open (<see cref="OpenChanged"/>)
    /// and the first card is taken for them after <see cref="Net.PartyRules.LevelUpTimeout"/>.
    /// </summary>
    public bool Coop { get; set; }

    /// <summary>Raised when the cards open or close (co-op: ward the player).</summary>
    public event System.Action<bool>? OpenChanged;

    /// <summary>Raised after a card is applied (co-op client: tell the host).</summary>
    public event System.Action<UpgradeOffer>? Picked;

    /// <summary>Seconds after the cards appear before keys or clicks pick one.</summary>
    public const float ArmDelay = 0.45f;

    private float _timeLeft;
    private string _hintText = "";

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
        _title = UiTheme.Label("", 40, UiTheme.Gold, HorizontalAlignment.Center);
        box.AddChild(_title);
        _hint = UiTheme.Label("", 18, UiTheme.TextDim, HorizontalAlignment.Center);
        box.AddChild(_hint);
        _cards = new UpgradeCards();
        box.AddChild(_cards);
        _cards.Chosen += OnChosen;
        _cards.RerollRequested += Reroll;
        _rerollInfo = UiTheme.Label("", 15, UiTheme.TextDim, HorizontalAlignment.Center);
        box.AddChild(_rerollInfo);
        _buildSummary = UiTheme.Label("", 15, UiTheme.TextDim, HorizontalAlignment.Center);
        _buildSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _buildSummary.CustomMinimumSize = new Vector2(820f, 0f);
        box.AddChild(_buildSummary);
        _root.Visible = false;

        State.LevelUpReady += _ => TryOpen();
    }

    /// <summary>The run-start pick (docs/design/progression.md -> "The start pick"):
    /// class skills for a skill-less character, otherwise skill cards for the
    /// skills it brought. Rerolls work here too (same two charges).</summary>
    public void OpenStartPick(System.Action onPicked)
    {
        _startPick = true;
        _offeringLevel = 0;
        _onStartPicked = onPicked;
        SetOpen(true);
        bool skillLess = !Build.Skills.Any();
        _title.Text = skillLess ? "Choose your first skill" : "Start of the run — choose a skill card";
        SetHint(skillLess
            ? "Your build grows from here: every level adds a skill, a support or a power-up."
            : "Your skills start at level 1 here. Learn a new one or level one up.  (1 / 2 / 3)");
        _buildSummary.Text = skillLess ? "" : BuildSummary(Build);
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
        if (IsOpen || State.PendingLevelUps <= 0 || State.Ended)
        {
            return;
        }

        SetOpen(true);
        ShowNext();
    }

    private void ShowNext()
    {
        _offeringLevel = State.Level - State.PendingLevelUps + 1;
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
        return true;
    }

    private void Reroll(int index) => RerollCard(index);

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
        BlocksInput = open;
        _timeLeft = Net.PartyRules.LevelUpTimeout;
        if (!Coop)
        {
            GetTree().Paused = open;
        }

        OpenChanged?.Invoke(open);
    }

    public override void _ExitTree() => BlocksInput = false;

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

        if (!Coop || !IsOpen)
        {
            return;
        }

        // Co-op: the run goes on around a warded chooser — but not forever.
        _timeLeft -= (float)delta;
        _hint.Text = $"{_hintText}\nThe party fights on — {Mathf.CeilToInt(Mathf.Max(0f, _timeLeft))} s (you are warded while choosing)";
        if (_timeLeft <= 0f && _cards.OfferCount > 0)
        {
            _timeLeft = Net.PartyRules.LevelUpTimeout;
            ChooseIndex(0);
        }
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
