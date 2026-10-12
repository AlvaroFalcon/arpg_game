namespace ArpgGame.Items;

/// <summary>
/// The one seam docs/design/items.md's "Grid inventory" section names as
/// "the whole trick": <see cref="Inventory"/>'s placement, collision, and
/// free-space search never reason about a rectangle directly — they reason
/// about a boolean occupancy mask, and this is the single place that mask
/// gets generated from an item's <see cref="BaseItemDefinition.Width"/>/
/// <see cref="BaseItemDefinition.Height"/>.
///
/// Every item shipping today is rectangular (docs/design/items.md ->
/// "Non-rectangular items are not shipping"), so <see cref="MaskFor"/> always
/// returns a fully-true <c>width x height</c> mask. The point of factoring it
/// out anyway is that a future non-rectangular item changes only this method
/// (and whatever authors the per-cell data it would read instead of
/// width/height) — nothing in <see cref="Inventory"/> would need to change,
/// because <see cref="Inventory"/> only ever asks "which cells does this
/// footprint occupy," never "what shape is this rectangle."
/// </summary>
public static class ItemShape
{
    /// <summary>A <paramref name="width"/> x <paramref name="height"/> mask,
    /// indexed <c>[x, y]</c>, with every cell occupied.</summary>
    public static bool[,] MaskFor(int width, int height)
    {
        var mask = new bool[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                mask[x, y] = true;
            }
        }

        return mask;
    }
}
