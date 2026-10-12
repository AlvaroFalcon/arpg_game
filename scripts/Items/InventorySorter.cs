#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArpgGame.Items;

/// <summary>
/// Repacks an <see cref="Inventory"/> into a readable order — docs/design/items.md
/// -> "Sorting".
///
/// Kept out of <see cref="Inventory"/> itself: the inventory owns "what is where
/// and may this go here", which the UI, pickup and equipping all depend on, while
/// this is one policy about presentation layered on top. A second ordering (by
/// value, say) should be a sibling of this, not another branch inside the grid.
/// </summary>
public static class InventorySorter
{
    /// <summary>
    /// The sort key. Class first, because the question asked of a sorted bag is
    /// "where are my rings", not "what is my densest packing". Height descending
    /// within a class is a packing concern rather than a readability one: placing
    /// tall items before short ones stops a row of 1x1s stranding a column a 2x3
    /// then cannot use.
    /// </summary>
    private static IEnumerable<EquipmentItem> InOrder(IEnumerable<EquipmentItem> items) =>
        items
            .OrderBy(i => (int)i.Descriptor.ItemClass)
            .ThenByDescending(i => (int)i.Rarity)
            .ThenByDescending(i => i.Descriptor.ItemLevel)
            .ThenByDescending(i => i.Base?.Height ?? 1)
            .ThenByDescending(i => i.Base?.Width ?? 1)
            .ThenBy(i => i.Descriptor.DisplayName);

    /// <summary>
    /// Repacks in place. Returns false and leaves the inventory **exactly** as it
    /// was if any item fails to place.
    ///
    /// That all-or-nothing guarantee is the whole reason this is not a loop over
    /// Remove/TryAdd. The items being placed are precisely the items that already
    /// fitted, so a failure means the new order fragmented worse than the old one —
    /// and a half-sorted bag that has silently dropped an item is far worse than an
    /// unsorted one. The scratch layout is built first and only committed once
    /// every item has landed.
    /// </summary>
    public static bool TrySort(Inventory inventory)
    {
        List<EquipmentItem> ordered = InOrder(inventory.Placements.Keys).ToList();
        if (ordered.Count == 0)
        {
            return true;
        }

        Dictionary<EquipmentItem, Vector2I> original = new(inventory.Placements);
        var packed = new Dictionary<EquipmentItem, Vector2I>();
        var occupied = new bool[inventory.Columns, inventory.Rows];

        foreach (EquipmentItem item in ordered)
        {
            int width = item.Base?.Width ?? 1;
            int height = item.Base?.Height ?? 1;

            if (!TryFit(occupied, width, height, out Vector2I origin))
            {
                return false;
            }

            Stamp(occupied, width, height, origin);
            packed[item] = origin;
        }

        foreach (EquipmentItem item in original.Keys)
        {
            inventory.Remove(item);
        }

        foreach ((EquipmentItem item, Vector2I origin) in packed)
        {
            if (inventory.TryPlaceAt(item, origin))
            {
                continue;
            }

            // Unreachable if the scratch pack agrees with the grid's own placement
            // rules, but restoring beats a partially-applied sort if they ever
            // diverge.
            foreach (EquipmentItem placed in packed.Keys)
            {
                inventory.Remove(placed);
            }

            foreach ((EquipmentItem restored, Vector2I where) in original)
            {
                inventory.TryPlaceAt(restored, where);
            }

            return false;
        }

        return true;
    }

    /// <summary>First fit in reading order against the scratch mask.</summary>
    private static bool TryFit(bool[,] occupied, int width, int height, out Vector2I origin)
    {
        for (int y = 0; y + height <= occupied.GetLength(1); y++)
        {
            for (int x = 0; x + width <= occupied.GetLength(0); x++)
            {
                if (IsFree(occupied, width, height, x, y))
                {
                    origin = new Vector2I(x, y);
                    return true;
                }
            }
        }

        origin = default;
        return false;
    }

    private static bool IsFree(bool[,] occupied, int width, int height, int x, int y)
    {
        bool[,] shape = ItemShape.MaskFor(width, height);
        for (int dy = 0; dy < height; dy++)
        {
            for (int dx = 0; dx < width; dx++)
            {
                if (shape[dx, dy] && occupied[x + dx, y + dy])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void Stamp(bool[,] occupied, int width, int height, Vector2I origin)
    {
        bool[,] shape = ItemShape.MaskFor(width, height);
        for (int dy = 0; dy < height; dy++)
        {
            for (int dx = 0; dx < width; dx++)
            {
                if (shape[dx, dy])
                {
                    occupied[origin.X + dx, origin.Y + dy] = true;
                }
            }
        }
    }
}
