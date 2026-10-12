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
    public static Mesh? MeshFor(AnimatedSprite2D sprite)
    {
        if (sprite.SpriteFrames is not { } frames || ModelPathFor(frames) is not { } path || Load(path) is not { } set)
        {
            return null;
        }

        StringName anim = sprite.Animation;
        if (!frames.HasAnimation(anim) || frames.GetFrameCount(anim) == 0)
        {
            return null;
        }

        int frame = Mathf.Clamp(sprite.Frame, 0, frames.GetFrameCount(anim) - 1);
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
