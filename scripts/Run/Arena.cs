#nullable enable
using Godot;
using PathToExile.Combat;

namespace PathToExile.Run;

/// <summary>
/// The Ashen Arena — docs/design/run.md -> "The arena". Built at runtime from
/// the EasyPixel tileset: an open stone floor (no obstacles: enemies chase in
/// straight lines, and being herded into a pillar by a swarm with no pathing
/// reads as a bug), a brick wall rim, a lava moat outside it, and a rune
/// circle at the centre where bosses appear.
///
/// Tile atlas coordinates match tools/art/sprites/tiles.ts.
/// </summary>
[GlobalClass]
public partial class Arena : Node2D
{
    public const int TilePx = 16;
    public const float TileScale = 2f;

    [Export]
    public int WidthTiles = 80;

    [Export]
    public int HeightTiles = 60;

    /// <summary>Lava tiles drawn beyond the walls (visual only).</summary>
    [Export]
    public int MoatTiles = 14;

    [Export]
    public int Seed = 7;

    private static readonly Vector2I[] FloorTiles = { new(0, 0), new(1, 0), new(2, 0), new(3, 0) };
    private static readonly Vector2I[] LavaTiles = { new(0, 1), new(1, 1) };
    private static readonly Vector2I WallTile = new(2, 1);
    private static readonly Vector2I RuneTile = new(3, 1);

    public float TileWorld => TilePx * TileScale;

    /// <summary>The walkable rectangle, in world space.</summary>
    public Rect2 Bounds => new(GlobalPosition + Vector2.One * TileWorld, new Vector2((WidthTiles - 2) * TileWorld, (HeightTiles - 2) * TileWorld));

    public Vector2 Center => GlobalPosition + new Vector2(WidthTiles, HeightTiles) * TileWorld / 2f;

    public override void _Ready()
    {
        var tileSet = GD.Load<TileSet>("res://assets/tiles/ashen_arena_tileset.tres");
        var map = new TileMap { TileSet = tileSet, Scale = Vector2.One * TileScale, ZIndex = -20, Name = "Tiles" };
        AddChild(map);

        var rng = new RandomNumberGenerator { Seed = (ulong)Seed };
        Vector2I centre = new(WidthTiles / 2, HeightTiles / 2);
        for (int y = -MoatTiles; y < HeightTiles + MoatTiles; y++)
        {
            for (int x = -MoatTiles; x < WidthTiles + MoatTiles; x++)
            {
                Vector2I cell = new(x, y);
                bool outside = x < 0 || y < 0 || x >= WidthTiles || y >= HeightTiles;
                bool rim = !outside && (x == 0 || y == 0 || x == WidthTiles - 1 || y == HeightTiles - 1);
                Vector2I atlas;
                if (outside)
                {
                    atlas = LavaTiles[rng.RandiRange(0, LavaTiles.Length - 1)];
                }
                else if (rim)
                {
                    atlas = WallTile;
                }
                else
                {
                    float d = ((Vector2)(cell - centre)).Length();
                    bool runeRing = Mathf.Abs(d - 7f) < 0.5f && (x + y) % 2 == 0;
                    atlas = runeRing || cell == centre ? RuneTile : FloorTiles[PickFloor(rng)];
                }

                map.SetCell(0, cell, 0, atlas);
            }
        }

        BuildWalls();
    }

    private static int PickFloor(RandomNumberGenerator rng)
    {
        float r = rng.Randf();
        return r < 0.45f ? 0 : r < 0.75f ? 1 : r < 0.92f ? 2 : 3;
    }

    private void BuildWalls()
    {
        var body = new StaticBody2D { CollisionLayer = CollisionLayers.World, CollisionMask = 0, Name = "Walls" };
        AddChild(body);
        float w = WidthTiles * TileWorld;
        float h = HeightTiles * TileWorld;
        float t = TileWorld;
        AddWall(body, new Vector2(w / 2f, t / 2f), new Vector2(w, t));
        AddWall(body, new Vector2(w / 2f, h - t / 2f), new Vector2(w, t));
        AddWall(body, new Vector2(t / 2f, h / 2f), new Vector2(t, h));
        AddWall(body, new Vector2(w - t / 2f, h / 2f), new Vector2(t, h));
    }

    private static void AddWall(StaticBody2D body, Vector2 position, Vector2 size)
    {
        body.AddChild(new CollisionShape2D { Position = position, Shape = new RectangleShape2D { Size = size } });
    }

    /// <summary>Clamps a point into the walkable area with a margin.</summary>
    public Vector2 Clamp(Vector2 point, float margin = 24f)
    {
        Rect2 b = Bounds.Grow(-margin);
        return new Vector2(Mathf.Clamp(point.X, b.Position.X, b.End.X), Mathf.Clamp(point.Y, b.Position.Y, b.End.Y));
    }
}
