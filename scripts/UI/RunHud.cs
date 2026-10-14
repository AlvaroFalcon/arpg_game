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
/// timer, kills. Bottom-centre: the life cluster — life on the left, the
/// danger gauge on the right (no mana in the Rift) — plus dodge charges.
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
    private HBoxContainer _skillRow = null!;
    private Label _banner = null!;
    private Label _bannerSub = null!;
    private Label _callout = null!;
    private Label _controlsHint = null!;
    private Label _toast = null!;
    private float _toastTime;
    private float _bannerTime;
    private float _calloutTime;
    private BossEnemy? _boss;
    private TargetFrame _target = null!;
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
        _target = new TargetFrame { Player = Player, Boss = () => _boss, Name = "TargetFrame" };
        root.AddChild(_target);
        _target.Dock(56f);

        // --- bottom centre: life, level badge, flask, skills (no mana here) ---
        var bar = new PlayerHudBar { Player = Player, Progress = () => (State.Level, State.ExperienceFraction), RightGauge = DangerGauge };
        root.AddChild(bar);
        bar.Dock();
        _skillRow = PlayerHudBar.CreateSkillRow(root);

        // Movable in the HUD editor (pause menu → Edit HUD); ids shared with the realities.
        HudLayout.Register(_levelLabel, "run_level", "Level");
        HudLayout.Register(_timerLabel, "timer", "Timer");
        HudLayout.Register(_killsLabel, "kills", "Kills");
        HudLayout.Register(_target, "boss", "Target / boss life", _target.Preview, HudAvoid.Slide);
        HudLayout.Register(bar, "vitals", "Life & flask");
        HudLayout.Register(_skillRow, "skills", "Skills");

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
        _controlsHint = UiTheme.Label("WASD move  ·  Space dodge (charges refill)  ·  skills fire on their own timers  ·  Esc pause", 17, UiTheme.TextDim, HorizontalAlignment.Center);
        root.AddChild(_controlsHint);
        _controlsHint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom, true);
        _controlsHint.OffsetLeft = -480f;
        _controlsHint.OffsetRight = 480f;
        // Above the life cluster (it used to sit across the bars).
        _controlsHint.OffsetTop = -252f;

        // Pickup toasts (orbs, power-ups), just above the hint's spot.
        _toast = UiTheme.Label("", 20, UiTheme.Gold, HorizontalAlignment.Center);
        root.AddChild(_toast);
        _toast.SetAnchorsPreset(Control.LayoutPreset.CenterBottom, true);
        _toast.OffsetLeft = -400f;
        _toast.OffsetRight = 400f;
        _toast.OffsetTop = -284f;
        State.Toast += ShowToast;

        Director.Announced += ShowBanner;
        Director.BossSpawned += OnBossSpawned;
    }


    public void ShowBanner(string title, string subtitle, Color color)
    {
        _banner.Text = title;
        _banner.AddThemeColorOverride("font_color", color);
        _bannerSub.Text = subtitle;
        _bannerTime = 3f;
    }

    public void ShowToast(string text, Color color)
    {
        _toast.Text = text;
        _toast.AddThemeColorOverride("font_color", color);
        _toastTime = 2.2f;
    }

    /// <summary>The right-hand bar of the life-only cluster: the danger level,
    /// filling towards the next one, and what is coming.</summary>
    public PlayerHudBar.SideGauge? DangerGauge()
    {
        float t = State.Elapsed;
        int danger = RunDirector.Danger(t);
        float frac = t / 60f - Mathf.Floor(t / 60f);
        string next = Director.Surging ? "Surge! The horde presses in"
            : Director.ColossusDefeated ? "Portal open · stay for more rewards"
            : t < Director.HeraldTime ? $"Bone Herald in {UiTheme.FormatTime(Director.HeraldTime - t)}"
            : t < Director.ColossusTime ? $"Ember Colossus in {UiTheme.FormatTime(Director.ColossusTime - t)}"
            : "The Ember Colossus";
        // Amber early, blood red as it climbs.
        float heat = Mathf.Clamp((danger - 1) / 10f, 0f, 1f);
        Color top = new Color(1f, 0.75f, 0.25f).Lerp(new Color(0.85f, 0.15f, 0.35f), heat);
        return new PlayerHudBar.SideGauge($"Danger {danger}", next, frac, top, top.Darkened(0.55f));
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

        float hintAlpha = Mathf.Clamp((25f - State.Elapsed) / 3f, 0f, 1f);
        _controlsHint.Visible = hintAlpha > 0f;
        _controlsHint.Modulate = new Color(1f, 1f, 1f, hintAlpha);

        UpdateSkills();

        if (_toastTime > 0f)
        {
            _toastTime -= dt;
            _toast.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(_toastTime / 0.5f, 0f, 1f));
        }
        else
        {
            _toast.Text = "";
        }

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
            // The auto-cast timer is what the player waits on in the Rift.
            float auto = 0f;
            if (Player.GetNodeOrNull<PlayerSkillInput>("PlayerSkillInput") is { } input)
            {
                foreach (SkillSlotId slot in new[] { SkillSlotId.Lmb, SkillSlotId.Rmb, SkillSlotId.Slot1, SkillSlotId.Slot2, SkillSlotId.Slot3, SkillSlotId.Slot4 })
                {
                    if (Player.SkillUser.GetSkill(slot) == view.Skill)
                    {
                        auto = input.AutoCastFraction(slot);
                    }
                }
            }

            view.Cooldown = Mathf.Max(Player.SkillUser.CooldownFraction(view.Skill), auto);
        }
    }

}
