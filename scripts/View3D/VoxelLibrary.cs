#nullable enable
using System.Collections.Generic;
using Godot;

namespace ArpgGame.View3D;

/// <summary>
/// The voxel models exported by tools/art/voxels.ts (EasyPixel's voxel export):
/// one .glb per sprite with a node per animation frame (<c>f&lt;index&gt;</c>),
/// one per tileset with its terrain pieces. Loaded on demand and cached.
/// </summary>
public static class VoxelLibrary
{
    private static readonly Dictionary<string, Dictionary<string, Mesh>?> Sets = new();

    /// <summary>All meshes of one .glb by node name, or null if it doesn't exist.</summary>
    public static Dictionary<string, Mesh>? Load(string path)
    {
        if (Sets.TryGetValue(path, out Dictionary<string, Mesh>? set))
        {
            return set;
        }

        set = null;
        if (ResourceLoader.Exists(path) && GD.Load<PackedScene>(path) is { } scene)
        {
            set = new Dictionary<string, Mesh>();
            Node root = scene.Instantiate();
            Collect(root, set);
            root.Free();
        }

        Sets[path] = set;
        return set;
    }

    private static void Collect(Node node, Dictionary<string, Mesh> into)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } mi)
        {
            into[mi.Name] = mesh;
        }

        foreach (Node child in node.GetChildren())
        {
            Collect(child, into);
        }
    }

    /// <summary>"res://assets/sprites/enemies/slime.tres" → "res://assets/voxels/enemies/slime.glb".</summary>
    public static string? ModelPathFor(SpriteFrames? frames)
    {
        string? path = frames?.ResourcePath;
        if (string.IsNullOrEmpty(path) || !path.StartsWith("res://assets/sprites/"))
        {
            return null;
        }

        return path.Replace("res://assets/sprites/", "res://assets/voxels/").Replace(".tres", ".glb");
    }

    /// <summary>
    /// The voxel mesh for the frame a 2D sprite is showing. Frames are matched
    /// through the AtlasTexture region: its cell in the EasyPixel spritesheet is
    /// the frame's index in the document, which is how the .glb names its nodes.
    /// </summary>
    public static Mesh? MeshFor(AnimatedSprite2D sprite) => MeshFor(sprite.SpriteFrames, sprite.Animation, sprite.Frame);

    /// <summary>One mesh per animation frame of a SpriteFrames, resolved once
    /// (docs/design/performance.md -> "Frame budget pass", I3): a lookup per frame
    /// built a model path and an "f{index}" string for every actor every frame.</summary>
    private static readonly Dictionary<ulong, Dictionary<StringName, Mesh?[]>?> FrameTables = new();

    /// <summary>The mesh of <paramref name="frame"/> of <paramref name="anim"/>, from the
    /// per-SpriteFrames table; null when there is no model or no such animation.</summary>
    public static Mesh? MeshFor(SpriteFrames? frames, StringName? anim, int frame) =>
        MeshesFor(frames, anim) is { Length: > 0 } meshes ? meshes[Mathf.Clamp(frame, 0, meshes.Length - 1)] : null;

    /// <summary>The meshes of every frame of <paramref name="anim"/> (index = frame), from the
    /// per-SpriteFrames table; null when there is no model or no such animation.</summary>
    public static Mesh?[]? MeshesFor(SpriteFrames? frames, StringName? anim)
    {
        if (frames is null || anim is null)
        {
            return null;
        }

        ulong id = frames.GetInstanceId();
        if (!FrameTables.TryGetValue(id, out Dictionary<StringName, Mesh?[]>? table))
        {
            FrameTables[id] = table = BuildFrameTable(frames);
        }

        return table is not null && table.TryGetValue(anim, out Mesh?[]? meshes) ? meshes : null;
    }

    private static Dictionary<StringName, Mesh?[]>? BuildFrameTable(SpriteFrames frames)
    {
        if (ModelPathFor(frames) is not { } path || Load(path) is null)
        {
            return null;
        }

        var table = new Dictionary<StringName, Mesh?[]>();
        foreach (string name in frames.GetAnimationNames())
        {
            var anim = new StringName(name);
            int count = frames.GetFrameCount(anim);
            var meshes = new Mesh?[count];
            for (int i = 0; i < count; i++)
            {
                meshes[i] = ResolveFrame(frames, anim, i);
            }

            table[anim] = meshes;
        }

        return table;
    }

    /// <summary>
    /// The lookup by name, as it ran every frame before the table: the region's
    /// cell index → node "f{index}", falling back to "f0". Kept as the reference
    /// the table is built from and checked against (EndgameSmokeTest
    /// VOXEL-FRAME-TABLE-MATCHES).
    /// </summary>
    public static Mesh? ResolveFrame(SpriteFrames? frames, StringName anim, int frame)
    {
        if (frames is null || ModelPathFor(frames) is not { } path || Load(path) is not { } set)
        {
            return null;
        }

        if (!frames.HasAnimation(anim) || frames.GetFrameCount(anim) == 0)
        {
            return null;
        }

        frame = Mathf.Clamp(frame, 0, frames.GetFrameCount(anim) - 1);
        if (frames.GetFrameTexture(anim, frame) is AtlasTexture { Atlas: { } atlas } at && at.Region.Size.X > 0f)
        {
            int cols = Mathf.Max(1, Mathf.RoundToInt(atlas.GetWidth() / at.Region.Size.X));
            int index = Mathf.RoundToInt(at.Region.Position.Y / at.Region.Size.Y) * cols + Mathf.RoundToInt(at.Region.Position.X / at.Region.Size.X);
            if (set.TryGetValue($"f{index}", out Mesh? mesh))
            {
                return mesh;
            }
        }

        return set.TryGetValue("f0", out Mesh? first) ? first : null;
    }

    /// <summary>"res://assets/tiles/bone_crypt_tileset.tres" → its terrain .glb.</summary>
    public static Dictionary<string, Mesh>? TerrainFor(TileSet? tileSet)
    {
        string? path = tileSet?.ResourcePath;
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        string name = path.GetFile().Replace("_tileset.tres", "").Replace(".tres", "");
        return Load($"res://assets/voxels/tiles/{name}.glb");
    }
}
