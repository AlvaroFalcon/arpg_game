#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Core;
using ArpgGame.Items;

namespace ArpgGame.UI;

/// <summary>
/// The Artificer's bench in the smithy — docs/design/crafting.md. Two tabs (orbs
/// themselves are used from the inventory's Crafting tab):
/// <list type="bullet">
/// <item><b>Disenchant</b>: right-click an item to break it into dust of its rarity.</item>
/// <item><b>Forge</b>: choose a base and a modifier, then spend dust to make a
/// new item that is guaranteed to carry it.</item>
/// </list>
/// </summary>
public partial class ArtificerPanel : ModalPanel
{
    public enum Tab
    {
        Disenchant,
        Forge,
    }

    public Player Player { get; set; } = null!;

    protected override string Title => "Artificer's Bench";

    protected override Vector2 FrameSize => new(1040f, 600f);

    public Tab Current { get; private set; } = Tab.Disenchant;

    public string LastMessage => _message.Text;

    private readonly RandomNumberGenerator _rng = new();
    private HBoxContainer _tabs = null!;
    private HBoxContainer _content = null!;
    private Label _message = null!;
    private InventoryGridControl? _grid;
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
        _dustList = null;
        _grades = null;
        switch (Current)
        {
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
            HBoxContainer line = UiTheme.HBox(_dustList, 6);
            line.AddChild(UiTheme.PixelIcon(ItemArt.ForCurrency(dust.Id), 24f));
            line.AddChild(UiTheme.Label($"{dust.Name}   ×{Player.Wallet.GetBalance(dust.Id)}", 16, dust.Color));
        }

        HBoxContainer goldLine = UiTheme.HBox(_dustList, 6);
        goldLine.AddChild(UiTheme.PixelIcon(ItemArt.ForCurrency("gold"), 24f));
        goldLine.AddChild(UiTheme.Label($"Gold   {Player.Wallet.GetBalance(Core.Profile.GoldId)}", 16, UiTheme.Gold));
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
            if (ItemArt.Fit(ItemArt.ForBase(all[i].Id), 32) is { } icon)
            {
                bases.AddIconItem(icon, $"{all[i].DisplayName}  ({all[i].ItemClass})", i);
            }
            else
            {
                bases.AddItem($"{all[i].DisplayName}  ({all[i].ItemClass})", i);
            }
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
            case Tab.Disenchant:
                (CraftingCurrency dust, int amount) = Crafting.DisenchantYield(item);
                return $"[color=#{dust.Color.ToHtml(false)}]Disenchants into {amount} {dust.Name}[/color]";
            default:
                return null;
        }
    }

    protected override void OnClosed()
    {
        ItemTooltipControl.Footer = null;
        Core.Profile.Get(this)?.CaptureFrom(Player);
    }
}
