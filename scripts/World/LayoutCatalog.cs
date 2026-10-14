#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArpgGame.World;

public enum RoomShape
{
    Rect,
    Circle,
}

/// <summary>A room of a layout, in grid cells (one cell = 64 px).</summary>
public sealed class RoomDef
{
    public string Id { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public int W { get; init; }
    public int H { get; init; }
    public RoomShape Shape { get; init; } = RoomShape.Rect;

    /// <summary>Pack markers placed at random spots inside the room:
    /// 'p' normal pack, 'm' magic pack, 'r' rare pack, 'u' possible unique-rare.</summary>
    public string Packs { get; init; } = "";

    /// <summary>Wall pillars (1x1) scattered inside (obstacles to fight around).</summary>
    public int Pillars { get; init; }

    /// <summary>Hazard pools (non-walkable liquid) inside the room.</summary>
    public int Pools { get; init; }

    public Rect2I Rect => new(X, Y, W, H);
    public Vector2I Center => new(X + W / 2, Y + H / 2);
}

/// <summary>A corridor between two rooms. Corridors sharing a non-empty
/// <see cref="Group"/> are alternatives: one of them is built per reality.</summary>
public sealed class CorridorDef
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public int Width { get; init; } = 3;
    public string Group { get; init; } = "";

    /// <summary>True: go vertical first, then horizontal.</summary>
    public bool VerticalFirst { get; init; }

    public string Packs { get; init; } = "";
}

/// <summary>
/// A hand-designed reality layout — docs/design/endgame.md -> "Layout".
/// Authored as a graph of rooms and corridors (fixed positions and sizes, so a
/// layout is a recognisable place) and rasterised to a walkable grid at load,
/// with the details randomised per reality: which alternative corridors exist,
/// where packs, pillars and pools land inside each room. Authoring rooms
/// instead of drawing every cell keeps layouts connected by construction.
/// </summary>
public sealed class LayoutDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ThemeId { get; init; } = "";
    public string Entrance { get; init; } = "";
    public string BossRoom { get; init; } = "";
    public IReadOnlyList<RoomDef> Rooms { get; init; } = new List<RoomDef>();
    public IReadOnlyList<CorridorDef> Corridors { get; init; } = new List<CorridorDef>();

    /// <summary>Set for a generated cavern (CaveGenerator); rooms and corridors are then unused.</summary>
    public CaveStyle? Cave { get; init; }
}

public static class LayoutCatalog
{
    /// <summary>
    /// What the Mirror rolls: generated caverns, one style per theme —
    /// docs/design/endgame.md -> "Caverns". The hand-built room layouts
    /// (<see cref="RoomLayouts"/>) are kept for later and stay reachable by id.
    /// </summary>
    public static readonly IReadOnlyList<LayoutDef> Layouts = new[]
    {
        new LayoutDef
        {
            Id = "ashen_depths", Name = "The Ashen Depths", ThemeId = "ashen",
            // Wide caves with rivers of lava the path bridges over.
            Cave = new CaveStyle { Width = 98, Height = 62, Radius = 6.4f, Branches = 3, Bends = 6, Openness = 0.58f, Liquid = CaveLiquid.Rivers, LiquidAmount = 0.6f, PillarRate = 0.012f, PackSpots = 18, ArenaRadius = 8 },
        },
        new LayoutDef
        {
            Id = "bone_warrens", Name = "The Bone Warrens", ThemeId = "crypt",
            // Narrow, winding and branching, many pillars, small pools of ooze; climbs north.
            Cave = new CaveStyle { Width = 70, Height = 96, Vertical = true, Radius = 4.2f, Branches = 6, Bends = 8, Openness = 0.62f, Liquid = CaveLiquid.Pools, LiquidAmount = 0.4f, PillarRate = 0.04f, PackSpots = 19, ArenaRadius = 7 },
        },
        new LayoutDef
        {
            Id = "drowned_grotto", Name = "The Drowned Grotto", ThemeId = "drowned",
            // Broad lagoons with islands; the path runs along causeways.
            Cave = new CaveStyle { Width = 100, Height = 64, Radius = 7f, Branches = 2, Bends = 5, Openness = 0.62f, Liquid = CaveLiquid.Lagoons, LiquidAmount = 0.7f, PillarRate = 0.006f, PackSpots = 17, ArenaRadius = 8 },
        },
    };

    /// <summary>The hand-designed room-and-corridor layouts (the first generator), kept for reuse.</summary>
    public static readonly IReadOnlyList<LayoutDef> RoomLayouts = new[]
    {
        new LayoutDef
        {
            Id = "ashen_halls", Name = "The Ashen Halls", ThemeId = "ashen", Entrance = "gate", BossRoom = "heart",
            Rooms = new[]
            {
                new RoomDef { Id = "gate", X = 2, Y = 19, W = 8, H = 8 },
                new RoomDef { Id = "hall", X = 15, Y = 15, W = 13, H = 15, Packs = "ppm", Pillars = 4 },
                new RoomDef { Id = "north", X = 14, Y = 2, W = 13, H = 8, Packs = "pr" },
                new RoomDef { Id = "south", X = 14, Y = 35, W = 13, H = 8, Packs = "pmu", Pillars = 2 },
                new RoomDef { Id = "forge", X = 34, Y = 9, W = 14, H = 13, Packs = "rpp", Pillars = 3 },
                new RoomDef { Id = "ossuary", X = 34, Y = 31, W = 11, H = 10, Packs = "mp" },
                new RoomDef { Id = "ante", X = 52, Y = 21, W = 8, H = 8, Packs = "m" },
                new RoomDef { Id = "heart", X = 64, Y = 15, W = 17, H = 17, Shape = RoomShape.Circle },
            },
            Corridors = new[]
            {
                new CorridorDef { From = "gate", To = "hall", Width = 3, Packs = "p" },
                new CorridorDef { From = "hall", To = "north", Width = 3, VerticalFirst = true },
                new CorridorDef { From = "hall", To = "south", Width = 3, VerticalFirst = true },
                new CorridorDef { From = "north", To = "forge", Width = 3, Group = "a" },
                new CorridorDef { From = "hall", To = "forge", Width = 3, Group = "a", Packs = "m" },
                new CorridorDef { From = "south", To = "ossuary", Width = 3 },
                new CorridorDef { From = "forge", To = "ante", Width = 3, VerticalFirst = true, Group = "b" },
                new CorridorDef { From = "ossuary", To = "ante", Width = 3, VerticalFirst = true, Group = "b" },
                new CorridorDef { From = "forge", To = "ossuary", Width = 3, VerticalFirst = true, Packs = "p" },
                new CorridorDef { From = "ante", To = "heart", Width = 3 },
            },
        },
        new LayoutDef
        {
            Id = "bone_crypt", Name = "The Bone Crypt", ThemeId = "crypt", Entrance = "stairs", BossRoom = "sanctum",
            Rooms = new[]
            {
                new RoomDef { Id = "stairs", X = 30, Y = 62, W = 9, H = 7 },
                new RoomDef { Id = "nave_low", X = 28, Y = 44, W = 13, H = 12, Packs = "ppm", Pillars = 4 },
                new RoomDef { Id = "chapel_w", X = 8, Y = 42, W = 12, H = 10, Packs = "pr" },
                new RoomDef { Id = "chapel_e", X = 49, Y = 42, W = 12, H = 10, Packs = "pm" },
                new RoomDef { Id = "nave_high", X = 28, Y = 24, W = 13, H = 13, Packs = "rpu", Pillars = 4 },
                new RoomDef { Id = "catacomb_w", X = 6, Y = 20, W = 12, H = 14, Packs = "mpp", Pillars = 2 },
                new RoomDef { Id = "catacomb_e", X = 51, Y = 20, W = 12, H = 14, Packs = "ppm", Pillars = 2 },
                new RoomDef { Id = "vestibule", X = 30, Y = 13, W = 9, H = 6, Packs = "m" },
                new RoomDef { Id = "sanctum", X = 26, Y = 0, W = 17, H = 11, Shape = RoomShape.Rect, Pillars = 0 },
            },
            Corridors = new[]
            {
                new CorridorDef { From = "stairs", To = "nave_low", Width = 3 },
                new CorridorDef { From = "nave_low", To = "chapel_w", Width = 3 },
                new CorridorDef { From = "nave_low", To = "chapel_e", Width = 3 },
                new CorridorDef { From = "nave_low", To = "nave_high", Width = 4, Group = "a", Packs = "p" },
                new CorridorDef { From = "chapel_w", To = "catacomb_w", Width = 3, Group = "a" },
                new CorridorDef { From = "chapel_e", To = "catacomb_e", Width = 3 },
                new CorridorDef { From = "catacomb_w", To = "nave_high", Width = 3 },
                new CorridorDef { From = "catacomb_e", To = "nave_high", Width = 3, Group = "b" },
                new CorridorDef { From = "catacomb_e", To = "vestibule", Width = 3, VerticalFirst = true, Group = "b" },
                new CorridorDef { From = "nave_high", To = "vestibule", Width = 3 },
                new CorridorDef { From = "vestibule", To = "sanctum", Width = 3 },
            },
        },
        new LayoutDef
        {
            Id = "drowned_mirror", Name = "The Drowned Mirror", ThemeId = "drowned", Entrance = "shore", BossRoom = "reflection",
            Rooms = new[]
            {
                new RoomDef { Id = "shore", X = 1, Y = 28, W = 8, H = 8 },
                new RoomDef { Id = "baths", X = 14, Y = 24, W = 12, H = 16, Packs = "ppm", Pools = 2 },
                new RoomDef { Id = "cistern_n", X = 16, Y = 4, W = 14, H = 11, Packs = "pr", Pools = 1 },
                new RoomDef { Id = "cistern_s", X = 16, Y = 48, W = 14, H = 11, Packs = "pmu", Pools = 1 },
                new RoomDef { Id = "plaza", X = 33, Y = 22, W = 18, H = 20, Packs = "rppm", Pools = 3, Pillars = 2 },
                new RoomDef { Id = "gallery_n", X = 38, Y = 4, W = 14, H = 10, Packs = "mp" },
                new RoomDef { Id = "gallery_s", X = 38, Y = 50, W = 14, H = 10, Packs = "pp" },
                new RoomDef { Id = "threshold", X = 57, Y = 28, W = 8, H = 8, Packs = "m" },
                new RoomDef { Id = "reflection", X = 69, Y = 22, W = 19, H = 19, Shape = RoomShape.Circle },
            },
            Corridors = new[]
            {
                new CorridorDef { From = "shore", To = "baths", Width = 3 },
                new CorridorDef { From = "baths", To = "cistern_n", Width = 3, VerticalFirst = true },
                new CorridorDef { From = "baths", To = "cistern_s", Width = 3, VerticalFirst = true },
                new CorridorDef { From = "baths", To = "plaza", Width = 4, Group = "a" },
                new CorridorDef { From = "cistern_n", To = "gallery_n", Width = 3 },
                new CorridorDef { From = "cistern_s", To = "gallery_s", Width = 3 },
                new CorridorDef { From = "gallery_n", To = "plaza", Width = 3, VerticalFirst = true, Group = "a" },
                new CorridorDef { From = "gallery_s", To = "plaza", Width = 3, VerticalFirst = true },
                new CorridorDef { From = "plaza", To = "threshold", Width = 3, Packs = "p" },
                new CorridorDef { From = "gallery_n", To = "threshold", Width = 3, VerticalFirst = true, Group = "b" },
                new CorridorDef { From = "gallery_s", To = "threshold", Width = 3, VerticalFirst = true, Group = "b" },
                new CorridorDef { From = "threshold", To = "reflection", Width = 3 },
            },
        },
    };

    /// <summary>
    /// The introduction every new character plays once — docs/design/tutorial.md.
    /// Not in <see cref="Layouts"/>, so the Mirror never rolls it. A straight line
    /// of rooms, one lesson each: a quiet camp to learn to move, a long hall to
    /// sprint and dodge, a yard with a small pack, a cache with a magic pack, and
    /// a weakened guardian behind the gate.
    /// </summary>
    public static readonly LayoutDef Tutorial = new()
    {
        Id = "threshold", Name = "The Threshold", ThemeId = "ashen", Entrance = "camp", BossRoom = "seal",
        Rooms = new[]
        {
            new RoomDef { Id = "camp", X = 2, Y = 9, W = 8, H = 8 },
            new RoomDef { Id = "yard", X = 22, Y = 7, W = 11, H = 11, Packs = "p", Pillars = 2 },
            new RoomDef { Id = "cache", X = 38, Y = 8, W = 10, H = 9, Packs = "m" },
            new RoomDef { Id = "ante", X = 53, Y = 9, W = 7, H = 7 },
            new RoomDef { Id = "seal", X = 65, Y = 5, W = 15, H = 15, Shape = RoomShape.Circle },
        },
        Corridors = new[]
        {
            new CorridorDef { From = "camp", To = "yard", Width = 3 },
            new CorridorDef { From = "yard", To = "cache", Width = 3 },
            new CorridorDef { From = "cache", To = "ante", Width = 3 },
            new CorridorDef { From = "ante", To = "seal", Width = 3 },
        },
    };

    public static LayoutDef? Find(string id)
    {
        if (id == Tutorial.Id)
        {
            return Tutorial;
        }

        foreach (LayoutDef l in Layouts.Concat(RoomLayouts))
        {
            if (l.Id == id)
            {
                return l;
            }
        }

        return null;
    }
}
