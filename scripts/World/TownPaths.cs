#nullable enable
using System;
using System.Collections.Generic;
using Godot;

namespace ArpgGame.World;

/// <summary>
/// Precomputed walks to the town's stations — docs/design/hideout.md -> "Walking
/// to a station". The town directory (and the guide's "Show me", and the map)
/// walk the player to a station; Godot's navigation mesh of floor cells knew
/// nothing about the props standing on it, so those walks ran into stalls and
/// stuck there. Instead, the town keeps one distance field per station over a
/// fine grid whose blocked cells come from the physics world itself (anything
/// the player's own circle would touch), computed when the town loads and again
/// after the editor closes. Walking is then just going downhill.
///
/// Only those station walks use this; ordinary click-to-move is unchanged.
/// </summary>
public sealed class TownPaths
{
    /// <summary>Grid cell size. Half a town tile, offset so cell centres land on
    /// tile centres: a one-tile door has a free cell right down its middle.</summary>
    public const float CellPx = HideoutTown.CellPx / 2f;

    public static readonly Vector2 Origin = new(-CellPx / 2f, -CellPx / 2f);

    private const int Straight = 10;
    private const int Diagonal = 14;
    public const int Unreachable = int.MaxValue;

    public int Width { get; }
    public int Height { get; }

    private readonly bool[,] _blocked;
    private readonly Dictionary<string, int[,]> _fields = new();

    private static readonly Vector2I[] Neighbours =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    /// <summary>Builds the grid over <paramref name="bounds"/>; a cell is blocked
    /// when <paramref name="isBlocked"/> says so for its centre.</summary>
    public TownPaths(Rect2 bounds, Func<Vector2, bool> isBlocked)
    {
        Width = Mathf.CeilToInt((bounds.End.X - Origin.X) / CellPx);
        Height = Mathf.CeilToInt((bounds.End.Y - Origin.Y) / CellPx);
        _blocked = new bool[Width, Height];
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Vector2 at = CenterOf(new Vector2I(x, y));
                _blocked[x, y] = !bounds.HasPoint(at) || isBlocked(at);
            }
        }
    }

    public Vector2I CellOf(Vector2 world)
    {
        Vector2 local = (world - Origin) / CellPx;
        return new Vector2I(Mathf.FloorToInt(local.X), Mathf.FloorToInt(local.Y));
    }

    public Vector2 CenterOf(Vector2I c) => Origin + (new Vector2(c.X, c.Y) + Vector2.One * 0.5f) * CellPx;

    public bool InBounds(Vector2I c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

    public bool IsFree(Vector2I c) => InBounds(c) && !_blocked[c.X, c.Y];

    public bool HasField(string id) => _fields.ContainsKey(id);

    /// <summary>
    /// Distances to <paramref name="id"/>'s goal: every free cell whose centre
    /// <paramref name="isGoal"/> accepts (the spots the station can be used from).
    /// Dijkstra over 8 neighbours (10 / 14), no corner cutting. Returns whether
    /// any goal cell is free at all.
    /// </summary>
    public bool AddField(string id, Func<Vector2, bool> isGoal)
    {
        var dist = new int[Width, Height];
        var open = new PriorityQueue<Vector2I, int>();
        bool any = false;
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                var c = new Vector2I(x, y);
                bool goal = !_blocked[x, y] && isGoal(CenterOf(c));
                dist[x, y] = goal ? 0 : Unreachable;
                if (goal)
                {
                    open.Enqueue(c, 0);
                    any = true;
                }
            }
        }

        while (open.TryDequeue(out Vector2I c, out int d))
        {
            if (d > dist[c.X, c.Y])
            {
                continue;
            }

            foreach (Vector2I off in Neighbours)
            {
                Vector2I n = c + off;
                if (!Step(c, off))
                {
                    continue;
                }

                int nd = d + (off.X != 0 && off.Y != 0 ? Diagonal : Straight);
                if (nd < dist[n.X, n.Y])
                {
                    dist[n.X, n.Y] = nd;
                    open.Enqueue(n, nd);
                }
            }
        }

        _fields[id] = dist;
        return any;
    }

    /// <summary>Whether one step from <paramref name="c"/> by <paramref name="off"/>
    /// is allowed: the target is free and a diagonal doesn't clip a corner.</summary>
    private bool Step(Vector2I c, Vector2I off)
    {
        Vector2I n = c + off;
        if (!IsFree(n))
        {
            return false;
        }

        return off.X == 0 || off.Y == 0 || (IsFree(new Vector2I(c.X + off.X, c.Y)) && IsFree(new Vector2I(c.X, c.Y + off.Y)));
    }

    public int DistanceAt(string id, Vector2I c) =>
        _fields.TryGetValue(id, out int[,]? f) && InBounds(c) ? f[c.X, c.Y] : Unreachable;

    /// <summary>The cell to start from: the player's own cell if free, else the
    /// nearest free cell around it that has a way to the goal (standing right
    /// against a prop puts the player's centre in its blocked margin).</summary>
    public Vector2I? StartCell(string id, Vector2 world)
    {
        Vector2I c = CellOf(world);
        if (IsFree(c) && DistanceAt(id, c) != Unreachable)
        {
            return c;
        }

        Vector2I? best = null;
        float bestScore = float.MaxValue;
        for (int r = 1; r <= 3 && best is null; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    var n = new Vector2I(c.X + dx, c.Y + dy);
                    if (!IsFree(n) || DistanceAt(id, n) == Unreachable)
                    {
                        continue;
                    }

                    float score = CenterOf(n).DistanceTo(world) * 10f + DistanceAt(id, n);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = n;
                    }
                }
            }
        }

        return best;
    }

    /// <summary>The downhill chain of cells from <paramref name="start"/> to the
    /// goal (start included), at most <paramref name="max"/> cells.</summary>
    public List<Vector2I> Chain(string id, Vector2I start, int max = 4096)
    {
        var chain = new List<Vector2I> { start };
        if (!_fields.TryGetValue(id, out int[,]? f))
        {
            return chain;
        }

        Vector2I c = start;
        while (chain.Count < max && f[c.X, c.Y] > 0 && f[c.X, c.Y] != Unreachable)
        {
            Vector2I next = c;
            int best = f[c.X, c.Y];
            foreach (Vector2I off in Neighbours)
            {
                Vector2I n = c + off;
                if (Step(c, off) && f[n.X, n.Y] < best)
                {
                    best = f[n.X, n.Y];
                    next = n;
                }
            }

            if (next == c)
            {
                break;
            }

            chain.Add(next);
            c = next;
        }

        return chain;
    }

    /// <summary>True if every grid sample on the segment (every 4 px) is a free cell.</summary>
    public bool GridLineClear(Vector2 a, Vector2 b)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(a.DistanceTo(b) / 4f));
        for (int i = 1; i <= steps; i++)
        {
            if (!IsFree(CellOf(a.Lerp(b, i / (float)steps))))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The point to walk toward now: the farthest cell of the route ahead
    /// (looking up to <paramref name="lookahead"/> cells) that can be reached in a
    /// straight line, so the walk runs straight across open ground and only turns
    /// at corners. <paramref name="straightClear"/> confirms a candidate segment
    /// (the town casts the player's circle along it). Null once arrived.
    /// </summary>
    public Vector2? Waypoint(string id, Vector2 from, Func<Vector2, Vector2, bool> straightClear, int lookahead = 40)
    {
        if (StartCell(id, from) is not { } start || DistanceAt(id, start) == 0)
        {
            return null;
        }

        List<Vector2I> chain = Chain(id, start, lookahead);
        for (int i = chain.Count - 1; i >= 1; i--)
        {
            Vector2 to = CenterOf(chain[i]);
            if (i == 1 || (GridLineClear(from, to) && straightClear(from, to)))
            {
                return to;
            }
        }

        // Pushed into a blocked margin: head back to the start cell first.
        return CenterOf(start);
    }

    /// <summary>The whole route as points, from <paramref name="from"/> to the goal
    /// (for drawing on the map). Empty when there is no route.</summary>
    public List<Vector2> Route(string id, Vector2 from)
    {
        var points = new List<Vector2>();
        if (StartCell(id, from) is not { } start)
        {
            return points;
        }

        points.Add(from);
        foreach (Vector2I c in Chain(id, start))
        {
            points.Add(CenterOf(c));
        }

        return points;
    }
}
