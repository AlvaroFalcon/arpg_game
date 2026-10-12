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
    private const float Life = 1.6f;
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

    /// <summary>Breaks a voxel model (one frame of a monster) into cubes.</summary>
    public static void Burst(Node3D parent, Mesh model, Vector3 at, float scale, int count)
    {
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

        if (points.Count == 0)
        {
            return;
        }

        var debris = new VoxelDebris { Multimesh = NewMultiMesh(count), CastShadow = ShadowCastingSetting.On };
        debris._cubes = new Cube[count];
        Vector3 centre = Vector3.Zero;
        foreach ((Vector3 p, _) in points)
        {
            centre += p;
        }

        centre /= points.Count;
        for (int i = 0; i < count; i++)
        {
            (Vector3 p, Color c) = points[Rng.RandiRange(0, points.Count - 1)];
            Vector3 local = p * scale;
            Vector3 outward = (p - centre).Normalized();
            if (outward == Vector3.Zero)
            {
                outward = Vector3.Up;
            }

            debris._cubes[i] = new Cube
            {
                Position = at + local,
                Velocity = (outward * Rng.RandfRange(2f, 5.5f) + new Vector3(0f, Rng.RandfRange(2.5f, 6f), 0f)) * Mathf.Sqrt(scale),
                Spin = new Vector3(Rng.RandfRange(-9f, 9f), Rng.RandfRange(-9f, 9f), Rng.RandfRange(-9f, 9f)),
                Size = Rng.RandfRange(0.12f, 0.2f) * scale,
            };
            debris.Multimesh.SetInstanceColor(i, c);
        }

        parent.AddChild(debris);
    }

    /// <summary>Floor cubes kicked up where a heavy hit lands.</summary>
    public static void Puff(Node3D parent, Vector3 at, float radius, Color tint, int count)
    {
        var debris = new VoxelDebris { Multimesh = NewMultiMesh(count), CastShadow = ShadowCastingSetting.Off };
        debris._cubes = new Cube[count];
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
            debris.Multimesh.SetInstanceColor(i, c);
        }

        parent.AddChild(debris);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        if (_t >= Life)
        {
            QueueFree();
            return;
        }

        float shrink = Mathf.Clamp((Life - _t) / 0.6f, 0f, 1f);
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
            Basis basis = Basis.FromEuler(c.Angle).Scaled(Vector3.One * c.Size * shrink);
            Multimesh.SetInstanceTransform(i, new Transform3D(basis, c.Position));
        }
    }
}
