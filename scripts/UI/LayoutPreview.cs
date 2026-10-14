#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.World;

namespace ArpgGame.UI;

/// <summary>
/// A top-down picture of a rasterised layout: rock, floor, the theme's liquid,
/// pillars, the boss arena and its gate, the entrance (green) and the boss (red);
/// optionally the pack spots by rarity. Used by the capture tool's layout sheets (docs/design/endgame.md -> "Caverns").
/// </summary>
public static class LayoutPreview
{
    public static Color LiquidColor(string themeId) => themeId switch
    {
        "drowned" => new Color(0.15f, 0.45f, 0.75f),
        "crypt" => new Color(0.35f, 0.75f, 0.2f),
        _ => new Color(0.95f, 0.4f, 0.1f),
    };

    public static Image Draw(RasterLayout m, string themeId, int px, bool packs)
    {
        Image image = Image.Create(m.Width * px, m.Height * px, false, Image.Format.Rgba8);
        image.Fill(new Color(0.06f, 0.05f, 0.08f));
        Color liquid = LiquidColor(themeId);
        var gate = new HashSet<Vector2I>(m.Gate);
        for (int x = 0; x < m.Width; x++)
        {
            for (int y = 0; y < m.Height; y++)
            {
                var c = new Vector2I(x, y);
                Color? col = m.Cells[x, y] switch
                {
                    CellKind.Floor => m.BossRegion.Contains(c) ? new Color(0.45f, 0.3f, 0.35f) : new Color(0.55f, 0.52f, 0.48f),
                    CellKind.Hazard => liquid,
                    CellKind.Pillar => new Color(0.25f, 0.22f, 0.2f),
                    _ => null,
                };
                if (gate.Contains(c))
                {
                    col = new Color(1f, 0.85f, 0.2f);
                }

                if (col is { } fill)
                {
                    image.FillRect(new Rect2I(x * px, y * px, px, px), fill);
                }
            }
        }

        if (packs)
        {
            foreach (PackSpot p in m.Packs)
            {
                Color pc = p.Kind switch { 'm' => new Color(0.35f, 0.5f, 1f), 'r' => new Color(1f, 0.9f, 0.2f), 'u' => new Color(1f, 0.45f, 0.1f), _ => Colors.White };
                Mark(image, p.Cell, px, 1, pc);
            }
        }

        Mark(image, m.Entrance, px, 2, new Color(0.2f, 1f, 0.4f));
        Mark(image, m.BossCell, px, 2, new Color(1f, 0.15f, 0.15f));
        return image;
    }

    private static void Mark(Image image, Vector2I cell, int px, int grow, Color color)
    {
        var r = new Rect2I(cell.X * px - grow * px / 2, cell.Y * px - grow * px / 2, px * (1 + grow), px * (1 + grow));
        r = r.Intersection(new Rect2I(0, 0, image.GetWidth(), image.GetHeight()));
        if (r.Size.X > 0 && r.Size.Y > 0)
        {
            image.FillRect(r, color);
        }
    }
}
