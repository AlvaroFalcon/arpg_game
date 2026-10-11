#nullable enable
using System.Collections.Generic;
using Godot;

namespace PathToExile.World;

/// <summary>
/// Pathing for crowds — docs/design/endgame.md -> "Layout". Instead of one
/// NavigationAgent per monster (a hundred path queries every few frames), the
/// reality keeps one breadth-first distance field from the player over the
/// layout's walkable grid, recomputed four times a second; every monster just
/// steps toward its lowest-distance neighbour cell. Cheap for any number of
/// monsters, and corridors, doors and corners come for free.
/// </summary>
public sealed class FlowField
{
    /// <summary>The field monsters use, or null outside a layout (open arenas).</summary>
    public static FlowField? Current { get; set; }

    public const int Unreachable = int.MaxValue;

    public int Width { get; }
    public int Height { get; }
    public float CellSize { get; }
    public Vector2 Origin { get; }

    private readonly bool[,] _walkable;
    private readonly int[,] _distance;
    private Vector2I _target = new(-1, -1);

    private static readonly Vector2I[] Neighbours =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    public FlowField(bool[,] walkable, float cellSize, Vector2 origin)
    {
        _walkable = walkable;
        Width = walkable.GetLength(0);
        Height = walkable.GetLength(1);
        CellSize = cellSize;
        Origin = origin;
        _distance = new int[Width, Height];
    }

    public Vector2I CellOf(Vector2 world)
    {
        Vector2 local = (world - Origin) / CellSize;
        return new Vector2I(Mathf.FloorToInt(local.X), Mathf.FloorToInt(local.Y));
    }

    public Vector2 CenterOf(Vector2I cell) => Origin + (new Vector2(cell.X, cell.Y) + Vector2.One * 0.5f) * CellSize;

    public bool InBounds(Vector2I c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

    public bool IsWalkable(Vector2I c) => InBounds(c) && _walkable[c.X, c.Y];

    public void SetWalkable(Vector2I c, bool walkable)
    {
        if (InBounds(c))
        {
            _walkable[c.X, c.Y] = walkable;
            _target = new Vector2I(-1, -1);
        }
    }

    /// <summary>True if the straight line between two points crosses only walkable
    /// cells (sampled every 12 px): what auto-aim and the bot use as line of sight.</summary>
    public bool HasLineOfSight(Vector2 a, Vector2 b)
    {
        int steps = Mathf.CeilToInt(a.DistanceTo(b) / 12f);
        for (int i = 1; i < steps; i++)
        {
            if (!IsWalkable(CellOf(a.Lerp(b, i / (float)steps))))
            {
                return false;
            }
        }

        return true;
    }

    public int DistanceAt(Vector2 world)
    {
        Vector2I c = CellOf(world);
        return InBounds(c) ? _distance[c.X, c.Y] : Unreachable;
    }

    /// <summary>Recomputes distances from <paramref name="targetWorld"/> (skipped if
    /// the target is still in the same cell).</summary>
    public void Update(Vector2 targetWorld)
    {
        Vector2I target = CellOf(targetWorld);
        if (target == _target || !InBounds(target))
        {
            return;
        }

        _target = target;
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                _distance[x, y] = Unreachable;
            }
        }

        var queue = new Queue<Vector2I>();
        _distance[target.X, target.Y] = 0;
        queue.Enqueue(target);
        while (queue.Count > 0)
        {
            Vector2I c = queue.Dequeue();
            int d = _distance[c.X, c.Y] + 1;
            for (int i = 0; i < 4; i++)
            {
                Vector2I n = c + Neighbours[i];
                if (IsWalkable(n) && _distance[n.X, n.Y] > d)
                {
                    _distance[n.X, n.Y] = d;
                    queue.Enqueue(n);
                }
            }
        }
    }

    /// <summary>Unit direction to walk from <paramref name="world"/> toward the
    /// target; zero if already in the target cell or unreachable.</summary>
    public Vector2 Direction(Vector2 world)
    {
        Vector2I c = CellOf(world);
        if (!InBounds(c))
        {
            return Vector2.Zero;
        }

        int best = _distance[c.X, c.Y];
        if (best == 0)
        {
            return Vector2.Zero;
        }

        Vector2I bestCell = c;
        foreach (Vector2I off in Neighbours)
        {
            Vector2I n = c + off;
            if (!IsWalkable(n) || _distance[n.X, n.Y] >= best)
            {
                continue;
            }

            // No corner cutting: a diagonal needs both orthogonal cells open.
            if (off.X != 0 && off.Y != 0 && (!IsWalkable(new Vector2I(c.X + off.X, c.Y)) || !IsWalkable(new Vector2I(c.X, c.Y + off.Y))))
            {
                continue;
            }

            best = _distance[n.X, n.Y];
            bestCell = n;
        }

        return bestCell == c ? Vector2.Zero : world.DirectionTo(CenterOf(bestCell));
    }
}
