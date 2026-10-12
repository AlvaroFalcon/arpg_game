#nullable enable
using Godot;
using ArpgGame.Items;

namespace ArpgGame.UI;

/// <summary>
/// The equipment paper doll — docs/design/items.md -&gt; "Paper doll" (replaces
/// the earlier flat labelled strip, at the owner's request). A body silhouette
/// with every <see cref="EquipmentSlot"/> placed on it: helmet on the head,
/// amulet at the neck, body armour on the torso, belt at the waist, pants below
/// it, the main weapon in the left arm and the offhand/shield in the right,
/// gloves under the left hand, boots under the right hand, a ring by each hand.
///
/// Same architecture as <see cref="InventoryGridControl"/>: one Control draws
/// every slot and hit-tests by rectangle rather than one child node per slot,
/// and the same "nothing mutates until a drop lands" rule applies — see
/// <see cref="InventoryDragPayload"/>'s class doc. Only grid-sourced drags drop
/// here (slot-to-slot dragging is out of scope).
/// </summary>
[GlobalClass]
public partial class EquipmentStripControl : Control
{
    public const int WidthPx = 384;
    public const int HeightPx = 300;

    private static readonly (EquipmentSlot Slot, Rect2 Rect)[] Layout =
    {
        (EquipmentSlot.Helmet, new Rect2(160, 4, 64, 64)),
        (EquipmentSlot.Amulet, new Rect2(228, 8, 42, 42)),
        (EquipmentSlot.BodyArmour, new Rect2(148, 76, 88, 110)),
        (EquipmentSlot.Weapon, new Rect2(56, 54, 64, 128)),
        (EquipmentSlot.Offhand, new Rect2(264, 54, 64, 128)),
        (EquipmentSlot.Ring1, new Rect2(6, 116, 44, 44)),
        (EquipmentSlot.Ring2, new Rect2(334, 116, 44, 44)),
        (EquipmentSlot.Gloves, new Rect2(56, 190, 64, 64)),
        (EquipmentSlot.Boots, new Rect2(264, 190, 64, 64)),
        (EquipmentSlot.Belt, new Rect2(148, 190, 88, 26)),
        (EquipmentSlot.Pants, new Rect2(160, 220, 64, 76)),
    };

    private static string Label(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Weapon => "Weapon",
        EquipmentSlot.Offhand => "Offhand",
        EquipmentSlot.Helmet => "Helmet",
        EquipmentSlot.BodyArmour => "Body",
        EquipmentSlot.Gloves => "Gloves",
        EquipmentSlot.Boots => "Boots",
        EquipmentSlot.Belt => "Belt",
        EquipmentSlot.Amulet => "Amulet",
        EquipmentSlot.Ring1 => "Ring",
        EquipmentSlot.Ring2 => "Ring",
        EquipmentSlot.Pants => "Pants",
        _ => slot.ToString(),
    };

    [Export]
    public NodePath InventoryPath = new();

    [Export]
    public NodePath EquipmentPath = new();

    /// <summary>Same optional-wiring convention as
    /// <see cref="InventoryGridControl.ItemTooltipPath"/> — see
    /// <see cref="ItemTooltipControl"/>'s class doc.</summary>
    [Export]
    public NodePath ItemTooltipPath = new();

    private Inventory _inventory = null!;
    private Equipment _equipment = null!;
    private ItemTooltipControl? _tooltip;

    public override void _Ready()
    {
        _inventory = GetNode<Inventory>(InventoryPath);
        _equipment = GetNode<Equipment>(EquipmentPath);
        _tooltip = GetNodeOrNull<ItemTooltipControl>(ItemTooltipPath)
            ?? GetTree().CurrentScene.GetNodeOrNull<ItemTooltipControl>("ItemTooltip");

        // Every tooltip call site is null-conditional, so an unresolved tooltip
        // disables the whole feature in total silence. Say so instead.
        if (_tooltip is null)
        {
            GD.PushWarning("[strip] ItemTooltipControl not found: no 'ItemTooltip' node on the current scene, and ItemTooltipPath is unset. Tooltips are disabled.");
        }

        CustomMinimumSize = new Vector2(WidthPx, HeightPx);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Process(double delta)
    {
        // IsVisibleInTree, not Visible: this control stays Visible while the
        // inventory panel around it is hidden, and must not show tooltips then.
        if (IsVisibleInTree())
        {
            QueueRedraw();
            UpdateHoverTooltip();
        }
        else
        {
            _tooltip?.Clear(this);
        }
    }

    /// <summary>Same per-frame poll shape as
    /// <see cref="InventoryGridControl.UpdateHoverTooltip"/>: hit-tests the mouse
    /// against the slot rectangles every visible frame.</summary>
    private void UpdateHoverTooltip()
    {
        EquipmentItem? hovered = SlotAt(GetLocalMousePosition()) is { } slot ? _equipment.GetEquipped(slot) : null;
        if (hovered is not null)
        {
            _tooltip?.Show(hovered, this);
        }
        else
        {
            _tooltip?.Clear(this);
        }
    }

    private static EquipmentSlot? SlotAt(Vector2 localPosition)
    {
        foreach ((EquipmentSlot slot, Rect2 rect) in Layout)
        {
            if (rect.HasPoint(localPosition))
            {
                return slot;
            }
        }

        return null;
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
        DrawSilhouette();
        Font font = ThemeDB.FallbackFont;
        foreach ((EquipmentSlot slot, Rect2 rect) in Layout)
        {
            EquipmentItem? equipped = _equipment.GetEquipped(slot);
            Color rarity = equipped is not null ? RarityColor(equipped) : new Color(0.3f, 0.28f, 0.38f);
            DrawRect(rect, equipped is not null ? new Color(0.1f, 0.09f, 0.14f, 0.95f) : new Color(0.07f, 0.06f, 0.1f, 0.85f));
            DrawRect(rect, rarity, filled: false, width: equipped is not null ? 2f : 1f);

            if (equipped is null)
            {
                DrawString(font, new Vector2(rect.Position.X, rect.GetCenter().Y + 5f), Label(slot),
                    HorizontalAlignment.Center, rect.Size.X, 12, new Color(0.45f, 0.43f, 0.55f));
                continue;
            }

            // Jewellery slots are too small for a name: a gem in the rarity colour
            // (the tooltip has the details).
            if (rect.Size.X < 50f)
            {
                Vector2 c = rect.GetCenter();
                DrawColoredPolygon(new[] { c + new Vector2(0, -12), c + new Vector2(11, 0), c + new Vector2(0, 12), c + new Vector2(-11, 0) }, rarity);
                DrawCircle(c + new Vector2(-3f, -4f), 2.5f, new Color(1f, 1f, 1f, 0.7f));
                continue;
            }

            // Item name wrapped into the slot, in its rarity colour.
            string[] words = equipped.Descriptor.DisplayName.Split(' ');
            float y = rect.Position.Y + 14f;
            string line = "";
            foreach (string w in words)
            {
                string attempt = line.Length == 0 ? w : line + " " + w;
                if (font.GetStringSize(attempt, HorizontalAlignment.Left, -1, 11).X > rect.Size.X - 6f && line.Length > 0)
                {
                    DrawString(font, new Vector2(rect.Position.X + 3f, y), line, HorizontalAlignment.Left, rect.Size.X - 6f, 11, rarity);
                    y += 12f;
                    line = w;
                }
                else
                {
                    line = attempt;
                }

                if (y > rect.End.Y - 4f)
                {
                    break;
                }
            }

            if (line.Length > 0 && y <= rect.End.Y - 4f)
            {
                DrawString(font, new Vector2(rect.Position.X + 3f, y), line, HorizontalAlignment.Left, rect.Size.X - 6f, 11, rarity);
            }
        }
    }

    private static Color RarityColor(EquipmentItem item) => item.Rarity switch
    {
        Rarity.Magic => new Color(0.5f, 0.6f, 1f),
        Rarity.Rare => new Color(1f, 0.88f, 0.4f),
        _ => new Color(0.85f, 0.85f, 0.85f),
    };

    /// <summary>A faint body behind the slots so the layout reads as a person.</summary>
    private void DrawSilhouette()
    {
        var body = new Color(0.24f, 0.21f, 0.3f, 1f);
        DrawCircle(new Vector2(192f, 36f), 30f, body);                 // head
        DrawRect(new Rect2(180, 62, 24, 16), body);                     // neck
        DrawRect(new Rect2(140, 72, 104, 120), body);                   // torso
        DrawRect(new Rect2(100, 76, 44, 22), body);                     // shoulders
        DrawRect(new Rect2(240, 76, 44, 22), body);
        DrawRect(new Rect2(78, 84, 26, 110), body);                     // arms
        DrawRect(new Rect2(280, 84, 26, 110), body);
        DrawRect(new Rect2(156, 192, 30, 104), body);                   // legs
        DrawRect(new Rect2(198, 192, 30, 104), body);
    }
}
