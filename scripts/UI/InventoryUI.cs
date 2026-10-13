#nullable enable
using Godot;

namespace ArpgGame.UI;

/// <summary>
/// Toggles the inventory/equipment panel on <c>toggle_inventory</c> ("i")
/// — docs/design/items.md -&gt; "Inventory interaction": "The world keeps
/// running behind it — this is an ARPG, not a menu game." That is why this
/// only flips a child <see cref="Control"/>'s visibility rather than pausing
/// <see cref="SceneTree"/> or setting <c>ProcessMode</c> — every sibling
/// system (movement, skills, enemy AI) keeps ticking exactly as before.
///
/// A <see cref="CanvasLayer"/> like <see cref="DebugReadout"/>, and found the
/// same way by its own children (<see cref="InventoryGridControl"/>/
/// <see cref="EquipmentStripControl"/> each point straight at the player's
/// "Inventory"/"Equipment" siblings via their own <c>[Export]</c>
/// <see cref="NodePath"/>s) rather than this node brokering references between
/// them — nothing here needs to know what its children actually do.
///
/// Also this project's one <see cref="PanelStack"/> participant today —
/// docs/design/items.md -&gt; "Closing panels": "<c>i</c> still toggles the
/// inventory specifically," so this keeps polling <c>toggle_inventory</c>
/// itself, but joins the shared stack (<see cref="IClosablePanel"/>) so
/// <c>Escape</c> can close it too without this class binding its own Escape
/// handler. This is also the one place that polls <c>Escape</c>/<c>ui_cancel</c>
/// at all — not because <see cref="PanelStack"/> requires a specific owner, but
/// because a keyboard action needs exactly one poller to avoid double-handling,
/// and this node already polls every frame for its own toggle key. A future
/// second panel does not need to move this call: it only has to
/// push/remove itself on <see cref="PanelStack"/>, same as this class does in
/// <see cref="SetOpen"/>.
/// </summary>
[GlobalClass]
public partial class InventoryUI : CanvasLayer, IClosablePanel
{
    private Control _panel = null!;
    private Control _strip = null!;
    private Button _inventoryTab = null!;
    private Button _craftingTab = null!;

    /// <summary>The Crafting tab (docs/design/crafting.md -> "Using orbs").</summary>
    public CraftingBoard Board { get; private set; } = null!;

    public bool CraftingShown { get; private set; }

    public override void _Ready()
    {
        _panel = GetNode<Control>("Panel");
        _panel.Visible = false;
        _strip = _panel.GetNode<Control>("StripControl");

        // Inventory | Crafting tabs over the panel. Crafting swaps the paper doll
        // for the orb board; the grid stays, since orbs are used on its items.
        var tabs = new HBoxContainer { Position = new Vector2(0f, -38f) };
        tabs.AddThemeConstantOverride("separation", 4);
        _panel.AddChild(tabs);
        _inventoryTab = UiTheme.Button("Inventory", () => ShowCrafting(false), 15, 120f);
        _craftingTab = UiTheme.Button("Crafting", () => ShowCrafting(true), 15, 120f);
        tabs.AddChild(_inventoryTab);
        tabs.AddChild(_craftingTab);
        Board = new CraftingBoard
        {
            Player = GetParent<Player>(),
            Grid = _panel.GetNode<InventoryGridControl>("GridControl"),
            Position = new Vector2(16f, 16f),
            Visible = false,
            Name = "CraftingBoard",
        };
        _panel.AddChild(Board);
        StyleTabs();

        // docs/design/items.md -> "Sorting". The button lives in the scene so its
        // placement is an editor concern; the policy lives in InventorySorter.
        _panel.GetNode<Button>("SortButton").Pressed += OnSortPressed;
    }

    /// <summary>
    /// A sort that cannot fully repack leaves the grid untouched and says so,
    /// rather than half-applying — see InventorySorter.TrySort. Failure should be
    /// unreachable in practice (the items being packed are the ones that already
    /// fitted), so it is worth a warning if it ever happens rather than a silent
    /// no-op the player would read as a dead button.
    /// </summary>
    private void OnSortPressed()
    {
        var inventory = GetNode<Items.Inventory>("../Inventory");
        if (!Items.InventorySorter.TrySort(inventory))
        {
            GD.PushWarning("Inventory sort could not repack every item; the grid was left unchanged.");
        }
    }

    public void ShowCrafting(bool crafting)
    {
        CraftingShown = crafting;
        _strip.Visible = !crafting;
        if (crafting)
        {
            Board.Activate();
        }
        else
        {
            Board.Deactivate();
        }

        StyleTabs();
    }

    private void StyleTabs()
    {
        foreach ((Button tab, bool on) in new[] { (_inventoryTab, !CraftingShown), (_craftingTab, CraftingShown) })
        {
            tab.AddThemeStyleboxOverride("normal", UiTheme.Box(on ? new Color(0.24f, 0.18f, 0.32f) : new Color(0.07f, 0.06f, 0.1f, 0.97f), on ? UiTheme.Gold : UiTheme.PanelBorder, on ? 2 : 1));
        }
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("toggle_inventory"))
        {
            SetOpen(!_panel.Visible);
        }

        if (Input.IsActionJustPressed("ui_cancel"))
        {
            // An orb in hand is put away first; the next Esc closes the panel.
            if (_panel.Visible && Board.Holding)
            {
                Board.Disarm();
                PanelStack.MarkClosedThisFrame();
            }
            else
            {
                PanelStack.CloseTopmost();
            }
        }
    }

    /// <summary>The single place <see cref="_panel"/>'s visibility actually
    /// changes, so it and <see cref="PanelStack"/> can never disagree about
    /// whether this panel is open — both the toggle key and
    /// <see cref="Close"/> (Escape, via <see cref="PanelStack.CloseTopmost"/>)
    /// go through this one method.</summary>
    public bool IsOpen => _panel.Visible;

    public void SetOpen(bool open)
    {
        _panel.Visible = open;
        if (open)
        {
            PanelStack.Push(this);
            if (CraftingShown)
            {
                Board.Activate();
            }
        }
        else
        {
            PanelStack.Remove(this);
            if (CraftingShown)
            {
                Board.Deactivate();
            }
        }
    }

    /// <summary><see cref="IClosablePanel"/>'s contract, called only by
    /// <see cref="PanelStack.CloseTopmost"/> — never bind this to a key
    /// directly here; that is exactly the per-panel special case
    /// docs/design/items.md's "Closing panels" section rejects.</summary>
    public void Close() => SetOpen(false);
}
