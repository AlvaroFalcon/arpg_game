#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using ArpgGame.Combat;

namespace ArpgGame.World;

public enum CellKind : byte
{
    Void,
    Floor,
    Hazard,
    Pillar,
}

/// <summary>A pack spot produced by rasterising a layout.</summary>
public readonly record struct PackSpot(char Kind, Vector2I Cell, string Room);

/// <summary>The rasterised result of a <see cref="LayoutDef"/>.</summary>
public sealed class RasterLayout
{
    public const int CellPx = 64;

    public CellKind[,] Cells = null!;
    public int Width;
    public int Height;
    public Vector2I Entrance;
    public Vector2I BossCell;
    public HashSet<Vector2I> BossRegion = new();
    public List<Vector2I> Gate = new();
    public List<PackSpot> Packs = new();
    public List<string> BuiltCorridors = new();

    public bool Walkable(Vector2I c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height && Cells[c.X, c.Y] == CellKind.Floor;

    public static Vector2 World(Vector2I cell) => (new Vector2(cell.X, cell.Y) + Vector2.One * 0.5f) * CellPx;

    public bool[,] WalkableGrid()
    {
        var grid = new bool[Width, Height];
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                grid[x, y] = Cells[x, y] == CellKind.Floor;
            }
        }

        return grid;
    }

    /// <summary>ASCII dump for debugging and tests ('#' wall, '.' floor, '~' hazard, 'o' pillar, 'G' gate).</summary>
    public string Dump()
    {
        var gate = new HashSet<Vector2I>(Gate);
        var sb = new StringBuilder();
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var c = new Vector2I(x, y);
                sb.Append(c == Entrance ? 'E' : c == BossCell ? 'B' : gate.Contains(c) ? 'G' : Cells[x, y] switch
                {
                    CellKind.Floor => '.',
                    CellKind.Hazard => '~',
                    CellKind.Pillar => 'o',
                    _ => '#',
                });
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }
}

/// <summary>
/// Rasterises a <see cref="LayoutDef"/> and builds it into the scene —
/// docs/design/endgame.md -> "Layout". Rasterising is pure (testable without a
/// scene); <see cref="BuildScene"/> turns the grid into a TileMap with the
/// theme's EasyPixel tileset, wall collision, hazard collision on its own
/// layer, and the boss gate (disabled until the fight starts).
/// </summary>
public static class LayoutBuilder
{
    private const int Margin = 3;

    public static RasterLayout Rasterize(LayoutDef def, RandomNumberGenerator rng)
    {
        if (def.Cave is { } cave)
        {
            return CaveGenerator.Generate(cave, rng);
        }

        int maxX = def.Rooms.Max(r => r.X + r.W) + Margin * 2;
        int maxY = def.Rooms.Max(r => r.Y + r.H) + Margin * 2;
        var layout = new RasterLayout { Width = maxX, Height = maxY, Cells = new CellKind[maxX, maxY] };
        var rooms = def.Rooms.ToDictionary(r => r.Id);
        var offset = new Vector2I(Margin, Margin);

        bool InRoom(RoomDef r, Vector2I c)
        {
            Vector2I local = c - offset - new Vector2I(r.X, r.Y);
            if (local.X < 0 || local.Y < 0 || local.X >= r.W || local.Y >= r.H)
            {
                return false;
            }

            if (r.Shape == RoomShape.Rect)
            {
                return true;
            }

            var centre = new Vector2((r.W - 1) / 2f, (r.H - 1) / 2f);
            return new Vector2(local.X, local.Y).DistanceTo(centre) <= Mathf.Min(r.W, r.H) / 2f;
        }

        foreach (RoomDef r in def.Rooms)
        {
            for (int x = r.X; x < r.X + r.W; x++)
            {
                for (int y = r.Y; y < r.Y + r.H; y++)
                {
                    var c = new Vector2I(x, y) + offset;
                    if (InRoom(r, c))
                    {
                        layout.Cells[c.X, c.Y] = CellKind.Floor;
                    }
                }
            }
        }

        // Corridors: alternatives in a group — pick one per group.
        var chosen = new List<CorridorDef>();
        foreach (IGrouping<string, CorridorDef> group in def.Corridors.GroupBy(c => c.Group))
        {
            if (group.Key == "")
            {
                chosen.AddRange(group);
            }
            else
            {
                List<CorridorDef> options = group.ToList();
                chosen.Add(options[rng.RandiRange(0, options.Count - 1)]);
            }
        }

        foreach (CorridorDef c in chosen)
        {
            Vector2I a = rooms[c.From].Center + offset;
            Vector2I b = rooms[c.To].Center + offset;
            Vector2I corner = c.VerticalFirst ? new Vector2I(a.X, b.Y) : new Vector2I(b.X, a.Y);
            Carve(layout, a, corner, c.Width);
            Carve(layout, corner, b, c.Width);
            layout.BuiltCorridors.Add($"{c.From}->{c.To}");
            foreach (char p in c.Packs)
            {
                Vector2I mid = (a + b) / 2;
                Vector2I spot = layout.Walkable(mid) ? mid : layout.Walkable(corner) ? corner : a;
                layout.Packs.Add(new PackSpot(p, spot, $"{c.From}->{c.To}"));
            }
        }

        RoomDef boss = rooms[def.BossRoom];
        RoomDef entrance = rooms[def.Entrance];

        // Pillars and pools, never on the room's centre line so the room stays traversable.
        foreach (RoomDef r in def.Rooms)
        {
            if (r.Id == def.BossRoom || r.Id == def.Entrance)
            {
                continue;
            }

            for (int i = 0; i < r.Pillars; i++)
            {
                Vector2I c = new Vector2I(rng.RandiRange(r.X + 2, r.X + r.W - 3), rng.RandiRange(r.Y + 2, r.Y + r.H - 3)) + offset;
                if (c.X != r.Center.X + offset.X && c.Y != r.Center.Y + offset.Y && layout.Walkable(c))
                {
                    layout.Cells[c.X, c.Y] = CellKind.Pillar;
                }
            }

            for (int i = 0; i < r.Pools; i++)
            {
                int pw = rng.RandiRange(2, Mathf.Max(2, r.W / 3));
                int ph = rng.RandiRange(2, Mathf.Max(2, r.H / 3));
                int px = rng.RandiRange(r.X + 1, r.X + r.W - pw - 1);
                int py = rng.RandiRange(r.Y + 1, r.Y + r.H - ph - 1);
                for (int x = px; x < px + pw; x++)
                {
                    for (int y = py; y < py + ph; y++)
                    {
                        var c = new Vector2I(x, y) + offset;
                        bool onCross = x == r.Center.X || y == r.Center.Y;
                        if (!onCross && layout.Walkable(c))
                        {
                            layout.Cells[c.X, c.Y] = CellKind.Hazard;
                        }
                    }
                }
            }
        }

        layout.Entrance = entrance.Center + offset;
        layout.BossCell = boss.Center + offset;

        // Boss region = the boss room's cells; the gate = floor just outside it.
        for (int x = 0; x < layout.Width; x++)
        {
            for (int y = 0; y < layout.Height; y++)
            {
                var c = new Vector2I(x, y);
                if (InRoom(boss, c) && layout.Walkable(c))
                {
                    layout.BossRegion.Add(c);
                }
            }
        }

        var gate = new HashSet<Vector2I>();
        foreach (Vector2I c in layout.BossRegion)
        {
            foreach (Vector2I n in new[] { c + Vector2I.Right, c + Vector2I.Left, c + Vector2I.Up, c + Vector2I.Down })
            {
                if (layout.Walkable(n) && !layout.BossRegion.Contains(n))
                {
                    gate.Add(n);
                }
            }
        }

        layout.Gate = gate.ToList();

        // Pack spots inside rooms, spaced apart.
        foreach (RoomDef r in def.Rooms)
        {
            var used = new List<Vector2I>();
            foreach (char p in r.Packs)
            {
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    Vector2I c = new Vector2I(rng.RandiRange(r.X + 1, r.X + r.W - 2), rng.RandiRange(r.Y + 1, r.Y + r.H - 2)) + offset;
                    if (!layout.Walkable(c) || used.Any(u => (u - c).LengthSquared() < 9))
                    {
                        continue;
                    }

                    used.Add(c);
                    layout.Packs.Add(new PackSpot(p, c, r.Id));
                    break;
                }
            }
        }

        return layout;
    }

    private static void Carve(RasterLayout layout, Vector2I from, Vector2I to, int width)
    {
        int half = width / 2;
        int x0 = Mathf.Min(from.X, to.X) - half;
        int x1 = Mathf.Max(from.X, to.X) + (width - half - 1);
        int y0 = Mathf.Min(from.Y, to.Y) - half;
        int y1 = Mathf.Max(from.Y, to.Y) + (width - half - 1);
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                if (x >= 0 && y >= 0 && x < layout.Width && y < layout.Height && layout.Cells[x, y] == CellKind.Void)
                {
                    layout.Cells[x, y] = CellKind.Floor;
                }
            }
        }
    }

    // --- scene ---------------------------------------------------------------

    private static readonly Vector2I[] FloorTiles = { new(0, 0), new(1, 0), new(2, 0), new(3, 0) };
    private static readonly Vector2I[] HazardTiles = { new(0, 1), new(1, 1) };
    private static readonly Vector2I WallTile = new(2, 1);
    private static readonly Vector2I RuneTile = new(3, 1);

    /// <summary>Builds tiles and collision under <paramref name="parent"/>. Returns the gate body.</summary>
    public static StaticBody2D BuildScene(Node2D parent, RasterLayout layout, RealityTheme theme, RandomNumberGenerator rng)
    {
        var map = new TileMap { TileSet = GD.Load<TileSet>(theme.TilesetPath), Scale = Vector2.One * 2f, ZIndex = -20, Name = "Tiles" };
        parent.AddChild(map);
        for (int x = 0; x < layout.Width; x++)
        {
            for (int y = 0; y < layout.Height; y++)
            {
                CellKind kind = layout.Cells[x, y];
                var cell = new Vector2I(x, y);
                bool nearOpen = kind == CellKind.Void && HasOpenNeighbour(layout, cell);
                for (int tx = 0; tx < 2; tx++)
                {
                    for (int ty = 0; ty < 2; ty++)
                    {
                        Vector2I tile = new(x * 2 + tx, y * 2 + ty);
                        Vector2I? atlas = kind switch
                        {
                            CellKind.Floor => layout.BossRegion.Contains(cell) && (cell - layout.BossCell).LengthSquared() is 16 or 17 or 18 or 20 ? RuneTile : FloorTiles[PickFloor(rng)],
                            CellKind.Hazard => HazardTiles[rng.RandiRange(0, 1)],
                            CellKind.Pillar => WallTile,
                            _ => nearOpen ? WallTile : null,
                        };
                        if (atlas is { } a)
                        {
                            map.SetCell(0, tile, 0, a);
                        }
                    }
                }
            }
        }

        var walls = new StaticBody2D { CollisionLayer = CollisionLayers.World, CollisionMask = 0, Name = "Walls" };
        var hazards = new StaticBody2D { CollisionLayer = CollisionLayers.Hazard, CollisionMask = 0, Name = "Hazards" };
        parent.AddChild(walls);
        parent.AddChild(hazards);
        for (int y = 0; y < layout.Height; y++)
        {
            int x = 0;
            while (x < layout.Width)
            {
                CellKind kind = layout.Cells[x, y];
                bool solid = kind is CellKind.Void or CellKind.Pillar;
                bool hazard = kind == CellKind.Hazard;
                if (!solid && !hazard)
                {
                    x++;
                    continue;
                }

                int start = x;
                while (x < layout.Width && (solid ? layout.Cells[x, y] is CellKind.Void or CellKind.Pillar : layout.Cells[x, y] == CellKind.Hazard))
                {
                    x++;
                }

                var rect = new RectangleShape2D { Size = new Vector2((x - start) * RasterLayout.CellPx, RasterLayout.CellPx) };
                var shape = new CollisionShape2D { Shape = rect, Position = new Vector2((start + x) * 0.5f * RasterLayout.CellPx, (y + 0.5f) * RasterLayout.CellPx) };
                (solid ? walls : hazards).AddChild(shape);
            }
        }

        var gate = new StaticBody2D { CollisionLayer = CollisionLayers.World, CollisionMask = 0, Name = "Gate" };
        parent.AddChild(gate);
        foreach (Vector2I c in layout.Gate)
        {
            gate.AddChild(new CollisionShape2D
            {
                Shape = new RectangleShape2D { Size = Vector2.One * RasterLayout.CellPx },
                Position = RasterLayout.World(c),
                Disabled = true,
            });
        }

        return gate;
    }

    private static int PickFloor(RandomNumberGenerator rng)
    {
        float r = rng.Randf();
        return r < 0.45f ? 0 : r < 0.75f ? 1 : r < 0.92f ? 2 : 3;
    }

    private static bool HasOpenNeighbour(RasterLayout layout, Vector2I c)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                var n = new Vector2I(c.X + dx, c.Y + dy);
                if (n.X >= 0 && n.Y >= 0 && n.X < layout.Width && n.Y < layout.Height && layout.Cells[n.X, n.Y] is CellKind.Floor or CellKind.Hazard)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
