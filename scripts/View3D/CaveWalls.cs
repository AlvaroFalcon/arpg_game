#nullable enable
using System;
using System.Collections.Generic;
using Godot;

namespace ArpgGame.View3D;

/// <summary>
/// One continuous rock mesh for a cavern's walls — docs/design/art-pipeline.md ->
/// "Cave walls". The tile walls were a brick block per cell with its own height,
/// so curves (the boss arena's ring above all) read as a staircase of separate
/// cubes. Here the wall cells go through marching squares over the cell centres:
/// a straight wall still ends exactly on the cell edge (where its collision is),
/// a convex corner is cut on the diagonal and an inside corner filled, so a
/// curve becomes one chamfered face. Heights come from a continuous field
/// (shared corners, no steps), and the rock shader colours it in world space,
/// so there are no seams per block.
/// </summary>
public static class CaveWalls
{
    /// <summary>
    /// Builds the mesh. <paramref name="ground"/> maps fractional cell coordinates
    /// (cell centres at integers) to a ground point; <paramref name="height"/>
    /// gives the rock's height there. Cells outside <paramref name="walls"/> are open.
    /// </summary>
    public static ArrayMesh Build(HashSet<Vector2I> walls, Func<Vector2, Vector3> ground, Func<Vector2, float> height)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        if (walls.Count == 0)
        {
            return new ArrayMesh();
        }

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Vector2I c in walls)
        {
            minX = Math.Min(minX, c.X);
            minY = Math.Min(minY, c.Y);
            maxX = Math.Max(maxX, c.X);
            maxY = Math.Max(maxY, c.Y);
        }

        // Corners of a square, in order around it: (0,0) (1,0) (1,1) (0,1).
        Vector2I[] offsets = { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
        var poly = new List<(Vector2 P, bool Mid)>(8);
        for (int x = minX - 1; x <= maxX; x++)
        {
            for (int y = minY - 1; y <= maxY; y++)
            {
                var origin = new Vector2I(x, y);
                int count = 0;
                for (int i = 0; i < 4; i++)
                {
                    if (walls.Contains(origin + offsets[i]))
                    {
                        count++;
                    }
                }

                if (count == 0)
                {
                    continue;
                }

                // The wall part of this square: walk its corners, keeping wall corners
                // and the midpoints of edges where wall meets open. Every case comes out
                // convex (a diagonal pair joins into a hexagon: the rock stays one piece).
                poly.Clear();
                for (int i = 0; i < 4; i++)
                {
                    Vector2I a = origin + offsets[i];
                    Vector2I b = origin + offsets[(i + 1) % 4];
                    bool wa = walls.Contains(a);
                    if (wa)
                    {
                        poly.Add((a, false));
                    }

                    if (wa != walls.Contains(b))
                    {
                        poly.Add(((Vector2)(a + b) / 2f, true));
                    }
                }

                Vector2 centre = Vector2.Zero;
                foreach ((Vector2 p, _) in poly)
                {
                    centre += p;
                }

                centre /= poly.Count;
                Vector3 centre3 = ground(centre);

                // Top: a fan, each triangle lit flat (faceted rock).
                for (int i = 1; i < poly.Count - 1; i++)
                {
                    Vector3 p0 = Top(poly[0].P);
                    Vector3 p1 = Top(poly[i].P);
                    Vector3 p2 = Top(poly[i + 1].P);
                    Vector3 n = (p1 - p0).Cross(p2 - p0).Normalized();
                    if (n.Y < 0f)
                    {
                        n = -n;
                    }

                    Tri(st, p0, p1, p2, n);
                }

                // Sides: every edge between two midpoints is a stretch of contour.
                for (int i = 0; i < poly.Count; i++)
                {
                    (Vector2 a, bool am) = poly[i];
                    (Vector2 b, bool bm) = poly[(i + 1) % poly.Count];
                    if (!am || !bm)
                    {
                        continue;
                    }

                    Vector3 ga = ground(a);
                    Vector3 gb = ground(b);
                    Vector3 along = gb - ga;
                    var n = new Vector3(along.Z, 0f, -along.X).Normalized();
                    if (n.Dot((ga + gb) / 2f - centre3) < 0f)
                    {
                        n = -n;
                    }

                    Vector3 ta = Top(a);
                    Vector3 tb = Top(b);
                    Tri(st, ga, gb, tb, n);
                    Tri(st, ga, tb, ta, n);
                }
            }
        }

        return st.Commit();

        Vector3 Top(Vector2 p) => ground(p) + new Vector3(0f, height(p), 0f);
    }

    private static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
    {
        // Wind each triangle so its front faces along the normal.
        if ((b - a).Cross(c - a).Dot(n) > 0f)
        {
            (b, c) = (c, b);
        }

        foreach (Vector3 v in new[] { a, b, c })
        {
            st.SetNormal(n);
            st.AddVertex(v);
        }
    }
}
