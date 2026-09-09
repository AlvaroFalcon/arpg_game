#nullable enable
using Godot;

namespace PathToExile.UI;

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
/// </summary>
[GlobalClass]
public partial class InventoryUI : CanvasLayer
{
    private Control _panel = null!;

    public override void _Ready()
    {
        _panel = GetNode<Control>("Panel");
        _panel.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("toggle_inventory"))
        {
            _panel.Visible = !_panel.Visible;
        }
    }
}
