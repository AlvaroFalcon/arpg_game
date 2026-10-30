#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Enemies;
using ArpgGame.Items;
using ArpgGame.Skills;
using ArpgGame.World;

namespace ArpgGame.UI;

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

    private PlayerHudBar _bar = null!;
    private Control _infoHost = null!;

    /// <summary>The reality's info shows (only with the Tab map; tests).</summary>
    public bool InfoShowing => _infoHost.Visible;
    private Label _titleLabel = null!;
    private Label _infoLabel = null!;
    private Label _modsLabel = null!;
    private SkillBar _skillRow = null!;
    private Label _banner = null!;
    private Label _bannerSub = null!;
    private Label _callout = null!;
    private VBoxContainer _toasts = null!;
    private Minimap _minimap = null!;
    private HudCorner _corner = null!;

    /// <summary>The top-right corner: minimap, clock, quests (tests).</summary>
    public HudCorner Corner => _corner;
    private float _bannerTime;
    private float _calloutTime;
    private BossEnemy? _boss;
    private TargetFrame _target = null!;
    private CharacterBuild? _build;

    public override void _Ready()
    {
        Layer = 10;
        Control root = UiTheme.FullRect(this);


        // --- top-left, under the character: the reality's name, tier, state and modifiers ---
        // Only while the Tab map is open, over it (owner, 2026-10-30); the character's name
        // and level left the HUD (they are in the character panel, C).
        _infoHost = new Control { Name = "RealityInfoHost", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        var info = new VBoxContainer { Name = "RealityInfo", MouseFilter = Control.MouseFilterEnum.Ignore };
        info.AddThemeConstantOverride("separation", 2);
        _infoHost.AddChild(info);
        info.Position = new Vector2(16f, 16f);
        _titleLabel = UiTheme.Label("", 18, RealityTiers.ShardColor(Reality.Tier));
        info.AddChild(_titleLabel);
        _infoLabel = UiTheme.Label("", 14, UiTheme.Text);
        _infoLabel.CustomMinimumSize = new Vector2(330f, 0f);
        info.AddChild(_infoLabel);
        _modsLabel = UiTheme.Label("", 13, UiTheme.Ember);
        _modsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _modsLabel.CustomMinimumSize = new Vector2(330f, 0f);
        info.AddChild(_modsLabel);

        // --- top-right: the minimap, the local time, the quests (docs/design/menus.md -> "HUD: top-right corner") ---
        _minimap = new Minimap { Reality = Reality };
        MapOverlay overlay = new() { Reality = Reality, Name = "MapOverlay" };
        _corner = new HudCorner
        {
            Map = _minimap,
            Tracker = new QuestTracker { OpenLog = id => Reality.GetNodeOrNull<CharacterPanels>("CharacterPanels")?.Quests.OpenOn(id) },
            BigMapOpen = () => overlay.Visible,
        };

        // --- boss bar ---
        _target = new TargetFrame { Player = Player, Boss = () => Reality.BossFightActive ? _boss : null, Name = "TargetFrame" };
        root.AddChild(_target);
        _target.Dock(30f);

        // --- the Tab overlay map, under every other HUD element ---
        root.AddChild(overlay);
        root.AddChild(_infoHost);
        root.AddChild(_corner);
        _corner.Dock();

        // --- bottom centre: life, mana, level badge, flasks, skills ---
        _bar = new PlayerHudBar
        {
            Player = Player,
            Progress = () => _build is null ? (Profile.Get(this)?.Active.Level ?? 1, 0f) : (_build.Data.Level, _build.ExperienceFraction),
            Resource = () => _build?.Resource,
        };
        root.AddChild(_bar);
        _bar.Dock();
        _skillRow = PlayerHudBar.CreateSkillRow(root);
        if (Flasks.Of(Player) is { } flasks)
        {
            flasks.Refused += reason => ShowToast(reason, UiTheme.TextDim);
        }

        // --- toasts (left, under the level) ---
        _toasts = new VBoxContainer();
        _toasts.AddThemeConstantOverride("separation", 2);
        root.AddChild(_toasts);
        _toasts.Position = new Vector2(16f, 16f);
        _toasts.CustomMinimumSize = new Vector2(520f, 0f);

        // --- loot feed (left, mid-height) ---
        LootFeedView feed = AddLootFeed(root);

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

        // Movable in the HUD editor (pause menu → Edit HUD).
        HudLayout.Register(info, "reality_info", "Reality info (Tab map)", on => _infoHost.Visible = on || _corner.BigMapOpen(), HudAvoid.Hide);
        HudLayout.Register(_corner, "corner", "Minimap, clock & quests", on => _corner.Tracker?.SetPreview(on), HudAvoid.Hide);
        HudLayout.Register(_target, "boss", "Target / boss life", _target.Preview, HudAvoid.Slide);
        HudLayout.Register(_bar, "vitals", "Life, mana & flasks");
        HudLayout.Register(_skillRow, "skills", "Skills");
        HudLayout.Register(_toasts, "toasts", "Notifications", on => PreviewToasts(_toasts, on));
        HudLayout.Register(feed, "loot_feed", "Loot picked up", feed.SetPreview);

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

    /// <summary>The loot feed on the left edge, a little above mid-height (docs/design/menus.md -> "Loot feed").</summary>
    public static LootFeedView AddLootFeed(Control root)
    {
        var feed = new LootFeedView();
        root.AddChild(feed);
        feed.SetAnchorsPreset(Control.LayoutPreset.CenterLeft, true);
        feed.OffsetLeft = 0f;
        feed.OffsetTop = -110f;
        feed.OffsetRight = LootFeedView.RowWidth;
        feed.OffsetBottom = -110f + LootFeedView.MaxRows * (LootFeedView.RowHeight + LootFeedView.RowGap);
        return feed;
    }

    /// <summary>A placeholder line so the (usually empty) notification list has something to grab.</summary>
    public static void PreviewToasts(VBoxContainer toasts, bool on)
    {
        if (on && toasts.GetNodeOrNull("Preview") is null)
        {
            Label sample = UiTheme.Label("Notifications appear here", 16, UiTheme.TextDim);
            sample.Name = "Preview";
            sample.SetMeta("t", 9999f);
            toasts.AddChild(sample);
        }
        else if (!on && toasts.GetNodeOrNull("Preview") is { } preview)
        {
            preview.QueueFree();
        }
    }

    public void ShowBanner(string title, string subtitle, Color color)
    {
        _banner.Text = title;
        _banner.AddThemeColorOverride("font_color", color);
        _bannerSub.Text = subtitle;
        _bannerTime = 3.2f;
    }

    /// <summary>Where the quest toast's card goes: under the loot toasts' lines, wherever Edit HUD put them.</summary>
    public Vector2 QuestToastAnchor() => _toasts.GlobalPosition + new Vector2(0f, 158f);

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
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _infoHost.Visible = _corner.BigMapOpen();

        int portals = Reality.PortalsLeft;
        string cleared = Reality.MonstersTotal > 0 ? $"{Mathf.RoundToInt(100f * Reality.Kills / Reality.MonstersTotal)}%" : "-";
        _infoLabel.Text = $"Monster level {Reality.MonsterLevel}  ·  {portals} portal{(portals == 1 ? "" : "s")} left\nCleared {cleared}  ·  {UiTheme.FormatTime(Reality.Elapsed)}";

        UpdateSkills();
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

    private void UpdateSkills()
    {
        // Fixed bar order matching the bindings: LMB, RMB | mirror | Q, E (empty slots show too).
        _skillRow.Show(CharacterBuild.Slots.Select(slot => (SkillSlotView.LabelOf(slot), Player.SkillUser.GetSkill(slot))).ToList());
        _skillRow.Cooldowns(Player.SkillUser.CooldownFraction);
    }

    /// <summary>The minimap (tests read its allies).</summary>
    public Control MinimapControl => _minimap;

    /// <summary>The party members on the minimap (none offline; tests).</summary>
    public List<AllyMarker> MinimapAllies() => _minimap.Allies();

    /// <summary>Explored floor, the player, the allies, the boss room and the exit, PoE-style.</summary>
    private sealed partial class Minimap : Control
    {
        public RealityController Reality = null!;

        public List<AllyMarker> Allies() => PartyMarkers.Allies(Reality, Reality.Player);

        public override void _Draw()
        {
            RasterLayout r = Reality.Raster;
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.02f, 0.01f, 0.04f, 0.72f));
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

            // Allies (docs/design/multiplayer.md -> "Allies on the maps"), under your own dot.
            foreach (AllyMarker ally in Allies())
            {
                Vector2I a = Reality.Field.CellOf(ally.Position);
                PartyMarkers.Draw(this, origin + (new Vector2(a.X, a.Y) + Vector2.One * 0.5f) * px, ally, Mathf.Max(2.5f, px * 0.8f), label: false);
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
