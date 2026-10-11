#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using PathToExile.Characters;
using PathToExile.Core;
using PathToExile.Enemies;
using PathToExile.Skills;
using PathToExile.World;

namespace PathToExile.UI;

/// <summary>
/// The HUD inside an Altered Reality — docs/design/endgame.md -> "Reality HUD".
/// Same vitals/skill bar/boss bar language as the survival HUD, plus what a
/// dungeon needs: the character's experience bar and level, the reality's name,
/// tier, modifiers and portals left, a cleared counter, a minimap of explored
/// cells, the class resource, and a stack of loot/level toasts.
/// </summary>
public partial class RealityHud : CanvasLayer
{
    public RealityController Reality { get; set; } = null!;

    private Player Player => Reality.Player;

    private ProgressBar _xpBar = null!;
    private Label _levelLabel = null!;
    private Label _titleLabel = null!;
    private Label _infoLabel = null!;
    private Label _modsLabel = null!;
    private ProgressBar _lifeBar = null!;
    private ProgressBar _manaBar = null!;
    private Label _lifeLabel = null!;
    private Label _manaLabel = null!;
    private HBoxContainer _dodgeRow = null!;
    private HBoxContainer _resourceRow = null!;
    private Label _resourceLabel = null!;
    private HBoxContainer _skillRow = null!;
    private Control _bossBox = null!;
    private Label _bossName = null!;
    private Label _bossAttack = null!;
    private ProgressBar _bossBar = null!;
    private Control _bossMarkers = null!;
    private Label _banner = null!;
    private Label _bannerSub = null!;
    private Label _callout = null!;
    private VBoxContainer _toasts = null!;
    private Minimap _minimap = null!;
    private float _bannerTime;
    private float _calloutTime;
    private BossEnemy? _boss;
    private readonly List<SkillSlotView> _slots = new();
    private string _skillSignature = "";
    private CharacterBuild? _build;

    public override void _Ready()
    {
        Layer = 10;
        Control root = UiTheme.FullRect(this);

        _xpBar = UiTheme.Bar(UiTheme.Experience, 10f);
        root.AddChild(_xpBar);
        _xpBar.SetAnchorsPreset(Control.LayoutPreset.TopWide, true);
        _xpBar.OffsetBottom = 10f;

        _levelLabel = UiTheme.Label("", 20, UiTheme.Gold);
        root.AddChild(_levelLabel);
        _levelLabel.Position = new Vector2(16f, 16f);

        // --- top-right: reality info + minimap ---
        var info = new VBoxContainer();
        info.AddThemeConstantOverride("separation", 2);
        root.AddChild(info);
        info.SetAnchorsPreset(Control.LayoutPreset.TopRight, true);
        info.OffsetLeft = -330f;
        info.OffsetRight = -16f;
        info.OffsetTop = 16f;
        _titleLabel = UiTheme.Label("", 20, RealityTiers.ShardColor(Reality.Tier), HorizontalAlignment.Right);
        info.AddChild(_titleLabel);
        _infoLabel = UiTheme.Label("", 15, UiTheme.Text, HorizontalAlignment.Right);
        info.AddChild(_infoLabel);
        _modsLabel = UiTheme.Label("", 13, UiTheme.Ember, HorizontalAlignment.Right);
        _modsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _modsLabel.CustomMinimumSize = new Vector2(314f, 0f);
        info.AddChild(_modsLabel);
        _minimap = new Minimap { Reality = Reality, CustomMinimumSize = new Vector2(220f, 170f), SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        info.AddChild(_minimap);

        // --- boss bar ---
        _bossBox = new VBoxContainer { Visible = false };
        root.AddChild(_bossBox);
        _bossBox.SetAnchorsPreset(Control.LayoutPreset.CenterTop, true);
        _bossBox.OffsetLeft = -300f;
        _bossBox.OffsetRight = 300f;
        _bossBox.OffsetTop = 30f;
        _bossName = UiTheme.Label("", 22, UiTheme.Ember, HorizontalAlignment.Center);
        _bossBox.AddChild(_bossName);
        var barHolder = new Control { CustomMinimumSize = new Vector2(600f, 20f) };
        _bossBox.AddChild(barHolder);
        _bossBar = UiTheme.Bar(new Color(0.8f, 0.22f, 0.12f), 20f);
        barHolder.AddChild(_bossBar);
        _bossBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _bossMarkers = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        barHolder.AddChild(_bossMarkers);
        _bossMarkers.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _bossAttack = UiTheme.Label("", 18, UiTheme.Text, HorizontalAlignment.Center);
        _bossBox.AddChild(_bossAttack);

        // --- bottom-left: vitals, dodge, class resource ---
        var vitals = new VBoxContainer();
        vitals.AddThemeConstantOverride("separation", 4);
        root.AddChild(vitals);
        vitals.SetAnchorsPreset(Control.LayoutPreset.BottomLeft, true);
        vitals.OffsetLeft = 16f;
        vitals.OffsetTop = -150f;
        vitals.OffsetRight = 336f;
        vitals.OffsetBottom = -16f;
        vitals.Alignment = BoxContainer.AlignmentMode.End;
        _resourceRow = UiTheme.HBox(vitals, 4);
        _resourceLabel = UiTheme.Label("", 14, UiTheme.TextDim);
        vitals.AddChild(_resourceLabel);
        vitals.MoveChild(_resourceLabel, 0);
        (_lifeBar, _lifeLabel) = AddVital(vitals, UiTheme.Life);
        (_manaBar, _manaLabel) = AddVital(vitals, UiTheme.Mana);
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

        // --- toasts (left, under the level) ---
        _toasts = new VBoxContainer();
        _toasts.AddThemeConstantOverride("separation", 2);
        root.AddChild(_toasts);
        _toasts.Position = new Vector2(16f, 50f);
        _toasts.CustomMinimumSize = new Vector2(520f, 0f);

        // --- banners ---
        _banner = UiTheme.Label("", 44, UiTheme.Gold, HorizontalAlignment.Center);
        root.AddChild(_banner);
        _banner.SetAnchorsPreset(Control.LayoutPreset.Center, true);
        _banner.OffsetLeft = -560f;
        _banner.OffsetRight = 560f;
        _banner.OffsetTop = -200f;
        _bannerSub = UiTheme.Label("", 20, UiTheme.Text, HorizontalAlignment.Center);
        _bannerSub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_bannerSub);
        _bannerSub.SetAnchorsPreset(Control.LayoutPreset.Center, true);
        _bannerSub.OffsetLeft = -520f;
        _bannerSub.OffsetRight = 520f;
        _bannerSub.OffsetTop = -140f;

        _callout = UiTheme.Label("", 52, UiTheme.Ember, HorizontalAlignment.Center);
        root.AddChild(_callout);
        _callout.SetAnchorsPreset(Control.LayoutPreset.Center, true);
        _callout.OffsetLeft = -500f;
        _callout.OffsetRight = 500f;
        _callout.OffsetTop = 90f;

        Reality.Announced += ShowBanner;
        Reality.Toast += ShowToast;
        Reality.BossStarted += OnBossStarted;

        _build = CharacterBuild.Of(Player);
        if (_build is not null)
        {
            _build.LevelledUp += level => ShowToast($"Level {level}! A passive point is waiting (P)", UiTheme.Gold);
            _build.GemLevelled += (name, level) => ShowToast($"{name} reached gem level {level}", new Color(1f, 0.6f, 0.35f));
        }

        string mods = string.Join("\n", Reality.Modifiers.Select(m => RealityModifier.Find(m)?.Text ?? m));
        _modsLabel.Text = mods;
        _titleLabel.Text = $"{Reality.Layout.Name}  ·  T{Reality.Tier}";
    }

    private static (ProgressBar, Label) AddVital(VBoxContainer parent, Color color)
    {
        var holder = new Control { CustomMinimumSize = new Vector2(320f, 22f) };
        parent.AddChild(holder);
        ProgressBar bar = UiTheme.Bar(color, 22f);
        holder.AddChild(bar);
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Label label = UiTheme.Label("", 15, UiTheme.Text, HorizontalAlignment.Center);
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
        _bannerTime = 3.2f;
    }

    public void ShowToast(string text, Color color)
    {
        Label label = UiTheme.Label(text, 17, color);
        label.SetMeta("t", 5f);
        _toasts.AddChild(label);
        while (_toasts.GetChildCount() > 6)
        {
            Node oldest = _toasts.GetChild(0);
            _toasts.RemoveChild(oldest);
            oldest.QueueFree();
        }
    }

    private void OnBossStarted(Node2D boss)
    {
        if (boss is not BossEnemy b || b == _boss)
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
        _bossName.Text = b.ShownName.Length > 0 ? b.ShownName : b.DisplayName;
        foreach (float threshold in b.PhaseThresholds)
        {
            var marker = new ColorRect { Color = new Color(1f, 0.9f, 0.6f, 0.9f), Size = new Vector2(3f, 20f), MouseFilter = Control.MouseFilterEnum.Ignore };
            _bossMarkers.AddChild(marker);
            marker.Position = new Vector2(600f * threshold - 1.5f, 0f);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        CharacterData? c = Profile.Get(this)?.Active;
        if (_build is not null)
        {
            _xpBar.Value = _build.ExperienceFraction;
            _levelLabel.Text = $"{_build.Data.Name}  ·  Lv {_build.Data.Level} {_build.Data.AscendancyDef?.Name ?? _build.Data.Class.Name}";
        }
        else if (c is not null)
        {
            _levelLabel.Text = $"{c.Name}  ·  Lv {c.Level}";
        }

        int portals = Reality.PortalsLeft;
        string cleared = Reality.MonstersTotal > 0 ? $"{Mathf.RoundToInt(100f * Reality.Kills / Reality.MonstersTotal)}%" : "-";
        _infoLabel.Text = $"Monster lvl {Reality.MonsterLevel}  ·  {portals} portal{(portals == 1 ? "" : "s")}  ·  cleared {cleared}  ·  {UiTheme.FormatTime(Reality.Elapsed)}";

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

        UpdateDodge();
        UpdateResource();
        UpdateSkills();
        UpdateBoss();
        UpdateToasts(dt);
        _minimap.QueueRedraw();

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

    private void UpdateToasts(float dt)
    {
        foreach (Node n in _toasts.GetChildren())
        {
            if (n is not Label label)
            {
                continue;
            }

            float t = (float)label.GetMeta("t") - dt;
            label.SetMeta("t", t);
            label.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(t / 0.8f, 0f, 1f));
            if (t <= 0f)
            {
                label.QueueFree();
            }
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

    private void UpdateResource()
    {
        ClassResource? res = _build?.Resource;
        _resourceRow.Visible = res is not null;
        _resourceLabel.Visible = res is not null;
        if (res is null)
        {
            return;
        }

        _resourceLabel.Text = $"{res.Label} {res.Value}/{res.Max}";
        while (_resourceRow.GetChildCount() < res.Max)
        {
            _resourceRow.AddChild(new ColorRect { CustomMinimumSize = new Vector2(14f, 8f) });
        }

        for (int i = 0; i < _resourceRow.GetChildCount(); i++)
        {
            var pip = (ColorRect)_resourceRow.GetChild(i);
            pip.Visible = i < res.Max;
            pip.Color = i < res.Value ? res.Color : new Color(0.2f, 0.2f, 0.25f, 0.8f);
        }
    }

    private void UpdateSkills()
    {
        List<SkillInstance> skills = Player.SkillUser.EquippedSkills().Select(s => s.Skill).ToList();
        string signature = string.Join("|", skills.Select(s => $"{s.Definition.DisplayName}{s.Level}{s.Attached.Count}"));
        if (signature != _skillSignature)
        {
            _skillSignature = signature;
            foreach (SkillSlotView view in _slots)
            {
                view.QueueFree();
            }

            _slots.Clear();
            int key = 1;
            foreach (SkillInstance skill in skills)
            {
                var view = new SkillSlotView(skill, key++);
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
        bool show = Reality.BossFightActive && _boss is not null && IsInstanceValid(_boss) && !_boss.Health.IsDead;
        _bossBox.Visible = show;
        if (!show)
        {
            return;
        }

        _bossBar.Value = _boss!.LifeFraction;
        _bossAttack.Text = _boss.CurrentAttack.Length > 0 ? $"« {_boss.CurrentAttack} »" : "";
    }

    /// <summary>Explored floor, the player, the boss room and the exit, PoE-style.</summary>
    private sealed partial class Minimap : Control
    {
        public RealityController Reality = null!;

        public override void _Draw()
        {
            RasterLayout r = Reality.Raster;
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0f, 0f, 0f, 0.45f));
            float px = Mathf.Min(Size.X / r.Width, Size.Y / r.Height);
            Vector2 origin = (Size - new Vector2(r.Width, r.Height) * px) / 2f;
            var floor = new Color(0.75f, 0.72f, 0.85f, 0.55f);
            var boss = new Color(0.9f, 0.3f, 0.25f, 0.6f);
            foreach (Vector2I c in Reality.Explored)
            {
                if (!r.Walkable(c))
                {
                    continue;
                }

                DrawRect(new Rect2(origin + new Vector2(c.X, c.Y) * px, Vector2.One * px), r.BossRegion.Contains(c) ? boss : floor);
            }

            Vector2I p = Reality.Field.CellOf(Reality.Player.GlobalPosition);
            DrawCircle(origin + (new Vector2(p.X, p.Y) + Vector2.One * 0.5f) * px, Mathf.Max(2.5f, px * 0.8f), new Color(0.4f, 1f, 0.5f));
            Vector2I e = r.Entrance;
            DrawRect(new Rect2(origin + new Vector2(e.X, e.Y) * px, Vector2.One * px), new Color(0.4f, 0.7f, 1f));
            if (Reality.Completed)
            {
                Vector2I b = r.BossCell;
                DrawCircle(origin + (new Vector2(b.X, b.Y) + Vector2.One * 0.5f) * px, Mathf.Max(3f, px), new Color(1f, 0.85f, 0.4f));
            }

            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.35f, 0.28f, 0.62f), false, 1.5f);
        }
    }
}
