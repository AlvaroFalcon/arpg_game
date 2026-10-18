#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ArpgGame.World;

/// <summary>
/// The host's town on the wire — docs/design/hideout.md -> "Co-op: whose town".
/// A guest shows the host's layout (props and floors) and never saves it as its
/// own. What arrives is checked like any other host data (docs/design/
/// multiplayer.md -> "A hostile host"): a bounded number of known props with
/// unique ids inside the town, each station once, floors only on walkable cells
/// with known styles, stock counts in range. Pure: no nodes, unit-checked.
/// </summary>
public static class HideoutLayoutRules
{
    public const int MaxProps = 400;
    public const int UidChars = 32;
    public const int MaxStock = 9999;

    /// <summary>The wire form: <see cref="HideoutData"/> without the account-only flags.</summary>
    private sealed class Wire
    {
        public List<PlacedProp> Props { get; set; } = new();
        public Dictionary<string, string> Floors { get; set; } = new();
        public Dictionary<string, int>? Stock { get; set; }
    }

    /// <summary>The layout as JSON: props and floors, plus the storage when
    /// <paramref name="withStock"/> (an editor's copy needs it; a visitor doesn't).</summary>
    public static string Serialize(HideoutData data, bool withStock) => JsonSerializer.Serialize(new Wire
    {
        Props = data.Props,
        Floors = data.Floors,
        Stock = withStock ? data.Stock : null,
    });

    /// <summary>A layout received from the network, made safe to build a town
    /// from; null when it can't be read or breaks a rule.</summary>
    public static HideoutData? SanitizeReceived(string json, int maxChars)
    {
        if (json.Length > maxChars)
        {
            return null;
        }

        Wire? wire;
        try
        {
            wire = JsonSerializer.Deserialize<Wire>(json);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return null;
        }

        if (wire?.Props is null || wire.Floors is null)
        {
            return null;
        }

        var data = new HideoutData { Initialized = true, GuideSeen = true };
        return Problem(wire, data) is null ? data : null;
    }

    /// <summary>Why <paramref name="wire"/> isn't a valid town (null: it is, and
    /// <paramref name="into"/> holds it).</summary>
    private static string? Problem(Wire wire, HideoutData into)
    {
        if (wire.Props.Count > MaxProps)
        {
            return "too many props";
        }

        Rect2 bounds = HideoutTown.Bounds;
        var uids = new HashSet<string>();
        var stations = new HashSet<string>();
        foreach (PlacedProp? p in wire.Props)
        {
            if (p is null || string.IsNullOrEmpty(p.Uid) || p.Uid.Length > UidChars || !uids.Add(p.Uid))
            {
                return "bad prop id";
            }

            if (p.Kind is null || HideoutCatalog.Prop(p.Kind) is not { } def)
            {
                return "unknown prop";
            }

            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !bounds.HasPoint(p.Position))
            {
                return "prop outside the town";
            }

            // Each station once, under its own id (the scene keeps one node per station).
            if (def.Station && (p.Uid != def.Id || !stations.Add(def.Id)))
            {
                return "bad station";
            }

            into.Props.Add(new PlacedProp { Uid = p.Uid, Kind = def.Id, Position = p.Position });
        }

        if (wire.Floors.Count > HideoutTown.Width * HideoutTown.Height)
        {
            return "too many floors";
        }

        foreach ((string key, string style) in wire.Floors)
        {
            if (key is null || key.Length > 8 || style is null || !ValidFloorKey(key) || HideoutCatalog.Floors.All(f => f.Id != style))
            {
                return "bad floor";
            }

            into.Floors[key] = style;
        }

        foreach ((string id, int count) in wire.Stock ?? new Dictionary<string, int>())
        {
            if (id is null || id.Length > Net.NetLimits.IdChars || count < 0 || count > MaxStock
                || (HideoutCatalog.Prop(id) is null && HideoutCatalog.Floors.All(f => f.Id != id)))
            {
                return "bad stock";
            }

            if (count > 0)
            {
                into.Stock[id] = count;
            }
        }

        // A station a newer version added (or one a broken host left out) stands where it is designed to.
        HideoutCatalog.EnsureDefaults(into);
        return null;
    }

    /// <summary>"x,y" naming a walkable cell of the town.</summary>
    public static bool ValidFloorKey(string key)
    {
        string[] parts = key.Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y)
            && HideoutData.Key(new Vector2I(x, y)) == key && HideoutTown.Walkable(new Vector2I(x, y));
    }
}
