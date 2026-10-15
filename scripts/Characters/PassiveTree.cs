#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Progression;
using ArpgGame.Stats;

namespace ArpgGame.Characters;

public enum PassiveKind
{
    Start,
    Small,
    Notable,
    Keystone,
}

/// <summary>One stat line of a passive node.</summary>
public readonly record struct PassiveMod(StatId Stat, ModifierType Type, float Value, SkillTags Condition = SkillTags.None, SkillTags ConditionAny = SkillTags.None);

public sealed class PassiveNode
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public PassiveKind Kind { get; init; }
    public IReadOnlyList<PassiveMod> Mods { get; init; } = Array.Empty<PassiveMod>();

    /// <summary>For keystones: the <see cref="UpgradeCatalog.Keystones"/> id applied.</summary>
    public string? Keystone { get; init; }

    /// <summary>Class whose start this is (Start nodes only).</summary>
    public string? ClassId { get; init; }

    public Vector2 Position { get; set; }
    public List<string> Links { get; } = new();

    public string Describe()
    {
        if (Keystone is not null && UpgradeCatalog.Keystones.FirstOrDefault(k => k.Id == Keystone) is { } k)
        {
            return k.Description;
        }

        if (Kind == PassiveKind.Start)
        {
            return $"{ClassCatalog.Get(ClassId ?? "").Name} starting point";
        }

        return string.Join("\n", Mods.Select(PassiveText.Describe));
    }
}

/// <summary>Readable text for a passive stat line.</summary>
public static class PassiveText
{
    private static readonly Dictionary<StatId, string> Names = new()
    {
        { StatId.Damage, "Damage" }, { StatId.MaxLife, "maximum Life" }, { StatId.MaxMana, "maximum Mana" },
        { StatId.LifeRegen, "Life regenerated per second" }, { StatId.ManaRegen, "Mana regenerated per second" },
        { StatId.Armour, "Armour" }, { StatId.MovementSpeed, "Movement Speed" }, { StatId.AttackSpeed, "Attack Speed" },
        { StatId.CastSpeed, "Cast Speed" }, { StatId.CritChance, "Critical Strike Chance" }, { StatId.CritMultiplier, "Critical Strike Multiplier" },
        { StatId.CooldownRecovery, "Cooldown Recovery" }, { StatId.AreaOfEffect, "Area of Effect" }, { StatId.ProjectileSpeed, "Projectile Speed" },
        { StatId.PickupRadius, "Pickup Radius" }, { StatId.ExperienceGain, "Experience gained" }, { StatId.IgniteChance, "chance to Ignite" },
        { StatId.ShockChance, "chance to Shock" }, { StatId.SkillDuration, "Skill Duration" }, { StatId.LifeOnKill, "Life gained on kill" },
        { StatId.DodgeCharges, "Dodge charge" }, { StatId.DodgeRecovery, "Dodge recharge speed" }, { StatId.ExtraProjectiles, "Projectile" },
        { StatId.ExtraChains, "Chain" }, { StatId.ManaCostMultiplier, "Mana Cost of Skills" }, { StatId.Strength, "Strength" },
        { StatId.Dexterity, "Dexterity" }, { StatId.Intelligence, "Intelligence" }, { StatId.FireResistance, "Fire Resistance" },
        { StatId.ColdResistance, "Cold Resistance" }, { StatId.LightningResistance, "Lightning Resistance" }, { StatId.DamageTaken, "Damage taken" },
        { StatId.LifeLeech, "of Damage leeched as Life" }, { StatId.ManaOnKill, "Mana gained on kill" }, { StatId.FreezeChance, "chance to Freeze" },
        { StatId.ElementalDamageTaken, "Elemental Damage taken" }, { StatId.PhysicalDamageTaken, "Physical Damage taken" }, { StatId.ExtraPierce, "Pierce" },
    };

    public static string StatName(StatId stat) => Names.TryGetValue(stat, out string? n) ? n : stat.ToString();

    private static string TagWords(PassiveMod m)
    {
        if (m.ConditionAny == UpgradeCatalog.Elements)
        {
            return "Elemental ";
        }

        var words = new List<string>();
        foreach (SkillTags t in new[] { SkillTags.Fire, SkillTags.Cold, SkillTags.Lightning, SkillTags.Spell, SkillTags.Attack, SkillTags.Melee, SkillTags.Projectile, SkillTags.Area })
        {
            if ((m.Condition & t) != 0)
            {
                words.Add(t.ToString());
            }
        }

        return words.Count > 0 ? string.Join(" ", words) + " " : "";
    }

    public static string Describe(PassiveMod m)
    {
        string name = StatName(m.Stat);
        bool chance = m.Stat is StatId.IgniteChance or StatId.ShockChance or StatId.FreezeChance;
        return m.Type switch
        {
            ModifierType.Flat when chance => $"+{Mathf.RoundToInt(m.Value * 100)}% {name}",
            ModifierType.Flat when m.Stat == StatId.CritMultiplier => $"+{Mathf.RoundToInt(m.Value * 100)}% {name}",
            ModifierType.Flat when m.Stat == StatId.LifeLeech => $"{m.Value:0.#}% {name}",
            ModifierType.Flat when m.Stat == StatId.ExtraPierce => $"Projectiles Pierce {m.Value:0} additional {(m.Value > 1 ? "enemies" : "enemy")}",
            ModifierType.Flat when m.Stat is StatId.DodgeCharges or StatId.ExtraProjectiles or StatId.ExtraChains => $"+{m.Value:0} {name}{(m.Value > 1 ? "s" : "")}",
            ModifierType.Flat => $"+{Items.ItemTooltipText.Number(m.Value)} {TagWords(m)}{name}",
            ModifierType.Increased when m.Value < 0 => $"{-m.Value:0}% reduced {TagWords(m)}{name}",
            ModifierType.Increased => $"{m.Value:0}% increased {TagWords(m)}{name}",
            ModifierType.More when m.Value < 0 => $"{-m.Value:0}% less {TagWords(m)}{name}",
            _ => $"{m.Value:0}% more {TagWords(m)}{name}",
        };
    }
}

/// <summary>
/// The shared passive tree — docs/design/classes.md -> "Passive tree" (redesign
/// 2026-10-15). One tree, three starting points (PoE's model: any class can travel
/// anywhere, the start only decides what is cheap), about 340 nodes for 69 points,
/// so a character takes roughly a fifth of it and has to choose a road.
///
/// Shape, all generated from the data below (positions included):
/// - an inner ring of 24 small nodes around the starts (attributes, six utilities);
/// - 24 <b>clusters</b> ("wheels"): a notable in the middle of four themed small
///   nodes in a loop, entered from the side facing the centre, the notable on the
///   far side — reaching it costs three or four points inside the wheel;
/// - 12 clusters on a middle ring (each class's three plus a hybrid between each
///   pair of classes) and 12 on an outer ring, each outer one leading on to a
///   keystone;
/// - <b>travel nodes</b> between them (+4 to the region's attribute, +2/+2 on the
///   borders): four from the inner ring to a middle cluster, three between
///   neighbouring clusters and from a middle cluster out to its outer one, two to
///   a keystone.
/// Related themes sit near each other but never in the same wheel (projectile
/// damage in the middle, projectile speed and pierce outside it, Ricochet beyond).
/// </summary>
public static class PassiveTree
{
    private static Dictionary<string, PassiveNode>? _nodes;

    public static IReadOnlyDictionary<string, PassiveNode> Nodes => _nodes ??= Build();

    public static PassiveNode? Get(string id) => Nodes.TryGetValue(id, out PassiveNode? n) ? n : null;

    private const SkillTags E = UpgradeCatalog.Elements;

    private static PassiveMod Inc(StatId s, float v, SkillTags c = SkillTags.None, SkillTags any = SkillTags.None) => new(s, ModifierType.Increased, v, c, any);
    private static PassiveMod Flat(StatId s, float v, SkillTags c = SkillTags.None) => new(s, ModifierType.Flat, v, c);
    private static PassiveMod Less(StatId s, float v) => new(s, ModifierType.More, -v);

    // Radii of the layout (tree units; the panel scales them).
    private const float StartRadius = 110f;
    private const float RingRadius = 230f;
    private const float MiddleRadius = 520f;
    private const float OuterRadius = 860f;
    private const float WheelRadius = 58f;
    private const float KeystoneRadius = 1140f;

    /// <summary>Attribute granted by a travel node in a class's region.</summary>
    public const float TravelAttribute = 4f;

    /// <summary>A wheel: theme, angle, ring, four small nodes, the notable, and the keystone beyond it (outer ring only).</summary>
    private sealed record Cluster(string Id, float Angle, bool Outer, string Theme, PassiveMod[] Small, string NotableName, PassiveMod[] Notable, string? KeystoneId = null);

    /// <summary>
    /// The 24 clusters, in angle order per ring — THE place to change what the tree
    /// offers (docs/design/classes.md -> "Passive tree" mirrors it). Arcanist (int)
    /// at the top (−90°), Rogue (dex) bottom right (30°), Warrior (str) bottom left
    /// (150°); hybrids on the borders (−30°, 90°, 210°).
    /// </summary>
    private static readonly Cluster[] Clusters =
    {
        // --- middle ring ---
        new("spellcraft", -120f, false, "Spellcraft", new[] { Inc(StatId.Damage, 7f, SkillTags.Spell), Inc(StatId.CastSpeed, 3f), Inc(StatId.Damage, 7f, SkillTags.Spell), Inc(StatId.CastSpeed, 3f) },
            "Arcane Focus", new[] { Inc(StatId.Damage, 25f, SkillTags.Spell), Inc(StatId.CastSpeed, 6f) }),
        new("mana", -90f, false, "Mana", new[] { Flat(StatId.MaxMana, 12f), Flat(StatId.ManaRegen, 1.5f), Flat(StatId.MaxMana, 12f), Inc(StatId.ManaCostMultiplier, -4f) },
            "Wellspring", new[] { Flat(StatId.ManaRegen, 5f), Inc(StatId.ManaCostMultiplier, -8f), Flat(StatId.ManaOnKill, 2f) }),
        new("fire", -60f, false, "Fire", new[] { Inc(StatId.Damage, 7f, SkillTags.Fire), Flat(StatId.IgniteChance, 0.03f), Inc(StatId.Damage, 7f, SkillTags.Fire), Flat(StatId.IgniteChance, 0.03f) },
            "Pyromancer", new[] { Inc(StatId.Damage, 25f, SkillTags.Fire), Flat(StatId.IgniteChance, 0.08f) }),
        new("elemental", -30f, false, "Elemental", new[] { Inc(StatId.Damage, 6f, any: E), Inc(StatId.Damage, 6f, any: E), Inc(StatId.Damage, 6f, any: E), Inc(StatId.CastSpeed, 3f) },
            "Elemental Mastery", new[] { Inc(StatId.Damage, 20f, any: E), Inc(StatId.CastSpeed, 5f) }),
        new("projectiles", 0f, false, "Projectiles", new[] { Inc(StatId.Damage, 7f, SkillTags.Projectile), Inc(StatId.Damage, 7f, SkillTags.Projectile), Inc(StatId.AttackSpeed, 3f), Inc(StatId.Damage, 7f, SkillTags.Projectile) },
            "Volley Master", new[] { Flat(StatId.ExtraProjectiles, 1f), Inc(StatId.Damage, 15f, SkillTags.Projectile) }),
        new("precision", 30f, false, "Precision", new[] { Inc(StatId.CritChance, 15f), Flat(StatId.CritMultiplier, 0.08f), Inc(StatId.CritChance, 15f), Flat(StatId.CritMultiplier, 0.08f) },
            "Deadly Precision", new[] { Inc(StatId.CritChance, 40f), Flat(StatId.CritMultiplier, 0.25f) }),
        new("agility", 60f, false, "Agility", new[] { Inc(StatId.MovementSpeed, 3f), Inc(StatId.DodgeRecovery, 6f), Inc(StatId.MovementSpeed, 3f), Inc(StatId.DodgeRecovery, 6f) },
            "Acrobatics", new[] { Flat(StatId.DodgeCharges, 1f), Inc(StatId.MovementSpeed, 5f) }),
        new("duelist", 90f, false, "Duelist", new[] { Inc(StatId.AttackSpeed, 3f), Inc(StatId.Damage, 6f, SkillTags.Attack), Flat(StatId.LifeOnKill, 1f), Inc(StatId.Damage, 6f, SkillTags.Attack) },
            "Blade Dancer", new[] { Inc(StatId.AttackSpeed, 8f), Inc(StatId.CritChance, 25f), Flat(StatId.LifeOnKill, 2f) }),
        new("life", 120f, false, "Life", new[] { Flat(StatId.MaxLife, 10f), Inc(StatId.MaxLife, 3f), Flat(StatId.MaxLife, 10f), Inc(StatId.MaxLife, 3f) },
            "Unbreakable Hide", new[] { Inc(StatId.MaxLife, 10f), Flat(StatId.MaxLife, 30f) }),
        new("melee", 150f, false, "Melee", new[] { Inc(StatId.Damage, 7f, SkillTags.Melee), Inc(StatId.AttackSpeed, 3f), Inc(StatId.Damage, 7f, SkillTags.Melee), Inc(StatId.AttackSpeed, 3f) },
            "Butcher", new[] { Inc(StatId.Damage, 25f, SkillTags.Melee), Inc(StatId.AttackSpeed, 6f) }),
        new("armour", 180f, false, "Armour", new[] { Flat(StatId.Armour, 35f), Inc(StatId.Armour, 6f), Flat(StatId.Armour, 35f), Inc(StatId.Armour, 6f) },
            "Iron Skin", new[] { Inc(StatId.Armour, 20f), Less(StatId.DamageTaken, 4f) }),
        new("battlemage", 210f, false, "Battlemage", new[] { Inc(StatId.Damage, 5f), Flat(StatId.MaxLife, 8f), Flat(StatId.MaxMana, 8f), Inc(StatId.Damage, 5f) },
            "Battle Hardened", new[] { Inc(StatId.Damage, 15f), Inc(StatId.MaxLife, 6f) }),

        // --- outer ring (each leads on to a keystone) ---
        new("cold", -120f, true, "Cold", new[] { Inc(StatId.Damage, 7f, SkillTags.Cold), Flat(StatId.FreezeChance, 0.02f), Inc(StatId.Damage, 7f, SkillTags.Cold), Flat(StatId.FreezeChance, 0.02f) },
            "Frostbinder", new[] { Inc(StatId.Damage, 25f, SkillTags.Cold), Flat(StatId.FreezeChance, 0.06f) }, "avatar_of_frost"),
        new("arcana", -90f, true, "Arcana", new[] { Inc(StatId.Damage, 6f, SkillTags.Spell), Inc(StatId.CooldownRecovery, 4f), Inc(StatId.Damage, 6f, SkillTags.Spell), Inc(StatId.CooldownRecovery, 4f) },
            "Arcane Torrent", new[] { Inc(StatId.Damage, 20f, SkillTags.Spell), Inc(StatId.CooldownRecovery, 10f) }, "echoing_mind"),
        new("lightning", -60f, true, "Lightning", new[] { Inc(StatId.Damage, 7f, SkillTags.Lightning), Flat(StatId.ShockChance, 0.03f), Inc(StatId.Damage, 7f, SkillTags.Lightning), Flat(StatId.ShockChance, 0.03f) },
            "Stormcaller", new[] { Inc(StatId.Damage, 25f, SkillTags.Lightning), Flat(StatId.ExtraChains, 1f) }, "avatar_of_fire"),
        new("ailments", -30f, true, "Ailments", new[] { Flat(StatId.IgniteChance, 0.03f), Flat(StatId.ShockChance, 0.03f), Flat(StatId.FreezeChance, 0.02f), Inc(StatId.SkillDuration, 6f) },
            "Static Chains", new[] { Flat(StatId.ExtraChains, 1f), Flat(StatId.ShockChance, 0.06f) }, "chain_reaction"),
        new("velocity", 0f, true, "Velocity", new[] { Inc(StatId.ProjectileSpeed, 8f), Inc(StatId.Damage, 5f, SkillTags.Projectile), Inc(StatId.ProjectileSpeed, 8f), Inc(StatId.Damage, 5f, SkillTags.Projectile) },
            "Piercing Shots", new[] { Flat(StatId.ExtraPierce, 1f), Inc(StatId.ProjectileSpeed, 15f) }, "ricochet"),
        new("flurry", 30f, true, "Flurry", new[] { Inc(StatId.AttackSpeed, 4f), Inc(StatId.Damage, 6f, SkillTags.Attack), Inc(StatId.AttackSpeed, 4f), Inc(StatId.Damage, 6f, SkillTags.Attack) },
            "Flurry", new[] { Inc(StatId.AttackSpeed, 10f), Inc(StatId.Damage, 15f, SkillTags.Attack) }, "glass_cannon"),
        new("shadow", 60f, true, "Shadow", new[] { Inc(StatId.DodgeRecovery, 8f), Inc(StatId.MovementSpeed, 3f), Inc(StatId.DodgeRecovery, 8f), Inc(StatId.MovementSpeed, 3f) },
            "Ghost Step", new[] { Inc(StatId.DodgeRecovery, 20f), Less(StatId.DamageTaken, 5f) }, "phase_dancer"),
        new("bloodthirst", 90f, true, "Bloodthirst", new[] { Flat(StatId.LifeLeech, 0.3f), Inc(StatId.Damage, 6f, SkillTags.Attack), Flat(StatId.LifeLeech, 0.3f), Inc(StatId.Damage, 6f, SkillTags.Attack) },
            "Bloodthirst", new[] { Flat(StatId.LifeLeech, 1f), Inc(StatId.Damage, 10f, SkillTags.Attack) }, "vampiric_pact"),
        new("regeneration", 120f, true, "Regeneration", new[] { Flat(StatId.LifeRegen, 1f), Flat(StatId.LifeOnKill, 1f), Flat(StatId.LifeRegen, 1f), Flat(StatId.LifeOnKill, 1f) },
            "Troll Blood", new[] { Flat(StatId.LifeRegen, 4f), Inc(StatId.MaxLife, 5f) }, "blood_magic"),
        new("earthshaker", 150f, true, "Earthshaker", new[] { Inc(StatId.AreaOfEffect, 5f), Inc(StatId.Damage, 6f, SkillTags.Melee), Inc(StatId.AreaOfEffect, 5f), Inc(StatId.Damage, 6f, SkillTags.Melee) },
            "Earthshaker", new[] { Inc(StatId.AreaOfEffect, 12f), Inc(StatId.Damage, 15f, SkillTags.Melee) }, "juggernaut"),
        new("resistance", 180f, true, "Resistance", new[] { Flat(StatId.FireResistance, 6f), Flat(StatId.ColdResistance, 6f), Flat(StatId.LightningResistance, 6f), Inc(StatId.MaxLife, 3f) },
            "Bulwark", new[] { Flat(StatId.FireResistance, 12f), Flat(StatId.ColdResistance, 12f), Flat(StatId.LightningResistance, 12f), Less(StatId.ElementalDamageTaken, 5f) }, "iron_will"),
        new("warding", 210f, true, "Warding", new[] { Flat(StatId.FireResistance, 5f), Flat(StatId.ColdResistance, 5f), Flat(StatId.LightningResistance, 5f), Less(StatId.ElementalDamageTaken, 2f) },
            "Elemental Ward", new[] { Flat(StatId.FireResistance, 12f), Flat(StatId.ColdResistance, 12f), Flat(StatId.LightningResistance, 12f), Less(StatId.ElementalDamageTaken, 6f) }, "prismatic_bulwark"),
    };

    /// <summary>Utilities on the inner ring (every fourth ring node, from the top).</summary>
    private static readonly (string Id, string Name, PassiveMod Mod)[] Utilities =
    {
        ("u_insight", "Insight", Inc(StatId.ExperienceGain, 6f)),
        ("u_reach", "Reach", Inc(StatId.PickupRadius, 25f)),
        ("u_persistence", "Persistence", Inc(StatId.SkillDuration, 10f)),
        ("u_expanse", "Expanse", Inc(StatId.AreaOfEffect, 8f)),
        ("u_feast", "Feast", Flat(StatId.LifeOnKill, 1f)),
        ("u_momentum", "Momentum", Inc(StatId.CooldownRecovery, 6f)),
    };

    private static readonly (string Class, float Angle, StatId Attr)[] Starts =
    {
        ("arcanist", -90f, StatId.Intelligence),
        ("rogue", 30f, StatId.Dexterity),
        ("warrior", 150f, StatId.Strength),
    };

    /// <summary>A travel node's attributes at this angle: its class's, or half of each neighbour's on a border.</summary>
    private static PassiveMod[] TravelMods(float angle, out string name)
    {
        var near = Starts
            .Select(s => (s.Attr, Dist: Mathf.Abs(Mathf.Wrap(angle - s.Angle, -180f, 180f))))
            .OrderBy(s => s.Dist)
            .ToArray();
        if (near[1].Dist - near[0].Dist < 20f)
        {
            name = $"{AttrName(near[0].Attr)} and {AttrName(near[1].Attr)}";
            return new[] { Flat(near[0].Attr, TravelAttribute / 2f), Flat(near[1].Attr, TravelAttribute / 2f) };
        }

        name = AttrName(near[0].Attr);
        return new[] { Flat(near[0].Attr, TravelAttribute) };
    }

    private static string AttrName(StatId s) => s switch { StatId.Strength => "Strength", StatId.Dexterity => "Dexterity", _ => "Intelligence" };

    private static Dictionary<string, PassiveNode> Build()
    {
        var nodes = new Dictionary<string, PassiveNode>();
        void Add(PassiveNode n) => nodes[n.Id] = n;
        void Link(string a, string b)
        {
            if (!nodes[a].Links.Contains(b)) nodes[a].Links.Add(b);
            if (!nodes[b].Links.Contains(a)) nodes[b].Links.Add(a);
        }

        Vector2 Polar(float deg, float r) => Vector2.Right.Rotated(Mathf.DegToRad(deg)) * r;
        float AngleOf(Vector2 p) => Mathf.RadToDeg(p.Angle());

        // A chain of travel nodes strictly between two points, linked end to end.
        int travel = 0;
        void Road(string from, string to, int count)
        {
            Vector2 a = nodes[from].Position;
            Vector2 b = nodes[to].Position;
            string prev = from;
            for (int i = 1; i <= count; i++)
            {
                Vector2 at = a.Lerp(b, i / (float)(count + 1));
                // Gentle outward bow on long sideways roads so they follow the rings.
                float bow = Mathf.Abs(a.Length() - b.Length()) < 40f ? Mathf.Sin(Mathf.Pi * i / (count + 1)) * 0.04f : 0f;
                at *= 1f + bow;
                string id = $"t{travel++}";
                PassiveMod[] mods = TravelMods(AngleOf(at), out string name);
                Add(new PassiveNode { Id = id, Name = name, Kind = PassiveKind.Small, Mods = mods, Position = at });
                Link(prev, id);
                prev = id;
            }

            Link(prev, to);
        }

        // Starts.
        foreach ((string cls, float angle, StatId _) in Starts)
        {
            Add(new PassiveNode { Id = $"start_{cls}", Name = ClassCatalog.Get(cls).Name, Kind = PassiveKind.Start, ClassId = cls, Position = Polar(angle, StartRadius) });
        }

        // Inner ring: 24 nodes every 15° from the top; every fourth is a utility.
        const int ringCount = 24;
        string Ring(int i) => $"ring_{((i % ringCount) + ringCount) % ringCount}";
        int RingAt(float deg) => Mathf.RoundToInt(Mathf.Wrap(deg + 90f, 0f, 360f) / (360f / ringCount)) % ringCount;
        for (int i = 0; i < ringCount; i++)
        {
            float deg = -90f + i * 360f / ringCount;
            if (i % 4 == 2)
            {
                (string uid, string uname, PassiveMod umod) = Utilities[i / 4];
                Add(new PassiveNode { Id = Ring(i), Name = uname, Kind = PassiveKind.Small, Mods = new[] { umod }, Position = Polar(deg, RingRadius) });
                continue;
            }

            PassiveMod[] attrs = TravelMods(deg, out string name);
            Add(new PassiveNode { Id = Ring(i), Name = name, Kind = PassiveKind.Small, Mods = attrs, Position = Polar(deg, RingRadius) });
        }

        for (int i = 0; i < ringCount; i++)
        {
            Link(Ring(i), Ring(i + 1));
        }

        foreach ((string cls, float angle, StatId _) in Starts)
        {
            int at = RingAt(angle);
            Link($"start_{cls}", Ring(at - 1));
            Link($"start_{cls}", Ring(at));
            Link($"start_{cls}", Ring(at + 1));
        }

        // Wheels: s0 faces the centre, s1 and s3 the sides, s2 outward; the smalls
        // loop and the notable hangs off s2 only, so it is always the far end (three
        // points in from s0, two from a side road).
        foreach (Cluster c in Clusters)
        {
            Vector2 centre = Polar(c.Angle, c.Outer ? OuterRadius : MiddleRadius);
            Vector2 radial = centre.Normalized();
            Vector2 tangent = new(-radial.Y, radial.X);
            Vector2[] offsets = { -radial, tangent, radial, -tangent };
            for (int i = 0; i < 4; i++)
            {
                Add(new PassiveNode { Id = $"{c.Id}_{i}", Name = c.Theme, Kind = PassiveKind.Small, Mods = new[] { c.Small[i] }, Position = centre + offsets[i] * WheelRadius });
            }

            for (int i = 0; i < 4; i++)
            {
                Link($"{c.Id}_{i}", $"{c.Id}_{(i + 1) % 4}");
            }

            Add(new PassiveNode { Id = $"{c.Id}_notable", Name = c.NotableName, Kind = PassiveKind.Notable, Mods = c.Notable, Position = centre + radial * (WheelRadius * 0.15f) });
            Link($"{c.Id}_notable", $"{c.Id}_2");

            if (c.KeystoneId is { } ks)
            {
                string name = UpgradeCatalog.Keystones.FirstOrDefault(k => k.Id == ks)?.Name ?? ks;
                Add(new PassiveNode { Id = $"ks_{ks}", Name = name, Kind = PassiveKind.Keystone, Keystone = ks, Position = Polar(c.Angle, KeystoneRadius) });
                Road($"{c.Id}_2", $"ks_{ks}", 2);
            }
        }

        // Roads: inner ring → middle wheels (4), middle → outer (3), neighbours on each ring (3).
        Cluster[] middle = Clusters.Where(c => !c.Outer).OrderBy(c => Mathf.Wrap(c.Angle, -180f, 180f)).ToArray();
        Cluster[] outer = Clusters.Where(c => c.Outer).OrderBy(c => Mathf.Wrap(c.Angle, -180f, 180f)).ToArray();
        foreach (Cluster c in middle)
        {
            Road(Ring(RingAt(c.Angle)), $"{c.Id}_0", 4);
            if (outer.FirstOrDefault(o => Mathf.IsEqualApprox(Mathf.Wrap(o.Angle - c.Angle, -180f, 180f), 0f)) is { } o)
            {
                Road($"{c.Id}_2", $"{o.Id}_0", 3);
            }
        }

        foreach (Cluster[] ring in new[] { middle, outer })
        {
            for (int i = 0; i < ring.Length; i++)
            {
                Cluster a = ring[i];
                Cluster b = ring[(i + 1) % ring.Length];
                Road($"{a.Id}_1", $"{b.Id}_3", 3);
            }
        }

        return nodes;
    }

    /// <summary>Whether <paramref name="id"/> can be allocated next (adjacent to the allocated set).</summary>
    public static bool CanAllocate(CharacterData c, string id) =>
        !c.Passives.Contains(id) && Get(id) is { Kind: not PassiveKind.Start } node && node.Links.Any(c.Passives.Contains);

    /// <summary>
    /// Whether removing <paramref name="id"/> keeps every other allocated node
    /// connected to the character's start (a refund must never orphan nodes).
    /// </summary>
    public static bool CanRefund(CharacterData c, string id)
    {
        if (!c.Passives.Contains(id) || id.StartsWith("start_"))
        {
            return false;
        }

        var remaining = new HashSet<string>(c.Passives);
        remaining.Remove(id);
        var seen = new HashSet<string> { c.Class.TreeStart };
        var queue = new Queue<string>(seen);
        while (queue.Count > 0)
        {
            foreach (string next in Nodes[queue.Dequeue()].Links)
            {
                if (remaining.Contains(next) && seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return remaining.All(seen.Contains);
    }

    /// <summary>Gold to refund one node. Free while the tree is being play-tested
    /// (owner, 2026-10-15); docs/design/classes.md -> "Passive tree" has the old
    /// level-based price to restore later.</summary>
    public static long RefundCost(CharacterData c) => 0;

    /// <summary>
    /// The cheapest chain of unallocated nodes that would connect <paramref name="id"/>
    /// to the character's allocated set (breadth-first: every node costs one point),
    /// ending with <paramref name="id"/> itself, in allocation order. Empty if the
    /// node is taken, a start, or unreachable.
    /// </summary>
    public static List<string> PathTo(CharacterData c, string id)
    {
        var path = new List<string>();
        if (c.Passives.Contains(id) || Get(id) is not { } target || target.Kind == PassiveKind.Start)
        {
            return path;
        }

        var prev = new Dictionary<string, string?> { [id] = null };
        var queue = new Queue<string>();
        queue.Enqueue(id);
        while (queue.Count > 0)
        {
            string cur = queue.Dequeue();
            foreach (string next in Nodes[cur].Links)
            {
                if (prev.ContainsKey(next))
                {
                    continue;
                }

                if (c.Passives.Contains(next))
                {
                    // Reached the allocated set: walk back from cur to the target.
                    for (string? k = cur; k is not null; k = prev[k])
                    {
                        path.Add(k);
                    }

                    return path;
                }

                if (Nodes[next].Kind == PassiveKind.Start)
                {
                    continue; // another class's start is not a road
                }

                prev[next] = cur;
                queue.Enqueue(next);
            }
        }

        return path;
    }

    /// <summary>Refunds every allocated node (the class start stays). Free.</summary>
    public static void ResetAll(CharacterData c)
    {
        c.Passives.RemoveWhere(id => !id.StartsWith("start_"));
        c.Passives.Add(c.Class.TreeStart);
    }

    /// <summary>
    /// Bumped whenever node ids change. A character saved with an older tree
    /// gets every point back (owner: just reset them, no message).
    /// </summary>
    public const int Version = 2;

    /// <summary>Applies every allocated node to a build.</summary>
    public static void Apply(CharacterData c, IBuildContext build)
    {
        foreach (string id in c.Passives)
        {
            if (Get(id) is not { } node)
            {
                continue;
            }

            foreach (PassiveMod m in node.Mods)
            {
                build.Stats.Get(m.Stat).AddModifier(new StatModifier(m.Type, m.Value, build.Source, m.Condition, m.ConditionAny));
            }

            if (node.Keystone is { } ks && UpgradeCatalog.Keystones.FirstOrDefault(k => k.Id == ks) is { } def)
            {
                def.Apply(build);
            }
        }
    }
}
