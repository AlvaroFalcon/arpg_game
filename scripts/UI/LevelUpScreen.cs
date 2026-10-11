#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using PathToExile.Progression;
using PathToExile.Run;

namespace PathToExile.UI;

/// <summary>
/// docs/design/menus.md -> "Level-up": pauses the run and offers three cards
/// (keystones every fifth level). Queued level-ups are offered back to back.
/// Two rerolls per run. Also hosts the run-start skill pick, which is the same
/// card row with every skill on it.
/// </summary>
public partial class LevelUpScreen : CanvasLayer
{
    public RunState State { get; set; } = null!;
    public RunBuild Build { get; set; } = null!;

    /// <summary>True while this screen owns the pause.</summary>
    public bool IsOpen { get; private set; }

    private Control _root = null!;
    private Label _title = null!;
    private Label _hint = null!;
    private UpgradeCards _cards = null!;
    private Button _reroll = null!;
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
        HBoxContainer row = UiTheme.HBox(box, 16);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        _reroll = UiTheme.Button("Reroll (R)", Reroll, 18, 200f);
        row.AddChild(_reroll);
        _buildSummary = UiTheme.Label("", 15, UiTheme.TextDim, HorizontalAlignment.Center);
        _buildSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _buildSummary.CustomMinimumSize = new Vector2(820f, 0f);
        box.AddChild(_buildSummary);
        _root.Visible = false;

        State.LevelUpReady += _ => TryOpen();
    }

    /// <summary>The run-start pick: every skill, no reroll.</summary>
    public void OpenStartPick(System.Action onPicked)
    {
        _startPick = true;
        _onStartPicked = onPicked;
        IsOpen = true;
        GetTree().Paused = true;
        _title.Text = "Choose your first skill";
        _hint.Text = "Your build grows from here: every level adds a skill, a support or a passive.";
        _reroll.Visible = false;
        _buildSummary.Text = "";
        var offers = UpgradeCatalog.Skills.Select(entry => new UpgradeOffer
        {
            Kind = UpgradeKind.NewSkill,
            Id = entry.Id,
            Title = entry.Definition.DisplayName,
            Subtitle = TagLine(entry.Definition.Tags),
            Description = entry.Definition.Description,
            Icon = entry.Definition.Icon,
            Apply = () => Build.AddSkill(entry.Id),
        }).ToList();
        _cards.SetOffers(offers);
        _cards.FocusFirst();
        _root.Visible = true;
    }

    private static string TagLine(Combat.SkillTags tags)
    {
        var words = new List<string>();
        foreach (Combat.SkillTags t in new[] { Combat.SkillTags.Spell, Combat.SkillTags.Projectile, Combat.SkillTags.Area, Combat.SkillTags.Duration, Combat.SkillTags.Fire, Combat.SkillTags.Cold, Combat.SkillTags.Lightning })
        {
            if ((tags & t) != 0)
            {
                words.Add(t.ToString());
            }
        }

        return string.Join(" · ", words);
    }

    private void TryOpen()
    {
        if (IsOpen || State.PendingLevelUps <= 0 || State.Ended)
        {
            return;
        }

        IsOpen = true;
        GetTree().Paused = true;
        ShowNext();
    }

    private void ShowNext()
    {
        _offeringLevel = State.Level - State.PendingLevelUps + 1;
        bool keystone = _offeringLevel % RunBuild.KeystoneEvery == 0;
        _title.Text = keystone ? $"Level {_offeringLevel} — choose a Keystone" : $"Level {_offeringLevel}";
        _hint.Text = keystone ? "Keystones change how your whole build works. Choose carefully." : "Choose an upgrade  (1 / 2 / 3)";
        _reroll.Visible = true;
        RefreshReroll();
        _buildSummary.Text = BuildSummary(Build);
        _cards.SetOffers(Build.RollOffers(_offeringLevel, _rng));
        _cards.FocusFirst();
        _root.Visible = true;
        Core.Sfx.Play(this, "levelup");
    }

    private void RefreshReroll()
    {
        _reroll.Text = $"Reroll (R) — {Build.RerollsLeft} left";
        _reroll.Disabled = Build.RerollsLeft <= 0;
    }

    private void Reroll()
    {
        if (!IsOpen || _startPick || Build.RerollsLeft <= 0)
        {
            return;
        }

        Build.RerollsLeft--;
        RefreshReroll();
        _cards.SetOffers(Build.RollOffers(_offeringLevel, _rng));
        _cards.FocusFirst();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (IsOpen && @event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.R })
        {
            GetViewport().SetInputAsHandled();
            Reroll();
        }
    }

    private void OnChosen(UpgradeOffer offer)
    {
        offer.Apply();
        if (_startPick)
        {
            _startPick = false;
            Close();
            _onStartPicked?.Invoke();
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
        IsOpen = false;
        GetTree().Paused = false;
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
