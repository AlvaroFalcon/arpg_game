#nullable enable
using System.Collections.Generic;
using Godot;

namespace ArpgGame.View3D;

/// <summary>
/// Flying voxel cubes — docs/design/art-pipeline.md -> "Voxels". A monster's
/// death breaks its current voxel model into cubes of its own colours that
/// burst outwards, fall, bounce on the floor and shrink away; heavy hits kick
/// up cubes of floor. One MultiMesh per burst, simulated on the CPU (a few
/// dozen cubes for a second and a half).
/// </summary>
public partial class VoxelDebris : MultiMeshInstance3D
{
    private float _life = 1.6f;
    private const float Gravity = 16f;
    private const float FloorY = 0.12f;

    private struct Cube
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 Spin;
        public Vector3 Angle;
        public float Size;
    }

    private Cube[] _cubes = System.Array.Empty<Cube>();

    /// <summary>The MultiMesh buffer (12 floats of transform + 4 of colour per cube),
    /// uploaded in one call a frame instead of one call per cube.</summary>
    private float[] _buffer = System.Array.Empty<float>();
    private const int Stride = 16;
    private float _t;
    private static readonly RandomNumberGenerator Rng = new();
    private static BoxMesh? _box;
    private static StandardMaterial3D? _material;

    private static MultiMesh NewMultiMesh(int count)
    {
        _box ??= new BoxMesh { Size = Vector3.One };
        _material ??= new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f };
        _box.Material = _material;
        return new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = _box,
            InstanceCount = count,
        };
    }

    /// <summary>
    /// The cube centres and colours of a voxel model, extracted once per mesh
    /// (docs/design/performance.md -> "Mass deaths"). Reading a mesh back with
    /// <c>SurfaceGetArrays</c> copies its vertex buffer back from the GPU (the
    /// RenderingDevice waits for it), so doing it on every death made a pack dying
    /// at once stall the frame. The model frames never change, so the result is
    /// cached for the session.
    /// </summary>
    private static readonly Dictionary<ulong, ((Vector3 Point, Color Color)[] Points, Vector3 Centre)?> Shapes = new();

    private static ((Vector3 Point, Color Color)[] Points, Vector3 Centre)? CubesOf(Mesh model)
    {
        ulong key = model.GetInstanceId();
        if (Shapes.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var points = new List<(Vector3 Point, Color Color)>();
        for (int s = 0; s < model.GetSurfaceCount(); s++)
        {
            Color color = model.SurfaceGetMaterial(s) is BaseMaterial3D bm ? bm.AlbedoColor : Colors.Gray;
            var arrays = model.SurfaceGetArrays(s);
            if (arrays.Count == 0 || arrays[(int)Mesh.ArrayType.Vertex].Obj is not Vector3[] verts)
            {
                continue;
            }

            for (int v = 0; v + 3 < verts.Length; v += 4)
            {
                points.Add(((verts[v] + verts[v + 2]) / 2f, color));
            }
        }

        ((Vector3 Point, Color Color)[] Points, Vector3 Centre)? shape = null;
        if (points.Count > 0)
        {
            Vector3 centre = Vector3.Zero;
            foreach ((Vector3 p, _) in points)
            {
                centre += p;
            }

            shape = (points.ToArray(), centre / points.Count);
        }

        Shapes[key] = shape;
        return shape;
    }

    /// <summary>
    /// Breaks a voxel model (one frame of a monster) into cubes.
    /// <paramref name="power"/> scales how far they fly and how long they last:
    /// 1 is the full boss explosion, ordinary monsters crumble at well under half.
    /// </summary>
    public static void Burst(Node3D parent, Mesh model, Vector3 at, float scale, int count, float power = 1f)
    {
        if (CubesOf(model) is not { } shape)
        {
            return;
        }

        (Vector3 Point, Color Color)[] points = shape.Points;
        Vector3 centre = shape.Centre;
        var debris = new VoxelDebris { Multimesh = NewMultiMesh(count), CastShadow = ShadowCastingSetting.On };
        debris._cubes = new Cube[count];
        debris._buffer = new float[count * Stride];
        debris._life = Mathf.Lerp(0.7f, 1.6f, Mathf.Clamp(power, 0f, 1f));
        for (int i = 0; i < count; i++)
        {
            (Vector3 p, Color c) = points[Rng.RandiRange(0, points.Length - 1)];
            debris._cubes[i] = NewCube(p, centre, at, scale, power);
            debris.SetColor(i, c);
        }

        parent.AddChild(debris);
    }

    /// <summary>
    /// Several crumbles in one MultiMesh: each model gets <paramref name="countEach"/>
    /// cubes exactly as <see cref="Burst"/> would give it, but the frame's deaths
    /// share one node and one draw call (docs/design/performance.md -> "Mass deaths").
    /// </summary>
    public static void BurstMany(Node3D parent, List<(Mesh Model, Vector3 At, float Scale)> bursts, int countEach, float power)
    {
        int total = 0;
        foreach ((Mesh model, _, _) in bursts)
        {
            if (CubesOf(model) is not null)
            {
                total += countEach;
            }
        }

        if (total == 0)
        {
            return;
        }

        var debris = new VoxelDebris { Multimesh = NewMultiMesh(total), CastShadow = ShadowCastingSetting.On };
        debris._cubes = new Cube[total];
        debris._buffer = new float[total * Stride];
        debris._life = Mathf.Lerp(0.7f, 1.6f, Mathf.Clamp(power, 0f, 1f));
        int i = 0;
        foreach ((Mesh model, Vector3 at, float scale) in bursts)
        {
            if (CubesOf(model) is not { } shape)
            {
                continue;
            }

            for (int k = 0; k < countEach; k++, i++)
            {
                (Vector3 p, Color c) = shape.Points[Rng.RandiRange(0, shape.Points.Length - 1)];
                debris._cubes[i] = NewCube(p, shape.Centre, at, scale, power);
                debris.SetColor(i, c);
            }
        }

        parent.AddChild(debris);
    }

    /// <summary>
    /// Cubes from ready-made shapes (points and colours in model space, 1/16 m per
    /// pixel), all bursts of a frame in one MultiMesh — what <see cref="DeathBurst"/>
    /// uses for flat figures, whose shape is the pixels of their 2D frame.
    /// <paramref name="cubeSize"/> is in metres before <c>Scale</c> (one art pixel is 1/16 m).
    /// </summary>
    public static void BurstShapes(Node3D parent, List<((Vector3 Point, Color Color)[] Points, Vector3 Centre, Vector3 At, float Scale, int Count)> bursts,
        float power, float life, float cubeSize)
    {
        int total = 0;
        foreach (var b in bursts)
        {
            total += b.Points.Length > 0 ? b.Count : 0;
        }

        if (total == 0)
        {
            return;
        }

        var debris = new VoxelDebris { Multimesh = NewMultiMesh(total), CastShadow = ShadowCastingSetting.On };
        debris._cubes = new Cube[total];
        debris._buffer = new float[total * Stride];
        debris._life = life;
        int i = 0;
        foreach (var b in bursts)
        {
            if (b.Points.Length == 0)
            {
                continue;
            }

            for (int k = 0; k < b.Count; k++, i++)
            {
                (Vector3 p, Color c) = b.Points[Rng.RandiRange(0, b.Points.Length - 1)];
                Cube cube = NewCube(p, b.Centre, b.At, b.Scale, power);
                cube.Size = cubeSize * b.Scale * Rng.RandfRange(0.85f, 1.15f);
                debris._cubes[i] = cube;
                debris.SetColor(i, c);
            }
        }

        parent.AddChild(debris);
    }

    private static Cube NewCube(Vector3 p, Vector3 centre, Vector3 at, float scale, float power)
    {
        Vector3 outward = (p - centre).Normalized();
        if (outward == Vector3.Zero)
        {
            outward = Vector3.Up;
        }

        return new Cube
        {
            Position = at + p * scale,
            Velocity = (outward * Rng.RandfRange(2f, 5.5f) + new Vector3(0f, Rng.RandfRange(2.5f, 6f), 0f)) * Mathf.Sqrt(scale) * power,
            Spin = new Vector3(Rng.RandfRange(-9f, 9f), Rng.RandfRange(-9f, 9f), Rng.RandfRange(-9f, 9f)),
            Size = Rng.RandfRange(0.12f, 0.2f) * scale,
        };
    }

    /// <summary>Floor cubes kicked up where a heavy hit lands.</summary>
    public static void Puff(Node3D parent, Vector3 at, float radius, Color tint, int count)
    {
        var debris = new VoxelDebris { Multimesh = NewMultiMesh(count), CastShadow = ShadowCastingSetting.Off };
        debris._cubes = new Cube[count];
        debris._buffer = new float[count * Stride];
        for (int i = 0; i < count; i++)
        {
            float a = Rng.Randf() * Mathf.Tau;
            float r = Mathf.Sqrt(Rng.Randf()) * radius;
            var offset = new Vector3(Mathf.Cos(a) * r, 0.1f, Mathf.Sin(a) * r);
            debris._cubes[i] = new Cube
            {
                Position = at + offset,
                Velocity = new Vector3(offset.X * 1.5f, Rng.RandfRange(2f, 5f), offset.Z * 1.5f),
                Spin = new Vector3(Rng.RandfRange(-8f, 8f), Rng.RandfRange(-8f, 8f), Rng.RandfRange(-8f, 8f)),
                Size = Rng.RandfRange(0.1f, 0.18f),
            };
            Color c = tint.Lerp(new Color(0.25f, 0.22f, 0.24f), Rng.Randf() * 0.6f);
            debris.SetColor(i, c);
        }

        parent.AddChild(debris);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        if (_t >= _life)
        {
            QueueFree();
            return;
        }

        float shrink = Mathf.Clamp((_life - _t) / 0.6f, 0f, 1f);
        for (int i = 0; i < _cubes.Length; i++)
        {
            ref Cube c = ref _cubes[i];
            c.Velocity.Y -= Gravity * dt;
            c.Position += c.Velocity * dt;
            if (c.Position.Y < FloorY)
            {
                c.Position.Y = FloorY;
                c.Velocity = new Vector3(c.Velocity.X * 0.6f, -c.Velocity.Y * 0.35f, c.Velocity.Z * 0.6f);
                c.Spin *= 0.6f;
            }

            c.Angle += c.Spin * dt;
            Basis b = Basis.FromEuler(c.Angle).Scaled(Vector3.One * c.Size * shrink);
            int o = i * Stride;
            _buffer[o] = b.X.X;
            _buffer[o + 1] = b.Y.X;
            _buffer[o + 2] = b.Z.X;
            _buffer[o + 3] = c.Position.X;
            _buffer[o + 4] = b.X.Y;
            _buffer[o + 5] = b.Y.Y;
            _buffer[o + 6] = b.Z.Y;
            _buffer[o + 7] = c.Position.Y;
            _buffer[o + 8] = b.X.Z;
            _buffer[o + 9] = b.Y.Z;
            _buffer[o + 10] = b.Z.Z;
            _buffer[o + 11] = c.Position.Z;
        }

        RenderingServer.MultimeshSetBuffer(Multimesh.GetRid(), _buffer);
    }

    private void SetColor(int i, Color c)
    {
        int o = i * Stride + 12;
        _buffer[o] = c.R;
        _buffer[o + 1] = c.G;
        _buffer[o + 2] = c.B;
        _buffer[o + 3] = c.A;
    }
}
