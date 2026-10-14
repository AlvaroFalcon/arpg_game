#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArpgGame.World;

/// <summary>What a jewel socket improves (all of it applies inside Altered Realities).</summary>
public enum JewelStat
{
    Quantity,
    Rarity,
    ShardChance,
    ShardUpgrade,
    Density,
    PackRarity,
    Gold,
    BossGold,
    Orbs,
    Experience,
    BossItems,
    BossRarity,
}

public enum JewelNodeKind
{
    Heart,
    Small,
    Notable,
}

/// <summary>One socket of the Mirror's jewel tree.</summary>
public sealed class JewelNode
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public JewelNodeKind Kind { get; init; }
    public Vector2 Position { get; init; }
    public List<string> Links { get; } = new();
    public (JewelStat Stat, float Amount)[] Effects { get; init; } = System.Array.Empty<(JewelStat, float)>();

    /// <summary>The branch it belongs to (colour), -1 for the heart and bridges.</summary>
    public int Branch { get; init; } = -1;

    public string Describe() => string.Join("\n", Effects.Select(e => JewelTree.Text(e.Stat, e.Amount)));
}

/// <summary>The account's jewels: how many were ever found, and which sockets hold one.</summary>
public sealed class JewelData
{
    public int Found { get; set; }
    public HashSet<string> Socketed { get; set; } = new();

    public int Free => Mathf.Max(0, Found - Socketed.Count);
}

/// <summary>Everything the socketed jewels add up to.</summary>
public sealed class JewelBonuses
{
    private readonly Dictionary<JewelStat, float> _sum = new();

    public float this[JewelStat stat] => _sum.GetValueOrDefault(stat);

    public void Add(JewelStat stat, float amount) => _sum[stat] = this[stat] + amount;

    public static readonly JewelBonuses None = new();
}

/// <summary>
/// The Mirror's jewel tree — docs/design/endgame.md -> "Mirror jewels". 70 sockets:
/// the Mirror's Heart in the middle and six branches of eleven (a spine of small
/// sockets ending in a notable, a side path with its own notable, one dead-end
/// small), plus three bridges joining neighbouring branches. A jewel goes into a
/// socket next to one already filled; any jewel can be taken back out as long as
/// the rest stays connected (free — jewels are the scarce thing, not respecs).
/// Jewels drop from reality bosses: freely up to <see cref="EasyJewels"/> found,
/// then only from harder bosses (<see cref="RequiredTier"/>), until every socket
/// can be filled. Account-wide, like the Mirror itself.
/// </summary>
public static class JewelTree
{
    public const string HeartId = "heart";

    /// <summary>Jewels any boss can drop; past this, harder bosses only.</summary>
    public const int EasyJewels = 40;

    private static Dictionary<string, JewelNode>? _nodes;

    public static IReadOnlyDictionary<string, JewelNode> Nodes => _nodes ??= Build();

    /// <summary>Sockets that take a jewel (all but the heart): 69.</summary>
    public static int Sockets => Nodes.Count - 1;

    public static JewelNode? Get(string id) => Nodes.GetValueOrDefault(id);

    /// <summary>Lowest reality tier whose boss can drop the next jewel (0 = no more jewels).</summary>
    public static int RequiredTier(int found) => found >= Sockets ? 0 : found < EasyJewels ? 1 : found < 55 ? 12 : 17;

    /// <summary>Chance a reality boss of <paramref name="tier"/> drops a jewel.</summary>
    public static float DropChance(int found, int tier)
    {
        int required = RequiredTier(found);
        return required == 0 || tier < required ? 0f : 0.3f + 0.01f * tier;
    }

    public static bool IsActive(JewelData data, string id) => id == HeartId || data.Socketed.Contains(id);

    public static bool CanSocket(JewelData data, string id) =>
        Get(id) is { } node && node.Kind != JewelNodeKind.Heart && !data.Socketed.Contains(id) && data.Free > 0
        && node.Links.Any(l => IsActive(data, l));

    /// <summary>A jewel can come out if every other socketed jewel stays connected to the heart.</summary>
    public static bool CanUnsocket(JewelData data, string id)
    {
        if (!data.Socketed.Contains(id))
        {
            return false;
        }

        var rest = new HashSet<string>(data.Socketed);
        rest.Remove(id);
        var seen = new HashSet<string> { HeartId };
        var queue = new Queue<string>(seen);
        while (queue.Count > 0)
        {
            foreach (string l in Nodes[queue.Dequeue()].Links)
            {
                if (rest.Contains(l) && seen.Add(l))
                {
                    queue.Enqueue(l);
                }
            }
        }

        return rest.All(seen.Contains);
    }

    public static bool Socket(JewelData data, string id) => CanSocket(data, id) && data.Socketed.Add(id);

    public static bool Unsocket(JewelData data, string id) => CanUnsocket(data, id) && data.Socketed.Remove(id);

    public static JewelBonuses Bonuses(JewelData? data)
    {
        if (data is null)
        {
            return JewelBonuses.None;
        }

        var bonuses = new JewelBonuses();
        foreach (string id in data.Socketed)
        {
            if (Get(id) is { } node)
            {
                foreach ((JewelStat stat, float amount) in node.Effects)
                {
                    bonuses.Add(stat, amount);
                }
            }
        }

        return bonuses;
    }

    public static string Text(JewelStat stat, float a) => stat switch
    {
        JewelStat.Quantity => $"{a * 100f:0}% increased item quantity",
        JewelStat.Rarity => $"{a * 100f:0}% increased item rarity",
        JewelStat.ShardChance => $"+{a * 100f:0}% chance of an extra boss shard",
        JewelStat.ShardUpgrade => $"+{a * 100f:0}% chance a boss shard is one tier higher",
        JewelStat.Density => $"{a * 100f:0}% more monsters",
        JewelStat.PackRarity => $"+{a * 100f:0}% chance for magic and rare packs",
        JewelStat.Gold => $"{a * 100f:0}% increased gold found",
        JewelStat.BossGold => $"Bosses drop {a * 100f:0}% more gold",
        JewelStat.Orbs => $"{a * 100f:0}% more crafting orbs",
        JewelStat.Experience => $"{a * 100f:0}% more character experience",
        JewelStat.BossItems => $"Bosses drop {a:0} extra item{(a == 1f ? "" : "s")}",
        JewelStat.BossRarity => $"Boss items roll {a * 100f:0}% rarer",
        _ => "",
    };

    /// <summary>One branch: the small socket stat, its notables, and the bridge stat.</summary>
    private sealed record BranchSpec(string Name, JewelStat Small, float SmallAmount, string Notable, (JewelStat, float)[] NotableEffects,
        string SideNotable, (JewelStat, float)[] SideEffects);

    public static readonly Color[] BranchColors =
    {
        new(0.92f, 0.92f, 0.95f), new(1f, 0.85f, 0.35f), new(0.45f, 0.7f, 1f),
        new(1f, 0.38f, 0.35f), new(1f, 0.65f, 0.25f), new(0.5f, 0.95f, 0.55f),
    };

    private static readonly BranchSpec[] Branches =
    {
        new("Abundance", JewelStat.Quantity, 0.03f, "Overflowing Glass", new[] { (JewelStat.Quantity, 0.10f) },
            "Hoarder's Reflection", new[] { (JewelStat.BossItems, 1f) }),
        new("Gilded Glass", JewelStat.Rarity, 0.05f, "Prismatic", new[] { (JewelStat.Rarity, 0.15f) },
            "Crowned Spoils", new[] { (JewelStat.BossRarity, 0.3f) }),
        new("Shardbound", JewelStat.ShardChance, 0.04f, "Shard-Sworn", new[] { (JewelStat.ShardChance, 0.2f) },
            "Ascending Echo", new[] { (JewelStat.ShardUpgrade, 0.15f) }),
        new("Teeming", JewelStat.Density, 0.03f, "Swarming Reflections", new[] { (JewelStat.Density, 0.10f) },
            "Dangerous Company", new[] { (JewelStat.PackRarity, 0.12f) }),
        new("Avarice", JewelStat.Gold, 0.06f, "Midas' Mirror", new[] { (JewelStat.Gold, 0.25f) },
            "Tithe of Kings", new[] { (JewelStat.BossGold, 1f) }),
        new("Artifice", JewelStat.Orbs, 0.05f, "Crucible", new[] { (JewelStat.Orbs, 0.2f) },
            "Insight", new[] { (JewelStat.Experience, 0.1f) }),
    };

    private static Dictionary<string, JewelNode> Build()
    {
        var nodes = new Dictionary<string, JewelNode>();
        void Add(JewelNode n) => nodes[n.Id] = n;
        void Link(string a, string b)
        {
            nodes[a].Links.Add(b);
            nodes[b].Links.Add(a);
        }

        Vector2 At(float angleDeg, float radius) => Vector2.Right.Rotated(Mathf.DegToRad(angleDeg)) * radius;

        Add(new JewelNode { Id = HeartId, Name = "The Mirror's Heart", Kind = JewelNodeKind.Heart, Position = Vector2.Zero });
        for (int b = 0; b < Branches.Length; b++)
        {
            BranchSpec spec = Branches[b];
            float angle = -90f + b * 60f;
            (JewelStat, float)[] small = { (spec.Small, spec.SmallAmount) };
            string prev = HeartId;
            for (int k = 1; k <= 6; k++)
            {
                string id = $"b{b}s{k}";
                Add(new JewelNode { Id = id, Name = spec.Name, Kind = JewelNodeKind.Small, Position = At(angle, 70f * k), Effects = small, Branch = b });
                Link(prev, id);
                prev = id;
            }

            Add(new JewelNode { Id = $"b{b}n", Name = spec.Notable, Kind = JewelNodeKind.Notable, Position = At(angle, 500f), Effects = spec.NotableEffects, Branch = b });
            Link(prev, $"b{b}n");

            // Side path off the third socket, ending in the second notable.
            Add(new JewelNode { Id = $"b{b}a1", Name = spec.Name, Kind = JewelNodeKind.Small, Position = At(angle + 13f, 265f), Effects = small, Branch = b });
            Add(new JewelNode { Id = $"b{b}a2", Name = spec.Name, Kind = JewelNodeKind.Small, Position = At(angle + 18f, 330f), Effects = small, Branch = b });
            Add(new JewelNode { Id = $"b{b}m", Name = spec.SideNotable, Kind = JewelNodeKind.Notable, Position = At(angle + 21f, 405f), Effects = spec.SideEffects, Branch = b });
            Link($"b{b}s3", $"b{b}a1");
            Link($"b{b}a1", $"b{b}a2");
            Link($"b{b}a2", $"b{b}m");

            // A dead-end small socket off the fifth.
            Add(new JewelNode { Id = $"b{b}x", Name = spec.Name, Kind = JewelNodeKind.Small, Position = At(angle - 11f, 375f), Effects = small, Branch = b });
            Link($"b{b}s5", $"b{b}x");
        }

        // Bridges between branch pairs (0-1, 2-3, 4-5): a mix of both.
        for (int b = 0; b < Branches.Length; b += 2)
        {
            BranchSpec l = Branches[b];
            BranchSpec r = Branches[b + 1];
            string id = $"bridge{b}";
            Add(new JewelNode
            {
                Id = id, Name = $"{l.Name} · {r.Name}", Kind = JewelNodeKind.Small, Position = At(-90f + b * 60f + 30f, 165f),
                Effects = new[] { (l.Small, l.SmallAmount * 0.6f), (r.Small, r.SmallAmount * 0.6f) },
            });
            Link($"b{b}s2", id);
            Link($"b{b + 1}s2", id);
        }

        return nodes;
    }
}
