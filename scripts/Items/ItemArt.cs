#nullable enable
using System.Collections.Generic;
using Godot;

namespace ArpgGame.Items;

/// <summary>
/// Inventory art — docs/design/items.md -> "Item art". Item icons live in
/// assets/sprites/items/&lt;base id&gt;.png (16 px per inventory cell, drawn at 2x),
/// currency icons in assets/sprites/currency/&lt;id&gt;.png (16x16). Anything without
/// art falls back to the old coloured box with a name.
/// </summary>
public static class ItemArt
{
    private static readonly Dictionary<string, Texture2D?> Textures = new();
    private static readonly Dictionary<(ulong, int), Texture2D?> Scaled = new();

    public static Texture2D? ForBase(string baseId) => Load($"res://assets/sprites/items/{baseId}.png");

    public static Texture2D? ForItem(EquipmentItem item) => item.Base is { } b ? ForBase(b.Id) : null;

    public static Texture2D? ForCurrency(string id) => Load($"res://assets/sprites/currency/{id}.png");

    /// <summary>The first idle frame of a prop sprite (hideout decorations).</summary>
    public static Texture2D? ForProp(string spritePath) =>
        ResourceLoader.Exists($"res://assets/sprites/{spritePath}.tres")
            ? GD.Load<SpriteFrames>($"res://assets/sprites/{spritePath}.tres")?.GetFrameTexture("idle", 0)
            : null;

    /// <summary>A Mirror Shard of a tier (its colour band's pickup sprite).</summary>
    public static Texture2D? ForShard(int tier) => ForProp($"pickups/{World.RealityTiers.ShardSprite(tier)}");

    private static Texture2D? Load(string path)
    {
        if (!Textures.TryGetValue(path, out Texture2D? texture))
        {
            texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
            Textures[path] = texture;
        }

        return texture;
    }

    /// <summary>A crisp nearest-neighbour copy that fits in <paramref name="size"/> px
    /// (button icons draw at their native size, too small for 16 px art).</summary>
    public static Texture2D? Fit(Texture2D? source, int size)
    {
        if (source is null)
        {
            return null;
        }

        var key = (source.GetInstanceId(), size);
        if (!Scaled.TryGetValue(key, out Texture2D? result))
        {
            Image? image = source.GetImage();
            if (image is not null && !image.IsEmpty())
            {
                if (image.IsCompressed())
                {
                    image.Decompress();
                }

                int factor = Mathf.Max(1, size / Mathf.Max(image.GetWidth(), image.GetHeight()));
                image.Resize(image.GetWidth() * factor, image.GetHeight() * factor, Image.Interpolation.Nearest);
                result = ImageTexture.CreateFromImage(image);
            }

            Scaled[key] = result;
        }

        return result;
    }

    /// <summary>Draws <paramref name="texture"/> as large as it fits in <paramref name="rect"/>,
    /// at a whole-number scale when possible so pixels stay square.</summary>
    public static void DrawFitted(CanvasItem canvas, Texture2D texture, Rect2 rect, Color? modulate = null)
    {
        Vector2 size = texture.GetSize();
        float scale = Mathf.Min(rect.Size.X / size.X, rect.Size.Y / size.Y);
        if (scale >= 1f)
        {
            scale = Mathf.Floor(scale);
        }

        Vector2 drawn = size * scale;
        canvas.DrawTextureRect(texture, new Rect2(rect.GetCenter() - drawn / 2f, drawn), false, modulate ?? Colors.White);
    }
}
