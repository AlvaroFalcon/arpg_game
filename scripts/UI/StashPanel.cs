#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using PathToExile.Core;
using PathToExile.Items;

namespace PathToExile.UI;

/// <summary>
/// The stash — docs/design/items.md -> "Stash". Opened at the chest in the
/// hideout (E). Account-wide tabs (shared by every character, like PoE), each a
/// 12×10 grid; the first is free and more are bought with gold, each costing
/// double the last. Right-click moves an item between the stash tab and the
/// inventory (PoE's ctrl-click); dragging rearranges within a grid.
/// </summary>
public partial class StashPanel : ModalPanel
{
    public Player Player { get; set; } = null!;

    protected override string Title => "Stash";

    protected override Vector2 FrameSize => new(1000f, 560f);

    private HBoxContainer _tabsRow = null!;
    private Control _stashHolder = null!;
    private Control _inventoryHolder = null!;
    private Label _goldLabel = null!;
    private readonly List<Inventory> _tabs = new();
    private int _current;

    private Profile? Profile => Core.Profile.Get(this);

    protected override void Build(VBoxContainer body)
    {
        HBoxContainer columns = UiTheme.HBox(body, 24);

        VBoxContainer left = UiTheme.VBox(columns, 8);
        _tabsRow = UiTheme.HBox(left, 4);
        _stashHolder = new Control { CustomMinimumSize = new Vector2(Core.Profile.StashColumns * InventoryGridControl.CellPx, Core.Profile.StashRows * InventoryGridControl.CellPx) };
        left.AddChild(_stashHolder);

        VBoxContainer right = UiTheme.VBox(columns, 8);
        right.AddChild(UiTheme.Label("Inventory", 18, UiTheme.Gold));
        _inventoryHolder = new Control { CustomMinimumSize = new Vector2(Inventory.Width * InventoryGridControl.CellPx, Inventory.Height * InventoryGridControl.CellPx) };
        right.AddChild(_inventoryHolder);
        Label hint = UiTheme.Label("Right-click an item to move it between the stash and your inventory. Drag to rearrange. Tabs are shared by all your characters.", 14, UiTheme.TextDim);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.CustomMinimumSize = new Vector2(384f, 0f);
        right.AddChild(hint);
        _goldLabel = UiTheme.Label("", 16, UiTheme.Gold);
        right.AddChild(_goldLabel);
    }

    public override void Refresh()
    {
        if (Profile is not { } profile)
        {
            return;
        }

        // Live inventory nodes for every tab, built once per opening.
        if (_tabs.Count != profile.StashTabs.Count)
        {
            BuildTabs(profile);
        }

        ClearChildren(_tabsRow);
        for (int i = 0; i < _tabs.Count; i++)
        {
            int index = i;
            Button tab = UiTheme.Button($"Tab {i + 1}", () => { _current = index; Refresh(); }, 15, 74f);
            if (i == _current)
            {
                tab.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.24f, 0.18f, 0.32f), UiTheme.Gold, 2));
            }

            _tabsRow.AddChild(tab);
        }

        long cost = profile.NextStashTabCost;
        if (cost > 0)
        {
            Button buy = UiTheme.Button($"+ Buy tab ({cost} gold)", BuyTab, 15, 180f);
            buy.Disabled = Player.Wallet.GetBalance(Core.Profile.GoldId) < cost;
            _tabsRow.AddChild(buy);
        }

        ClearChildren(_stashHolder);
        _stashHolder.AddChild(new InventoryGridControl
        {
            InventoryNode = _tabs[_current],
            EquipmentNode = Player.Equipment,
            RightClickOverride = MoveToInventory,
        });

        ClearChildren(_inventoryHolder);
        _inventoryHolder.AddChild(new InventoryGridControl
        {
            InventoryNode = Player.Inventory,
            EquipmentNode = Player.Equipment,
            RightClickOverride = MoveToStash,
        });

        _goldLabel.Text = $"Gold: {Player.Wallet.GetBalance(Core.Profile.GoldId)}";
    }

    private void BuildTabs(Profile profile)
    {
        foreach (Inventory old in _tabs)
        {
            old.QueueFree();
        }

        _tabs.Clear();
        foreach (List<(EquipmentItem Item, Vector2I Cell)> data in profile.StashTabs)
        {
            var tab = new Inventory { Columns = Core.Profile.StashColumns, Rows = Core.Profile.StashRows, Name = $"StashTab{_tabs.Count}" };
            AddChild(tab);
            foreach ((EquipmentItem item, Vector2I cell) in data)
            {
                if (!tab.TryPlaceAt(item, cell))
                {
                    tab.TryAdd(item);
                }
            }

            _tabs.Add(tab);
        }

        _current = Mathf.Clamp(_current, 0, _tabs.Count - 1);
    }

    private bool MoveToStash(EquipmentItem item)
    {
        Inventory tab = _tabs[_current];
        if (!tab.TryFindFirstFit(item, out Vector2I at))
        {
            WorldMessage.Show(Player, "This stash tab is full", new Color(1f, 0.45f, 0.35f));
            return false;
        }

        Player.Inventory.Remove(item);
        tab.TryPlaceAt(item, at);
        Sfx.Play(this, "pickup", -6f);
        return true;
    }

    private bool MoveToInventory(EquipmentItem item)
    {
        if (!Player.Inventory.TryFindFirstFit(item, out Vector2I at))
        {
            WorldMessage.Show(Player, "Inventory full", new Color(1f, 0.45f, 0.35f));
            return false;
        }

        _tabs[_current].Remove(item);
        Player.Inventory.TryPlaceAt(item, at);
        Sfx.Play(this, "pickup", -6f);
        return true;
    }

    private void BuyTab()
    {
        if (Profile is not { } profile || profile.NextStashTabCost <= 0 || !Player.Wallet.TrySpend(Core.Profile.GoldId, profile.NextStashTabCost))
        {
            return;
        }

        profile.StashTabs.Add(new List<(EquipmentItem, Vector2I)>());
        _current = profile.StashTabs.Count - 1;
        Sfx.Play(this, "levelup", -6f);
        BuildTabs(profile);
        SaveBack();
        Refresh();
    }

    /// <summary>Writes the live tabs back into the profile and saves.</summary>
    private void SaveBack()
    {
        if (Profile is not { } profile)
        {
            return;
        }

        for (int i = 0; i < _tabs.Count && i < profile.StashTabs.Count; i++)
        {
            profile.StashTabs[i] = _tabs[i].Placements.Select(kv => (kv.Key, kv.Value)).ToList();
        }

        profile.CaptureFrom(Player);
    }

    protected override void OnClosed() => SaveBack();
}
