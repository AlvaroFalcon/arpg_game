#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArpgGame.World;

/// <summary>How a cave's liquid is laid out (docs/design/endgame.md -> "Caverns").</summary>
public enum CaveLiquid
{
    /// <summary>Scattered pools (the crypt's ooze).</summary>
    Pools,

    /// <summary>Meandering rivers the path bridges over, plus a few pools (lava).</summary>
    Rivers,

    /// <summary>Broad lagoons with islands; the path runs along causeways (water).</summary>
    Lagoons,
}

/// <summary>The knobs that give each theme's caverns their character.</summary>
public sealed class CaveStyle
{
    public int Width { get; init; } = 96;
    public int Height { get; init; } = 60;

    /// <summary>The path runs bottom → top instead of left → right.</summary>
    public bool Vertical { get; init; }

    /// <summary>Typical half-width of the cave around its path, in cells.</summary>
    public float Radius { get; init; } = 5.5f;

    /// <summary>Side passages leaving the main path (each ends in a pocket).</summary>
    public int Branches { get; init; } = 3;

    /// <summary>Bends of the main path.</summary>
    public int Bends { get; init; } = 6;

    public CaveLiquid Liquid { get; init; } = CaveLiquid.Pools;

    /// <summary>0 – 1: how much of the open floor the liquid takes.</summary>
    public float LiquidAmount { get; init; } = 0.5f;

    /// <summary>Chance a fully open cell becomes a rock pillar.</summary>
    public float PillarRate { get; init; } = 0.012f;

    /// <summary>Seed probability of open ground inside the cave (lower = more rock outcrops).</summary>
    public float Openness { get; init; } = 0.68f;

    public int PackSpots { get; init; } = 18;
    public int ArenaRadius { get; init; } = 8;
}

/// <summary>
/// Caverns grown by cellular automata — docs/design/endgame.md -> "Caverns". Pure
/// cellular automata gives either scattered caves or one shapeless blob, so it is
/// steered: a winding path from the entrance to the boss (plus a few side branches)
/// is drawn first, cells are seeded open with a probability that falls off with the
/// distance to that path (the falloff radius itself wobbles with noise, so the cave
/// swells into chambers and pinches into passages), and a few smoothing passes of
/// the classic rule (a cell becomes rock with five or more rock neighbours) turn
/// the noise into organic walls. Then: the path is re-carved so it always exists,
/// a round arena is cut at its end behind a wall ring with one opening (the gate),
/// everything not connected to the entrance is filled in, one-cell gaps are
/// widened, liquid and pillars are added by theme, and pack spots are scattered
/// with a minimum spacing, stronger ones further in.
/// Output is a <see cref="RasterLayout"/>, the same as the room-graph layouts, so
/// navigation, the gate, the map and the voxel view need no changes.
/// </summary>
public static class CaveGenerator
{
    private const int Border = 2;

    public static RasterLayout Generate(CaveStyle style, RandomNumberGenerator rng)
    {
        // Work in "path along X" space; vertical caves are transposed at the end.
        int w = style.Vertical ? style.Height : style.Width;
        int h = style.Vertical ? style.Width : style.Height;
        var open = new bool[w, h];
        var noise = new FastNoiseLite { Seed = (int)(rng.Randi() & 0x7fffffff), Frequency = 0.07f, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex };
        var liquidNoise = new FastNoiseLite { Seed = (int)(rng.Randi() & 0x7fffffff), Frequency = 0.045f, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, FractalOctaves = 2 };

        // --- the path ------------------------------------------------------------
        var spine = new List<Vector2>();
        float y = h / 2f + rng.RandfRange(-h / 6f, h / 6f);
        int bends = Mathf.Max(3, style.Bends);
        float startX = 7f;
        float endX = w - style.ArenaRadius - 5f;
        for (int i = 0; i <= bends; i++)
        {
            float x = Mathf.Lerp(startX, endX, i / (float)bends);
            if (i > 0 && i < bends)
            {
                y = Mathf.Clamp(y + rng.RandfRange(-h / 3f, h / 3f), 9f, h - 10f);
            }
            else if (i == bends)
            {
                y = Mathf.Clamp(y + rng.RandfRange(-h / 6f, h / 6f), style.ArenaRadius + 5f, h - style.ArenaRadius - 6f);
            }

            spine.Add(new Vector2(x, y));
        }

        var segments = new List<(Vector2 A, Vector2 B)>();
        for (int i = 0; i + 1 < spine.Count; i++)
        {
            segments.Add((spine[i], spine[i + 1]));
        }

        var branchEnds = new List<Vector2>();
        var branchSegments = new List<(Vector2 A, Vector2 B)>();
        for (int b = 0; b < style.Branches; b++)
        {
            Vector2 from = spine[rng.RandiRange(1, spine.Count - 3)];
            float dir = from.Y > h / 2f ? -1f : 1f;
            if (rng.Randf() < 0.3f)
            {
                dir = -dir;
            }

            var mid = new Vector2(from.X + rng.RandfRange(-8f, 8f), from.Y + dir * rng.RandfRange(6f, 11f));
            var end = new Vector2(mid.X + rng.RandfRange(-10f, 10f), mid.Y + dir * rng.RandfRange(3f, 8f));
            mid = Clamp(mid, w, h, 6f);
            end = Clamp(end, w, h, 7f);
            branchSegments.Add((from, mid));
            branchSegments.Add((mid, end));
            branchEnds.Add(end);
        }

        // --- seed and grow -------------------------------------------------------
        for (int x = Border; x < w - Border; x++)
        {
            for (int yy = Border; yy < h - Border; yy++)
            {
                var c = new Vector2(x, yy);
                float d = Mathf.Min(Distance(c, segments), Distance(c, branchSegments) + 0.8f);
                foreach (Vector2 pocket in branchEnds)
                {
                    d = Mathf.Min(d, c.DistanceTo(pocket) - 2.5f);
                }

                float radius = style.Radius * (0.6f + 0.85f * (noise.GetNoise2D(x, yy) * 0.5f + 0.5f));
                // Inside the cave the seed is only mostly open, so smoothing leaves
                // rock outcrops and islands rather than one clean blob.
                float p = d < radius ? style.Openness : d < radius + 2.5f ? 0.42f : 0f;
                open[x, yy] = rng.Randf() < p;
            }
        }

        for (int pass = 0; pass < 5; pass++)
        {
            open = Smooth(open, w, h);
        }

        // The path always exists, three cells wide.
        foreach ((Vector2 a, Vector2 b) in segments.Concat(branchSegments))
        {
            CarveLine(open, w, h, a, b, 1.6f);
        }

        Vector2 entrance = spine[0];
        CarveDisc(open, w, h, entrance, 3.2f);
        WidenGaps(open, w, h);

        // --- the boss arena: a round hall behind a wall ring with one opening -----
        Vector2 boss = spine[^1];
        float r = style.ArenaRadius;
        // The opening runs from the arena towards the last path point outside the
        // ring (the final stretch of path can be shorter than the ring is wide).
        Vector2 outside = spine.Take(spine.Count - 1).Reverse().FirstOrDefault(p => p.DistanceTo(boss) > r + 4.5f, spine[0]);
        Vector2 inA = boss;
        Vector2 inB = boss + boss.DirectionTo(outside) * (r + 4.5f);
        CarveLine(open, w, h, inB, outside, 1.6f);
        for (int x = 0; x < w; x++)
        {
            for (int yy = 0; yy < h; yy++)
            {
                var c = new Vector2(x, yy);
                float d = c.DistanceTo(boss);
                if (d <= r)
                {
                    open[x, yy] = true;
                }
                else if (d <= r + 3f)
                {
                    // The ring; the incoming path cuts the one opening through it.
                    open[x, yy] = DistanceToSegment(c, inA, inB) < 1.6f && d <= r + 3f;
                }
            }
        }

        var layout = new RasterLayout { Width = w, Height = h, Cells = new CellKind[w, h] };
        for (int x = 0; x < w; x++)
        {
            for (int yy = 0; yy < h; yy++)
            {
                bool edge = x < Border || yy < Border || x >= w - Border || yy >= h - Border;
                layout.Cells[x, yy] = open[x, yy] && !edge ? CellKind.Floor : CellKind.Void;
            }
        }

        var entranceCell = new Vector2I(Mathf.RoundToInt(entrance.X), Mathf.RoundToInt(entrance.Y));
        var bossCell = new Vector2I(Mathf.RoundToInt(boss.X), Mathf.RoundToInt(boss.Y));
        FixPinches(layout, boss, r);
        KeepConnected(layout, entranceCell);

        // --- liquid and pillars ----------------------------------------------------
        bool Protected(Vector2I c)
        {
            var v = new Vector2(c.X, c.Y);
            return Distance(v, segments) < 2.2f || v.DistanceTo(entrance) < 6f || v.DistanceTo(boss) < r + 3.5f;
        }

        for (int x = 0; x < w; x++)
        {
            for (int yy = 0; yy < h; yy++)
            {
                var c = new Vector2I(x, yy);
                if (layout.Cells[x, yy] != CellKind.Floor || Protected(c))
                {
                    continue;
                }

                float n = liquidNoise.GetNoise2D(x, yy) * 0.5f + 0.5f;
                bool liquid = style.Liquid switch
                {
                    CaveLiquid.Rivers => Mathf.Abs(n - 0.5f) < 0.03f + 0.03f * style.LiquidAmount || n > 0.86f - 0.08f * style.LiquidAmount,
                    CaveLiquid.Lagoons => n > 0.66f - 0.16f * style.LiquidAmount,
                    _ => n > 0.8f - 0.12f * style.LiquidAmount,
                };
                if (liquid)
                {
                    layout.Cells[x, yy] = CellKind.Hazard;
                }
            }
        }

        RemoveSpecks(layout, CellKind.Hazard, 3);
        for (int x = 1; x < w - 1; x++)
        {
            for (int yy = 1; yy < h - 1; yy++)
            {
                var c = new Vector2I(x, yy);
                if (layout.Cells[x, yy] == CellKind.Floor && !Protected(c) && OpenAround(layout, c) && rng.Randf() < style.PillarRate)
                {
                    layout.Cells[x, yy] = CellKind.Pillar;
                }
            }
        }

        // --- entrance, arena, gate -------------------------------------------------
        layout.Entrance = entranceCell;
        layout.BossCell = bossCell;
        for (int x = 0; x < w; x++)
        {
            for (int yy = 0; yy < h; yy++)
            {
                var c = new Vector2I(x, yy);
                if (layout.Walkable(c) && new Vector2(x, yy).DistanceTo(boss) <= r + 0.5f)
                {
                    layout.BossRegion.Add(c);
                }
            }
        }

        var gate = new HashSet<Vector2I>();
        foreach (Vector2I c in layout.BossRegion)
        {
            foreach (Vector2I nb in Neighbours4(c))
            {
                if (layout.Walkable(nb) && !layout.BossRegion.Contains(nb))
                {
                    gate.Add(nb);
                }
            }
        }

        layout.Gate = gate.ToList();
        PlacePacks(layout, style, rng);

        return style.Vertical ? Transpose(layout) : layout;
    }

    // --- cellular automata ---------------------------------------------------------

    /// <summary>One smoothing pass: rock with five or more rock neighbours (out of
    /// bounds counts as rock), open with three or fewer, unchanged otherwise.</summary>
    private static bool[,] Smooth(bool[,] open, int w, int h)
    {
        var next = new bool[w, h];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                int rock = 0;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h || !open[nx, ny])
                        {
                            rock++;
                        }
                    }
                }

                next[x, y] = rock >= 5 ? false : rock <= 3 || open[x, y];
            }
        }

        return next;
    }

    // --- cleanup -------------------------------------------------------------------

    /// <summary>Opens one-cell gaps (rock on both sides) so nothing gets stuck. Reads
    /// the previous pass and writes a copy, so an opened cell never chains into a
    /// one-cell hairline.</summary>
    private static void WidenGaps(bool[,] open, int w, int h)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            var before = (bool[,])open.Clone();
            for (int x = Border + 1; x < w - Border - 1; x++)
            {
                for (int y = Border + 1; y < h - Border - 1; y++)
                {
                    if (!before[x, y])
                    {
                        continue;
                    }

                    if (!before[x - 1, y] && !before[x + 1, y])
                    {
                        open[x + 1, y] = true;
                    }

                    if (!before[x, y - 1] && !before[x, y + 1])
                    {
                        open[x, y + 1] = true;
                    }
                }
            }
        }

        // Dead-end spurs one cell wide (open on one side only) are filled in.
        for (int pass = 0; pass < 3; pass++)
        {
            var before = (bool[,])open.Clone();
            for (int x = Border; x < w - Border; x++)
            {
                for (int y = Border; y < h - Border; y++)
                {
                    if (before[x, y] && (before[x - 1, y] ? 1 : 0) + (before[x + 1, y] ? 1 : 0) + (before[x, y - 1] ? 1 : 0) + (before[x, y + 1] ? 1 : 0) <= 1)
                    {
                        open[x, y] = false;
                    }
                }
            }
        }
    }

    /// <summary>
    /// After the arena ring is cut: any floor cell squeezed between rock on both
    /// sides is widened into the neighbouring rock, unless that rock is part of the
    /// arena's ring (that would make a second way in), in which case it is filled.
    /// </summary>
    private static void FixPinches(RasterLayout layout, Vector2 boss, float r)
    {
        bool Ring(int x, int y)
        {
            float d = new Vector2(x, y).DistanceTo(boss);
            return d > r && d <= r + 3.5f;
        }

        bool Rock(int x, int y) => layout.Cells[x, y] == CellKind.Void;
        bool Inner(int x, int y) => x >= Border && y >= Border && x < layout.Width - Border && y < layout.Height - Border;
        for (int pass = 0; pass < 8; pass++)
        {
            bool changed = false;
            for (int x = 1; x < layout.Width - 1; x++)
            {
                for (int y = 1; y < layout.Height - 1; y++)
                {
                    if (layout.Cells[x, y] != CellKind.Floor)
                    {
                        continue;
                    }

                    foreach ((Vector2I a, Vector2I b) in new[] { (new Vector2I(x - 1, y), new Vector2I(x + 1, y)), (new Vector2I(x, y - 1), new Vector2I(x, y + 1)) })
                    {
                        if (!Rock(a.X, a.Y) || !Rock(b.X, b.Y) || layout.Cells[x, y] != CellKind.Floor)
                        {
                            continue;
                        }

                        Vector2I widen = Inner(b.X, b.Y) && !Ring(b.X, b.Y) ? b : Inner(a.X, a.Y) && !Ring(a.X, a.Y) ? a : new Vector2I(-1, -1);
                        if (widen.X >= 0)
                        {
                            layout.Cells[widen.X, widen.Y] = CellKind.Floor;
                        }
                        else
                        {
                            layout.Cells[x, y] = CellKind.Void;
                        }

                        changed = true;
                    }
                }
            }

            if (!changed)
            {
                return;
            }
        }

        // Whatever still pinches after that is filled in (until nothing does: a
        // filled cell can leave its neighbour pinched).
        for (int pass = 0; pass < 30; pass++)
        {
            bool filled = false;
            for (int x = 1; x < layout.Width - 1; x++)
            {
                for (int y = 1; y < layout.Height - 1; y++)
                {
                    if (layout.Cells[x, y] == CellKind.Floor && ((Rock(x - 1, y) && Rock(x + 1, y)) || (Rock(x, y - 1) && Rock(x, y + 1))))
                    {
                        layout.Cells[x, y] = CellKind.Void;
                        filled = true;
                    }
                }
            }

            if (!filled)
            {
                return;
            }
        }
    }

    /// <summary>Fills every open cell the entrance can't reach (4-connected).</summary>
    private static void KeepConnected(RasterLayout layout, Vector2I from)
    {
        HashSet<Vector2I> reach = Flood(layout, from, c => layout.Cells[c.X, c.Y] != CellKind.Void);
        for (int x = 0; x < layout.Width; x++)
        {
            for (int y = 0; y < layout.Height; y++)
            {
                if (layout.Cells[x, y] != CellKind.Void && !reach.Contains(new Vector2I(x, y)))
                {
                    layout.Cells[x, y] = CellKind.Void;
                }
            }
        }
    }

    /// <summary>Turns patches of <paramref name="kind"/> smaller than <paramref name="min"/> back into floor.</summary>
    private static void RemoveSpecks(RasterLayout layout, CellKind kind, int min)
    {
        var seen = new HashSet<Vector2I>();
        for (int x = 0; x < layout.Width; x++)
        {
            for (int y = 0; y < layout.Height; y++)
            {
                var c = new Vector2I(x, y);
                if (layout.Cells[x, y] != kind || seen.Contains(c))
                {
                    continue;
                }

                HashSet<Vector2I> patch = Flood(layout, c, n => layout.Cells[n.X, n.Y] == kind);
                seen.UnionWith(patch);
                if (patch.Count < min)
                {
                    foreach (Vector2I p in patch)
                    {
                        layout.Cells[p.X, p.Y] = CellKind.Floor;
                    }
                }
            }
        }
    }

    // --- packs ---------------------------------------------------------------------

    /// <summary>
    /// Pack spots: reachable floor at least twelve steps from the entrance, outside
    /// the arena, in open ground, at least seven cells apart. The further in, the
    /// likelier a magic or rare pack; one unique-rare at most, in the back half.
    /// "Rooms" become zones by walking distance (for pack bookkeeping).
    /// </summary>
    private static void PlacePacks(RasterLayout layout, CaveStyle style, RandomNumberGenerator rng)
    {
        Dictionary<Vector2I, int> steps = Distances(layout, layout.Entrance);
        int max = Mathf.Max(1, steps.Values.Max());
        var gate = new HashSet<Vector2I>(layout.Gate);
        List<Vector2I> candidates = steps.Keys
            .Where(c => steps[c] > 12 && !layout.BossRegion.Contains(c) && !gate.Contains(c) && OpenAround(layout, c))
            .OrderBy(_ => rng.Randi())
            .ToList();
        bool unique = false;
        // Seven cells apart if the cave has room; a lagoon-heavy map relaxes to five.
        var chosen = new List<Vector2I>();
        foreach (int spacing in new[] { 7, 6, 5 })
        {
            foreach (Vector2I c in candidates)
            {
                if (chosen.Count >= style.PackSpots)
                {
                    break;
                }

                if (!chosen.Any(u => (u - c).LengthSquared() < spacing * spacing))
                {
                    chosen.Add(c);
                }
            }

            if (chosen.Count >= style.PackSpots)
            {
                break;
            }
        }

        foreach (Vector2I c in chosen)
        {
            float progress = steps[c] / (float)max;
            float roll = rng.Randf();
            char kind;
            if (!unique && progress > 0.55f && roll < 0.1f)
            {
                kind = 'u';
                unique = true;
            }
            else if (roll < 0.1f + 0.14f * progress)
            {
                kind = 'r';
            }
            else if (roll < 0.36f + 0.1f * progress)
            {
                kind = 'm';
            }
            else
            {
                kind = 'p';
            }

            layout.Packs.Add(new PackSpot(kind, c, $"zone{Mathf.Min(4, (int)(progress * 5f))}"));
        }
    }

    // --- helpers -------------------------------------------------------------------

    private static Vector2 Clamp(Vector2 v, int w, int h, float margin) =>
        new(Mathf.Clamp(v.X, margin, w - margin), Mathf.Clamp(v.Y, margin, h - margin));

    private static float Distance(Vector2 p, List<(Vector2 A, Vector2 B)> segments)
    {
        float best = float.MaxValue;
        foreach ((Vector2 a, Vector2 b) in segments)
        {
            best = Mathf.Min(best, DistanceToSegment(p, a, b));
        }

        return best;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.LengthSquared() < 0.0001f ? 0f : Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
        return p.DistanceTo(a + ab * t);
    }

    private static void CarveLine(bool[,] open, int w, int h, Vector2 a, Vector2 b, float radius)
    {
        int steps = Mathf.CeilToInt(a.DistanceTo(b) * 2f) + 1;
        for (int i = 0; i <= steps; i++)
        {
            CarveDisc(open, w, h, a.Lerp(b, i / (float)steps), radius);
        }
    }

    private static void CarveDisc(bool[,] open, int w, int h, Vector2 c, float radius)
    {
        for (int x = Mathf.FloorToInt(c.X - radius); x <= Mathf.CeilToInt(c.X + radius); x++)
        {
            for (int y = Mathf.FloorToInt(c.Y - radius); y <= Mathf.CeilToInt(c.Y + radius); y++)
            {
                if (x >= Border && y >= Border && x < w - Border && y < h - Border && new Vector2(x, y).DistanceTo(c) <= radius)
                {
                    open[x, y] = true;
                }
            }
        }
    }

    private static bool OpenAround(RasterLayout layout, Vector2I c)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (!layout.Walkable(new Vector2I(c.X + dx, c.Y + dy)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static IEnumerable<Vector2I> Neighbours4(Vector2I c)
    {
        yield return c + Vector2I.Right;
        yield return c + Vector2I.Left;
        yield return c + Vector2I.Up;
        yield return c + Vector2I.Down;
    }

    private static HashSet<Vector2I> Flood(RasterLayout layout, Vector2I from, System.Func<Vector2I, bool> pass)
    {
        var seen = new HashSet<Vector2I>();
        if (from.X < 0 || from.Y < 0 || from.X >= layout.Width || from.Y >= layout.Height || !pass(from))
        {
            return seen;
        }

        var queue = new Queue<Vector2I>();
        seen.Add(from);
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            foreach (Vector2I n in Neighbours4(queue.Dequeue()))
            {
                if (n.X >= 0 && n.Y >= 0 && n.X < layout.Width && n.Y < layout.Height && !seen.Contains(n) && pass(n))
                {
                    seen.Add(n);
                    queue.Enqueue(n);
                }
            }
        }

        return seen;
    }

    /// <summary>Walking distance (4-connected, floor only) from a cell to every reachable floor cell.</summary>
    public static Dictionary<Vector2I, int> Distances(RasterLayout layout, Vector2I from)
    {
        var dist = new Dictionary<Vector2I, int>();
        if (!layout.Walkable(from))
        {
            return dist;
        }

        var queue = new Queue<Vector2I>();
        dist[from] = 0;
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            Vector2I c = queue.Dequeue();
            foreach (Vector2I n in Neighbours4(c))
            {
                if (layout.Walkable(n) && !dist.ContainsKey(n))
                {
                    dist[n] = dist[c] + 1;
                    queue.Enqueue(n);
                }
            }
        }

        return dist;
    }

    /// <summary>Mirrors a layout across its diagonal (path along X → path along Y).</summary>
    private static RasterLayout Transpose(RasterLayout s)
    {
        static Vector2I T(Vector2I c) => new(c.Y, c.X);
        var t = new RasterLayout { Width = s.Height, Height = s.Width, Cells = new CellKind[s.Height, s.Width] };
        for (int x = 0; x < s.Width; x++)
        {
            for (int y = 0; y < s.Height; y++)
            {
                t.Cells[y, x] = s.Cells[x, y];
            }
        }

        // Flip so the path climbs from the bottom (entrance) to the top (boss).
        var f = new RasterLayout { Width = t.Width, Height = t.Height, Cells = new CellKind[t.Width, t.Height] };
        Vector2I F(Vector2I c) => new(c.X, t.Height - 1 - c.Y);
        for (int x = 0; x < t.Width; x++)
        {
            for (int y = 0; y < t.Height; y++)
            {
                f.Cells[x, t.Height - 1 - y] = t.Cells[x, y];
            }
        }

        f.Entrance = F(T(s.Entrance));
        f.BossCell = F(T(s.BossCell));
        f.BossRegion = new HashSet<Vector2I>(s.BossRegion.Select(c => F(T(c))));
        f.Gate = s.Gate.Select(c => F(T(c))).ToList();
        f.Packs = s.Packs.Select(p => p with { Cell = F(T(p.Cell)) }).ToList();
        return f;
    }
}
