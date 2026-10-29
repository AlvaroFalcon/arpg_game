#nullable enable
using System.Collections.Generic;
using Godot;

namespace ArpgGame.View3D;

/// <summary>How a flat figure dies (docs/design/art-pipeline.md -> "HD-2D with flat figures").</summary>
public enum DeathBurstStyle
{
    /// <summary>The figure bursts into cubes of its own pixels, which fly, bounce and shrink away.</summary>
    Pixels,

    /// <summary>No burst: the figure just disappears.</summary>
    None,
}

/// <summary>Tuning of one kind of death burst.</summary>
/// <param name="Cubes">How many pixel cubes fly out.</param>
/// <param name="Power">How far and high they fly (1 = the full boss explosion).</param>
/// <param name="Life">Seconds until the last cube has shrunk away.</param>
/// <param name="CubePixels">Size of a cube, in art pixels (1 = exactly one pixel of the sprite).</param>
public sealed record DeathBurstPreset(int Cubes, float Power, float Life, float CubePixels);

/// <summary>
/// The death effect of flat figures, in one place so it is easy to change: pick a
/// <see cref="Style"/>, tune <see cref="Monster"/> / <see cref="Boss"/>, or replace
/// <see cref="Play"/> with another effect (an animation, particles…). A monster's
/// current 2D frame becomes the cubes' shape and colours; every death of a frame
/// shares one MultiMesh (docs/design/performance.md -> "Mass deaths").
/// </summary>
public static class DeathBurst
{
    public static DeathBurstStyle Style = DeathBurstStyle.Pixels;

    /// <summary>Ordinary monsters crumble: a few pixels that barely leave the ground.</summary>
    public static DeathBurstPreset Monster = new(Cubes: 18, Power: 0.4f, Life: 0.9f, CubePixels: 1.5f);

    /// <summary>Bosses explode into many pixels.</summary>
    public static DeathBurstPreset Boss = new(Cubes: 220, Power: 1f, Life: 1.6f, CubePixels: 1.5f);

    private const float PixelMetres = 1f / 16f;

    private static readonly Dictionary<ulong, ((Vector3 Point, Color Color)[] Points, Vector3 Centre)> Shapes = new();
    private static readonly Dictionary<DeathBurstPreset, List<((Vector3 Point, Color Color)[], Vector3, Vector3, float, int)>> Pending = new();
    private static Node3D? _parent;

    /// <summary>The frame a sprite shows now.</summary>
    public static Texture2D? FrameOf(AnimatedSprite2D sprite) =>
        sprite.SpriteFrames is { } frames && frames.HasAnimation(sprite.Animation) ? frames.GetFrameTexture(sprite.Animation, sprite.Frame) : null;

    /// <summary>Plays the death of a flat figure showing <paramref name="frame"/>, feet at <paramref name="at"/>.</summary>
    public static void Play(Node3D parent, Texture2D frame, Vector3 at, float scale, bool boss)
    {
        if (Style == DeathBurstStyle.None || ShapeOf(frame) is not { } shape)
        {
            return;
        }

        DeathBurstPreset preset = boss ? Boss : Monster;
        if (!Pending.TryGetValue(preset, out var list))
        {
            Pending[preset] = list = new();
        }

        if (_parent is null)
        {
            Callable.From(Flush).CallDeferred();
        }

        _parent = parent;
        list.Add((shape.Points, shape.Centre, at, scale, preset.Cubes));
    }

    private static void Flush()
    {
        Node3D? parent = _parent;
        _parent = null;
        foreach ((DeathBurstPreset preset, var list) in Pending)
        {
            if (parent is not null && GodotObject.IsInstanceValid(parent) && list.Count > 0)
            {
                VoxelDebris.BurstShapes(parent, list, preset.Power, preset.Life, preset.CubePixels * PixelMetres);
            }

            list.Clear();
        }
    }

    /// <summary>The opaque pixels of a frame as points (feet at the origin, 1/16 m per
    /// pixel, standing in the XY plane) and their colours; cached per texture.</summary>
    private static ((Vector3 Point, Color Color)[] Points, Vector3 Centre)? ShapeOf(Texture2D frame)
    {
        ulong key = frame.GetInstanceId();
        if (Shapes.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Image? image = frame is AtlasTexture { Atlas: { } atlas } at
            ? atlas.GetImage()?.GetRegion(new Rect2I((Vector2I)at.Region.Position, (Vector2I)at.Region.Size))
            : frame.GetImage();
        if (image is null)
        {
            return null;
        }

        if (image.IsCompressed())
        {
            image.Decompress();
        }

        int w = image.GetWidth();
        int h = image.GetHeight();
        var points = new List<(Vector3 Point, Color Color)>();
        Vector3 centre = Vector3.Zero;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color c = image.GetPixel(x, y);
                if (c.A < 0.5f)
                {
                    continue;
                }

                var p = new Vector3((x - w / 2f) * PixelMetres, (h - y) * PixelMetres, 0f);
                points.Add((p, new Color(c.R, c.G, c.B)));
                centre += p;
            }
        }

        if (points.Count == 0)
        {
            return null;
        }

        var shape = (points.ToArray(), centre / points.Count);
        Shapes[key] = shape;
        return shape;
    }
}
