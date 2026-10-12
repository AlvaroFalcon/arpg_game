#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Enemies;
using ArpgGame.Progression;
using ArpgGame.Run;
using ArpgGame.Skills;

namespace ArpgGame.UI;

/// <summary>
/// The in-run HUD — docs/design/menus.md -> "HUD". Top: experience bar, level,
/// timer, kills. Bottom-left: life and mana bars plus dodge charges.
/// Bottom-centre: the skill bar (icon, cooldown sweep, level, support pips).
/// Top-centre when a boss is up: its bar with phase markers and the name of
/// the attack it is performing. Centre: announcement banners and boss
/// mechanic callouts.
/// </summary>
public partial class RunHud : CanvasLayer
{
    public RunState State { get; set; } = null!;
    public Player Player { get; set; } = null!;
    public RunBuild Build { get; set; } = null!;
    public RunDirector Director { get; set; } = null!;

    private ProgressBar _xpBar = null!;
    private Label _levelLabel = null!;
    private Label _timerLabel = null!;
    private Label _killsLabel = null!;
    private ProgressBar _lifeBar = null!;
    private ProgressBar _manaBar = null!;
    private ProgressBar _staminaBar = null!;
    private Label _lifeLabel = null!;
    private Label _manaLabel = null!;
    private HBoxContainer _dodgeRow = null!;
    private HBoxContainer _skillRow = null!;
    private Control _bossBox = null!;
    private Label _bossName = null!;
    private Label _bossAttack = null!;
    private ProgressBar _bossBar = null!;
    private Control _bossMarkers = null!;
    private Label _banner = null!;
    private Label _bannerSub = null!;
    private Label _callout = null!;
    private Label _controlsHint = null!;
    private float _bannerTime;
    private float _calloutTime;
    private BossEnemy? _boss;
    private readonly List<SkillSlotView> _slots = new();
    private string _skillSignature = "";

    public override void _Ready()
    {
        Layer = 10;
        Control root = UiTheme.FullRect(this);

        // --- top bar ---
        _xpBar = UiTheme.Bar(UiTheme.Experience, 12f);
        root.AddChild(_xpBar);
        _xpBar.SetAnchorsPreset(Control.LayoutPreset.TopWide, true);
        _xpBar.OffsetBottom = 12f;

        _levelLabel = UiTheme.Label("Lv 1", 22, UiTheme.Gold);
        root.AddChild(_levelLabel);
        _levelLabel.Position = new Vector2(16f, 16f);

        _timerLabel = UiTheme.Label("00:00", 30, UiTheme.Text, HorizontalAlignment.Center);
        root.AddChild(_timerLabel);
        _timerLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop, true);
        _timerLabel.OffsetLeft = -100f;
        _timerLabel.OffsetRight = 100f;
        _timerLabel.OffsetTop = 14f;

        _killsLabel = UiTheme.Label("0 kills", 20, UiTheme.TextDim, HorizontalAlignment.Right);
        root.AddChild(_killsLabel);
        _killsLabel.SetAnchorsPreset(Control.LayoutPreset.TopRight, true);
        _killsLabel.OffsetLeft = -220f;
        _killsLabel.OffsetRight = -16f;
        _killsLabel.OffsetTop = 16f;

        // --- boss bar ---
        _bossBox = new VBoxContainer { Visible = false };
        root.AddChild(_bossBox);
        _bossBox.SetAnchorsPreset(Control.LayoutPreset.CenterTop, true);
        _bossBox.OffsetLeft = -320f;
        _bossBox.OffsetRight = 320f;
        _bossBox.OffsetTop = 56f;
        _bossName = UiTheme.Label("", 22, UiTheme.Ember, HorizontalAlignment.Center);
        _bossBox.AddChild(_bossName);
        var barHolder = new Control { CustomMinimumSize = new Vector2(640f, 20f) };
        _bossBox.AddChild(barHolder);
        _bossBar = UiTheme.Bar(new Color(0.8f, 0.22f, 0.12f), 20f);
        barHolder.AddChild(_bossBar);
        _bossBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _bossMarkers = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        barHolder.AddChild(_bossMarkers);
        _bossMarkers.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _bossAttack = UiTheme.Label("", 18, UiTheme.Text, HorizontalAlignment.Center);
        _bossBox.AddChild(_bossAttack);

        // --- bottom-left: life / mana / dodge ---
        var vitals = new VBoxContainer();
        vitals.AddThemeConstantOverride("separation", 4);
        root.AddChild(vitals);
        vitals.SetAnchorsPreset(Control.LayoutPreset.BottomLeft, true);
        vitals.OffsetLeft = 16f;
        vitals.OffsetTop = -118f;
        vitals.OffsetRight = 336f;
        vitals.OffsetBottom = -16f;
        (_lifeBar, _lifeLabel) = AddVital(vitals, UiTheme.Life);
        (_manaBar, _manaLabel) = AddVital(vitals, UiTheme.Mana);
        _staminaBar = UiTheme.Bar(new Color(0.85f, 0.8f, 0.3f), 7f);
        _staminaBar.CustomMinimumSize = new Vector2(320f, 7f);
        vitals.AddChild(_staminaBar);
        // No mana in the Survival Rift: skills are free and automatic.
        _manaBar.GetParent<Control>().Visible = !Player.Stats.IgnoresResourceCosts;
        _dodgeRow = UiTheme.HBox(vitals, 6);

        // --- skill bar ---
        _skillRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _skillRow.AddThemeConstantOverride("separation", 10);
        root.AddChild(_skillRow);
        _skillRow.SetAnchorsPreset(Control.LayoutPreset.CenterBottom, true);
        _skillRow.OffsetLeft = -200f;
        _skillRow.OffsetRight = 200f;
        _skillRow.OffsetTop = -96f;
        _skillRow.OffsetBottom = -16f;

        // --- banners ---
        _banner = UiTheme.Label("", 46, UiTheme.Gold, HorizontalAlignment.Center);
        root.AddChild(_banner);
        _banner.SetAnchorsPreset(Control.LayoutPreset.Center, true);
        _banner.OffsetLeft = -500f;
        _banner.OffsetRight = 500f;
        _banner.OffsetTop = -190f;
        _bannerSub = UiTheme.Label("", 22, UiTheme.Text, HorizontalAlignment.Center);
        root.AddChild(_bannerSub);
        _bannerSub.SetAnchorsPreset(Control.LayoutPreset.Center, true);
        _bannerSub.OffsetLeft = -500f;
        _bannerSub.OffsetRight = 500f;
        _bannerSub.OffsetTop = -130f;

        _callout = UiTheme.Label("", 54, UiTheme.Ember, HorizontalAlignment.Center);
        root.AddChild(_callout);
        _callout.SetAnchorsPreset(Control.LayoutPreset.Center, true);
        _callout.OffsetLeft = -500f;
        _callout.OffsetRight = 500f;
        _callout.OffsetTop = 90f;

        // First-seconds controls reminder, above the skill bar.
        _controlsHint = UiTheme.Label("WASD move  ·  Space dodge (charges refill)  ·  skills cast themselves  ·  Esc pause", 17, UiTheme.TextDim, HorizontalAlignment.Center);
        root.AddChild(_controlsHint);
        _controlsHint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom, true);
        _controlsHint.OffsetLeft = -480f;
        _controlsHint.OffsetRight = 480f;
        _controlsHint.OffsetTop = -130f;

        Director.Announced += ShowBanner;
        Director.BossSpawned += OnBossSpawned;
    }

    private static (ProgressBar, Label) AddVital(VBoxContainer parent, Color color)
    {
        var holder = new Control { CustomMinimumSize = new Vector2(320f, 24f) };
        parent.AddChild(holder);
        ProgressBar bar = UiTheme.Bar(color, 24f);
        holder.AddChild(bar);
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Label label = UiTheme.Label("", 16, UiTheme.Text, HorizontalAlignment.Center);
        holder.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.VerticalAlignment = VerticalAlignment.Center;
        return (bar, label);
    }

    public void ShowBanner(string title, string subtitle, Color color)
    {
        _banner.Text = title;
        _banner.AddThemeColorOverride("font_color", color);
        _bannerSub.Text = subtitle;
        _bannerTime = 3f;
    }

    private void OnBossSpawned(Node2D boss)
    {
        if (boss is not BossEnemy b)
        {
            return;
        }

        _boss = b;
        b.MechanicCalled += (text, color) =>
        {
            _callout.Text = text;
            _callout.AddThemeColorOverride("font_color", color);
            _calloutTime = 2.2f;
        };
        b.PhaseChanged += (_, message) => ShowBanner($"Phase {b.Phase}", message, UiTheme.Ember);
        _bossName.Text = b.DisplayName;
        foreach (Node child in _bossMarkers.GetChildren())
        {
            child.QueueFree();
        }

        foreach (float threshold in b.PhaseThresholds)
        {
            var marker = new ColorRect { Color = new Color(1f, 0.9f, 0.6f, 0.9f), Size = new Vector2(3f, 20f), MouseFilter = Control.MouseFilterEnum.Ignore };
            _bossMarkers.AddChild(marker);
            marker.Position = new Vector2(640f * threshold - 1.5f, 0f);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _xpBar.Value = State.ExperienceFraction;
        _levelLabel.Text = $"Lv {State.Level}";
        _timerLabel.Text = UiTheme.FormatTime(State.Elapsed);
        float toBoss = (Director.ColossusTime - State.Elapsed);
        _timerLabel.AddThemeColorOverride("font_color", toBoss is > 0f and < 30f ? UiTheme.Ember : UiTheme.Text);
        _killsLabel.Text = $"{State.Kills} kills";

        if (Player.Stats.Life is { } life)
        {
            _lifeBar.Value = life.Max > 0 ? life.Current / life.Max : 0;
            _lifeLabel.Text = $"{Mathf.CeilToInt(Mathf.Min(life.Current, life.Max))} / {Mathf.RoundToInt(life.Max)}";
        }

        if (Player.Stats.Mana is { } mana)
        {
            _manaBar.Value = mana.Max > 0 ? mana.Current / mana.Max : 0;
            _manaLabel.Text = $"{Mathf.FloorToInt(mana.Current)} / {Mathf.RoundToInt(mana.Max)}";
        }

        if (Player.MaxStamina > 0f)
        {
            _staminaBar.Value = Mathf.Max(0f, Player.Stamina) / Player.MaxStamina;
            _staminaBar.Modulate = Player.Exhausted ? new Color(1f, 0.45f, 0.4f) : Colors.White;
        }

        float hintAlpha = Mathf.Clamp((25f - State.Elapsed) / 3f, 0f, 1f);
        _controlsHint.Visible = hintAlpha > 0f;
        _controlsHint.Modulate = new Color(1f, 1f, 1f, hintAlpha);

        UpdateDodge();
        UpdateSkills();
        UpdateBoss();

        if (_bannerTime > 0f)
        {
            _bannerTime -= dt;
            float a = Mathf.Clamp(_bannerTime / 0.6f, 0f, 1f);
            _banner.Modulate = new Color(1f, 1f, 1f, a);
            _bannerSub.Modulate = new Color(1f, 1f, 1f, a);
        }
        else
        {
            _banner.Text = "";
            _bannerSub.Text = "";
        }

        if (_calloutTime > 0f)
        {
            _calloutTime -= dt;
            float pulse = 1f + 0.06f * Mathf.Sin((float)Time.GetTicksMsec() * 0.02f);
            _callout.Scale = Vector2.One * pulse;
            _callout.PivotOffset = _callout.Size / 2f;
        }
        else
        {
            _callout.Text = "";
        }
    }

    private void UpdateDodge()
    {
        if (Player.Dodge is not { } dodge)
        {
            return;
        }

        int max = dodge.MaxCharges;
        while (_dodgeRow.GetChildCount() < max)
        {
            _dodgeRow.AddChild(new DodgePip());
        }

        for (int i = 0; i < _dodgeRow.GetChildCount(); i++)
        {
            var pip = (DodgePip)_dodgeRow.GetChild(i);
            pip.Visible = i < max;
            pip.Fill = i < dodge.Charges ? 1f : i == dodge.Charges ? dodge.RechargeProgress : 0f;
        }
    }

    private void UpdateSkills()
    {
        List<SkillInstance> skills = Build.Skills.ToList();
        string signature = string.Join("|", skills.Select(s => $"{s.Definition.DisplayName}{s.Level}{s.Attached.Count}"));
        if (signature != _skillSignature)
        {
            _skillSignature = signature;
            foreach (SkillSlotView view in _slots)
            {
                view.QueueFree();
            }

            _slots.Clear();
            foreach (SkillInstance skill in skills)
            {
                // Survival: everything auto-casts, so no key labels.
                var view = new SkillSlotView(skill, "");
                _skillRow.AddChild(view);
                _slots.Add(view);
            }
        }

        foreach (SkillSlotView view in _slots)
        {
            view.Cooldown = Player.SkillUser.CooldownFraction(view.Skill);
        }
    }

    private void UpdateBoss()
    {
        bool show = _boss is not null && IsInstanceValid(_boss) && !_boss.Health.IsDead;
        _bossBox.Visible = show;
        if (!show)
        {
            return;
        }

        _bossBar.Value = _boss!.LifeFraction;
        _bossAttack.Text = _boss.CurrentAttack.Length > 0 ? $"« {_boss.CurrentAttack} »" : "";
    }
}
