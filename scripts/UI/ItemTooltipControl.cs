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
    private RichTextLabel _compareLabel = null!;
    private HBoxContainer _row = null!;
    private PanelContainer _panel = null!;
    private PanelContainer _comparePanel = null!;

    /// <summary>Whoever most recently called <see cref="Show"/>. See <see cref="Clear"/>.</summary>
    private object? _owner;

    /// <summary>Extra lines under the tooltip while a station is open — the
    /// merchant's price, what disenchanting yields, what an armed orb does.</summary>
    public static System.Func<EquipmentItem, string?>? Footer;

    /// <summary>Alt shows modifier tiers (docs/design/crafting.md -> "Tooltip").</summary>
    public static bool DetailHeld => ForceDetail || Input.IsKeyPressed(Key.Alt);

    /// <summary>Captures: render as if Alt were held.</summary>
    public static bool ForceDetail;

    private EquipmentItem? _item;
    private bool _compare;
    private bool _detailed;

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
        ProcessMode = ProcessModeEnum.Always;

        // The item on the left, the comparison with what you wear beside it
        // (Path of Exile style), so a long diff never makes one tower of text.
        _row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _row.AddThemeConstantOverride("separation", 6);
        AddChild(_row);
        (_panel, _label) = Box(270f);
        (_comparePanel, _compareLabel) = Box(230f);
        _row.AddChild(_panel);
        _row.AddChild(_comparePanel);

        Visible = false;
    }

    private static (PanelContainer, RichTextLabel) Box(float width)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(width, 0f),
        };
        panel.AddChild(label);
        return (panel, label);
    }

    private static StyleBoxFlat Frame(Color border)
    {
        var box = new StyleBoxFlat { BgColor = new Color(0.045f, 0.035f, 0.07f, 0.97f), BorderColor = border };
        box.SetBorderWidthAll(1);
        box.BorderWidthTop = 2;
        box.SetCornerRadiusAll(3);
        box.SetContentMarginAll(10f);
        return box;
    }

    /// <summary>Shows (or refreshes) the tooltip for <paramref name="item"/> at
    /// the current mouse position. Safe to call every frame from a hover-polling
    /// <c>_Process</c> — it only ever overwrites the same label/position, never
    /// accumulates state.</summary>
    /// <param name="compare">Show the "compared to what you're wearing" panel.
    /// Off for items hovered in the equipment strip itself.</param>
    public void Show(EquipmentItem item, object requester, bool compare = true)
    {
        _owner = requester;
        _item = item;
        _compare = compare;
        _detailed = DetailHeld;
        Render();
        Visible = true;
        Place();
    }

    private void Render()
    {
        if (_item is null || !IsInstanceValid(_item))
        {
            return;
        }

        // Wider while Alt is held, so the tier notes sit beside their lines.
        _label.CustomMinimumSize = new Vector2(_detailed ? 400f : 270f, 0f);
        string text = ItemTooltipText.Build(_item, _detailed);
        if (Footer?.Invoke(_item) is { Length: > 0 } footer)
        {
            text += "\n" + ItemTooltipText.Separator + "\n" + footer;
        }

        if (_label.Text != text)
        {
            _label.Text = text;
        }

        Color border = (FilterChain.Default.Evaluate(_item.Descriptor).TextColor ?? Colors.White).Darkened(0.25f);
        _panel.AddThemeStyleboxOverride("panel", Frame(border));

        EquipmentItem? equipped = null;
        bool comparing = _compare && TryComparisonTarget(_item, out equipped);
        _comparePanel.Visible = comparing;
        if (comparing)
        {
            string diff = ItemTooltipText.BuildComparison(_item, equipped);
            if (_compareLabel.Text != diff)
            {
                _compareLabel.Text = diff;
                _comparePanel.AddThemeStyleboxOverride("panel", Frame(new Color(0.35f, 0.3f, 0.48f)));
            }
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        // Ground items only call Show when hovered, so pressing or releasing Alt
        // while the tooltip is up re-renders it here.
        if (DetailHeld != _detailed)
        {
            _detailed = !_detailed;
            Render();
        }

        // Rich text measures itself a frame late: keep the placement current.
        Place();
    }

    private bool TryComparisonTarget(EquipmentItem item, out EquipmentItem? equipped)
    {
        equipped = null;
        Equipment? equipment = Combat.CombatQuery.LocalPlayer(GetTree())?.GetNodeOrNull<Equipment>("Equipment");
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
        _row.ResetSize();
        Vector2 size = _row.Size;
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

        _row.Position = new Vector2(Mathf.Clamp(at.X, 0f, Mathf.Max(0f, screen.X - size.X)), Mathf.Max(0f, at.Y));
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
        _item = null;
        Visible = false;
    }
}
