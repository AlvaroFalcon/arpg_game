#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;

namespace ArpgGame.World;

/// <summary>
/// The hideout town's ground — docs/design/hideout.md. A hand-drawn map (central
/// plaza with the Mirror, the smithy, temple, vault, training yard, a canal with
/// three bridges, the rift gate up north) rasterised into one TileMap per tileset,
/// with wall and water collision and a navigation mesh of walkable cells. Cells
/// the player repaved with bought floor tiles come from <see cref="HideoutData.Floors"/>.
/// </summary>
public partial class HideoutTown : Node2D
{
    public const int CellPx = 32;

    // '#' wall · '.' cobblestone · 'o' plaza mosaic · '~' canal · 'p' planks
    // 'g' grass · 'm' marble · 'c' carpet.
    public static readonly string[] Map =
    {
        "##################################################",
        "#................................................#",
        "#..#######.#######......ooo......#######.#######.#",
        "#..#######.#######......ooo......#######.#######.#",
        "#..#######.#######......ooo......#######.#######.#",
        "#..#######.#######...............#######.#######.#",
        "#..#######...............................#######.#",
        "#................................................#",
        "#................................................#",
        "#..############.......ooooooo.......ggggggggggg..#",
        "#..#pppppppppp#......o.......o......ggggggggggg..#",
        "#..#pppppppppp#.....o.........o.....ggggggggggg..#",
        "#..#pppppppppp#....o...........o....ggggggggggg..#",
        "#..#pppppppppp#....o...........o....ggggggggggg..#",
        "#..#ppppppppppp....o....ooo....o....ggggggggggg..#",
        "#..#ppppppppppp....o....ooo....o....ggggggggggg..#",
        "#..#ppppppppppp....o....ooo....o....ggggggggggg..#",
        "#..#pppppppppp#....o...........o....ggggggggggg..#",
        "#..#pppppppppp#....o...........o....ggggggggggg..#",
        "#..#pppppppppp#.....o.........o.....ggggggggggg..#",
        "#..#pppppppppp#......o.......o......ggggggggggg..#",
        "#..############.......ooooooo....................#",
        "#................................................#",
        "#................................................#",
        "#~~~~~~~.....~~~~~~~~~~.....~~~~~~~~~~.....~~~~~~#",
        "#~~~~~~~.....~~~~~~~~~~.....~~~~~~~~~~.....~~~~~~#",
        "#................................................#",
        "#..######mmm#####................######ccc#####..#",
        "#..#mmmmmmmmmmmm#..gggggggggggg..#cccccccccccc#..#",
        "#..#mmmmmmmmmmmm#..gggggggggggg..#cccccccccccc#..#",
        "#..#mmmmmmmmmmmm#..gggggggggggg..#cccccccccccc#..#",
        "#..#mmmmmmmmmmmm#..gggggggggggg..#cccccccccccc#..#",
        "#..#mmmmmmmmmmmm#..gggggggggggg..#cccccccccccc#..#",
        "#..#mmmmmmmmmmmm#..gggggggggggg..#cccccccccccc#..#",
        "#..##############................##############..#",
        "##################################################",
    };

    public static int Width => Map[0].Length;
    public static int Height => Map.Length;

    /// <summary>Where a character appears: in the plaza, south of the Mirror.</summary>
    public static readonly Vector2I SpawnCell = new(25, 19);

    public HideoutData Data { get; set; } = new();

    /// <summary>Raised when cells of a TileMap changed (the voxel view rebuilds it).</summary>
    public event Action<TileMap>? MapChanged;

    private readonly Dictionary<string, TileMap> _maps = new();

    public static Vector2 World(Vector2I cell) => (new Vector2(cell.X, cell.Y) + Vector2.One * 0.5f) * CellPx;

    public static Vector2I CellOf(Vector2 world) => new(Mathf.FloorToInt(world.X / CellPx), Mathf.FloorToInt(world.Y / CellPx));

    public static bool InBounds(Vector2I c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

    public static char BaseChar(Vector2I c) => InBounds(c) ? Map[c.Y][c.X] : '#';

    /// <summary>Open ground: everything but walls and the canal.</summary>
    public static bool Walkable(Vector2I c) => BaseChar(c) is not ('#' or '~');

    public static Rect2 Bounds => new(Vector2.Zero, new Vector2(Width, Height) * CellPx);

    public static string BaseStyle(Vector2I c) => BaseChar(c) switch
    {
        '#' => "wall",
        '~' => "water",
        'o' => "mosaic",
        'p' => "planks",
        'g' => "grass",
        'm' => "marble",
        'c' => "carpet",
        _ => "cobble",
    };

    /// <summary>The part of town a point is in ("Plaza", "Smithy"...), for the town guide.</summary>
    public static string QuarterOf(Vector2 world)
    {
        Vector2I c = CellOf(world);
        if (((Vector2)(c - new Vector2I(25, 15))).Length() < 7.5f)
        {
            return "Plaza";
        }

        return c switch
        {
            { X: >= 3 and <= 14, Y: >= 9 and <= 21 } => "Smithy",
            { X: >= 3 and <= 16, Y: >= 27 } => "Temple",
            { X: >= 33 and <= 46, Y: >= 27 } => "Vault",
            { X: >= 19 and <= 30, Y: >= 27 } => "Garden",
            { X: >= 35, Y: >= 8 and <= 21 } => "Training yard",
            { Y: <= 7 } => "North gate",
            { Y: >= 23 and <= 26 } => "Canal",
            _ => "Streets",
        };
    }

    public string StyleAt(Vector2I c) =>
        Walkable(c) && Data.Floors.TryGetValue(HideoutData.Key(c), out string? id) ? id : BaseStyle(c);

    public IEnumerable<TileMap> Maps => _maps.Values;

    public override void _Ready()
    {
        Name = "Town";
        foreach (string tileset in HideoutCatalog.Tilesets)
        {
            if (!ResourceLoader.Exists(tileset))
            {
                continue;
            }

            var map = new TileMap { TileSet = GD.Load<TileSet>(tileset), Scale = Vector2.One * 2f, ZIndex = -20, Name = $"Tiles_{tileset.GetFile().GetBaseName()}" };
            AddChild(map);
            _maps[tileset] = map;
        }

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                SetTile(new Vector2I(x, y));
            }
        }

        BuildCollision();
        BuildNavigation();
    }

    private void SetTile(Vector2I c)
    {
        FloorStyle style = HideoutCatalog.Floor(StyleAt(c));
        foreach (TileMap map in _maps.Values)
        {
            map.EraseCell(0, c);
        }

        if (_maps.TryGetValue(style.Tileset, out TileMap? target))
        {
            // A fixed per-cell variant so repaving looks the same every visit.
            int pick = (int)((uint)(c.X * 73856093 ^ c.Y * 19349663) % (uint)style.Atlas.Length);
            target.SetCell(0, c, 0, style.Atlas[pick]);
        }
    }

    /// <summary>Paves <paramref name="c"/> with a bought floor (one tile from stock);
    /// the tile it replaces goes back to stock unless it was the town's own.</summary>
    public bool Paint(Vector2I c, string styleId)
    {
        FloorStyle style = HideoutCatalog.Floor(styleId);
        if (!Walkable(c) || style.PackPrice <= 0 || StyleAt(c) == styleId || Data.StockOf(styleId) <= 0)
        {
            return false;
        }

        string old = StyleAt(c);
        Data.AddStock(styleId, -1);
        if (Data.Floors.ContainsKey(HideoutData.Key(c)))
        {
            Data.AddStock(old, 1);
        }

        Data.Floors[HideoutData.Key(c)] = styleId;
        Changed(c, old);
        return true;
    }

    /// <summary>Restores a repaved cell to the town's own floor and returns the tile to stock.</summary>
    public bool Unpaint(Vector2I c)
    {
        string key = HideoutData.Key(c);
        if (!Data.Floors.TryGetValue(key, out string? old))
        {
            return false;
        }

        Data.Floors.Remove(key);
        Data.AddStock(old, 1);
        Changed(c, old);
        return true;
    }

    private void Changed(Vector2I c, string oldStyle)
    {
        SetTile(c);
        if (_maps.TryGetValue(HideoutCatalog.Floor(oldStyle).Tileset, out TileMap? before))
        {
            MapChanged?.Invoke(before);
        }

        if (_maps.TryGetValue(HideoutCatalog.Floor(StyleAt(c)).Tileset, out TileMap? after) && after != before)
        {
            MapChanged?.Invoke(after);
        }
    }

    /// <summary>Walls and the canal block movement: merged runs of cells per row.</summary>
    private void BuildCollision()
    {
        var body = new StaticBody2D { CollisionLayer = CollisionLayers.World, CollisionMask = 0, Name = "Walls" };
        AddChild(body);
        for (int y = 0; y < Height; y++)
        {
            int x = 0;
            while (x < Width)
            {
                if (Walkable(new Vector2I(x, y)))
                {
                    x++;
                    continue;
                }

                int start = x;
                while (x < Width && !Walkable(new Vector2I(x, y)))
                {
                    x++;
                }

                body.AddChild(new CollisionShape2D
                {
                    Shape = new RectangleShape2D { Size = new Vector2((x - start) * CellPx, CellPx) },
                    Position = new Vector2((start + x) * 0.5f * CellPx, (y + 0.5f) * CellPx),
                });
            }
        }
    }

    /// <summary>Click-to-move paths: one navigation quad per walkable cell, sharing
    /// corner vertices with its neighbours so the mesh is connected.</summary>
    private void BuildNavigation()
    {
        var nav = new NavigationPolygon();
        var vertices = new List<Vector2>();
        var index = new Dictionary<Vector2I, int>();
        int Vertex(int x, int y)
        {
            var key = new Vector2I(x, y);
            if (!index.TryGetValue(key, out int i))
            {
                i = vertices.Count;
                index[key] = i;
                vertices.Add(new Vector2(x, y) * CellPx);
            }

            return i;
        }

        var polygons = new List<int[]>();
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (Walkable(new Vector2I(x, y)))
                {
                    polygons.Add(new[] { Vertex(x, y), Vertex(x + 1, y), Vertex(x + 1, y + 1), Vertex(x, y + 1) });
                }
            }
        }

        nav.Vertices = vertices.ToArray();
        foreach (int[] p in polygons)
        {
            nav.AddPolygon(p);
        }

        AddChild(new NavigationRegion2D { NavigationPolygon = nav, Name = "Navigation" });
    }
}
