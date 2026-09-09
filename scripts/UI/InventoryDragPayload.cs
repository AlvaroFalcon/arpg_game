#nullable enable
using Godot;
using PathToExile.Items;

namespace PathToExile.UI;

/// <summary>Where a dragged item came from — the other half of
/// <see cref="InventoryDragPayload"/>, read by whichever control the item is
/// dropped on to decide what to unwind if the target rejects it (nothing: see
/// that type's doc comment) and what the source needs unequipped/removed if
/// the target accepts it.</summary>
public enum DragSourceKind
{
    Grid,
    EquipmentSlot,
}

/// <summary>
/// Drag payload for docs/design/items.md's grid &lt;-&gt; equipment strip
/// drag-and-drop (see "Inventory interaction"). Godot's <c>_GetDragData</c> /
/// <c>_CanDropData</c> / <c>_DropData</c> trio is typed in terms of
/// <see cref="Variant"/>, so this wraps a plain <see cref="Godot.Collections.Dictionary"/>
/// — the simplest Variant-native carrier for "an item plus where it came
/// from" — behind a small typed struct so call sites never touch dictionary
/// keys directly.
///
/// <b>Nothing is mutated until a drop is accepted.</b> The dragging control's
/// <c>_GetDragData</c> only reads (see <see cref="InventoryGridControl"/> and
/// <see cref="EquipmentStripControl"/>'s class docs) — the item is still
/// exactly where <see cref="Items.Inventory"/>/<see cref="Equipment"/> had it
/// for the whole drag. That is what makes "drop on nothing returns it to
/// where it came from" free: if no target's <c>_CanDropData</c> accepts the
/// drop, nothing was ever changed in the first place, so there is nothing to
/// undo.
/// </summary>
public readonly struct InventoryDragPayload
{
    public EquipmentItem Item { get; }
    public DragSourceKind Kind { get; }

    /// <summary>Valid when <see cref="Kind"/> is <see cref="DragSourceKind.Grid"/>.</summary>
    public Vector2I GridOrigin { get; }

    /// <summary>Valid when <see cref="Kind"/> is <see cref="DragSourceKind.EquipmentSlot"/>.</summary>
    public EquipmentSlot Slot { get; }

    private InventoryDragPayload(EquipmentItem item, DragSourceKind kind, Vector2I gridOrigin, EquipmentSlot slot)
    {
        Item = item;
        Kind = kind;
        GridOrigin = gridOrigin;
        Slot = slot;
    }

    public static InventoryDragPayload FromGrid(EquipmentItem item, Vector2I origin) =>
        new(item, DragSourceKind.Grid, origin, default);

    public static InventoryDragPayload FromEquipmentSlot(EquipmentItem item, EquipmentSlot slot) =>
        new(item, DragSourceKind.EquipmentSlot, default, slot);

    private const string ItemKey = "item";
    private const string KindKey = "kind";
    private const string XKey = "x";
    private const string YKey = "y";
    private const string SlotKey = "slot";

    public Variant ToVariant()
    {
        var dict = new Godot.Collections.Dictionary
        {
            [ItemKey] = Item,
            [KindKey] = (int)Kind,
            [XKey] = GridOrigin.X,
            [YKey] = GridOrigin.Y,
            [SlotKey] = (int)Slot,
        };
        return dict;
    }

    /// <summary>
    /// Parses a drop's <see cref="Variant"/> payload back into a typed
    /// struct. Returns false for anything that isn't one of our own payloads
    /// (a stray drag from elsewhere in the engine, or the item having been
    /// freed between drag-start and drop) rather than throwing — a rejected
    /// drop is an ordinary, expected outcome of <c>_CanDropData</c>, not an
    /// error.
    /// </summary>
    public static bool TryParse(Variant data, out InventoryDragPayload payload)
    {
        payload = default;

        if (data.VariantType != Variant.Type.Dictionary)
        {
            return false;
        }

        Godot.Collections.Dictionary dict = data.AsGodotDictionary();
        if (!dict.TryGetValue(ItemKey, out Variant itemVariant)
            || itemVariant.AsGodotObject() is not EquipmentItem item
            || !GodotObject.IsInstanceValid(item))
        {
            return false;
        }

        if (!dict.TryGetValue(KindKey, out Variant kindVariant))
        {
            return false;
        }

        var kind = (DragSourceKind)kindVariant.AsInt32();
        int x = dict.TryGetValue(XKey, out Variant xVariant) ? xVariant.AsInt32() : 0;
        int y = dict.TryGetValue(YKey, out Variant yVariant) ? yVariant.AsInt32() : 0;
        var slot = dict.TryGetValue(SlotKey, out Variant slotVariant) ? (EquipmentSlot)slotVariant.AsInt32() : default;

        payload = new InventoryDragPayload(item, kind, new Vector2I(x, y), slot);
        return true;
    }
}
