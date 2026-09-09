#nullable enable
using Godot;
using PathToExile.Items;

namespace PathToExile.UI;

/// <summary>
/// The nine-slot equipment strip (docs/design/items.md -&gt; "The equipment
/// strip") — ten labelled cells, one per <see cref="EquipmentSlot"/> value
/// (the design doc says "nine" while listing eight distinct categories plus
/// "Ring x2"; the milestone report flags this count mismatch. Every slot the
/// existing <see cref="EquipmentSlot"/> enum/<see cref="Equipment"/> component
/// already treats as independent — including both ring slots — gets its own
/// row here, since that is the enum this milestone builds on). Deliberately a
/// flat labelled list, not a paper-doll figure, per the design doc.
///
/// Same architecture as <see cref="InventoryGridControl"/>: one Control draws
/// every row and hit-tests by row index rather than one child node per slot,
/// and the same "nothing mutates until a drop lands" rule applies — see
/// <see cref="InventoryDragPayload"/>'s class doc.
///
/// Only accepts drops whose <see cref="InventoryDragPayload.Kind"/> is
/// <see cref="DragSourceKind.Grid"/> — slot-to-slot dragging (e.g. swapping
/// Ring1 and Ring2 by dragging between the two rows) is not part of this
/// milestone's scope ("drag between grid and equipment slots in both
/// directions," not slot-to-slot). Dropping a slot-sourced drag here simply
/// fails like any other rejected drop: nothing changes, the item stays
/// equipped where it was.
/// </summary>
[GlobalClass]
public partial class EquipmentStripControl : Control
{
    public const int RowPx = 32;
    public const int WidthPx = 160;

    private static readonly EquipmentSlot[] Order =
    {
        EquipmentSlot.Weapon,
        EquipmentSlot.Offhand,
        EquipmentSlot.Helmet,
        EquipmentSlot.BodyArmour,
        EquipmentSlot.Gloves,
        EquipmentSlot.Boots,
        EquipmentSlot.Belt,
        EquipmentSlot.Amulet,
        EquipmentSlot.Ring1,
        EquipmentSlot.Ring2,
    };

    private static string Label(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Weapon => "Weapon",
        EquipmentSlot.Offhand => "Offhand",
        EquipmentSlot.Helmet => "Helmet",
        EquipmentSlot.BodyArmour => "Body Armour",
        EquipmentSlot.Gloves => "Gloves",
        EquipmentSlot.Boots => "Boots",
        EquipmentSlot.Belt => "Belt",
        EquipmentSlot.Amulet => "Amulet",
        EquipmentSlot.Ring1 => "Ring 1",
        EquipmentSlot.Ring2 => "Ring 2",
        _ => slot.ToString(),
    };

    [Export]
    public NodePath InventoryPath = new();

    [Export]
    public NodePath EquipmentPath = new();

    private Inventory _inventory = null!;
    private Equipment _equipment = null!;

    public override void _Ready()
    {
        _inventory = GetNode<Inventory>(InventoryPath);
        _equipment = GetNode<Equipment>(EquipmentPath);

        CustomMinimumSize = new Vector2(WidthPx, Order.Length * RowPx);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Process(double delta)
    {
        if (Visible)
        {
            QueueRedraw();
        }
    }

    private static EquipmentSlot? SlotAt(Vector2 localPosition)
    {
        int row = Mathf.FloorToInt(localPosition.Y / RowPx);
        return row >= 0 && row < Order.Length ? Order[row] : null;
    }

    /// <summary>
    /// Symmetric convenience to the grid's right-click-to-equip shortcut:
    /// right-clicking an occupied strip row unequips it back into the grid
    /// (<see cref="Items.Inventory.TryUnequipToGrid"/>), which already fails
    /// safely (leaves the item equipped) if the grid has no room. Not called
    /// out explicitly in the design doc, which only specifies right-click in
    /// the grid — added because it is a direct, already-tested use of that
    /// method and an obvious symmetric affordance once the strip exists.
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } mouseButton)
        {
            return;
        }

        if (SlotAt(mouseButton.Position) is not { } slot)
        {
            return;
        }

        _inventory.TryUnequipToGrid(_equipment, slot);
        QueueRedraw();
        AcceptEvent();
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (SlotAt(atPosition) is not { } slot)
        {
            return default;
        }

        EquipmentItem? item = _equipment.GetEquipped(slot);
        if (item is null)
        {
            return default;
        }

        SetDragPreview(new Label { Text = item.Descriptor.DisplayName, Modulate = new Color(1, 1, 1, 0.85f) });
        return InventoryDragPayload.FromEquipmentSlot(item, slot).ToVariant();
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (SlotAt(atPosition) is not { } slot)
        {
            return false;
        }

        if (!InventoryDragPayload.TryParse(data, out InventoryDragPayload payload)
            || payload.Kind != DragSourceKind.Grid
            || payload.Item.Base is null)
        {
            return false;
        }

        return Equipment.IsValidSlot(payload.Item.Base.ItemClass, slot);
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (SlotAt(atPosition) is not { } slot || !InventoryDragPayload.TryParse(data, out InventoryDragPayload payload))
        {
            return;
        }

        // The item is still sitting in the grid at this point (mutation is
        // deferred to drop-acceptance — see InventoryDragPayload's class doc),
        // so Inventory.TryEquipFromGrid can look it up by its own placement.
        _inventory.TryEquipFromGrid(payload.Item, slot, _equipment);
        QueueRedraw();
    }

    public override void _Draw()
    {
        Font font = ThemeDB.FallbackFont;
        int fontSize = ThemeDB.FallbackFontSize;

        for (int i = 0; i < Order.Length; i++)
        {
            EquipmentSlot slot = Order[i];
            var rect = new Rect2(0, i * RowPx, WidthPx, RowPx);

            EquipmentItem? equipped = _equipment.GetEquipped(slot);
            Color fill = equipped is not null
                ? new Color(0.3f, 0.5f, 0.3f, 0.9f)
                : new Color(0.15f, 0.15f, 0.18f, 0.9f);

            DrawRect(rect, fill);
            DrawRect(rect, new Color(0, 0, 0, 0.6f), filled: false);

            string text = equipped?.Descriptor.DisplayName ?? Label(slot);
            DrawString(font, rect.Position + new Vector2(4, 20), text,
                HorizontalAlignment.Left, rect.Size.X - 8, fontSize);
        }
    }
}
