#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Core;
using ArpgGame.Items;

namespace ArpgGame.UI;

/// <summary>
/// The Artificer's bench in the hideout — docs/design/crafting.md. Three tabs:
/// <list type="bullet">
/// <item><b>Orbs</b>: click an orb to pick it up, then left-click an item to use
/// it (Path of Exile style; Shift keeps the orb in hand, right-click puts it
/// away). Items the orb can touch light up.</item>
/// <item><b>Disenchant</b>: right-click an item to break it into dust of its rarity.</item>
/// <item><b>Forge</b>: choose a base and a modifier, then spend dust to make a
/// new item that is guaranteed to carry it.</item>
/// </list>
/// </summary>
public partial class ArtificerPanel : ModalPanel
{
    public enum Tab
    {
        Orbs,
        Disenchant,
        Forge,
    }

    public Player Player { get; set; } = null!;

    protected override string Title => "Artificer's Bench";

    protected override Vector2 FrameSize => new(1040f, 600f);

    public Tab Current { get; private set; } = Tab.Orbs;

    public CraftingCurrency? Armed { get; private set; }

    public string LastMessage => _message.Text;

    private readonly RandomNumberGenerator _rng = new();
    private HBoxContainer _tabs = null!;
    private HBoxContainer _content = null!;
    private Label _message = null!;
    private Label _cursor = null!;
    private InventoryGridControl? _grid;
    private readonly List<(CraftingCurrency Orb, Button Button)> _orbButtons = new();
    private VBoxContainer? _dustList;

    // Forge selections.
    private BaseItemDefinition? _forgeBase;
    private AffixDefinition? _forgeAffix;
    private VBoxContainer? _grades;

    protected override void Build(VBoxContainer body)
    {
        _rng.Randomize();
        _tabs = UiTheme.HBox(body, 6);
        _content = UiTheme.HBox(body, 24);
        _content.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _message = UiTheme.Label("", 16, UiTheme.Text);
        body.AddChild(_message);
        _cursor = UiTheme.Label("", 15, Colors.White);
        _cursor.MouseFilter = Control.MouseFilterEnum.Ignore;
        _cursor.ZIndex = 10;
        Root.AddChild(_cursor);
    }

    public override void Refresh()
    {
        ItemTooltipControl.Footer = FooterFor;
        ClearChildren(_tabs);
        foreach (Tab tab in System.Enum.GetValues<Tab>())
        {
            Tab t = tab;
            Button b = UiTheme.Button(tab.ToString(), () => Show(t), 16, 130f);
            if (tab == Current)
            {
                b.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.24f, 0.18f, 0.32f), UiTheme.Gold, 2));
            }

            _tabs.AddChild(b);
        }

        ClearChildren(_content);
        _orbButtons.Clear();
        _dustList = null;
        _grades = null;
        switch (Current)
        {
            case Tab.Orbs:
                BuildOrbs();
                break;
            case Tab.Disenchant:
                BuildDisenchant();
                break;
            case Tab.Forge:
                BuildForge();
                break;
        }
    }

    public void Show(Tab tab)
    {
        Disarm();
        Current = tab;
        _message.Text = "";
        Refresh();
    }

    private VBoxContainer InventoryColumn(string heading)
    {
        VBoxContainer left = UiTheme.VBox(_content, 6);
        left.AddChild(UiTheme.Label(heading, 16, UiTheme.Gold));
        var holder = new Control { CustomMinimumSize = new Vector2(Inventory.Width, Inventory.Height) * InventoryGridControl.CellPx };
        left.AddChild(holder);
        _grid = new InventoryGridControl { InventoryNode = Player.Inventory, EquipmentNode = Player.Equipment };
        holder.AddChild(_grid);
        return left;
    }

    // --- orbs ------------------------------------------------------------------------

    private void BuildOrbs()
    {
        VBoxContainer left = InventoryColumn("Your inventory");
        _grid!.LeftClickOverride = ApplyArmed;
        _grid.RightClickOverride = _ => { Disarm(); return true; };
        _grid.RightClickEmpty = Disarm;
        _grid.Tint = TintFor;
        Label how = UiTheme.Label("Click an orb to pick it up, then click an item to use it. Hold Shift to keep using it; right-click puts it away. Unequip gear to craft on it.", 13, UiTheme.TextDim);
        how.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        how.CustomMinimumSize = new Vector2(Inventory.Width * InventoryGridControl.CellPx, 0f);
        left.AddChild(how);

        VBoxContainer right = UiTheme.VBox(_content, 8);
        right.AddChild(UiTheme.Label("Orbs", 18, UiTheme.Gold));
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        right.AddChild(grid);
        foreach (CraftingCurrency orb in Crafting.Orbs)
        {
            CraftingCurrency picked = orb;
            Button b = UiTheme.Button("", () => Arm(picked), 14, 150f);
            b.CustomMinimumSize = new Vector2(150f, 52f);
            b.AddThemeColorOverride("font_color", orb.Color);
            b.AddThemeColorOverride("font_hover_color", orb.Color.Lightened(0.3f));
            b.TooltipText = $"{orb.Name}\n{orb.Effect}";
            grid.AddChild(b);
            _orbButtons.Add((orb, b));
        }

        UpdateOrbCounts();
    }

    private void UpdateOrbCounts()
    {
        foreach ((CraftingCurrency orb, Button b) in _orbButtons)
        {
            long n = Player.Wallet.GetBalance(orb.Id);
            b.Text = $"{orb.Name}\n×{n}";
            b.Disabled = n <= 0;
            b.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.1f, 0.085f, 0.14f), Armed == orb ? orb.Color : UiTheme.PanelBorder, Armed == orb ? 3 : 1));
        }
    }

    public void Arm(CraftingCurrency orb)
    {
        if (Player.Wallet.GetBalance(orb.Id) <= 0)
        {
            return;
        }

        Armed = orb;
        _message.Text = $"{orb.Name} in hand — {orb.Effect}";
        _message.AddThemeColorOverride("font_color", orb.Color);
        UpdateOrbCounts();
    }

    public void Disarm()
    {
        if (Armed is null)
        {
            return;
        }

        Armed = null;
        _message.Text = "";
        UpdateOrbCounts();
    }

    /// <summary>Uses the orb in hand on <paramref name="item"/> (a left-click).</summary>
    public bool ApplyArmed(EquipmentItem item)
    {
        if (Armed is not { } orb)
        {
            return false;
        }

        if (Crafting.CannotApply(orb, item) is { } reason)
        {
            Say(reason, new Color(1f, 0.45f, 0.35f));
            return true;
        }

        if (!Player.Wallet.TrySpend(orb.Id, 1) || !Crafting.Apply(orb, item, _rng, out string message))
        {
            return true;
        }

        Sfx.Play(this, "levelup", -10f);
        Say(message, orb.Color);
        if (!Input.IsKeyPressed(Key.Shift) || Player.Wallet.GetBalance(orb.Id) <= 0)
        {
            Armed = null;
        }

        UpdateOrbCounts();
        return true;
    }

    private Color? TintFor(EquipmentItem item) => Armed is { } orb
        ? Crafting.CannotApply(orb, item) is null ? new Color(0.4f, 1f, 0.5f, 0.22f) : new Color(0f, 0f, 0f, 0.55f)
        : null;

    private void Say(string text, Color color)
    {
        _message.Text = text;
        _message.AddThemeColorOverride("font_color", color);
    }

    // --- disenchant ------------------------------------------------------------------

    private void BuildDisenchant()
    {
        VBoxContainer left = InventoryColumn("Your inventory — right-click an item to disenchant it");
        _grid!.RightClickOverride = Disenchant;
        Label how = UiTheme.Label("Every item breaks down into the dust of its rarity: grey from common, azure from magic, gilded from rare, prismatic from majestic and set items. Dust pays for forging.", 13, UiTheme.TextDim);
        how.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        how.CustomMinimumSize = new Vector2(Inventory.Width * InventoryGridControl.CellPx, 0f);
        left.AddChild(how);
        VBoxContainer right = UiTheme.VBox(_content, 8);
        right.AddChild(UiTheme.Label("Dust", 18, UiTheme.Gold));
        _dustList = UiTheme.VBox(right, 4);
        UpdateDust();
    }

    private void UpdateDust()
    {
        if (_dustList is null)
        {
            return;
        }

        ClearChildren(_dustList);
        foreach (CraftingCurrency dust in Crafting.Dusts)
        {
            _dustList.AddChild(UiTheme.Label($"{dust.Name}   ×{Player.Wallet.GetBalance(dust.Id)}", 16, dust.Color));
        }

        _dustList.AddChild(UiTheme.Label($"Gold   {Player.Wallet.GetBalance(Core.Profile.GoldId)}", 16, UiTheme.Gold));
    }

    public bool Disenchant(EquipmentItem item)
    {
        if (!Player.Inventory.Placements.ContainsKey(item))
        {
            return false;
        }

        (CraftingCurrency dust, int amount) = Crafting.DisenchantYield(item);
        Player.Inventory.Remove(item);
        Player.Wallet.Add(dust.Id, amount);
        Say($"{item.Descriptor.DisplayName} → {amount} {dust.Name}", dust.Color);
        Sfx.Play(this, "hit", -6f);
        if (!item.IsInsideTree())
        {
            item.Free();
        }

        UpdateDust();
        return true;
    }

    // --- forge -----------------------------------------------------------------------

    public int ForgeItemLevel => Crafting.ForgeItemLevel(Core.Profile.Get(this)?.Active.HighestTierCleared ?? 1);

    private void BuildForge()
    {
        int ilvl = ForgeItemLevel;
        VBoxContainer left = UiTheme.VBox(_content, 8);
        left.CustomMinimumSize = new Vector2(420f, 0f);
        left.AddChild(UiTheme.Label("1 · Choose a base", 16, UiTheme.Gold));
        var bases = new OptionButton();
        List<BaseItemDefinition> all = ItemFactory.Bases.OrderBy(b => b.ItemClass).ThenBy(b => b.DisplayName).ToList();
        _forgeBase ??= all.FirstOrDefault();
        for (int i = 0; i < all.Count; i++)
        {
            bases.AddItem($"{all[i].DisplayName}  ({all[i].ItemClass})", i);
            if (all[i] == _forgeBase)
            {
                bases.Selected = i;
            }
        }

        bases.ItemSelected += index =>
        {
            _forgeBase = all[(int)index];
            _forgeAffix = null;
            Refresh();
        };
        left.AddChild(bases);

        left.AddChild(UiTheme.Label("2 · Choose the modifier it will carry", 16, UiTheme.Gold));
        var affixes = new OptionButton();
        List<AffixDefinition> options = _forgeBase is null ? new() : Crafting.ForgeableAffixes(_forgeBase, ilvl);
        if (_forgeAffix is null || !options.Contains(_forgeAffix))
        {
            _forgeAffix = options.FirstOrDefault();
        }

        for (int i = 0; i < options.Count; i++)
        {
            affixes.AddItem($"{SpacedStat(options[i])}  ·  {options[i].Pool}", i);
            if (options[i] == _forgeAffix)
            {
                affixes.Selected = i;
            }
        }

        affixes.ItemSelected += index =>
        {
            _forgeAffix = options[(int)index];
            Refresh();
        };
        affixes.Disabled = options.Count == 0;
        left.AddChild(affixes);

        Label level = UiTheme.Label($"Forged items are item level {ilvl}. Clear higher reality tiers to forge better tiers.", 13, UiTheme.TextDim);
        level.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        left.AddChild(level);

        left.AddChild(UiTheme.Label("Dust", 16, UiTheme.Gold));
        _dustList = UiTheme.VBox(left, 2);
        UpdateDust();

        VBoxContainer right = UiTheme.VBox(_content, 8);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.AddChild(UiTheme.Label("3 · Forge", 16, UiTheme.Gold));
        _grades = UiTheme.VBox(right, 8);
        BuildGrades(ilvl);
    }

    private void BuildGrades(int ilvl)
    {
        if (_grades is null)
        {
            return;
        }

        ClearChildren(_grades);
        foreach (Crafting.ForgeGrade grade in Crafting.Grades)
        {
            var box = new PanelContainer();
            box.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.12f, 0.1f, 0.17f), UiTheme.PanelBorder, 1, 4, 8));
            _grades.AddChild(box);
            HBoxContainer row = UiTheme.HBox(box, 10);
            VBoxContainer text = UiTheme.VBox(row, 0);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            text.AddChild(UiTheme.Label(grade.Name, 16, grade.Result == Rarity.Rare ? new Color(1f, 0.85f, 0.35f) : new Color(0.55f, 0.65f, 1f)));
            text.AddChild(UiTheme.Label(grade.Summary, 12, UiTheme.TextDim));
            if (_forgeAffix is { } affix)
            {
                int tier = Crafting.ForgeTier(affix, grade, ilvl);
                (float min, float max) = affix.ValueRangeForTier(tier);
                string values = grade.PerfectValue || Mathf.IsEqualApprox(min, max) ? ItemTooltipText.Number(max) : $"{ItemTooltipText.Number(min)}–{ItemTooltipText.Number(max)}";
                text.AddChild(UiTheme.Label($"→ {values}{ItemTooltipText.Unit(affix.Type)} {SpacedStat(affix)} (tier {tier})", 13, new Color(0.72f, 0.94f, 1f)));
            }

            text.AddChild(UiTheme.Label(Crafting.CostText(grade), 13, Crafting.CanAfford(Player.Wallet, grade) ? UiTheme.Gold : new Color(1f, 0.45f, 0.35f)));
            Crafting.ForgeGrade chosen = grade;
            Button forge = UiTheme.Button("Forge", () => Forge(chosen), 16, 100f);
            forge.Disabled = _forgeBase is null || _forgeAffix is null || !Crafting.CanAfford(Player.Wallet, grade);
            row.AddChild(forge);
        }
    }

    public EquipmentItem? Forge(Crafting.ForgeGrade grade)
    {
        if (_forgeBase is not { } baseItem || _forgeAffix is not { } affix)
        {
            return null;
        }

        int ilvl = ForgeItemLevel;
        EquipmentItem item = Crafting.Forge(baseItem, affix, grade, ilvl, _rng);
        if (!Player.Inventory.TryFindFirstFit(item, out _))
        {
            item.Free();
            Say("Your inventory is full", new Color(1f, 0.45f, 0.35f));
            return null;
        }

        if (!Crafting.TryPay(Player.Wallet, grade))
        {
            item.Free();
            Say("You can't afford that", new Color(1f, 0.45f, 0.35f));
            return null;
        }

        Player.Inventory.TryAdd(item);
        Sfx.Play(this, "levelup", -6f);
        Say($"Forged {item.Descriptor.DisplayName}", grade.Result == Rarity.Rare ? new Color(1f, 0.85f, 0.35f) : new Color(0.55f, 0.65f, 1f));
        UpdateDust();
        BuildGrades(ilvl);
        return item;
    }

    /// <summary>For tests and captures: pick the forge's base and modifier.</summary>
    public void SelectForge(BaseItemDefinition baseItem, AffixDefinition affix)
    {
        _forgeBase = baseItem;
        _forgeAffix = affix;
        if (Current == Tab.Forge && IsOpen)
        {
            Refresh();
        }
    }

    private static string SpacedStat(AffixDefinition affix) =>
        System.Text.RegularExpressions.Regex.Replace(affix.Stat.ToString(), "(?<=[a-z])([A-Z])", " $1");

    // --- shared ----------------------------------------------------------------------

    private string? FooterFor(EquipmentItem item)
    {
        if (!Player.Inventory.Placements.ContainsKey(item))
        {
            return null;
        }

        switch (Current)
        {
            case Tab.Orbs when Armed is { } orb:
                return Crafting.CannotApply(orb, item) is { } reason
                    ? $"[color=#ff6a5a]{reason}[/color]"
                    : $"[color=#{orb.Color.ToHtml(false)}]{orb.Name}: {orb.Effect}[/color]";
            case Tab.Disenchant:
                (CraftingCurrency dust, int amount) = Crafting.DisenchantYield(item);
                return $"[color=#{dust.Color.ToHtml(false)}]Disenchants into {amount} {dust.Name}[/color]";
            default:
                return null;
        }
    }

    public override void _Process(double delta)
    {
        if (IsOpen && Armed is not null && Input.IsActionJustPressed("ui_cancel"))
        {
            // Esc first puts the orb away; a second Esc closes the bench.
            Disarm();
            PanelStack.MarkClosedThisFrame();
        }

        base._Process(delta);
        if (IsOpen && _grid is not null && IsInstanceValid(_grid))
        {
            _grid.QueueRedraw();
        }

        _cursor.Visible = IsOpen && Armed is not null;
        if (Armed is { } armed)
        {
            _cursor.Text = $"◆ {armed.Name} ×{Player.Wallet.GetBalance(armed.Id)}";
            _cursor.AddThemeColorOverride("font_color", armed.Color);
            _cursor.Position = Root.GetLocalMousePosition() + new Vector2(16f, -6f);
        }
    }

    protected override void OnClosed()
    {
        Armed = null;
        ItemTooltipControl.Footer = null;
        Core.Profile.Get(this)?.CaptureFrom(Player);
    }
}
