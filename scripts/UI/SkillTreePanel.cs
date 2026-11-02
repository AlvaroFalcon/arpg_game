#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Progression;
using ArpgGame.Skills;

namespace ArpgGame.UI;

/// <summary>Escaldón's two tabs (docs/design/skills.md -> "Escaldón sells the skills").</summary>
public enum EscaldonTab
{
    Upgrade,
    BuySkills,
}

/// <summary>
/// Escaldón's workshop — docs/design/skills.md -> "Skill upgrade trees" (owner's brief,
/// 2026-10-30). Left: every skill gem the character owns, with its level and unspent
/// points. Right: the chosen skill — its level and the gold to raise it (up to 20, each
/// level dearer), and its upgrade tree: three branches of nodes, lesser ones with several
/// ranks and notables at the end, some of them either/or. Left click spends a point,
/// right click takes one back for a little gold. Changes reach the equipped skill at once.
/// </summary>
public partial class SkillTreePanel : ModalPanel
{
    public Player Player { get; set; } = null!;

    /// <summary>The character to work on; defaults to the profile's active one (tests set it).</summary>
    public CharacterData? Character { get; set; }

    protected override string Title => "Escaldón";

    public override string? StationUid => "escaldon";

    protected override Vector2 FrameSize => new(1060f, 620f);

    public static readonly Color Accent = new(0.4f, 0.85f, 0.75f);

    private VBoxContainer _list = null!;
    private Label _name = null!;
    private Label _level = null!;
    private Label _desc = null!;
    private Button _raise = null!;
    private Label _gold = null!;
    private Label _message = null!;
    private TreeCanvas _canvas = null!;
    private GemData? _gem;
    private Control _upgradePage = null!;
    private Button _upgradeTab = null!;
    private Button _buyTab = null!;
    private Panel _buyDot = null!;
    private bool _gemsSeenThisVisit;

    /// <summary>The tab showing (the quest dialogue's "Open Buy skills" sets it).</summary>
    public EscaldonTab Tab { get; private set; }

    /// <summary>The skill shop (moved here from Odran's stall on 2026-11-01).</summary>
    public GemShopView Gems { get; private set; } = null!;

    /// <summary>The ember dot on the Buy skills tab: gems unlocked since it was last opened.</summary>
    public bool GemsTabDot => _buyDot.Visible;

    /// <summary>The tree's drawing (tests point the mouse at a node through it).</summary>
    public Control TreeView => _canvas;

    /// <summary>Points at a node as the mouse would (tests, captures); null clears it.</summary>
    public void HoverNode(string? id) => _canvas.Hover = id;

    /// <summary>The gem being worked on (tests).</summary>
    public GemData? Selected => _gem;

    /// <summary>The last line said to the player (tests).</summary>
    public string Message => _message.Text;

    private CharacterData? Who => Character ?? CharacterBuild.Of(Player)?.Data ?? Profile.Get(this)?.Active;

    protected override void Build(VBoxContainer body)
    {
        TitleLabel.AddThemeColorOverride("font_color", Accent);
        Label quote = UiTheme.Label("\"Every skill can be more than it is. Gold buys the practice; the choices are yours.\"", 15, UiTheme.TextDim);
        quote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(quote);

        HBoxContainer tabs = UiTheme.HBox(body, 6);
        _upgradeTab = UiTheme.Button("Your skills", () => ShowTab(EscaldonTab.Upgrade), 16, 170f);
        _buyTab = UiTheme.Button("Buy skills", () => ShowTab(EscaldonTab.BuySkills), 16, 170f);
        foreach (Button t in new[] { _upgradeTab, _buyTab })
        {
            t.CustomMinimumSize = new Vector2(170f, 38f);
            tabs.AddChild(t);
        }

        _buyDot = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, Position = new Vector2(154f, 6f), Size = new Vector2(10f, 10f) };
        var dot = new StyleBoxFlat { BgColor = UiTheme.Ember };
        dot.SetCornerRadiusAll(5);
        _buyDot.AddThemeStyleboxOverride("panel", dot);
        _buyTab.AddChild(_buyDot);

        Gems = new GemShopView { Player = Player, CharacterSource = () => Who, OpenSkills = OpenSkillsPanel, SizeFlagsVertical = Control.SizeFlags.ExpandFill, Name = "GemShopView", Visible = false };
        body.AddChild(Gems);

        HBoxContainer columns = UiTheme.HBox(body, 18);
        _upgradePage = columns;
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        VBoxContainer left = UiTheme.VBox(columns, 6);
        left.CustomMinimumSize = new Vector2(240f, 0f);
        left.AddChild(UiTheme.Label("Your skills", 16, Accent));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(240f, 420f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        left.AddChild(scroll);
        _list = UiTheme.VBox(scroll, 4);
        _list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _gold = UiTheme.Label("", 15, UiTheme.Gold);
        left.AddChild(_gold);

        VBoxContainer right = UiTheme.VBox(columns, 6);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        HBoxContainer head = UiTheme.HBox(right, 12);
        VBoxContainer titles = UiTheme.VBox(head, 0);
        titles.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _name = UiTheme.Label("", 22, UiTheme.Gold);
        titles.AddChild(_name);
        _level = UiTheme.Label("", 14, UiTheme.Text);
        titles.AddChild(_level);
        _raise = UiTheme.PrimaryButton("", () => RaiseLevel(), 15, 250f);
        _raise.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(_raise);
        UiTheme.InstantTooltip(_raise, () => _gem is null ? "" : CannotRaiseLevel(_gem) ?? $"+1 point for the tree, {SkillInstance.DamagePerLevel * 100f:0}% more damage and {SkillInstance.CostPerLevel * 100f:0}% more mana cost per level");
        _desc = UiTheme.Label("", 13, UiTheme.TextDim);
        _desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _desc.CustomMinimumSize = new Vector2(700f, 0f);
        right.AddChild(_desc);
        _canvas = new TreeCanvas { Panel = this, CustomMinimumSize = new Vector2(740f, 380f) };
        right.AddChild(_canvas);
        _message = UiTheme.Label("", 14, UiTheme.Text);
        right.AddChild(_message);
        StyleTabs();
    }

    /// <summary>Shows a tab (the quest dialogue's "Open Buy skills" uses this too).</summary>
    public void ShowTab(EscaldonTab tab)
    {
        Tab = tab;
        _upgradePage.Visible = tab == EscaldonTab.Upgrade;
        Gems.Visible = tab == EscaldonTab.BuySkills;
        StyleTabs();
        if (tab == EscaldonTab.BuySkills)
        {
            Gems.Player = Player;
            Gems.OnShown(!_gemsSeenThisVisit);
            _gemsSeenThisVisit = true;
        }
        else
        {
            Refresh();
        }

        UpdateDot();
    }

    private void StyleTabs()
    {
        foreach ((Button b, bool on) in new[] { (_upgradeTab, Tab == EscaldonTab.Upgrade), (_buyTab, Tab == EscaldonTab.BuySkills) })
        {
            b.AddThemeStyleboxOverride("normal", UiTheme.Box(on ? new Color(0.1f, 0.2f, 0.18f) : new Color(0.08f, 0.07f, 0.11f), on ? Accent : UiTheme.PanelBorder, on ? 2 : 1));
            b.AddThemeColorOverride("font_color", on ? Accent : UiTheme.TextDim);
        }
    }

    private void UpdateDot() => _buyDot.Visible = Tab != EscaldonTab.BuySkills && Who is { } c && GemShop.NewCount(c) > 0;

    private void OpenSkillsPanel()
    {
        CharacterPanels? panels = GetParent()?.GetNodeOrNull<CharacterPanels>("CharacterPanels");
        Close();
        panels?.Skills.Open();
    }

    protected override void OnClosed()
    {
        _gemsSeenThisVisit = false;
        Tab = EscaldonTab.Upgrade;
        _upgradePage.Visible = true;
        Gems.Visible = false;
        StyleTabs();
        // A gem bought or levelled here is saved when the panel closes, as at Odran's.
        Profile.Get(this)?.CaptureFrom(Player);
    }

    /// <summary>Picks the gem whose tree shows (tests, the list).</summary>
    public void Select(GemData gem)
    {
        _gem = gem;
        _message.Text = "";
        Refresh();
    }

    public override void Refresh()
    {
        if (Who is not { } c)
        {
            return;
        }

        Gems.Player = Player;
        UpdateDot();
        if (Tab == EscaldonTab.BuySkills)
        {
            Gems.Rebuild();
        }

        List<GemData> skills = c.Gems.Where(g => !g.IsSupport && SkillTrees.For(g.Id) is not null).OrderByDescending(g => Slot(c, g) is not null).ThenBy(g => GemLoot.NameOf(g.Id, false)).ToList();
        if (_gem is null || !skills.Contains(_gem))
        {
            _gem = skills.FirstOrDefault();
        }

        ClearChildren(_list);
        foreach (GemData g in skills)
        {
            _list.AddChild(SkillRow(c, g));
        }

        _gold.Text = $"Gold: {Player.Wallet.GetBalance(Profile.GoldId):N0}";
        if (_gem is null || SkillTrees.For(_gem.Id) is not { } tree)
        {
            _name.Text = "No skills yet";
            _level.Text = "Buy your first skill in the Buy skills tab.";
            _desc.Text = "";
            _raise.Visible = false;
            _canvas.Tree = null;
            _canvas.QueueRedraw();
            return;
        }

        SkillDefinition? def = UpgradeCatalog.FindSkill(_gem.Id)?.Definition;
        _name.Text = def?.DisplayName ?? _gem.Id;
        _level.Text = $"Level {_gem.Level} / {GemData.MaxLevel}   ·   {_gem.TreePointsFree} point{(_gem.TreePointsFree == 1 ? "" : "s")} to spend   ·   {_gem.TreePointsSpent} spent";
        _desc.Text = def?.Description ?? "";
        _raise.Visible = true;
        string? why = CannotRaiseLevel(_gem);
        _raise.Text = _gem.Level >= GemData.MaxLevel ? "Mastered" : $"Raise to level {_gem.Level + 1} · {SkillTrees.LevelCost(_gem.Level):N0} g";
        _raise.Disabled = why is not null;
        _canvas.Tree = tree;
        _canvas.Gem = _gem;
        _canvas.QueueRedraw();
    }

    private static string? Slot(CharacterData c, GemData g)
    {
        for (int i = 0; i < c.Loadout.Length; i++)
        {
            if (c.Loadout[i].SkillUid == g.Uid)
            {
                return CharacterData.SlotLabels[i];
            }
        }

        return null;
    }

    private Control SkillRow(CharacterData c, GemData g)
    {
        bool selected = g == _gem;
        string name = GemLoot.NameOf(g.Id, false);
        string slot = Slot(c, g) is { } s ? $"  [{s}]" : "";
        Button row = UiTheme.Button($"{name}{slot}", () => Select(g), 14, 230f);
        row.Alignment = HorizontalAlignment.Left;
        row.Text = $"{name}{slot}   Lv {g.Level}" + (g.TreePointsFree > 0 ? $"  · {g.TreePointsFree} pt" : "");
        if (selected)
        {
            row.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.12f, 0.22f, 0.2f), Accent, 2, 4, 8));
        }

        if (UpgradeCatalog.FindSkill(g.Id)?.Definition.Icon is { } icon)
        {
            row.Icon = icon;
            row.ExpandIcon = false;
            row.AddThemeConstantOverride("icon_max_width", 20);
        }

        return row;
    }

    private string? CannotRaiseLevel(GemData gem)
    {
        if (gem.Level >= GemData.MaxLevel)
        {
            return "This skill is at its highest level";
        }

        long cost = SkillTrees.LevelCost(gem.Level);
        long gold = Player.Wallet.GetBalance(Profile.GoldId);
        return gold < cost ? $"Not enough gold: {cost - gold:N0} more" : null;
    }

    /// <summary>Spends the gold and raises the selected gem one level (tests call it too).</summary>
    public bool RaiseLevel()
    {
        if (_gem is null || CannotRaiseLevel(_gem) is { } why)
        {
            _message.Text = _gem is null ? "" : CannotRaiseLevel(_gem) ?? "";
            return false;
        }

        Player.Wallet.TrySpend(Profile.GoldId, SkillTrees.LevelCost(_gem.Level));
        _gem.Level++;
        _message.Text = $"{GemLoot.NameOf(_gem.Id, false)} reached level {_gem.Level}: one more point for its tree.";
        Sfx.Play(this, "levelup", -6f);
        Apply();
        return true;
    }

    /// <summary>Spends a point on a node (left click).</summary>
    public bool RaiseNode(string nodeId)
    {
        if (_gem is null || SkillTrees.For(_gem.Id) is not { } tree || tree.Node(nodeId) is not { } node)
        {
            return false;
        }

        if (SkillTreeRules.CannotRaise(_gem, tree, node) is { } why)
        {
            _message.Text = why;
            return false;
        }

        SkillTreeRules.Raise(_gem, tree, node);
        _message.Text = $"{node.Name} {SkillTreeRules.RankOf(_gem, node.Id)}/{node.MaxRank}: {node.Describe(SkillTreeRules.RankOf(_gem, node.Id))}";
        Sfx.Play(this, "pickup", -4f);
        Apply();
        return true;
    }

    /// <summary>Takes a point back from a node for a little gold (right click).</summary>
    public bool LowerNode(string nodeId)
    {
        if (_gem is null || SkillTrees.For(_gem.Id) is not { } tree || tree.Node(nodeId) is not { } node)
        {
            return false;
        }

        if (SkillTreeRules.CannotLower(_gem, tree, node) is { } why)
        {
            _message.Text = why;
            return false;
        }

        long cost = SkillTrees.RefundCost(_gem.Level);
        if (!Player.Wallet.TrySpend(Profile.GoldId, cost))
        {
            _message.Text = $"Taking a point back costs {cost:N0} gold";
            return false;
        }

        SkillTreeRules.Lower(_gem, tree, node);
        _message.Text = $"Took a point back from {node.Name} ({cost:N0} gold).";
        Sfx.Play(this, "ui", -4f);
        Apply();
        return true;
    }

    /// <summary>The equipped skill picks the change up at once.</summary>
    private void Apply()
    {
        CharacterBuild.Of(Player)?.EquipLoadout();
        Refresh();
    }

    /// <summary>The tree: branches as rows, depth as columns, lines to what each node needs.</summary>
    private sealed partial class TreeCanvas : Control
    {
        public SkillTreePanel Panel = null!;
        public SkillTree? Tree;
        public GemData? Gem;
        private string? _hover;

        private const float ColW = 240f;
        private const float NodeW = 214f;
        private const float RowH = 124f;
        private const float NodeH = 74f;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Stop;
            // One tooltip per node, with every rank's full text (the node's own lines are cut to
            // its width): refilled whenever the mouse moves onto another node.
            UiTheme.InstantTooltip(this, FillTip);
        }

        /// <summary>Shows the tooltip of the node under the mouse now (or hides it).</summary>
        public void RefreshTip() => UiTheme.RefreshInstantTooltip(this, FillTip);

        /// <summary>The node whose tooltip shows (tests point at one).</summary>
        public string? Hover
        {
            get => _hover;
            set
            {
                _hover = value;
                QueueRedraw();
                RefreshTip();
            }
        }

        /// <summary>Where a node is drawn; two notables at the same spot (a choice) share it, one above the other.</summary>
        public Rect2 RectOf(SkillTreeNode node)
        {
            float x = 28f + node.Depth * ColW;
            float y = 26f + node.Branch * RowH;
            if (Tree is not null && node.Exclusive is not null)
            {
                List<SkillTreeNode> pair = Tree.Nodes.Where(n => n.Branch == node.Branch && n.Depth == node.Depth).ToList();
                if (pair.Count > 1)
                {
                    int i = pair.IndexOf(node);
                    float h = NodeH * 0.62f;
                    return new Rect2(x, y - 12f + i * (h + 6f), NodeW, h);
                }
            }

            return new Rect2(x, y, NodeW, NodeH);
        }

        private SkillTreeNode? At(Vector2 p) => Tree?.Nodes.FirstOrDefault(n => RectOf(n).HasPoint(p));

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseMotion m)
            {
                string? id = At(m.Position)?.Id;
                if (id != _hover)
                {
                    Hover = id;
                }
            }
            else if (e is InputEventMouseButton { Pressed: true } b && At(b.Position) is { } node)
            {
                if (b.ButtonIndex == MouseButton.Left)
                {
                    Panel.RaiseNode(node.Id);
                    AcceptEvent();
                }
                else if (b.ButtonIndex == MouseButton.Right)
                {
                    Panel.LowerNode(node.Id);
                    AcceptEvent();
                }

                // The rank changed: the tooltip's "now" and "next" too.
                RefreshTip();
            }
        }

        /// <summary>The hovered node, in full: every rank (the current one marked), what it
        /// needs, what it excludes, and what a click does.</summary>
        private void FillTip(HoverTooltip tip)
        {
            if (Tree is null || Gem is null || _hover is null || Tree.Node(_hover) is not { } node)
            {
                return;
            }

            int rank = SkillTreeRules.RankOf(Gem, node.Id);
            bool notable = node.Kind == SkillTreeNodeKind.Notable;
            tip.Begin($"{node.Name}  ({rank}/{node.MaxRank})", notable ? UiTheme.Gold : Accent);
            tip.AddLine(notable ? "Notable · one rank, changes how the skill plays" : $"Lesser · {node.MaxRank} rank{(node.MaxRank == 1 ? "" : "s")}", UiTheme.TextDim, 12);
            for (int i = 1; i <= node.MaxRank; i++)
            {
                bool now = i == rank;
                string label = node.MaxRank == 1 ? "" : $"Rank {i}: ";
                tip.AddLine($"{(now ? "▶ " : "")}{label}{node.Describe(i)}", now ? Colors.White : i < rank ? UiTheme.TextDim : UiTheme.Text, 14);
            }

            List<(string Id, int Ranks)> ways = node.Parents.ToList();
            if (ways.Count > 0)
            {
                string needs = string.Join(" or ", ways.Select(p => $"{p.Ranks} point{(p.Ranks == 1 ? "" : "s")} in {Tree.Node(p.Id)?.Name ?? p.Id}"));
                tip.AddLine($"Needs {needs}", UiTheme.TextDim, 12);
            }

            if (node.Exclusive is { } group && Tree.Nodes.Where(n => n.Exclusive == group && n.Id != node.Id).Select(n => n.Name).ToList() is { Count: > 0 } others)
            {
                tip.AddLine($"Choose one: excludes {string.Join(", ", others)}", UiTheme.Ember, 12);
            }

            if (rank < node.MaxRank)
            {
                string? why = SkillTreeRules.CannotRaise(Gem, Tree, node);
                // "Needs ... first" is already the line above.
                if (why is null || !why.StartsWith("Needs"))
                {
                    tip.AddLine(why ?? "Left click: spend a point", why is null ? Accent : UiTheme.TextDim, 13);
                }
            }

            if (rank > 0)
            {
                string? why = SkillTreeRules.CannotLower(Gem, Tree, node);
                tip.AddLine(why ?? $"Right click: take one back ({SkillTrees.RefundCost(Gem.Level):N0} gold)", UiTheme.TextDim, 13);
            }
        }

        public override void _Draw()
        {
            if (Tree is null || Gem is null)
            {
                return;
            }

            Font font = ThemeDB.FallbackFont;
            for (int b = 0; b < Tree.Branches.Length; b++)
            {
                DrawString(font, new Vector2(28f, 16f + b * RowH), Tree.Branches[b].ToUpperInvariant(), HorizontalAlignment.Left, -1, 11, UiTheme.TextDim);
            }

            // Links first, under the nodes.
            foreach (SkillTreeNode node in Tree.Nodes)
            {
                foreach ((string parentId, int ranks) in node.Parents)
                {
                    if (Tree.Node(parentId) is not { } parent)
                    {
                        continue;
                    }

                    Rect2 from = RectOf(parent);
                    Rect2 to = RectOf(node);
                    bool met = SkillTreeRules.RankOf(Gem, parentId) >= ranks;
                    Vector2 a = new(from.End.X, from.GetCenter().Y);
                    Vector2 c = new(to.Position.X, to.GetCenter().Y);
                    DrawLine(a, c, met ? Accent : new Color(0.35f, 0.32f, 0.45f), met ? 2.5f : 1.5f, true);
                    DrawString(font, (a + c) / 2f + new Vector2(-12f, -4f), $"{ranks} pt", HorizontalAlignment.Left, -1, 10, met ? Accent : UiTheme.TextDim);
                }
            }

            foreach (SkillTreeNode node in Tree.Nodes)
            {
                DrawNode(font, node);
            }
        }

        private void DrawNode(Font font, SkillTreeNode node)
        {
            Rect2 r = RectOf(node);
            int rank = SkillTreeRules.RankOf(Gem!, node.Id);
            bool notable = node.Kind == SkillTreeNodeKind.Notable;
            bool can = SkillTreeRules.CannotRaise(Gem!, Tree!, node) is null;
            bool hover = node.Id == _hover;
            Color border = rank > 0 ? (notable ? UiTheme.Gold : Accent) : can ? Accent with { A = 0.7f } : new Color(0.3f, 0.27f, 0.4f);
            Color bg = rank > 0 ? new Color(0.1f, 0.2f, 0.18f, 0.95f) : new Color(0.07f, 0.06f, 0.1f, 0.95f);
            if (hover)
            {
                bg = bg.Lightened(0.08f);
            }

            var box = new StyleBoxFlat { BgColor = bg, BorderColor = border };
            box.SetBorderWidthAll(notable ? 3 : 2);
            box.SetCornerRadiusAll(notable ? 10 : 4);
            DrawStyleBox(box, r);
            float textW = r.Size.X - 16f;
            bool compact = r.Size.Y < NodeH;
            DrawString(font, r.Position + new Vector2(8f, 18f), node.Name, HorizontalAlignment.Left, textW - 40f, 15, rank > 0 || can ? Colors.White : UiTheme.TextDim);
            DrawString(font, r.Position + new Vector2(8f, 18f), $"{rank}/{node.MaxRank}", HorizontalAlignment.Right, textW, 13, rank >= node.MaxRank ? UiTheme.Gold : UiTheme.TextDim);
            string line = node.Describe(rank > 0 ? rank : 1);
            DrawString(font, r.Position + new Vector2(8f, compact ? 36f : 38f), line, HorizontalAlignment.Left, textW, 12, UiTheme.Text);
            if (!compact && rank > 0 && rank < node.MaxRank)
            {
                DrawString(font, r.Position + new Vector2(8f, 58f), $"next: {node.Describe(rank + 1)}", HorizontalAlignment.Left, textW, 11, UiTheme.TextDim);
            }
            else if (!compact && node.Exclusive is not null)
            {
                DrawString(font, r.Position + new Vector2(8f, 58f), "one of two", HorizontalAlignment.Left, textW, 11, UiTheme.TextDim);
            }

            // Rank pips along the bottom edge.
            for (int i = 0; i < node.MaxRank && node.MaxRank > 1; i++)
            {
                Vector2 p = new(r.End.X - 12f - i * 10f, r.End.Y - 8f);
                DrawCircle(p, 3f, i < rank ? Accent : new Color(0.25f, 0.23f, 0.32f));
            }
        }
    }
}
