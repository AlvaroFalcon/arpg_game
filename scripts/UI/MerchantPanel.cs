#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Core;
using ArpgGame.Items;

namespace ArpgGame.UI;

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

        HBoxContainer columns = UiTheme.HBox(body, 28);
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
    }

    public override void Refresh()
    {
        ItemTooltipControl.Footer = FooterFor;
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
