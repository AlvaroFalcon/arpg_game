#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Items;

namespace ArpgGame.UI;

/// <summary>
/// Renders whatever <see cref="ItemTooltipText.Build"/> hands it, following the
/// mouse cursor — docs/design/items.md -&gt; "Item tooltips". A single shared
/// instance rather than one per hoverable control, found the same
/// convention-over-configuration way <see cref="Items.GroundLabelPool"/> is: an
/// optional <c>[Export]</c> <see cref="NodePath"/>, falling back to a
/// well-known sibling under the current scene root
/// (<c>GetTree().CurrentScene.GetNodeOrNull&lt;ItemTooltipControl&gt;("ItemTooltip")</c>).
/// That single-instance-by-convention shape is what lets three unrelated
/// hover sources — <see cref="InventoryGridControl"/>, <see cref="EquipmentStripControl"/>,
/// and <see cref="Items.EquipmentItem"/> itself (ground hover) — all show the
/// same tooltip without a shared parent node to route through.
///
/// Deliberately a dumb renderer: it has no idea what rarity, tiers, or affixes
/// are, and never reads <see cref="Items.Rarity"/> or evaluates a
/// <see cref="Items.FilterChain"/> itself — see <see cref="ItemTooltipText"/>'s
/// class doc for why that separation is load-bearing, not just tidy.
/// </summary>
[GlobalClass]
public partial class ItemTooltipControl : CanvasLayer
{
    /// <summary>Offset from the cursor so the tooltip doesn't sit directly under
    /// the pointer, which would otherwise immediately re-trigger a hover-exit on
    /// whatever's being pointed at.</summary>
    [Export]
    public Vector2 CursorOffset = new(18f, 18f);

    private RichTextLabel _label = null!;

    /// <summary>Whoever most recently called <see cref="Show"/>. See <see cref="Clear"/>.</summary>
    private object? _owner;

    public override void _Ready()
    {
        // A tooltip is the topmost thing on screen by definition, so it sets its own
        // layer rather than depending on where it happens to sit in the tree. Every
        // other CanvasLayer here defaults to layer 0, which makes draw order fall
        // back to tree position — and in Main.tscn this node is declared *before*
        // the Player instance that owns InventoryUI, so at layer 0 the inventory
        // panel drew straight over it and hovering a grid item showed nothing at
        // all. Anchoring the value here means no scene can reintroduce that by
        // reordering nodes.
        Layer = 100;

        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(panel);

        _label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(250f, 0f),
        };
        panel.AddChild(_label);

        Visible = false;
    }

    /// <summary>Shows (or refreshes) the tooltip for <paramref name="item"/> at
    /// the current mouse position. Safe to call every frame from a hover-polling
    /// <c>_Process</c> — it only ever overwrites the same label/position, never
    /// accumulates state.</summary>
    /// <param name="compare">Append the "compared to what you're wearing"
    /// section. Off for items hovered in the equipment strip itself.</param>
    public void Show(EquipmentItem item, object requester, bool compare = true)
    {
        _owner = requester;

        string text = ItemTooltipText.Build(item);
        if (compare && TryComparisonTarget(item, out EquipmentItem? equipped))
        {
            text += "\n[color=#3a3450]──────────────[/color]\n" + ItemTooltipText.BuildComparison(item, equipped);
        }

        if (_label.Text != text)
        {
            _label.Text = text;
        }

        Visible = true;
        Place();
    }

    /// <summary>
    /// The slot this item would go into — the same "first free candidate slot,
    /// else the first one" order right-click equip uses — and what's in it.
    /// False when there's no player equipment around or the item isn't
    /// equippable (or is the very item already worn there).
    /// </summary>
    private bool TryComparisonTarget(EquipmentItem item, out EquipmentItem? equipped)
    {
        equipped = null;
        Equipment? equipment = GetTree().GetFirstNodeInGroup(Combat.CombatQuery.PlayerGroup)?.GetNodeOrNull<Equipment>("Equipment");
        IReadOnlyList<EquipmentSlot> slots = Equipment.CandidateSlots(item.Descriptor.ItemClass);
        if (equipment is null || slots.Count == 0 || slots.Any(slot => equipment.GetEquipped(slot) == item))
        {
            return false;
        }

        EquipmentSlot target = slots.FirstOrDefault(slot => equipment.GetEquipped(slot) is null, slots[0]);
        equipped = equipment.GetEquipped(target);
        return true;
    }

    /// <summary>Beside the cursor, flipped left/up and clamped so the whole
    /// tooltip always stays on screen (long ones used to run off the bottom).</summary>
    private void Place()
    {
        var panel = (Control)_label.GetParent();
        panel.ResetSize();
        Vector2 size = panel.Size;
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        Vector2 mouse = GetViewport().GetMousePosition();
        Vector2 at = mouse + CursorOffset;
        if (at.X + size.X > screen.X)
        {
            at.X = mouse.X - CursorOffset.X - size.X;
        }

        if (at.Y + size.Y > screen.Y)
        {
            at.Y = screen.Y - size.Y;
        }

        panel.Position = new Vector2(Mathf.Clamp(at.X, 0f, Mathf.Max(0f, screen.X - size.X)), Mathf.Max(0f, at.Y));
    }

    /// <summary>
    /// Hides the tooltip, but only if <paramref name="requester"/> is the source
    /// that last showed it.
    ///
    /// Three things drive this tooltip — the inventory grid, the equipment strip,
    /// and ground items — and the first two poll hover every frame. When each of
    /// them called <c>Hide()</c> unconditionally on a miss, whichever ran last in a
    /// frame won, and that was almost always a miss: the grid hid the tooltip every
    /// frame while the inventory was closed, so a ground-item hover could never
    /// survive, and with the panel open the grid and the strip undid each other.
    /// The feature was completely dead in both cases.
    ///
    /// Ownership rather than call order is what fixes it: a source can only take
    /// the tooltip away if it is the one currently holding it.
    /// </summary>
    public void Clear(object requester)
    {
        if (!ReferenceEquals(_owner, requester))
        {
            return;
        }

        _owner = null;
        Visible = false;
    }
}
