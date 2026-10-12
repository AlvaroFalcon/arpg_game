#nullable enable
using System;
using Godot;

namespace ArpgGame.Items;

/// <summary>
/// The pooled node <see cref="GroundLabelPool"/> actually creates — a thin
/// <see cref="Label"/> subclass so a ground item's label can be clicked
/// (docs/design/items.md -&gt; "Pickup is manual for items, automatic for
/// currency": "Equipment is picked up by clicking it — its ground label or its
/// sprite") and hovered (docs/design/items.md -&gt; "Item tooltips": "Hovering
/// an item — in the grid, in an equipment slot, or on the ground").
///
/// Every pooled label is one of these, including currency's — a plain
/// <see cref="CurrencyDrop"/> label simply never has <see cref="OnClicked"/>/
/// <see cref="OnHoverChanged"/> wired (<see cref="GroundLabelPool.Acquire"/>
/// leaves them null), so there is exactly one node type in the pool rather than
/// a currency/equipment split that would otherwise leak into the pool's own
/// bookkeeping.
///
/// <see cref="GroundLabelPool"/> also switches <see cref="Control.MouseFilter"/>
/// per-acquire: <see cref="Control.MouseFilterEnum.Stop"/> when a callback is
/// set, <see cref="Control.MouseFilterEnum.Ignore"/> when it isn't. An
/// always-Stop label would silently eat a click-to-move landing on a floating
/// currency label — the exact class of bug docs/design/items.md -&gt; "UI must
/// consume its own input" describes for the inventory panel, just for a
/// different widget — so a label with nothing to do gets out of the way
/// entirely instead of consuming input it has no use for.
/// </summary>
public partial class GroundLabel : Label
{
    /// <summary>Set by <see cref="GroundLabelPool.Acquire"/>, cleared by
    /// <see cref="GroundLabelPool.Release"/>. Null for every currency label.</summary>
    public Action? OnClicked;

    /// <summary>Fired true on hover start, false on hover end. Null for every
    /// currency label — no tooltip is specified for currency piles.</summary>
    public Action<bool>? OnHoverChanged;

    public override void _Ready()
    {
        MouseEntered += () => OnHoverChanged?.Invoke(true);
        MouseExited += () => OnHoverChanged?.Invoke(false);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (OnClicked is not null && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            OnClicked.Invoke();
            AcceptEvent();
        }
    }
}
