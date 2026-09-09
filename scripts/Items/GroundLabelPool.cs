#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace PathToExile.Items;

/// <summary>
/// docs/design/items.md -> "Label rendering": "Dozens of labels appear at once
/// when a pack dies. Required from day one: label pooling, world→screen
/// projection, and vertical stacking to avoid overlap." This owns a pool of plain
/// <see cref="Label"/> controls under itself (a <see cref="CanvasLayer"/>, so they
/// render in screen space regardless of camera zoom) and projects each one from
/// its owning item's live world position every frame — labels are never parented
/// under the world item itself, which is what makes stacking possible: several
/// items near each other in world space can be pushed apart in screen space
/// without touching their transforms.
///
/// Style always comes from whatever <see cref="DisplayStyle"/> the caller hands to
/// <see cref="Acquire"/> (produced by <see cref="FilterChain.Evaluate"/>) — this
/// class has no rarity/currency knowledge of its own and must never gain any; see
/// docs/design/items.md -> "Rarity colour is filter output, not label state".
/// </summary>
[GlobalClass]
public partial class GroundLabelPool : CanvasLayer
{
    /// <summary>
    /// The single visibility gate. docs/design/items.md marks hold-key-to-show as
    /// [PROPOSED], not decided — milestone 1 ships always-on. This field is
    /// deliberately the *only* place that decision lives, so switching to a
    /// hold-to-show input later is a one-line change here, not a search across
    /// every label producer.
    /// </summary>
    [Export]
    public bool LabelsVisible = true;

    [Export]
    public int InitialPoolSize = 24;

    /// <summary>Vertical pixel gap between labels stacked in the same horizontal
    /// bucket — see <see cref="StackBucketWidthPx"/>.</summary>
    [Export]
    public float StackSpacingPx = 16f;

    /// <summary>Screen-space bucket width used to decide "these labels are close
    /// enough to stack." Wide enough that two items a few pixels apart still
    /// stack instead of overlapping; narrow enough that unrelated items across the
    /// screen don't stack with each other.</summary>
    [Export]
    public float StackBucketWidthPx = 48f;

    /// <summary>World-space offset applied before projecting to screen, so a
    /// label floats above its item instead of centred on it.</summary>
    [Export]
    public Vector2 WorldOffset = new(0f, -30f);

    private sealed class Slot
    {
        public GroundLabel Label = null!;
        public bool InUse;
        public bool Hidden;
        public Func<Vector2>? GetWorldPosition;
    }

    private readonly List<Slot> _slots = new();

    public override void _Ready()
    {
        for (int i = 0; i < InitialPoolSize; i++)
        {
            _slots.Add(CreateSlot());
        }
    }

    private Slot CreateSlot()
    {
        var label = new GroundLabel { Visible = false };
        AddChild(label);
        return new Slot { Label = label };
    }

    /// <summary>
    /// Claims a pooled label for one dropped item. <paramref name="worldPosition"/>
    /// is a callback rather than a one-shot <see cref="Vector2"/> because the item
    /// persists and may move — projection happens every frame in
    /// <see cref="_Process"/>, not once at acquire time. Returns an opaque handle
    /// for <see cref="Release"/>.
    ///
    /// <paramref name="onClicked"/>/<paramref name="onHoverChanged"/> are the
    /// label half of manual pickup and tooltips (docs/design/items.md -&gt;
    /// "Pickup is manual..." and "Item tooltips") — null for every
    /// <see cref="CurrencyDrop"/> label, which has neither. When either is set,
    /// the label switches to <see cref="Control.MouseFilterEnum.Stop"/> so it can
    /// actually receive the click/hover; when both are null it stays
    /// <see cref="Control.MouseFilterEnum.Ignore"/> so an inert currency label
    /// never eats a click-to-move landing on it — see <see cref="GroundLabel"/>'s
    /// class doc.
    /// </summary>
    public int Acquire(
        Func<Vector2> worldPosition,
        string text,
        DisplayStyle style,
        Action? onClicked = null,
        Action<bool>? onHoverChanged = null)
    {
        Slot? slot = _slots.FirstOrDefault(s => !s.InUse);
        if (slot is null)
        {
            slot = CreateSlot();
            _slots.Add(slot);
        }

        slot.InUse = true;
        slot.GetWorldPosition = worldPosition;
        slot.Hidden = style.Hidden == true;
        slot.Label.Text = text;
        slot.Label.AddThemeColorOverride("font_color", style.TextColor ?? Colors.White);
        if (style.FontSize is { } fontSize)
        {
            slot.Label.AddThemeFontSizeOverride("font_size", fontSize);
        }

        slot.Label.OnClicked = onClicked;
        slot.Label.OnHoverChanged = onHoverChanged;
        slot.Label.MouseFilter = onClicked is null && onHoverChanged is null
            ? Control.MouseFilterEnum.Ignore
            : Control.MouseFilterEnum.Stop;

        return _slots.IndexOf(slot);
    }

    /// <summary>Returns a label to the pool. Idempotent — safe to call more than
    /// once for the same handle, since callers release both on pickup and again
    /// from <c>_ExitTree</c> as a safety net.</summary>
    public void Release(int handle)
    {
        if (handle < 0 || handle >= _slots.Count)
        {
            return;
        }

        Slot slot = _slots[handle];
        slot.InUse = false;
        slot.GetWorldPosition = null;
        slot.Label.Visible = false;

        // Clear callbacks so a recycled label (or a stray late signal from this
        // one) never fires a closure bound to the item that has already
        // released it — see GroundLabel's class doc.
        slot.Label.OnClicked = null;
        slot.Label.OnHoverChanged = null;
        slot.Label.MouseFilter = Control.MouseFilterEnum.Ignore;
    }

    /// <summary>
    /// Z toggles every ground label, the genre-standard binding. This replaces
    /// docs/design/items.md's [PROPOSED] hold-a-key-to-show: a toggle beats a held
    /// key once a screen has enough loot on it to be worth reading, because reading
    /// it should not cost you a finger.
    ///
    /// Visibility only. A hidden label still belongs to a live item that is still
    /// there and still clickable — the same rule filters follow.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("toggle_ground_labels"))
        {
            LabelsVisible = !LabelsVisible;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        List<Slot> active = _slots.Where(s => s.InUse && s.GetWorldPosition is not null).ToList();
        if (active.Count == 0)
        {
            return;
        }

        Transform2D canvasTransform = GetViewport().CanvasTransform;
        var anchors = new Dictionary<Slot, Vector2>();
        foreach (Slot slot in active)
        {
            anchors[slot] = canvasTransform * (slot.GetWorldPosition!() + WorldOffset);
        }

        // Vertical stacking: labels whose projected X lands in the same bucket
        // are stacked upward by StackSpacingPx instead of overlapping.
        foreach (IGrouping<int, Slot> bucket in active.GroupBy(s => Mathf.RoundToInt(anchors[s].X / StackBucketWidthPx)))
        {
            List<Slot> ordered = bucket.OrderBy(s => anchors[s].Y).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                Slot slot = ordered[i];

                // Centre the label horizontally on the item rather than starting
                // it at the item's X: a left-aligned label begins inside the
                // drop's own sprite, which draws over its leading characters — a
                // "115 Gold" pile reads as "5 Gold". GetMinimumSize is the
                // label's laid-out text width and is valid as soon as Text is set,
                // unlike Size, which is a frame behind on the first draw.
                float halfWidth = slot.Label.GetMinimumSize().X * 0.5f;
                slot.Label.Position =
                    anchors[slot] - new Vector2(halfWidth, i * StackSpacingPx);
                slot.Label.Visible = LabelsVisible && !slot.Hidden;
            }
        }
    }
}
