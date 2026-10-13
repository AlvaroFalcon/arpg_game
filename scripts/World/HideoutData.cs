#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;

namespace ArpgGame.World;

/// <summary>One thing standing in the hideout town: a station (the Mirror, the
/// stash...) or a decoration. Positions are world pixels.</summary>
public sealed class PlacedProp
{
    public string Uid { get; set; } = "";
    public string Kind { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }

    [JsonIgnore]
    public Vector2 Position
    {
        get => new(X, Y);
        set => (X, Y) = (value.X, value.Y);
    }
}

/// <summary>
/// The account's hideout town as the player arranged it — docs/design/hideout.md.
/// Saved in the profile (shared by every character): where each prop stands,
/// which cells were repainted with bought floor tiles, and what is in storage
/// (bought but not placed). An empty record means "the town as designed":
/// <see cref="HideoutTown.EnsureDefaults"/> fills it in on first visit.
/// </summary>
public sealed class HideoutData
{
    public List<PlacedProp> Props { get; set; } = new();

    /// <summary>"x,y" cell → floor style id (see <see cref="HideoutCatalog.Floors"/>).</summary>
    public Dictionary<string, string> Floors { get; set; } = new();

    /// <summary>Decorations and floor tiles owned but not placed, by id.</summary>
    public Dictionary<string, int> Stock { get; set; } = new();

    public bool Initialized { get; set; }

    public static string Key(Vector2I cell) => $"{cell.X},{cell.Y}";

    public static Vector2I Cell(string key)
    {
        string[] parts = key.Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y) ? new Vector2I(x, y) : Vector2I.Zero;
    }

    public int StockOf(string id) => Stock.GetValueOrDefault(id);

    public void AddStock(string id, int n)
    {
        int left = StockOf(id) + n;
        if (left <= 0)
        {
            Stock.Remove(id);
        }
        else
        {
            Stock[id] = left;
        }
    }

    public PlacedProp? Find(string uid) => Props.FirstOrDefault(p => p.Uid == uid);
}
