#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Items;

namespace ArpgGame.UI;

public enum MerchantTab
{
    Trade,
    SkillGems,
}

/// <summary>
/// The hideout merchant — docs/design/crafting.md -> "Merchant". Right-click an
/// item in your inventory to sell it for gold; everything sold this visit can be
/// bought back for the same price until the panel closes (a misclick never costs
/// a good item). The merchant also sells the common crafting shards.
/// </summary>
public partial class MerchantPanel : ModalPanel
{
    public Player Player { get; set; } = null!;

    protected override string Title => "Odran the Trader";

    protected override Vector2 FrameSize => new(1010f, 560f);

    /// <summary>The character the Skill gems tab sells to; defaults to the active one (tests set it).</summary>
    public CharacterData? Character { get; set; }

    public MerchantTab Tab { get; private set; }

    public GemShopView Gems { get; private set; } = null!;

    /// <summary>The ember dot on the Skill gems tab: gems unlocked since it was last opened.</summary>
    public bool GemsTabDot => _gemsDot.Visible;

    private CharacterData? Customer => Character ?? CharacterBuild.Of(Player)?.Data ?? Core.Profile.Get(this)?.Active;

    private Button _tradeTab = null!;
    private Button _gemsTab = null!;
    private Panel _gemsDot = null!;
    private HBoxContainer _tradePage = null!;
    private bool _gemsSeenThisVisit;
    private Control _inventoryHolder = null!;
    private Control _buybackHolder = null!;
    private VBoxContainer _wares = null!;
    private Label _gold = null!;
    private Label _message = null!;
    private Inventory _buyback = null!;
    private readonly Dictionary<EquipmentItem, long> _soldFor = new();

    public Inventory Buyback => _buyback;

    protected override void Build(VBoxContainer body)
    {
        _buyback = new Inventory { Columns = Inventory.Width, Rows = 2, Name = "Buyback" };
        AddChild(_buyback);

        HBoxContainer tabs = UiTheme.HBox(body, 6);
        _tradeTab = UiTheme.Button("Trade", () => ShowTab(MerchantTab.Trade), 16, 160f);
        _gemsTab = UiTheme.Button("Skill gems", () => ShowTab(MerchantTab.SkillGems), 16, 160f);
        foreach (Button t in new[] { _tradeTab, _gemsTab })
        {
            t.CustomMinimumSize = new Vector2(160f, 40f);
            tabs.AddChild(t);
        }

        _gemsDot = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, Position = new Vector2(144f, 6f), Size = new Vector2(10f, 10f) };
        var dot = new StyleBoxFlat { BgColor = UiTheme.Ember };
        dot.SetCornerRadiusAll(5);
        _gemsDot.AddThemeStyleboxOverride("panel", dot);
        _gemsTab.AddChild(_gemsDot);

        Gems = new GemShopView { Player = Player, CharacterSource = () => Customer, OpenSkills = OpenSkillsPanel, SizeFlagsVertical = Control.SizeFlags.ExpandFill, Name = "GemShopView" };
        body.AddChild(Gems);
        Gems.Visible = false;

        HBoxContainer columns = UiTheme.HBox(body, 28);
        _tradePage = columns;
        VBoxContainer left = UiTheme.VBox(columns, 6);
        left.AddChild(UiTheme.Label("Your inventory — right-click an item to sell it", 16, UiTheme.Gold));
        _inventoryHolder = new Control { CustomMinimumSize = new Vector2(Inventory.Width, Inventory.Height) * InventoryGridControl.CellPx };
        left.AddChild(_inventoryHolder);
        left.AddChild(UiTheme.Label("Sold this visit — right-click to buy back", 15, UiTheme.TextDim));
        _buybackHolder = new Control { CustomMinimumSize = new Vector2(Inventory.Width, 2) * InventoryGridControl.CellPx };
        left.AddChild(_buybackHolder);
        _gold = UiTheme.Label("", 17, UiTheme.Gold);
        left.AddChild(_gold);
        _message = UiTheme.Label("", 15, UiTheme.Text);
        left.AddChild(_message);

        VBoxContainer right = UiTheme.VBox(columns, 8);
        right.CustomMinimumSize = new Vector2(380f, 0f);
        right.AddChild(UiTheme.Label("Wares", 18, UiTheme.Gold));
        _wares = UiTheme.VBox(right, 6);
        StyleTabs();
    }

    /// <summary>Shows a tab (the quest dialogue's "Open the Skill gems tab" uses this too).</summary>
    public void ShowTab(MerchantTab tab)
    {
        Tab = tab;
        _tradePage.Visible = tab == MerchantTab.Trade;
        Gems.Visible = tab == MerchantTab.SkillGems;
        StyleTabs();
        if (tab == MerchantTab.SkillGems)
        {
            Gems.Player = Player;
            Gems.OnShown(!_gemsSeenThisVisit);
            _gemsSeenThisVisit = true;
            UpdateDot();
        }
        else
        {
            RefreshWares();
        }
    }

    private void StyleTabs()
    {
        foreach ((Button b, bool on) in new[] { (_tradeTab, Tab == MerchantTab.Trade), (_gemsTab, Tab == MerchantTab.SkillGems) })
        {
            b.AddThemeStyleboxOverride("normal", UiTheme.Box(on ? new Color(0.2f, 0.15f, 0.3f) : new Color(0.08f, 0.07f, 0.11f), on ? UiTheme.Gold : UiTheme.PanelBorder, on ? 2 : 1));
            b.AddThemeColorOverride("font_color", on ? UiTheme.Gold : UiTheme.TextDim);
        }
    }

    private void UpdateDot() => _gemsDot.Visible = Tab != MerchantTab.SkillGems && Customer is { } c && GemShop.NewCount(c) > 0;

    private void OpenSkillsPanel()
    {
        CharacterPanels? panels = GetParent()?.GetNodeOrNull<CharacterPanels>("CharacterPanels");
        Close();
        panels?.Skills.Open();
    }

    public override void Refresh()
    {
        ItemTooltipControl.Footer = FooterFor;
        Gems.Player = Player;
        UpdateDot();
        if (Tab == MerchantTab.SkillGems)
        {
            Gems.Rebuild();
        }

        ClearChildren(_inventoryHolder);
        _inventoryHolder.AddChild(new InventoryGridControl
        {
            InventoryNode = Player.Inventory,
            EquipmentNode = Player.Equipment,
            RightClickOverride = Sell,
        });
        ClearChildren(_buybackHolder);
        _buybackHolder.AddChild(new InventoryGridControl
        {
            InventoryNode = _buyback,
            EquipmentNode = Player.Equipment,
            RightClickOverride = BuyBack,
        });
        RefreshWares();
    }

    private void RefreshWares()
    {
        ClearChildren(_wares);
        foreach (CraftingCurrency orb in Crafting.Orbs.Where(o => o.BuyPrice > 0))
        {
            HBoxContainer row = UiTheme.HBox(_wares, 10);
            row.AddChild(UiTheme.PixelIcon(Items.ItemArt.ForCurrency(orb.Id), 32f));
            VBoxContainer text = UiTheme.VBox(row, 0);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            text.AddChild(UiTheme.Label($"{orb.Name}   ×{Player.Wallet.GetBalance(orb.Id)}", 16, orb.Color));
            Label effect = UiTheme.Label(orb.Effect, 12, UiTheme.TextDim);
            effect.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            effect.CustomMinimumSize = new Vector2(230f, 0f);
            text.AddChild(effect);
            CraftingCurrency bought = orb;
            Button buy = UiTheme.Button($"Buy · {orb.BuyPrice}g", () => Buy(bought), 15, 120f);
            buy.Disabled = Player.Wallet.GetBalance(Core.Profile.GoldId) < orb.BuyPrice;
            row.AddChild(buy);
        }

        _gold.Text = $"Gold: {Player.Wallet.GetBalance(Core.Profile.GoldId)}";
    }

    private string? FooterFor(EquipmentItem item) => _soldFor.TryGetValue(item, out long paid)
        ? $"[color=#e8b84a]Buy back for {paid} gold[/color]"
        : Player.Inventory.Placements.ContainsKey(item) ? $"[color=#e8b84a]Sells for {Crafting.SellPrice(item)} gold[/color]" : null;

    public bool Sell(EquipmentItem item)
    {
        if (!Player.Inventory.Placements.ContainsKey(item))
        {
            return false;
        }

        // The buyback shelf keeps the latest sales; the oldest falls off when full.
        while (!_buyback.TryFindFirstFit(item, out _) && _soldFor.Count > 0)
        {
            EquipmentItem oldest = _soldFor.Keys.First();
            _soldFor.Remove(oldest);
            _buyback.Remove(oldest);
            Discard(oldest);
        }

        long price = Crafting.SellPrice(item);
        Player.Inventory.Remove(item);
        _buyback.TryAdd(item);
        _soldFor[item] = price;
        Player.Wallet.Add(Core.Profile.GoldId, price);
        _message.Text = $"Sold {item.Descriptor.DisplayName} for {price} gold";
        Sfx.Play(this, "pickup", -4f);
        RefreshWares();
        GameEvents.Raise(GameEvents.ItemSold, (int)price);
        return true;
    }

    public bool BuyBack(EquipmentItem item)
    {
        if (!_soldFor.TryGetValue(item, out long price))
        {
            return false;
        }

        if (!Player.Inventory.TryFindFirstFit(item, out Vector2I at))
        {
            _message.Text = "Your inventory is full";
            return false;
        }

        if (!Player.Wallet.TrySpend(Core.Profile.GoldId, price))
        {
            _message.Text = $"Buying it back costs {price} gold";
            return false;
        }

        _soldFor.Remove(item);
        _buyback.Remove(item);
        Player.Inventory.TryPlaceAt(item, at);
        _message.Text = $"Bought back {item.Descriptor.DisplayName}";
        Sfx.Play(this, "pickup", -4f);
        RefreshWares();
        return true;
    }

    public bool Buy(CraftingCurrency orb)
    {
        if (orb.BuyPrice <= 0 || !Player.Wallet.TrySpend(Core.Profile.GoldId, orb.BuyPrice))
        {
            return false;
        }

        Player.Wallet.Add(orb.Id, 1);
        _message.Text = $"Bought a {orb.Name}";
        Sfx.Play(this, "pickup", -4f);
        RefreshWares();
        return true;
    }

    private static void Discard(EquipmentItem item)
    {
        if (IsInstanceValid(item) && !item.IsInsideTree())
        {
            item.Free();
        }
    }

    protected override void OnClosed()
    {
        ItemTooltipControl.Footer = null;
        _gemsSeenThisVisit = false;
        Tab = MerchantTab.Trade;
        _tradePage.Visible = true;
        Gems.Visible = false;
        StyleTabs();
        // Unclaimed buybacks are gone once you walk away, as in every ARPG.
        foreach (EquipmentItem sold in _soldFor.Keys)
        {
            _buyback.Remove(sold);
            Discard(sold);
        }

        _soldFor.Clear();
        Core.Profile.Get(this)?.CaptureFrom(Player);
    }
}
