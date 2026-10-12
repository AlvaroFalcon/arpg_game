#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace PathToExile.Items;

/// <summary>
/// The grid inventory component — milestone 2c (docs/design/items.md ->
/// "Grid inventory" and "Inventory interaction"). Sibling to
/// <see cref="Stats.ActorStats"/>/<see cref="Equipment"/>, found the same
/// convention-over-configuration way they are: a child node named "Inventory"
/// (see <see cref="Player.Inventory"/>). Unlike <see cref="Equipment"/> it has
/// no <c>_Ready</c>-time dependency on a sibling — every method that needs an
/// <see cref="Items.Equipment"/> takes one as a parameter instead, the same
/// shape <see cref="Wallet"/> uses for its own zero-dependency simplicity.
///
/// <b>12x5</b> (<see cref="Width"/> x <see cref="Height"/>) — docs/design/items.md
/// -> "Inventory interaction": "Matches PoE's dimensions... big enough not to
/// nag between packs, small enough that clearing it is a real decision."
///
/// <b>Every placement/collision/free-space algorithm below reasons about a
/// boolean occupancy mask (<see cref="BuildOccupancyMask"/>), never about
/// rectangle arithmetic</b> — per the design doc, this is "the whole trick":
/// non-rectangular items become pure data later (a different
/// <see cref="ItemShape.MaskFor"/>) with no rewrite here. The mask is rebuilt
/// from the live <see cref="_placements"/> dictionary on every query rather
/// than maintained incrementally — at 60 cells this costs nothing, and it is
/// also what makes "removing an item frees exactly its own cells and no
/// others" true by construction: there is no separate mask to fall out of
/// sync with the placements it is supposed to reflect.
/// </summary>
[GlobalClass]
public partial class Inventory : Node
{
    public const int Width = 12;
    public const int Height = 5;

    /// <summary>This grid's size. The player's inventory keeps the 12x5
    /// default (<see cref="Width"/>/<see cref="Height"/>); stash tabs are larger
    /// (docs/design/items.md -> "Stash").</summary>
    [Export]
    public int Columns = Width;

    [Export]
    public int Rows = Height;

    private readonly Dictionary<EquipmentItem, Vector2I> _placements = new();

    /// <summary>Read-only view of what's placed where — item -&gt; top-left
    /// cell. Exposed for UI drawing/hit-testing
    /// (<see cref="UI.InventoryGridControl"/>) and tests.</summary>
    public IReadOnlyDictionary<EquipmentItem, Vector2I> Placements => _placements;

    // ------------------------------------------------------------------
    // The occupancy mask — the seam docs/design/items.md calls "the whole
    // trick".
    // ------------------------------------------------------------------

    /// <summary>
    /// Grid-wide occupancy, stamped from every current placement's own mask
    /// (<see cref="ItemShape.MaskFor"/>) at its origin. <paramref name="ignore"/>
    /// lets a caller ask "what would the grid look like without this one
    /// item" without a temporary remove/restore — used throughout this class
    /// so that re-validating a move (or dropping an item back over its own
    /// current cells) never sees the item as an obstacle to itself.
    /// </summary>
    public bool[,] BuildOccupancyMask(EquipmentItem? ignore = null)
    {
        var mask = new bool[Columns, Rows];
        foreach ((EquipmentItem item, Vector2I origin) in _placements)
        {
            if (ReferenceEquals(item, ignore) || item.Base is null)
            {
                continue;
            }

            Stamp(mask, item.Base.Width, item.Base.Height, origin);
        }

        return mask;
    }

    private static void Stamp(bool[,] mask, int width, int height, Vector2I origin)
    {
        bool[,] itemMask = ItemShape.MaskFor(width, height);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (itemMask[x, y])
                {
                    mask[origin.X + x, origin.Y + y] = true;
                }
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="item"/>'s footprint fits at <paramref name="origin"/>:
    /// in bounds, and every cell it would occupy is free in the mask built
    /// with <paramref name="ignore"/> excluded. Defaults to ignoring
    /// <paramref name="item"/> itself — the common case (re-placing an item
    /// that may already be on the grid) — since an item's own current cells
    /// should never count as an obstacle to placing that same item somewhere
    /// else, including back where it started.
    /// </summary>
    public bool CanPlaceAt(EquipmentItem item, Vector2I origin, EquipmentItem? ignore = null)
    {
        if (item.Base is null)
        {
            return false;
        }

        int w = item.Base.Width;
        int h = item.Base.Height;
        if (origin.X < 0 || origin.Y < 0 || origin.X + w > Columns || origin.Y + h > Rows)
        {
            return false;
        }

        bool[,] mask = BuildOccupancyMask(ignore ?? item);
        bool[,] itemMask = ItemShape.MaskFor(w, h);
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (itemMask[x, y] && mask[origin.X + x, origin.Y + y])
                {
                    return false;
                }
            }
        }

        return true;
    }

    // ------------------------------------------------------------------
    // Placement.
    // ------------------------------------------------------------------

    /// <summary>
    /// Places <paramref name="item"/> at an explicit <paramref name="origin"/> —
    /// the drag-and-drop entry point, where the player chose the exact cell.
    /// Returns false and changes nothing if it doesn't fit.
    /// </summary>
    public bool TryPlaceAt(EquipmentItem item, Vector2I origin)
    {
        if (!CanPlaceAt(item, origin))
        {
            return false;
        }

        _placements[item] = origin;
        return true;
    }

    /// <summary>
    /// First free space in reading order — left to right, then top to bottom
    /// — per docs/design/items.md -&gt; "Inventory interaction". Scans
    /// candidate origins in that order and returns the first one
    /// <see cref="CanPlaceAt"/> accepts. Read-only: does not place anything.
    /// </summary>
    public bool TryFindFirstFit(EquipmentItem item, out Vector2I origin)
    {
        if (item.Base is null)
        {
            origin = default;
            return false;
        }

        int w = item.Base.Width;
        int h = item.Base.Height;
        for (int y = 0; y <= Rows - h; y++)
        {
            for (int x = 0; x <= Columns - w; x++)
            {
                var candidate = new Vector2I(x, y);
                if (CanPlaceAt(item, candidate))
                {
                    origin = candidate;
                    return true;
                }
            }
        }

        origin = default;
        return false;
    }

    /// <summary>
    /// Pickup's entry point (<see cref="EquipmentItem.OnPickup"/>): places
    /// <paramref name="item"/> at the first free space in reading order, or
    /// returns false with the grid left exactly as it was if nothing fits.
    /// docs/design/items.md -&gt; "Inventory interaction": "the item stays on
    /// the ground rather than being destroyed or silently swallowed" — that
    /// contract is the caller's job; this method only reports success.
    /// </summary>
    public bool TryAdd(EquipmentItem item) => TryFindFirstFit(item, out Vector2I origin) && TryPlaceAt(item, origin);

    /// <summary>Removes <paramref name="item"/> if present. Returns false
    /// (no-op) if it wasn't in the grid.</summary>
    public bool Remove(EquipmentItem item) => _placements.Remove(item);

    /// <summary>The item occupying <paramref name="cell"/>, or null. Used by
    /// the grid UI to hit-test a click/drag against multi-cell items without
    /// needing a Control per cell.</summary>
    public EquipmentItem? GetItemAt(Vector2I cell)
    {
        foreach ((EquipmentItem item, Vector2I origin) in _placements)
        {
            if (item.Base is null)
            {
                continue;
            }

            if (cell.X >= origin.X && cell.X < origin.X + item.Base.Width
                && cell.Y >= origin.Y && cell.Y < origin.Y + item.Base.Height)
            {
                return item;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Equipment strip interaction — docs/design/items.md -> "The equipment
    // strip". These orchestrate Inventory + Equipment together but never
    // reimplement Equipment's own slot validation or modifier bookkeeping —
    // both go through Equipment.TryEquip/Unequip, per the milestone brief.
    // ------------------------------------------------------------------

    /// <summary>
    /// Right-click shortcut (docs/design/items.md -&gt; "The equipment
    /// strip"): equips <paramref name="item"/> into the first valid free
    /// slot for its <see cref="ItemClass"/>, or swaps with the occupied one
    /// if none is free. A displaced item always lands back in the exact grid
    /// space <paramref name="item"/> just vacated. That is "always free and
    /// always large enough" per the design doc, and true here because every
    /// item that can occupy a given slot today comes from the same one
    /// authored base per slot (docs/design/items.md -&gt; "The first base
    /// items"), so displaced and incoming always share a footprint. See
    /// <see cref="EquipInto"/>'s fallback chain for what happens if that
    /// invariant is ever violated by a future multi-base slot.
    /// </summary>
    public bool TryRightClickEquip(EquipmentItem item, Equipment equipment)
    {
        if (item.Base is null || !_placements.TryGetValue(item, out Vector2I origin))
        {
            return false;
        }

        IReadOnlyList<EquipmentSlot> candidates = Equipment.CandidateSlots(item.Base.ItemClass);
        if (candidates.Count == 0)
        {
            return false;
        }

        EquipmentSlot slot = candidates.FirstOrDefault(s => equipment.GetEquipped(s) is null, candidates[0]);
        return EquipInto(item, slot, origin, equipment);
    }

    /// <summary>
    /// Drag-and-drop equip: the same swap-and-vacate contract as
    /// <see cref="TryRightClickEquip"/>, but into a caller-chosen
    /// <paramref name="slot"/> — the specific equipment-strip cell the
    /// player dropped the item onto, rather than "first free candidate."
    /// </summary>
    public bool TryEquipFromGrid(EquipmentItem item, EquipmentSlot slot, Equipment equipment)
    {
        if (item.Base is null
            || !Equipment.IsValidSlot(item.Base.ItemClass, slot)
            || !_placements.TryGetValue(item, out Vector2I origin))
        {
            return false;
        }

        return EquipInto(item, slot, origin, equipment);
    }

    private bool EquipInto(EquipmentItem item, EquipmentSlot slot, Vector2I vacatedOrigin, Equipment equipment)
    {
        Remove(item);
        if (!equipment.TryEquip(slot, item, out EquipmentItem? displaced))
        {
            // Unreachable given the validation both callers already did, but
            // never lose the item over it — put it back exactly where it was.
            TryPlaceAt(item, vacatedOrigin);
            return false;
        }

        if (displaced is not null && !TryPlaceAt(displaced, vacatedOrigin) && !TryAdd(displaced))
        {
            // Last-resort safety net for an invariant violation that cannot
            // happen with today's one-base-per-slot data (see class doc):
            // if the displaced item somehow doesn't fit the vacated space
            // *and* nowhere else in the grid has room, put it back in the
            // slot rather than let it vanish. This "fails" the swap instead
            // of losing an item — an acceptable trade for a case that is
            // currently impossible to reach.
            equipment.TryEquip(slot, displaced, out _);
        }

        return true;
    }

    /// <summary>
    /// Unequip-to-grid shortcut: moves whatever is in <paramref name="slot"/>
    /// into the first free grid space. <b>Grid space is checked before
    /// anything is unequipped</b> — if nothing fits, the item stays exactly
    /// where it was (still equipped, stats untouched) rather than coming off
    /// with nowhere to land. Not wired to a specific input in this milestone
    /// (drag-to-grid already covers "take it off"), but it is the safe,
    /// testable core of that interaction and a natural right-click-in-the-
    /// strip shortcut, symmetric with <see cref="TryRightClickEquip"/>.
    /// </summary>
    public bool TryUnequipToGrid(Equipment equipment, EquipmentSlot slot)
    {
        EquipmentItem? item = equipment.GetEquipped(slot);
        if (item is null || !TryFindFirstFit(item, out Vector2I origin))
        {
            return false;
        }

        equipment.Unequip(slot);
        TryPlaceAt(item, origin);
        return true;
    }
}
