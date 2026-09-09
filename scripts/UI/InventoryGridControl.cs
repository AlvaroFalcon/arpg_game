#nullable enable
using Godot;
using PathToExile.Items;

namespace PathToExile.UI;

/// <summary>
/// Draws the 12x5 grid (docs/design/items.md -&gt; "Grid inventory") and owns
/// every grid-side interaction from "Inventory interaction": drag-to-rearrange,
/// drag to/from the equipment strip, and the right-click equip shortcut.
///
/// A single <see cref="Control"/> rather than one child Control per cell or
/// item — items can span multiple cells and the whole grid is 60 cells, so
/// hand-rolled hit-testing against <see cref="Items.Inventory.GetItemAt"/> plus
/// one <c>_Draw</c> call is simpler than 60 nodes' worth of scene structure,
/// and it still goes entirely through Godot's documented
/// <c>_GuiInput</c>/<c>_GetDragData</c>/<c>_CanDropData</c>/<c>_DropData</c>
/// contract rather than hand-rolled mouse tracking.
///
/// <b>Mutation timing:</b> <see cref="_GetDragData"/> only reads —
/// nothing is removed from <see cref="Items.Inventory"/> or
/// <see cref="Equipment"/> until a drop is actually accepted in
/// <see cref="_DropData"/>. See <see cref="InventoryDragPayload"/>'s class doc
/// for why that is what makes "drop on nothing returns it to where it came
/// from" free.
/// </summary>
[GlobalClass]
public partial class InventoryGridControl : Control
{
    public const int CellPx = 32;

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

        CustomMinimumSize = new Vector2(Inventory.Width * CellPx, Inventory.Height * CellPx);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Process(double delta)
    {
        // The world keeps running behind the panel (docs/design/items.md ->
        // "Inventory interaction": "the world keeps running"), so a pickup can
        // land in the grid while it's open — redraw every visible frame rather
        // than only on explicit mutation, to stay cheap-but-correct at 60 cells.
        if (Visible)
        {
            QueueRedraw();
        }
    }

    private static Vector2I CellAt(Vector2 localPosition) =>
        new(Mathf.FloorToInt(localPosition.X / CellPx), Mathf.FloorToInt(localPosition.Y / CellPx));

    /// <summary>Right-click equip shortcut (docs/design/items.md -&gt; "The
    /// equipment strip"). Left-click drag is handled entirely through
    /// <see cref="_GetDragData"/> and needs no code here — Godot's Control
    /// drag machinery detects the press-and-move itself.</summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } mouseButton)
        {
            return;
        }

        EquipmentItem? item = _inventory.GetItemAt(CellAt(mouseButton.Position));
        if (item is null)
        {
            return;
        }

        _inventory.TryRightClickEquip(item, _equipment);
        QueueRedraw();
        AcceptEvent();
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        EquipmentItem? item = _inventory.GetItemAt(CellAt(atPosition));
        if (item is null || !_inventory.Placements.TryGetValue(item, out Vector2I origin))
        {
            return default;
        }

        SetDragPreview(BuildPreview(item));
        return InventoryDragPayload.FromGrid(item, origin).ToVariant();
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        return InventoryDragPayload.TryParse(data, out InventoryDragPayload payload)
            && _inventory.CanPlaceAt(payload.Item, CellAt(atPosition), ignore: payload.Item);
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!InventoryDragPayload.TryParse(data, out InventoryDragPayload payload))
        {
            return;
        }

        // Coming from a slot: it is still equipped right up to this point (see
        // this type's class doc on mutation timing), so take it off now that a
        // grid landing spot has actually been confirmed.
        if (payload.Kind == DragSourceKind.EquipmentSlot)
        {
            _equipment.Unequip(payload.Slot);
        }

        _inventory.TryPlaceAt(payload.Item, CellAt(atPosition));
        QueueRedraw();
    }

    private static Control BuildPreview(EquipmentItem item)
    {
        return new Label
        {
            Text = item.Descriptor.DisplayName,
            Modulate = new Color(1, 1, 1, 0.85f),
        };
    }

    public override void _Draw()
    {
        var gridSize = new Vector2(Inventory.Width * CellPx, Inventory.Height * CellPx);
        DrawRect(new Rect2(Vector2.Zero, gridSize), new Color(0.1f, 0.1f, 0.12f, 0.9f));

        var lineColor = new Color(1, 1, 1, 0.15f);
        for (int x = 0; x <= Inventory.Width; x++)
        {
            DrawLine(new Vector2(x * CellPx, 0), new Vector2(x * CellPx, gridSize.Y), lineColor);
        }

        for (int y = 0; y <= Inventory.Height; y++)
        {
            DrawLine(new Vector2(0, y * CellPx), new Vector2(gridSize.X, y * CellPx), lineColor);
        }

        Font font = ThemeDB.FallbackFont;
        int fontSize = ThemeDB.FallbackFontSize;

        foreach ((EquipmentItem item, Vector2I origin) in _inventory.Placements)
        {
            if (item.Base is null)
            {
                continue;
            }

            var rect = new Rect2(
                origin.X * CellPx + 1,
                origin.Y * CellPx + 1,
                item.Base.Width * CellPx - 2,
                item.Base.Height * CellPx - 2);

            DrawRect(rect, RarityFill(item.Rarity));
            DrawRect(rect, new Color(0, 0, 0, 0.6f), filled: false);
            DrawString(font, rect.Position + new Vector2(4, 16), item.Base.DisplayName,
                HorizontalAlignment.Left, rect.Size.X - 8, fontSize);
        }
    }

    /// <summary>Cheap in-grid rarity cue. Not the item filter's
    /// <see cref="DisplayStyle"/> — this is a fixed debug-grade fill, the same
    /// "not polish yet" trade-off <see cref="UI.DebugReadout"/> documents for
    /// the life/mana readout.</summary>
    private static Color RarityFill(Rarity rarity) => rarity switch
    {
        Rarity.Magic => new Color(0.25f, 0.35f, 0.75f, 0.9f),
        Rarity.Rare => new Color(0.75f, 0.7f, 0.2f, 0.9f),
        Rarity.Majestic => new Color(0.8f, 0.35f, 0.6f, 0.9f),
        Rarity.Set => new Color(0.85f, 0.5f, 0.15f, 0.9f),
        _ => new Color(0.4f, 0.4f, 0.45f, 0.9f),
    };
}
