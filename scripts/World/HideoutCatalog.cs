#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArpgGame.World;

/// <summary>A floor the hideout can be paved with (docs/design/hideout.md -> "Floors").</summary>
public sealed class FloorStyle
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Tileset { get; init; } = "";

    /// <summary>Atlas tiles; a cell picks one by its position, so large areas vary.</summary>
    public Vector2I[] Atlas { get; init; } = { Vector2I.Zero };

    /// <summary>Gold for a pack of <see cref="HideoutCatalog.FloorPack"/> tiles (0 = not sold).</summary>
    public long PackPrice { get; init; }

    public Color Swatch { get; init; } = Colors.Gray;
}

/// <summary>Something that can stand in the hideout: a station or a decoration.</summary>
public sealed class PropDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Sprite scene under assets/sprites (props/x, player/x), or "" for code-drawn.</summary>
    public string Sprite { get; init; } = "";
    public float Scale { get; init; } = 2f;

    /// <summary>Gold in the decorator's shop (0 = not sold).</summary>
    public long Price { get; init; }

    /// <summary>Collision radius in pixels (0 = walk-through).</summary>
    public float Radius { get; init; }

    /// <summary>Stations can be moved but never stored or sold.</summary>
    public bool Station { get; init; }

    /// <summary>What a station is for, in a few words (the town guide and the E prompt).</summary>
    public string Blurb { get; init; } = "";

    /// <summary>The verb on the E prompt ("Open", "Trade"...).</summary>
    public string Verb { get; init; } = "Use";

    /// <summary>Accent colour of the station in the HUD.</summary>
    public Color Accent { get; init; } = new(0.91f, 0.72f, 0.29f);

    /// <summary>Radius used to pick it with the mouse in edit mode.</summary>
    public float PickRadius { get; init; } = 26f;
}

/// <summary>
/// What the hideout town is made of — docs/design/hideout.md. Floors (the town's
/// own and the ones sold in packs), stations and decorations, and the town as
/// designed: <see cref="EnsureDefaults"/> places every station and the starting
/// decorations the first time an account visits.
/// </summary>
public static class HideoutCatalog
{
    public const int FloorPack = 10;

    private const string Town = "res://assets/tiles/town_tileset.tres";
    private const string TownFloors = "res://assets/tiles/town_floors_tileset.tres";

    private static readonly Vector2I[] Variants = { new(0, 0), new(1, 0), new(2, 0), new(3, 0) };

    public static readonly IReadOnlyList<FloorStyle> Floors = new FloorStyle[]
    {
        new() { Id = "cobble", Name = "Cobblestone", Tileset = Town, Atlas = Variants, Swatch = new Color(0.5f, 0.47f, 0.42f) },
        new() { Id = "mosaic", Name = "Plaza mosaic", Tileset = Town, Atlas = new[] { new Vector2I(3, 1) }, PackPrice = 100, Swatch = new Color(0.85f, 0.69f, 0.28f) },
        new() { Id = "marble", Name = "Marble", Tileset = TownFloors, Atlas = new[] { new Vector2I(0, 0) }, PackPrice = 60, Swatch = new Color(0.89f, 0.88f, 0.85f) },
        new() { Id = "planks", Name = "Oak planks", Tileset = TownFloors, Atlas = new[] { new Vector2I(1, 0) }, PackPrice = 40, Swatch = new Color(0.6f, 0.42f, 0.23f) },
        new() { Id = "grass", Name = "Grass", Tileset = TownFloors, Atlas = new[] { new Vector2I(2, 0) }, PackPrice = 30, Swatch = new Color(0.31f, 0.54f, 0.2f) },
        new() { Id = "carpet", Name = "Royal carpet", Tileset = TownFloors, Atlas = new[] { new Vector2I(3, 0) }, PackPrice = 80, Swatch = new Color(0.54f, 0.12f, 0.16f) },
        new() { Id = "gold_mosaic", Name = "Gilded mosaic", Tileset = TownFloors, Atlas = new[] { new Vector2I(3, 1) }, PackPrice = 150, Swatch = new Color(0.85f, 0.66f, 0.23f) },
        new() { Id = "ashen", Name = "Ashen stone", Tileset = "res://assets/tiles/ashen_arena_tileset.tres", Atlas = Variants, PackPrice = 50, Swatch = new Color(0.2f, 0.17f, 0.2f) },
        new() { Id = "crypt", Name = "Crypt stone", Tileset = "res://assets/tiles/bone_crypt_tileset.tres", Atlas = Variants, PackPrice = 50, Swatch = new Color(0.17f, 0.2f, 0.23f) },
        new() { Id = "drowned", Name = "Drowned stone", Tileset = "res://assets/tiles/drowned_mirror_tileset.tres", Atlas = Variants, PackPrice = 50, Swatch = new Color(0.12f, 0.2f, 0.23f) },
        new() { Id = "water", Name = "Canal water", Tileset = Town, Atlas = new[] { new Vector2I(0, 1), new Vector2I(1, 1) }, Swatch = new Color(0.12f, 0.35f, 0.52f) },
        new() { Id = "wall", Name = "Wall", Tileset = Town, Atlas = new[] { new Vector2I(2, 1) }, Swatch = new Color(0.54f, 0.45f, 0.35f) },
    };

    public static FloorStyle Floor(string id) => Floors.FirstOrDefault(f => f.Id == id) ?? Floors[0];

    public static IEnumerable<FloorStyle> FloorsForSale => Floors.Where(f => f.PackPrice > 0);

    public static IEnumerable<string> Tilesets => Floors.Select(f => f.Tileset).Distinct();

    public static readonly IReadOnlyList<PropDef> Props = new PropDef[]
    {
        // Stations.
        new() { Id = "mirror", Name = "Mirror of Realities", Sprite = "props/mirror", Scale = 2.5f, Station = true, Radius = 18f, PickRadius = 40f,
            Verb = "Open realities", Blurb = "Spend Mirror Shards to step into an Altered Reality.", Accent = new Color(0.55f, 0.75f, 1f) },
        new() { Id = "merchant", Name = "Odran the Trader", Sprite = "props/market_stall", Station = true, PickRadius = 44f,
            Verb = "Trade", Blurb = "Sell gear, buy crafting shards and skill gems.", Accent = new Color(0.95f, 0.75f, 0.35f) },
        new() { Id = "arehucaz", Name = "Arehucaz", Sprite = "props/arehucaz", Station = true, Radius = 10f, PickRadius = 34f,
            Verb = "Deal", Blurb = "Sells Mirror Shards of tiers you have cleared. Five at a time.", Accent = new Color(0.68f, 0.45f, 1f) },
        new() { Id = "decorator", Name = "Ysolde's Furnishings", Sprite = "props/crate", Station = true, PickRadius = 36f,
            Verb = "Browse", Blurb = "Floor tiles and decorations for your town.", Accent = new Color(0.55f, 0.85f, 0.75f) },
        new() { Id = "forge", Name = "Artificer's Forge", Sprite = "props/forge", Station = true, Radius = 22f, PickRadius = 40f,
            Verb = "Craft", Blurb = "Disenchant gear into dust and forge items with a chosen modifier.", Accent = new Color(1f, 0.55f, 0.25f) },
        new() { Id = "escaldon", Name = "Escaldón", Sprite = "props/weapon_rack", Station = true, Radius = 10f, PickRadius = 36f,
            Verb = "Hone skills", Blurb = "Raises your skills' levels for gold and shapes each one with its upgrade tree.", Accent = new Color(0.4f, 0.85f, 0.75f) },
        new() { Id = "stash", Name = "Stash", Sprite = "props/stash_chest", Station = true, Radius = 16f, PickRadius = 30f,
            Verb = "Open stash", Blurb = "Storage shared by all your characters.", Accent = new Color(0.8f, 0.6f, 0.4f) },
        new() { Id = "altar", Name = "Altar of Ascension", Station = true, PickRadius = 40f,
            Verb = "Ascend", Blurb = "Choose an ascendancy and spend ascendancy points.", Accent = new Color(0.8f, 0.6f, 1f) },
        new() { Id = "portal", Name = "Survival Rift", Station = true, PickRadius = 40f,
            Verb = "Walk in", Blurb = "Endless waves: survive as long as you can.", Accent = new Color(0.75f, 0.5f, 1f) },
        // Decorations sold by Ysolde.
        new() { Id = "brazier", Name = "Brazier", Sprite = "props/brazier", Price = 120, Radius = 8f },
        new() { Id = "lamp_post", Name = "Lamp post", Sprite = "props/lamp_post", Price = 80, Radius = 5f },
        new() { Id = "banner_red", Name = "Crimson banner", Sprite = "props/banner_red", Price = 60, Radius = 4f },
        new() { Id = "banner_blue", Name = "Azure banner", Sprite = "props/banner_blue", Price = 60, Radius = 4f },
        new() { Id = "potted_plant", Name = "Potted plant", Sprite = "props/potted_plant", Price = 40, Radius = 6f },
        new() { Id = "crate", Name = "Crate", Sprite = "props/crate", Price = 25, Radius = 10f },
        new() { Id = "barrel", Name = "Barrel", Sprite = "props/barrel", Price = 25, Radius = 8f },
        new() { Id = "bench", Name = "Bench", Sprite = "props/bench", Price = 70 },
        new() { Id = "flower_bed", Name = "Flower bed", Sprite = "props/flower_bed", Price = 50 },
        new() { Id = "weapon_rack", Name = "Weapon rack", Sprite = "props/weapon_rack", Price = 90, Radius = 6f },
        new() { Id = "tree", Name = "Oak tree", Sprite = "props/tree", Price = 150, Radius = 10f, PickRadius = 34f },
        new() { Id = "well", Name = "Well", Sprite = "props/well", Price = 300, Radius = 18f, PickRadius = 34f },
        new() { Id = "statue", Name = "Hero statue", Sprite = "props/statue", Price = 400, Radius = 12f, PickRadius = 34f },
        new() { Id = "training_dummy", Name = "Training dummy", Sprite = "props/training_dummy", Price = 100, Radius = 0f },
    };

    public static PropDef? Prop(string id) => Props.FirstOrDefault(p => p.Id == id);

    public static IEnumerable<PropDef> Decorations => Props.Where(p => !p.Station);

    /// <summary>The town as designed, in cells: station → where it stands.</summary>
    private static readonly (string Kind, int X, int Y)[] DefaultLayout =
    {
        ("mirror", 25, 15), ("merchant", 30, 18), ("decorator", 20, 18), ("forge", 8, 12), ("stash", 40, 31),
        ("altar", 10, 31), ("portal", 25, 3), ("arehucaz", 34, 7), ("escaldon", 35, 21),
        ("training_dummy", 39, 12), ("training_dummy", 43, 14), ("training_dummy", 39, 17),
        ("brazier", 18, 9), ("brazier", 32, 9), ("brazier", 18, 21), ("brazier", 32, 21),
        ("lamp_post", 22, 6), ("lamp_post", 28, 6), ("lamp_post", 22, 26), ("lamp_post", 28, 26),
        ("banner_red", 15, 13), ("banner_red", 15, 17), ("weapon_rack", 12, 10), ("barrel", 5, 19), ("barrel", 6, 20), ("crate", 12, 19),
        ("tree", 21, 30), ("tree", 29, 30), ("well", 25, 31), ("flower_bed", 7, 26), ("flower_bed", 12, 26),
        ("bench", 23, 22), ("bench", 27, 22), ("banner_blue", 37, 26), ("banner_blue", 43, 26), ("potted_plant", 35, 29), ("statue", 9, 29),
    };

    /// <summary>Fills an empty hideout record with the town as designed, and adds
    /// any station a newer version introduced to an old record.</summary>
    public static void EnsureDefaults(HideoutData data)
    {
        if (!data.Initialized)
        {
            int n = 0;
            foreach ((string kind, int x, int y) in DefaultLayout)
            {
                PropDef def = Prop(kind)!;
                data.Props.Add(new PlacedProp { Uid = def.Station ? kind : $"d{++n}", Kind = kind, Position = HideoutTown.World(new Vector2I(x, y)) });
            }

            data.Initialized = true;
            return;
        }

        foreach ((string kind, int x, int y) in DefaultLayout.Where(d => Prop(d.Kind)!.Station))
        {
            if (data.Find(kind) is null)
            {
                data.Props.Add(new PlacedProp { Uid = kind, Kind = kind, Position = HideoutTown.World(new Vector2I(x, y)) });
            }
        }
    }

    /// <summary>A fresh unique id for a newly placed decoration.</summary>
    public static string NewUid(HideoutData data)
    {
        int n = data.Props.Count + 1;
        while (data.Find($"d{n}") is not null)
        {
            n++;
        }

        return $"d{n}";
    }
}
