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
        bool chance = m.Stat is StatId.IgniteChance or StatId.ShockChance;
        return m.Type switch
        {
            ModifierType.Flat when chance => $"+{Mathf.RoundToInt(m.Value * 100)}% {name}",
            ModifierType.Flat when m.Stat == StatId.CritMultiplier => $"+{Mathf.RoundToInt(m.Value * 100)}% {name}",
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
/// The shared passive tree — docs/design/classes.md -> "Passive tree": one tree,
/// three starting points (PoE's model: any class can travel anywhere, the start
/// only decides what is cheap). Built from data in code: an inner ring around
/// the centre, three themed branches per class sector ending in a notable and
/// usually a keystone, and a hybrid branch between each pair of classes.
/// Positions are generated, so adding a node is one line.
/// </summary>
public static class PassiveTree
{
    private static Dictionary<string, PassiveNode>? _nodes;

    public static IReadOnlyDictionary<string, PassiveNode> Nodes => _nodes ??= Build();

    public static PassiveNode? Get(string id) => Nodes.TryGetValue(id, out PassiveNode? n) ? n : null;

    private const SkillTags E = UpgradeCatalog.Elements;

    private static PassiveMod Inc(StatId s, float v, SkillTags c = SkillTags.None, SkillTags any = SkillTags.None) => new(s, ModifierType.Increased, v, c, any);
    private static PassiveMod Flat(StatId s, float v, SkillTags c = SkillTags.None) => new(s, ModifierType.Flat, v, c);

    private sealed record Branch(string Id, float AngleDeg, string Theme, PassiveMod[] Small, string NotableName, PassiveMod[] Notable, string? KeystoneId);

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

        // Class sectors: Arcanist top, Rogue bottom-right, Warrior bottom-left.
        var starts = new (string Class, float Angle, StatId Attr)[]
        {
            ("arcanist", -90f, StatId.Intelligence),
            ("rogue", 30f, StatId.Dexterity),
            ("warrior", 150f, StatId.Strength),
        };
        foreach ((string cls, float angle, StatId _) in starts)
        {
            Add(new PassiveNode { Id = $"start_{cls}", Name = ClassCatalog.Get(cls).Name, Kind = PassiveKind.Start, ClassId = cls, Position = Polar(angle, 150f) });
        }

        // Inner ring: 18 small nodes. Attribute by nearest sector; the nodes
        // halfway between sectors grant both neighbours' attributes.
        const int ringCount = 18;
        string Ring(int i) => $"ring_{(i + ringCount) % ringCount}";
        for (int i = 0; i < ringCount; i++)
        {
            float deg = -90f + i * 360f / ringCount;
            var mods = new List<PassiveMod>();
            foreach ((string _, float a, StatId attr) in starts)
            {
                float d = Mathf.Abs(Mathf.Wrap(deg - a, -180f, 180f));
                if (d <= 60f)
                {
                    mods.Add(Flat(attr, d < 30f ? 12f : 7f));
                }
            }

            if (i % 3 == 1)
            {
                mods.Add(Inc(StatId.MaxLife, 4f));
            }

            Add(new PassiveNode { Id = Ring(i), Name = "Attribute", Kind = PassiveKind.Small, Mods = mods, Position = Polar(deg, 250f) });
        }

        for (int i = 0; i < ringCount; i++)
        {
            Link(Ring(i), Ring(i + 1));
        }

        foreach ((string cls, float angle, StatId _) in starts)
        {
            int nearest = Mathf.RoundToInt((angle + 90f) / (360f / ringCount));
            Link($"start_{cls}", Ring(nearest - 1));
            Link($"start_{cls}", Ring(nearest));
            Link($"start_{cls}", Ring(nearest + 1));
        }

        var branches = new List<Branch>
        {
            // Arcanist (-90)
            new("elemental", -118f, "Elemental", new[] { Inc(StatId.Damage, 8f, any: E), Inc(StatId.Damage, 8f, any: E), Flat(StatId.IgniteChance, 0.04f), Inc(StatId.Damage, 10f, any: E) },
                "Elemental Mastery", new[] { Inc(StatId.Damage, 25f, any: E), Inc(StatId.CastSpeed, 8f) }, "avatar_of_fire"),
            new("spellcraft", -90f, "Spellcraft", new[] { Inc(StatId.Damage, 10f, SkillTags.Spell), Inc(StatId.CastSpeed, 5f), Inc(StatId.Damage, 10f, SkillTags.Spell), Inc(StatId.AreaOfEffect, 8f) },
                "Arcane Focus", new[] { Inc(StatId.Damage, 30f, SkillTags.Spell), Inc(StatId.CooldownRecovery, 10f) }, "echoing_mind"),
            new("wellspring", -62f, "Mana", new[] { Flat(StatId.MaxMana, 15f), Flat(StatId.ManaRegen, 2f), Flat(StatId.MaxMana, 15f), Inc(StatId.ManaCostMultiplier, -5f) },
                "Wellspring", new[] { Flat(StatId.ManaRegen, 6f), Inc(StatId.ManaCostMultiplier, -10f) }, "blood_magic"),
            // Rogue (30)
            new("precision", 2f, "Precision", new[] { Inc(StatId.CritChance, 20f), Flat(StatId.CritMultiplier, 0.1f), Inc(StatId.CritChance, 20f), Flat(StatId.CritMultiplier, 0.15f) },
                "Deadly Precision", new[] { Inc(StatId.CritChance, 50f), Flat(StatId.CritMultiplier, 0.3f) }, null),
            new("agility", 30f, "Agility", new[] { Inc(StatId.MovementSpeed, 4f), Inc(StatId.DodgeRecovery, 8f), Inc(StatId.MovementSpeed, 4f), Inc(StatId.AttackSpeed, 5f) },
                "Acrobatics", new[] { Flat(StatId.DodgeCharges, 1f), Inc(StatId.DodgeRecovery, 15f) }, "phase_dancer"),
            new("marksman", 58f, "Projectiles", new[] { Inc(StatId.Damage, 10f, SkillTags.Projectile), Inc(StatId.ProjectileSpeed, 8f), Inc(StatId.Damage, 10f, SkillTags.Projectile), Inc(StatId.AttackSpeed, 4f) },
                "Volley Master", new[] { Flat(StatId.ExtraProjectiles, 1f), Inc(StatId.Damage, 15f, SkillTags.Projectile) }, "chain_reaction"),
            // Warrior (150)
            new("brawn", 122f, "Brawn", new[] { Flat(StatId.MaxLife, 12f), Inc(StatId.MaxLife, 4f), Flat(StatId.MaxLife, 12f), Flat(StatId.LifeRegen, 1f) },
                "Unbreakable Hide", new[] { Inc(StatId.MaxLife, 10f), Flat(StatId.Armour, 60f) }, "vampiric_pact"),
            new("weapon_master", 150f, "Melee", new[] { Inc(StatId.Damage, 10f, SkillTags.Melee), Inc(StatId.AttackSpeed, 4f), Inc(StatId.Damage, 10f, SkillTags.Melee), Inc(StatId.AreaOfEffect, 6f) },
                "Butcher", new[] { Inc(StatId.Damage, 30f, SkillTags.Melee), Inc(StatId.AttackSpeed, 6f) }, "glass_cannon"),
            new("fortitude", 178f, "Fortitude", new[] { Flat(StatId.Armour, 40f), Flat(StatId.FireResistance, 8f), Flat(StatId.Armour, 40f), Flat(StatId.ColdResistance, 8f) },
                "Bulwark", new[] { Flat(StatId.FireResistance, 12f), Flat(StatId.ColdResistance, 12f), Flat(StatId.LightningResistance, 12f), Flat(StatId.Armour, 50f) }, "iron_will"),
        };

        // Hybrid branches between sectors (shorter, no start of their own).
        var hybrids = new List<Branch>
        {
            new("arcweaver", -30f, "Ailments", new[] { Flat(StatId.ShockChance, 0.05f), Flat(StatId.IgniteChance, 0.05f), Inc(StatId.SkillDuration, 10f) },
                "Static Chains", new[] { Flat(StatId.ExtraChains, 1f), Flat(StatId.ShockChance, 0.08f) }, null),
            new("duelist", 90f, "Duelist", new[] { Inc(StatId.AttackSpeed, 5f), Inc(StatId.Damage, 8f, SkillTags.Attack), Flat(StatId.LifeOnKill, 1f) },
                "Blade Dancer", new[] { Inc(StatId.AttackSpeed, 10f), Inc(StatId.CritChance, 30f), Flat(StatId.LifeOnKill, 2f) }, null),
            new("battlemage", 210f, "Battlemage", new[] { Inc(StatId.Damage, 6f), Flat(StatId.MaxLife, 8f), Flat(StatId.MaxMana, 8f) },
                "Elemental Ward", new[] { Flat(StatId.FireResistance, 15f), Flat(StatId.ColdResistance, 15f), Flat(StatId.LightningResistance, 15f) }, null),
        };

        // Utility side nodes hung off the ring between branches.
        var utility = new (int Ring, string Id, string Name, PassiveMod Mod)[]
        {
            (2, "u_insight", "Insight", Inc(StatId.ExperienceGain, 6f)),
            (5, "u_reach", "Reach", Inc(StatId.PickupRadius, 25f)),
            (8, "u_persistence", "Persistence", Inc(StatId.SkillDuration, 12f)),
            (11, "u_expanse", "Expanse", Inc(StatId.AreaOfEffect, 10f)),
            (14, "u_feast", "Feast", Flat(StatId.LifeOnKill, 1f)),
            (17, "u_momentum", "Momentum", Inc(StatId.CooldownRecovery, 8f)),
        };
        foreach ((int ring, string id, string name, PassiveMod mod) in utility)
        {
            float deg = -90f + ring * 360f / ringCount;
            Add(new PassiveNode { Id = id, Name = name, Kind = PassiveKind.Small, Mods = new[] { mod }, Position = Polar(deg, 310f) });
            Link(id, Ring(ring));
        }

        void BuildBranch(Branch b, float startRadius)
        {
            string prev = Ring(Mathf.RoundToInt((b.AngleDeg + 90f) / (360f / ringCount)));
            float r = startRadius;
            for (int i = 0; i < b.Small.Length; i++)
            {
                string id = $"{b.Id}_{i}";
                Add(new PassiveNode { Id = id, Name = b.Theme, Kind = PassiveKind.Small, Mods = new[] { b.Small[i] }, Position = Polar(b.AngleDeg + (i % 2 == 0 ? -3f : 3f), r) });
                Link(prev, id);
                prev = id;
                r += 62f;
            }

            string notable = $"{b.Id}_notable";
            Add(new PassiveNode { Id = notable, Name = b.NotableName, Kind = PassiveKind.Notable, Mods = b.Notable, Position = Polar(b.AngleDeg, r + 8f) });
            Link(prev, notable);
            if (b.KeystoneId is { } ks && UpgradeCatalog.Keystones.FirstOrDefault(k => k.Id == ks) is { } def)
            {
                string kid = $"ks_{ks}";
                Add(new PassiveNode { Id = kid, Name = def.Name, Kind = PassiveKind.Keystone, Keystone = ks, Position = Polar(b.AngleDeg, r + 88f) });
                Link(notable, kid);
            }
        }

        foreach (Branch b in branches)
        {
            BuildBranch(b, 330f);
        }

        foreach (Branch b in hybrids)
        {
            BuildBranch(b, 340f);
        }

        // Cross-links between neighbouring branch tips so the outer tree is a
        // web, not only spokes (PoE-style travel without going back to the ring).
        Link("elemental_2", "arcweaver_1");
        Link("wellspring_2", "arcweaver_1");
        Link("precision_2", "arcweaver_2");
        Link("marksman_2", "duelist_1");
        Link("brawn_2", "duelist_1");
        Link("fortitude_2", "battlemage_1");
        Link("elemental_3", "battlemage_2");
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

    /// <summary>Gold to refund one node: grows with level (docs/design/classes.md -> respec costs gold).</summary>
    public static long RefundCost(CharacterData c) => 5 + c.Level * 3;

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
