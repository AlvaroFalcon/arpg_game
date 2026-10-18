#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.World;

namespace ArpgGame.UI;

/// <summary>
/// The Mirror of Realities — docs/design/endgame.md -> "The Mirror". Pick a
/// shard tier (tier 1 is always free), optionally empower it for gold (rolls
/// one to three shard modifiers: harder, more loot), and step through. The
/// Survival Rift (the original survivor mode) is offered here too. The Jewels tab
/// holds the Mirror's jewel tree (docs/design/endgame.md -> "Mirror jewels").
/// </summary>
public partial class MirrorPanel : ModalPanel
{
    public Player Player { get; set; } = null!;

    /// <summary>Called with the scene to travel to (the hideout saves first).</summary>
    public Action<string>? Travel { get; set; }

    protected override string Title => "Mirror of Realities";

    protected override Vector2 FrameSize => new(1060f, 660f);

    private Button _realitiesTab = null!;
    private Button _jewelsTab = null!;
    private VBoxContainer _realityPage = null!;
    private VBoxContainer _jewelPage = null!;
    private JewelTreeView _jewelView = null!;
    private Label _jewelSummary = null!;
    private Label _jewelTotals = null!;
    private HoverTooltip _tooltip = null!;

    /// <summary>The Jewels tab is showing.</summary>
    public bool ShowingJewels { get; private set; }

    public JewelTreeView JewelView => _jewelView;

    private GridContainer _tiers = null!;
    private Label _tierTitle = null!;
    private Label _tierInfo = null!;
    private Label _modsLabel = null!;
    private Button _empower = null!;
    private Button _open = null!;
    private Label _gold = null!;
    private int _selected = 1;
    private List<string> _mods = new();
    private readonly RandomNumberGenerator _rng = new();

    private CharacterData? Character => Profile.Get(this)?.Active;

    private long Gold => Player.Wallet.GetBalance(Profile.GoldId);

    protected override void Build(VBoxContainer outer)
    {
        _rng.Randomize();
        HBoxContainer tabs = UiTheme.HBox(outer, 6);
        _realitiesTab = UiTheme.Button("Realities", () => ShowTab(false), 16, 160f);
        _jewelsTab = UiTheme.Button("Jewels", () => ShowTab(true), 16, 160f);
        tabs.AddChild(_realitiesTab);
        tabs.AddChild(_jewelsTab);
        _realityPage = UiTheme.VBox(outer, 10);
        _realityPage.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        BuildJewelPage(outer);
        VBoxContainer body = _realityPage;
        body.AddChild(UiTheme.Label("Each Mirror Shard opens one Altered Reality of its tier. Bosses drop shards of the same tier or higher.", 16, UiTheme.TextDim));
        HBoxContainer columns = UiTheme.HBox(body, 24);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        VBoxContainer left = UiTheme.VBox(columns, 6);
        left.AddChild(UiTheme.Label("Shards", 20, UiTheme.Text));
        _tiers = new GridContainer { Columns = 4 };
        _tiers.AddThemeConstantOverride("h_separation", 6);
        _tiers.AddThemeConstantOverride("v_separation", 6);
        left.AddChild(_tiers);

        VBoxContainer right = UiTheme.VBox(columns, 10);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _tierTitle = UiTheme.Label("", 26, UiTheme.Gold);
        right.AddChild(_tierTitle);
        _tierInfo = UiTheme.Label("", 17, UiTheme.Text);
        _tierInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(_tierInfo);
        _modsLabel = UiTheme.Label("", 16, UiTheme.Ember);
        _modsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(_modsLabel);
        _gold = UiTheme.Label("", 16, UiTheme.Gold);
        right.AddChild(_gold);
        _empower = UiTheme.Button("Empower", Empower, 18, 320f);
        right.AddChild(_empower);
        _open = UiTheme.Button("Step through the Mirror", OpenReality, 22, 320f);
        right.AddChild(_open);
        right.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        right.AddChild(UiTheme.Label("Survival Rift: the endless arena — gear, gold and character experience.", 15, UiTheme.TextDim));
        right.AddChild(UiTheme.Button("Enter the Survival Rift", () => Leave(Scenes.Run), 18, 320f));
        ShowTab(false);
    }

    private void BuildJewelPage(VBoxContainer outer)
    {
        _jewelPage = UiTheme.VBox(outer, 8);
        _jewelPage.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _jewelSummary = UiTheme.Label("", 16, UiTheme.Text);
        _jewelPage.AddChild(_jewelSummary);
        HBoxContainer row = UiTheme.HBox(_jewelPage, 12);
        row.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _jewelView = new JewelTreeView { CustomMinimumSize = new Vector2(740f, 500f), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _jewelView.Hovered = HoverJewel;
        _jewelView.Clicked = (n, remove) => ClickJewel(n, remove);
        row.AddChild(_jewelView);
        VBoxContainer side = UiTheme.VBox(row, 6);
        side.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        side.AddChild(UiTheme.Label("Your jewels grant", 17, UiTheme.Gold));
        _jewelTotals = UiTheme.Label("", 14, new Color(0.8f, 0.72f, 1f));
        _jewelTotals.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _jewelTotals.CustomMinimumSize = new Vector2(250f, 0f);
        side.AddChild(_jewelTotals);
        _tooltip = new HoverTooltip();
        Root.AddChild(_tooltip);
        _jewelPage.Visible = false;
    }

    public void ShowTab(bool jewels)
    {
        ShowingJewels = jewels;
        _realityPage.Visible = !jewels;
        _jewelPage.Visible = jewels;
        foreach ((Button b, bool on) in new[] { (_realitiesTab, !jewels), (_jewelsTab, jewels) })
        {
            b.AddThemeStyleboxOverride("normal", UiTheme.Box(on ? new Color(0.2f, 0.15f, 0.3f) : new Color(0.08f, 0.07f, 0.11f), on ? UiTheme.Gold : UiTheme.PanelBorder, on ? 2 : 1));
            b.AddThemeColorOverride("font_color", on ? UiTheme.Gold : UiTheme.TextDim);
        }

        _tooltip.HideTooltip();
        Refresh();
    }

    private JewelData Jewels => Profile.Get(this)?.Jewels ?? _testJewels;

    private readonly JewelData _testJewels = new();

    private void RefreshJewels()
    {
        JewelData data = Jewels;
        _jewelView.Data = data;
        int next = JewelTree.RequiredTier(data.Found);
        string source = next == 0 ? "every socket can be filled — no more jewels drop"
            : next == 1 ? $"any reality boss can drop one (first {JewelTree.EasyJewels})"
            : $"only bosses of tier {next}+ drop more";
        _jewelSummary.Text = $"{data.Free} jewel{(data.Free == 1 ? "" : "s")} to socket  ·  {data.Socketed.Count}/{JewelTree.Sockets} sockets filled  ·  {data.Found} found — {source}   ·   LMB socket · RMB remove · drag · wheel";
        JewelBonuses b = JewelTree.Bonuses(data);
        var lines = Enum.GetValues<JewelStat>().Where(s => b[s] > 0f).Select(s => "• " + JewelTree.Text(s, b[s])).ToList();
        _jewelTotals.Text = lines.Count == 0 ? "Nothing yet. Socket a jewel next to the Mirror's Heart." : string.Join("\n", lines);
        _jewelsTab.Text = data.Free > 0 ? $"Jewels ({data.Free})" : "Jewels";
        _jewelView.QueueRedraw();
    }

    private void HoverJewel(JewelNode? node)
    {
        if (node is null)
        {
            _tooltip.HideTooltip();
            return;
        }

        JewelData data = Jewels;
        Color title = node.Branch >= 0 ? JewelTree.BranchColors[node.Branch] : new Color(0.85f, 0.7f, 1f);
        _tooltip.Begin(node.Name, title);
        _tooltip.AddLine(node.Kind switch { JewelNodeKind.Heart => "The root of the tree", JewelNodeKind.Notable => "Notable socket", _ => "Socket" }, UiTheme.TextDim, 13);
        foreach (string line in node.Describe().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            _tooltip.AddLine(line, new Color(0.8f, 0.72f, 1f));
        }

        if (node.Kind != JewelNodeKind.Heart)
        {
            bool on = data.Socketed.Contains(node.Id);
            string state = on ? (JewelTree.CanUnsocket(data, node.Id) ? "Socketed — right-click to take the jewel out" : "Socketed (others depend on it)")
                : JewelTree.CanSocket(data, node.Id) ? "Left-click to socket a jewel"
                : data.Free == 0 ? "No jewels to socket" : "Not connected yet";
            _tooltip.AddLine(state, on ? UiTheme.Gold : JewelTree.CanSocket(data, node.Id) ? UiTheme.Experience : UiTheme.TextDim, 13);
        }

        _tooltip.ShowTooltip();
    }

    public bool ClickJewel(JewelNode node, bool remove)
    {
        JewelData data = Jewels;
        bool changed = remove ? JewelTree.Unsocket(data, node.Id) : JewelTree.Socket(data, node.Id);
        if (!changed)
        {
            return false;
        }

        Sfx.Play(this, remove ? "ui" : "levelup", -8f);
        Profile.Get(this)?.Save();
        RefreshJewels();
        HoverJewel(node);
        if (!remove)
        {
            GameEvents.Raise(GameEvents.JewelSocketed, 0, node.Id);
        }

        return true;
    }

    protected override void OnClosed() => _tooltip.HideTooltip();

    public override void Refresh()
    {
        RefreshJewels();
        CharacterData? c = Character;
        ClearChildren(_tiers);
        for (int tier = 1; tier <= RealityTiers.MaxTier; tier++)
        {
            int t = tier;
            int owned = c?.ShardCount(tier) ?? 0;
            string count = tier == 1 ? "∞" : owned.ToString();
            Button b = UiTheme.Button($"T{tier}\n{count}", () => Select(t), 15, 74f);
            b.CustomMinimumSize = new Vector2(74f, 58f);
            b.Disabled = tier > 1 && owned <= 0;
            b.AddThemeColorOverride("font_color", RealityTiers.ShardColor(tier));
            if (tier == _selected)
            {
                b.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.22f, 0.17f, 0.3f), RealityTiers.ShardColor(tier), 2));
            }

            _tiers.AddChild(b);
        }

        int t2 = _selected;
        int portals = RealityTiers.Portals(t2);
        _tierTitle.Text = $"Tier {t2} Mirror Shard";
        _tierTitle.AddThemeColorOverride("font_color", RealityTiers.ShardColor(t2));
        _tierInfo.Text =
            $"Monster level {RealityTiers.MonsterLevel(t2)}  ·  monster life ×{RealityTiers.LifeMultiplier(t2):0.0}  ·  damage ×{RealityTiers.DamageMultiplier(t2):0.0}\n"
            + $"{portals} portal{(portals == 1 ? "" : "s")} (deaths allowed)  ·  a new cavern every time\n"
            + $"Magic packs {RealityTiers.MagicPackChance(t2) * 100f:0}%  ·  rare packs {RealityTiers.RarePackChance(t2) * 100f:0}%  ·  unique-rare chance {RealityTiers.UniqueChance(t2) * 100f:0}%\n"
            + (c is null ? "" : $"Highest tier cleared: {c.HighestTierCleared}  ·  owned: {(t2 == 1 ? "always free" : c.ShardCount(t2).ToString())}");
        (float q, float r) = RealityLaunch.Bonuses(_mods);
        _modsLabel.Text = _mods.Count == 0
            ? "Not empowered."
            : "Empowered:\n  " + string.Join("\n  ", _mods.Select(m => RealityModifier.Find(m)?.Text ?? m)) + $"\n  → +{q * 100f:0}% item quantity, +{r * 100f:0}% item rarity";
        long cost = RealityLaunch.EmpowerCost(t2);
        _gold.Text = $"Gold: {Gold}";
        _empower.Text = _mods.Count == 0 ? $"Empower ({cost} gold)" : $"Re-roll modifiers ({cost} gold)";
        _empower.Disabled = Gold < cost;
        _open.Disabled = c is not null && t2 > 1 && c.ShardCount(t2) <= 0;
    }

    public void Select(int tier)
    {
        if (tier != _selected)
        {
            _mods = new List<string>();
        }

        _selected = tier;
        Refresh();
    }

    public void Empower()
    {
        if (!Player.Wallet.TrySpend(Profile.GoldId, RealityLaunch.EmpowerCost(_selected)))
        {
            return;
        }

        _mods = RealityLaunch.RollModifiers(_selected, _rng);
        Sfx.Play(this, "levelup", -6f);
        Refresh();
    }

    private void OpenReality()
    {
        if (Character is { } c && !c.TrySpendShard(_selected))
        {
            return;
        }

        RealityLaunch.Tier = _selected;
        RealityLaunch.LayoutId = null;
        RealityLaunch.Modifiers = new List<string>(_mods);
        _mods = new List<string>();
        Leave(Scenes.Reality);
    }

    private void Leave(string scene)
    {
        Close();
        Travel?.Invoke(scene);
    }
}
