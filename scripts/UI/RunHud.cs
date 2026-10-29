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
    private SkillBar _skillRow = null!;
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

    // The reward panel of the endless phase (docs/design/run.md -> "Cash-out offer and the infinite Rift").
    private PanelContainer _rewardPanel = null!;
    private Label _rwTitle = null!;
    private Label _rwLine1 = null!;
    private Label _rwLine2 = null!;
    private ProgressBar _rwBar = null!;
    private Label _rwNext = null!;
    private StyleBoxFlat _rwStyle = null!;
    private float _rwRefresh;
    private float _rwFlash;
    private int _rwTier;

    /// <summary>Co-op: the party minimap (hidden solo).</summary>
    public RiftMinimap PartyMap { get; private set; } = null!;

    /// <summary>The reward panel's lines, for tests and captures.</summary>
    public bool RewardPanelVisible => _rewardPanel.Visible;
    public string ToastText => _toast.Text;
    public string RewardTitle => _rwTitle.Text;
    public string RewardNextText => _rwNext.Text;
    public string RewardLine1 => _rwLine1.Text;
    public string RewardLine2 => _rwLine2.Text;

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

        // Kills under the timer; the top right holds the local time like every HUD.
        _killsLabel = UiTheme.Label("0 kills", 18, UiTheme.TextDim, HorizontalAlignment.Center);
        root.AddChild(_killsLabel);
        _killsLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop, true);
        _killsLabel.OffsetLeft = -110f;
        _killsLabel.OffsetRight = 110f;
        _killsLabel.OffsetTop = 50f;
        var corner = new HudCorner();
        root.AddChild(corner);
        corner.Dock();
        corner.OffsetTop = 22f;

        BuildRewardPanel(root);

        // --- co-op: where the party is (docs/design/multiplayer.md -> "Allies on the maps") ---
        PartyMap = new RiftMinimap { State = State, Director = Director, Player = Player, Name = "PartyMap", Position = new Vector2(16f, 50f) };
        root.AddChild(PartyMap);

        // --- boss bar ---
        _target = new TargetFrame { Player = Player, Boss = () => _boss, Name = "TargetFrame" };
        root.AddChild(_target);
        _target.Dock(56f);

        // --- bottom centre: life, level badge, flask, skills (no mana here) ---
        var bar = new PlayerHudBar { Player = Player, Progress = () => (State.Level, State.ExperienceFraction), RightGauge = DangerGauge };
        root.AddChild(bar);
        bar.Dock();
        _skillRow = PlayerHudBar.CreateSkillRow(root);

        LootFeedView feed = RealityHud.AddLootFeed(root);

        // Movable in the HUD editor (pause menu → Edit HUD); ids shared with the realities.
        HudLayout.Register(_levelLabel, "run_level", "Level");
        HudLayout.Register(_timerLabel, "timer", "Timer");
        HudLayout.Register(_killsLabel, "kills", "Kills");
        HudLayout.Register(corner, "corner", "Minimap, clock & quests");
        HudLayout.Register(_rewardPanel, "rift_reward", "Rift reward", PreviewReward);
        HudLayout.Register(_target, "boss", "Target / boss life", _target.Preview, HudAvoid.Slide);
        HudLayout.Register(PartyMap, "party_map", "Party minimap (co-op)", on => PartyMap.HudEditing = on);
        HudLayout.Register(bar, "vitals", "Life & flask");
        HudLayout.Register(_skillRow, "skills", "Skills");
        HudLayout.Register(feed, "loot_feed", "Loot picked up", feed.SetPreview);

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


    private void BuildRewardPanel(Control root)
    {
        _rewardPanel = new PanelContainer { Name = "RewardPanel", Position = new Vector2(1006f, 56f), CustomMinimumSize = new Vector2(260f, 92f), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _rwStyle = UiTheme.Box(new Color(0.06f, 0.05f, 0.09f, 0.9f), UiTheme.PanelBorder with { A = 0.6f }, 1, 4, 8);
        _rwStyle.BorderWidthTop = 3;
        _rwStyle.BorderColor = UiTheme.Gold;
        _rewardPanel.AddThemeStyleboxOverride("panel", _rwStyle);
        root.AddChild(_rewardPanel);
        VBoxContainer box = UiTheme.VBox(_rewardPanel, 1);
        _rwTitle = UiTheme.Label("", 13, UiTheme.Gold);
        _rwLine1 = UiTheme.Label("", 15, UiTheme.Text);
        _rwLine2 = UiTheme.Label("", 13, UiTheme.TextDim);
        _rwBar = UiTheme.Bar(UiTheme.Gold, 6f);
        _rwBar.CustomMinimumSize = new Vector2(240f, 6f);
        _rwNext = UiTheme.Label("", 12, UiTheme.TextDim);
        foreach (Control c in new Control[] { _rwTitle, _rwLine1, _rwLine2, _rwBar, _rwNext, UiTheme.Label("Leave: walk into the portal, or Esc", 11, UiTheme.TextDim) })
        {
            c.MouseFilter = Control.MouseFilterEnum.Ignore;
            box.AddChild(c);
        }
    }

    private bool _rewardPreview;

    /// <summary>HUD edit mode: the panel shows a sample so it can be placed before the Colossus falls.</summary>
    private void PreviewReward(bool on)
    {
        _rewardPreview = on;
        if (on)
        {
            RunRewards sample = RunRewards.Preview(600f, 3, 300, true);
            _rwTitle.Text = $"REWARD · TIER {RunRewards.Roman(sample.Tier)}";
            _rwLine1.Text = $"{sample.Gold:N0} gold · {sample.OrbCount} orbs";
            _rwLine2.Text = $"{sample.Choices} cards · keep {sample.Keep} · item level {sample.ItemLevel}";
            _rwNext.Text = sample.NextTierText;
            _rwBar.Value = 0.6f;
        }

        _rewardPanel.Visible = on || Director.ColossusDefeated;
    }

    private string GaugeRewardLine()
    {
        RunRewards r = CurrentReward();
        return $"Reward tier {RunRewards.Roman(Mathf.Max(1, r.Tier))} · next at {UiTheme.FormatTime(r.NextTierAt)}";
    }

    private RunRewards CurrentReward() => RunRewards.Preview(State.Elapsed, State.BossesKilled, State.Kills, Director.ColossusDefeated);

    /// <summary>Four times a second: the reward as it stands, the bar to the next tier, a toast and a flash on a new tier.</summary>
    private void UpdateReward(float dt)
    {
        _rewardPanel.Visible = Director.ColossusDefeated || _rewardPreview;
        if (_rwFlash > 0f)
        {
            _rwFlash -= dt;
            _rwStyle.BorderColor = _rwFlash > 0f ? new Color(1f, 0.95f, 0.6f) : UiTheme.Gold;
            _rwStyle.BorderWidthTop = _rwFlash > 0f ? 4 : 3;
        }

        _rwRefresh -= dt;
        if (!Director.ColossusDefeated || _rwRefresh > 0f)
        {
            return;
        }

        _rwRefresh = 0.25f;
        RunRewards r = CurrentReward();
        _rwTitle.Text = $"REWARD · TIER {RunRewards.Roman(Mathf.Max(1, r.Tier))}";
        _rwLine1.Text = $"{r.Gold:N0} gold · {r.OrbCount} orbs";
        _rwLine2.Text = r.ItemsEarned ? $"{r.Choices} cards · keep {r.Keep} · item level {r.ItemLevel}" : $"no item yet · item level {r.ItemLevel}";
        float from = Mathf.Max(RunRewards.TierStart(Mathf.Max(1, r.Tier)), r.Tier <= 1 ? Director.ColossusTime : 0f);
        _rwBar.Value = Mathf.Clamp((State.Elapsed - from) / Mathf.Max(1f, r.NextTierAt - from), 0f, 1f);
        _rwNext.Text = r.NextTierText;
        if (_rwTier > 0 && r.Tier > _rwTier)
        {
            ShowToast(RunRewards.TierToast(r.Tier), UiTheme.Gold);
            Core.Sfx.Play(this, "levelup", -8f);
            _rwFlash = 0.6f;
        }

        _rwTier = r.Tier;
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
            : Director.ColossusDefeated ? GaugeRewardLine()
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
        UpdateReward(dt);

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
        // Survival: everything auto-casts, so no key labels; half the skills each side of the mirror.
        _skillRow.Show(Build.Skills.Select(skill => ("", (SkillInstance?)skill)).ToList());
        var input = Player.GetNodeOrNull<PlayerSkillInput>("PlayerSkillInput");
        _skillRow.Cooldowns(skill =>
        {
            // The auto-cast timer is what the player waits on in the Rift.
            float auto = 0f;
            if (input is not null)
            {
                foreach (SkillSlotId slot in new[] { SkillSlotId.Lmb, SkillSlotId.Rmb, SkillSlotId.Slot1, SkillSlotId.Slot2, SkillSlotId.Slot3, SkillSlotId.Slot4 })
                {
                    if (Player.SkillUser.GetSkill(slot) == skill)
                    {
                        auto = input.AutoCastFraction(slot);
                    }
                }
            }

            return Mathf.Max(Player.SkillUser.CooldownFraction(skill), auto);
        });
    }

}
