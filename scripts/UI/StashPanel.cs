#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Core;
using ArpgGame.Items;

namespace ArpgGame.UI;

/// <summary>
/// The stash — docs/design/items.md -> "Stash". Opened at the chest in the
/// hideout (E). Account-wide tabs (shared by every character, like PoE), each a
/// 12×10 grid; the first is free and more are bought with gold, each costing
/// double the last. Right-click moves an item between the stash tab and the
/// inventory (PoE's ctrl-click); dragging rearranges within a grid.
///
/// The last tab is **Pending items** (docs/design/items.md -> "Pending items"):
/// rewards that found a full inventory. It only gives: right-click sends an
/// item to the inventory, "Move all to stash" empties it into the tabs.
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
    private Inventory? _pending;
    private Label _pendingNote = null!;

    /// <summary>The tab index that shows Pending items.</summary>
    public const int PendingTab = -1;

    /// <summary>True while the Pending items tab is shown.</summary>
    public bool ShowingPending => _current == PendingTab;

    /// <summary>The live Pending items grid while the panel is built (tests, captures).</summary>
    public Inventory? PendingGrid => _pending;

    private Profile? Profile => Core.Profile.Get(this);

    protected override void Build(VBoxContainer body)
    {
        HBoxContainer columns = UiTheme.HBox(body, 24);

        VBoxContainer left = UiTheme.VBox(columns, 8);
        _tabsRow = UiTheme.HBox(left, 4);
        _stashHolder = new Control { CustomMinimumSize = new Vector2(Core.Profile.StashColumns * InventoryGridControl.CellPx, Core.Profile.StashRows * InventoryGridControl.CellPx) };
        left.AddChild(_stashHolder);
        _pendingNote = UiTheme.Label("", 14, UiTheme.TextDim);
        _pendingNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pendingNote.CustomMinimumSize = new Vector2(Core.Profile.StashColumns * InventoryGridControl.CellPx, 0f);
        left.AddChild(_pendingNote);

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
        if (_tabs.Count != profile.StashTabs.Count || _pending is null)
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

        int waiting = _pending!.Placements.Count;
        Button pendingTab = UiTheme.Button($"Pending ({waiting}/{Core.Profile.OverflowCap})", () => { _current = PendingTab; Refresh(); }, 15, 120f);
        pendingTab.Name = "PendingTab";
        pendingTab.TooltipText = "Rewards that arrived while your inventory was full";
        if (waiting > 0)
        {
            pendingTab.AddThemeColorOverride("font_color", UiTheme.Ember);
        }

        if (ShowingPending)
        {
            pendingTab.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.24f, 0.18f, 0.32f), UiTheme.Gold, 2));
        }

        _tabsRow.AddChild(pendingTab);

        ClearChildren(_stashHolder);
        _stashHolder.AddChild(new InventoryGridControl
        {
            InventoryNode = ShowingPending ? _pending : _tabs[_current],
            EquipmentNode = Player.Equipment,
            RightClickOverride = MoveToInventory,
        });
        _pendingNote.Visible = ShowingPending;
        if (ShowingPending)
        {
            _pendingNote.Text = waiting == 0
                ? "Pending items is empty. Rewards that find your inventory full wait here."
                : $"Rewards that found your inventory full. Right-click one to take it, or move them all to the stash. Holds {Core.Profile.OverflowCap}: when a new reward arrives with it full, the oldest is sold for gold.";
            if (waiting > 0)
            {
                Button all = UiTheme.Button("Move all to stash", () => MoveAllPendingToStash(), 14, 160f);
                all.Name = "MoveAllPending";
                _stashHolder.AddChild(all);
                all.Position = new Vector2(Core.Profile.StashColumns * InventoryGridControl.CellPx - 164f, Core.Profile.StashRows * InventoryGridControl.CellPx - 40f);
            }
        }

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

        _pending?.QueueFree();
        _pending = new Inventory { Columns = Core.Profile.OverflowColumns, Rows = Core.Profile.OverflowRows, Name = "PendingItems" };
        AddChild(_pending);
        foreach ((EquipmentItem item, Vector2I cell) in profile.Overflow)
        {
            if (!_pending.TryPlaceAt(item, cell))
            {
                _pending.TryAdd(item);
            }
        }

        // Opens on Pending items when something is waiting there.
        _current = profile.Overflow.Count > 0 && !_openedOnce ? PendingTab : _current == PendingTab ? PendingTab : Mathf.Clamp(_current, 0, _tabs.Count - 1);
        _openedOnce = true;
    }

    private bool _openedOnce;

    /// <summary>Empties Pending items into the stash tabs (first tab with room for each).
    /// Returns how many moved.</summary>
    public int MoveAllPendingToStash()
    {
        if (_pending is null)
        {
            return 0;
        }

        int moved = 0;
        foreach (EquipmentItem item in _pending.Placements.Keys.ToList())
        {
            foreach (Inventory tab in _tabs)
            {
                if (tab.TryFindFirstFit(item, out Vector2I at))
                {
                    _pending.Remove(item);
                    tab.TryPlaceAt(item, at);
                    moved++;
                    break;
                }
            }
        }

        if (moved > 0)
        {
            Sfx.Play(this, "pickup", -6f);
        }

        if (_pending.Placements.Count > 0)
        {
            WorldMessage.Show(Player, "The stash is full", new Color(1f, 0.45f, 0.35f));
        }

        SaveBack();
        Refresh();
        return moved;
    }

    /// <summary>Shows a stash tab, or Pending items with <see cref="PendingTab"/> (tests, captures).</summary>
    public void ShowTab(int index)
    {
        _current = index == PendingTab ? PendingTab : Mathf.Clamp(index, 0, Mathf.Max(0, _tabs.Count - 1));
        Refresh();
    }

    /// <summary>Moves one Pending item to the inventory as a right-click would (tests).</summary>
    public bool TakePending(EquipmentItem item) => ShowingPending && MoveToInventory(item);

    private bool MoveToStash(EquipmentItem item)
    {
        if (ShowingPending)
        {
            WorldMessage.Show(Player, "Pending items only holds rewards — pick a stash tab", new Color(1f, 0.45f, 0.35f));
            return false;
        }

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

        (ShowingPending ? _pending! : _tabs[_current]).Remove(item);
        Player.Inventory.TryPlaceAt(item, at);
        Sfx.Play(this, "pickup", -6f);
        if (ShowingPending)
        {
            SaveBack();
            CallDeferred(nameof(Refresh)); // the tab's count
        }

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

        if (_pending is not null)
        {
            // Keep arrival order (oldest first: the one sold at the cap).
            var still = profile.Overflow.Where(e => _pending.Placements.ContainsKey(e.Item)).Select(e => (e.Item, _pending.Placements[e.Item])).ToList();
            profile.Overflow.Clear();
            profile.Overflow.AddRange(still);
        }

        profile.CaptureFrom(Player);
    }

    protected override void OnClosed() => SaveBack();
}
