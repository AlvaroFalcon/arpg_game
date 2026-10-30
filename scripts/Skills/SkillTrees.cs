#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ArpgGame.Combat;
using ArpgGame.Progression;

namespace ArpgGame.Skills;

public enum SkillTreeNodeKind
{
    /// <summary>A small step, usually with several ranks (5% / 10% / 25%).</summary>
    Lesser,

    /// <summary>A change to how the skill plays (pierce, explode, convert...). One rank.</summary>
    Notable,
}

/// <summary>One node of a skill's upgrade tree — docs/design/skills.md -> "Skill upgrade trees".</summary>
public sealed class SkillTreeNode
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public SkillTreeNodeKind Kind { get; init; }
    public int MaxRank { get; init; } = 1;

    /// <summary>What the node does at a rank (1..MaxRank), e.g. "16% more damage".</summary>
    public Func<int, string> Describe { get; init; } = _ => "";

    /// <summary>Applies the node at a rank to one cast.</summary>
    public Action<CastContext, int> Apply { get; init; } = (_, _) => { };

    /// <summary>Cast-modifier order (conversions 5, counts 10, repeats 20, multipliers 30).</summary>
    public int Priority { get; init; } = 30;

    /// <summary>The node this one hangs from (null = a branch's first node) and how many
    /// of its ranks must be spent first.</summary>
    public string? Requires { get; init; }
    public int RequiresRanks { get; init; } = 1;

    /// <summary>Nodes sharing a group exclude each other: a choice, not a stack.</summary>
    public string? Exclusive { get; init; }

    /// <summary>Where the node sits in the panel: branch (row) and depth (column).</summary>
    public int Branch { get; init; }
    public int Depth { get; init; }
}

/// <summary>A skill's tree: three branches, each a lesser node, a second lesser node and
/// one or two notables at the end (two = pick one).</summary>
public sealed class SkillTree
{
    public string SkillId { get; init; } = "";
    public string[] Branches { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SkillTreeNode> Nodes { get; init; } = Array.Empty<SkillTreeNode>();

    public SkillTreeNode? Node(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    public int TotalRanks => Nodes.Where(n => n.Exclusive is null).Sum(n => n.MaxRank) + Nodes.Where(n => n.Exclusive is not null).GroupBy(n => n.Exclusive).Count();
}

/// <summary>The cast transform of the allocated nodes of one gem.</summary>
public sealed class SkillTreeModifier : CastModifier
{
    private readonly SkillTreeNode _node;
    private readonly int _rank;

    public SkillTreeModifier(SkillTreeNode node, int rank)
    {
        _node = node;
        _rank = rank;
    }

    public override int Priority => _node.Priority;

    public override void Apply(CastContext context) => _node.Apply(context, _rank);
}

/// <summary>
/// Every skill's upgrade tree (owner's brief, 2026-10-30): skills rise in level for gold
/// at Escaldón, up to 20, and each level past the first is a point to spend in the skill's
/// own tree. Trees are sparse on purpose — three branches and some either/or notables, with
/// more ranks than points — so two builds of the same skill can differ. Some lesser nodes
/// take several ranks (8% / 16% / 25%); a deeper node needs a minimum of points in the one
/// before it. A first draft for every skill, to tune once played.
/// </summary>
public static class SkillTrees
{
    public const int MaxLevel = Characters.GemData.MaxLevel;

    /// <summary>Gold to raise a gem from <paramref name="level"/> to the next: 30 · 1.45^(level-1),
    /// so 30 for level 2, about 1,600 for level 11 and 24,000 for level 20 (about 77,000 in all).</summary>
    public static long LevelCost(int level) => level >= MaxLevel ? 0 : (long)Math.Round(30.0 * Math.Pow(1.45, level - 1) / 5.0) * 5;

    /// <summary>Gold to take back one rank: cheap, so trying things out is not punished.</summary>
    public static long RefundCost(int gemLevel) => 10L * Math.Max(1, gemLevel);

    // --- node helpers -------------------------------------------------------------------

    private static string Pct(float v) => $"{Math.Round(v * 100f)}%";

    private static SkillTreeNode Lesser(string id, string name, int branch, int depth, float[] values, Func<float, string> text, Action<CastContext, float> apply, string? requires = null, int priority = 30) => new()
    {
        Id = id, Name = name, Kind = SkillTreeNodeKind.Lesser, MaxRank = values.Length, Branch = branch, Depth = depth,
        Requires = requires, RequiresRanks = 2, Priority = priority,
        Describe = r => text(values[Math.Clamp(r, 1, values.Length) - 1]),
        Apply = (c, r) => apply(c, values[Math.Clamp(r, 1, values.Length) - 1]),
    };

    private static SkillTreeNode Notable(string id, string name, int branch, string text, Action<CastContext> apply, string requires, string? exclusive = null, int priority = 30) => new()
    {
        Id = id, Name = name, Kind = SkillTreeNodeKind.Notable, MaxRank = 1, Branch = branch, Depth = 2,
        Requires = requires, RequiresRanks = 2, Exclusive = exclusive, Priority = priority,
        Describe = _ => text, Apply = (c, _) => apply(c),
    };

    // Lesser nodes shared by many trees (ids are per tree, so ranks never leak between skills).
    private static SkillTreeNode Power(int branch) => Lesser("power", "Power", branch, 0, new[] { 0.08f, 0.16f, 0.25f },
        v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v);

    private static SkillTreeNode Haste(int branch, int depth, string? requires = null) => Lesser("haste", "Haste", branch, depth, new[] { 0.05f, 0.1f, 0.15f },
        v => $"{Pct(v)} faster to use", (c, v) => c.UseTimeMultiplier *= 1f - v, requires);

    private static SkillTreeNode Efficiency(int branch, int depth, string? requires = null) => Lesser("efficiency", "Efficiency", branch, depth, new[] { 0.08f, 0.16f, 0.25f },
        v => $"{Pct(v)} less mana cost", (c, v) => c.CostMultiplier *= 1f - v, requires);

    private static SkillTreeNode Patience(int branch, int depth, string? requires = null) => Lesser("patience", "Patience", branch, depth, new[] { 0.1f, 0.2f, 0.3f },
        v => $"{Pct(v)} shorter cooldown", (c, v) => c.CooldownMultiplier *= 1f - v, requires);

    private static SkillTreeNode Reach(int branch, int depth, string? requires = null) => Lesser("reach", "Reach", branch, depth, new[] { 0.1f, 0.2f, 0.3f },
        v => $"{Pct(v)} more area", (c, v) => c.AreaMultiplier *= 1f + v, requires);

    private static SkillTreeNode Velocity(int branch, int depth, string? requires = null) => Lesser("velocity", "Velocity", branch, depth, new[] { 0.1f, 0.2f, 0.35f },
        v => $"{Pct(v)} faster projectiles", (c, v) => { if (c.Projectile is { } p) { p.Speed *= 1f + v; } }, requires);

    private static SkillTreeNode Lingering(int branch, int depth, string? requires = null) => Lesser("lingering", "Lingering", branch, depth, new[] { 0.15f, 0.3f, 0.5f },
        v => $"{Pct(v)} longer duration", (c, v) => c.DurationMultiplier *= 1f + v, requires);

    private static SkillTreeNode Kindle(int branch, int depth, string? requires = null) => Lesser("kindle", "Kindle", branch, depth, new[] { 0.1f, 0.2f, 0.3f },
        v => $"+{Pct(v)} chance to ignite", (c, v) => c.IgniteChanceBonus += v, requires);

    private static SkillTreeNode Searing(int branch, int depth, string? requires = null) => Lesser("searing", "Searing", branch, depth, new[] { 0.2f, 0.4f, 0.7f },
        v => $"Ignites deal {Pct(v)} more damage", (c, v) => c.IgniteDamageMultiplier *= 1f + v, requires);

    private static SkillTreeNode Static(int branch, int depth, string? requires = null) => Lesser("static", "Static", branch, depth, new[] { 0.1f, 0.2f, 0.3f },
        v => $"+{Pct(v)} chance to shock", (c, v) => c.ShockChanceBonus += v, requires);

    private static SkillTreeNode Rime(int branch, int depth, string? requires = null) => Lesser("rime", "Rime", branch, depth, new[] { 0.05f, 0.1f, 0.15f },
        v => $"+{Pct(v)} chance to freeze", (c, v) => c.FreezeChanceBonus += v, requires);

    private static SkillTreeNode Laceration(int branch, int depth, string? requires = null) => Lesser("laceration", "Laceration", branch, depth, new[] { 0.1f, 0.2f, 0.3f },
        v => $"{Pct(v)} chance to cause bleeding", (c, v) => c.BleedChance += v, requires);

    // Notables shared by many trees.
    private static SkillTreeNode KillBlast(int branch, string requires, string? exclusive = null) => Notable("kill_blast", "Detonation", branch,
        "Kills burst for 30% of the hit around the body", c => c.KillBlastFraction = Math.Max(c.KillBlastFraction, 0.3f), requires, exclusive);

    private static SkillTreeNode Convert(string id, string name, int branch, SkillTags to, string requires, string exclusive)
    {
        string element = to switch { SkillTags.Fire => "fire", SkillTags.Cold => "cold", SkillTags.Lightning => "lightning", _ => "physical" };
        return to == SkillTags.None
            ? Notable(id, name, branch, "Converts the damage to physical: 15% more damage and +25% chance to cause bleeding",
                c => { UpgradeCatalog.Convert(c, SkillTags.None); c.DamageMultiplier *= 1.15f; c.BleedChance += 0.25f; }, requires, exclusive, 5)
            : Notable(id, name, branch, $"Converts the damage to {element}",
                c => UpgradeCatalog.Convert(c, to), requires, exclusive, 5);
    }

    private static SkillTreeNode Echo(int branch, string requires, string? exclusive = null) => Notable("echo", "Echo", branch,
        "The skill repeats once, 20% less damage", c => { c.Repeats += 1; c.DamageMultiplier *= 0.8f; }, requires, exclusive, 20);

    private static SkillTreeNode Brutal(int branch, string requires, string? exclusive = null) => Notable("brutal", "Overwhelm", branch,
        "35% more damage, 25% more mana cost", c => { c.DamageMultiplier *= 1.35f; c.CostMultiplier *= 1.25f; }, requires, exclusive);

    private static SkillTreeNode ExtraProjectile(int branch, string requires, string? exclusive = null) => Notable("volley", "Volley", branch,
        "+1 projectile, 10% less damage", c => { c.ExtraCount += 1; c.DamageMultiplier *= 0.9f; }, requires, exclusive, 10);

    private static SkillTreeNode Piercing(int branch, string requires, string? exclusive = null) => Notable("piercing", "Piercing", branch,
        "Projectiles pass through 2 more enemies", c => { if (c.Projectile is { } p) { p.Pierce += 2; } }, requires, exclusive, 10);

    private static SkillTreeNode Ricochet(int branch, string requires, string? exclusive = null) => Notable("ricochet", "Ricochet", branch,
        "Projectiles chain to 2 more enemies", c => { if (c.Projectile is { } p) { p.Chains += 2; } }, requires, exclusive, 10);

    private static SkillTreeNode Flurry(int branch, string requires, string? exclusive = null) => Notable("flurry", "Flurry", branch,
        "Strikes one more time, 15% less damage", c => { c.ExtraCount += 1; c.DamageMultiplier *= 0.85f; }, requires, exclusive, 10);

    private static SkillTreeNode Wide(int branch, string requires, string? exclusive = null) => Notable("wide", "Shockwave", branch,
        "40% more area, 10% less damage", c => { c.AreaMultiplier *= 1.4f; c.DamageMultiplier *= 0.9f; }, requires, exclusive);

    // --- the trees ------------------------------------------------------------------------

    private static SkillTree Tree(string skill, string[] branches, params SkillTreeNode[] nodes) => new() { SkillId = skill, Branches = branches, Nodes = nodes };

    public static readonly IReadOnlyDictionary<string, SkillTree> All = new[]
    {
        Tree("fireball", new[] { "Inferno", "Wildfire", "Form" },
            Power(0),
            Lesser("blast", "Wider Blast", 0, 1, new[] { 0.15f, 0.3f, 0.5f }, v => $"The blast is {Pct(v)} larger", (c, v) => { if (c.Projectile is { } p) { p.ExplodeRadius *= 1f + v; } }, "power"),
            Notable("firestorm", "Firestorm", 0, "The blast deals the full hit instead of half", c => { if (c.Projectile is { } p) { p.ExplodeDamageFraction = 1f; } }, "blast"),
            Kindle(1, 0),
            Searing(1, 1, "kindle"),
            KillBlast(1, "searing"),
            Velocity(2, 0),
            Efficiency(2, 1, "velocity"),
            ExtraProjectile(2, "efficiency", "fireball_form"),
            Convert("frostfire", "Frostfire", 2, SkillTags.Cold, "efficiency", "fireball_form")),

        Tree("spark", new[] { "Charge", "Storm", "Path" },
            Power(0),
            Static(0, 1, "power"),
            Ricochet(0, "static", "spark_charge"),
            Notable("arc_blast", "Arc Blast", 0, "Sparks burst on hit (40 px, 40% of the hit)", c => { if (c.Projectile is { } p) { p.ExplodeRadius = Math.Max(p.ExplodeRadius, 40f); p.ExplodeDamageFraction = 0.4f; } }, "static", "spark_charge"),
            Haste(1, 0),
            Efficiency(1, 1, "haste"),
            ExtraProjectile(1, "efficiency", "spark_storm"),
            Piercing(1, "efficiency", "spark_storm"),
            Lesser("bounce", "Rebound", 2, 0, new[] { 1f, 2f, 3f }, v => $"+{v:0} wall bounce{(v > 1f ? "s" : "")}", (c, v) => { if (c.Projectile is { } p) { p.Bounces += (int)v; } }, null, 10),
            Lesser("range", "Long Arc", 2, 1, new[] { 0.15f, 0.3f, 0.5f }, v => $"Sparks fly {Pct(v)} farther", (c, v) => { if (c.Projectile is { } p) { p.Range *= 1f + v; } }, "bounce"),
            Convert("grounded", "Grounded", 2, SkillTags.None, "range", "spark_path"),
            KillBlast(2, "range", "spark_path")),

        Tree("frost_shards", new[] { "Ice", "Volley", "Shatter" },
            Power(0),
            Rime(0, 1, "power"),
            Brutal(0, "rime"),
            Velocity(1, 0),
            Efficiency(1, 1, "velocity"),
            ExtraProjectile(1, "efficiency", "shards_volley"),
            Piercing(1, "efficiency", "shards_volley"),
            Haste(2, 0),
            Lesser("brittle", "Brittle", 2, 1, new[] { 0.1f, 0.2f, 0.3f }, v => $"{Pct(v)} more damage, shards fly {Pct(v / 2f)} slower", (c, v) => { c.DamageMultiplier *= 1f + v; if (c.Projectile is { } p) { p.Speed *= 1f - v / 2f; } }, "haste"),
            KillBlast(2, "brittle")),

        Tree("throwing_knives", new[] { "Edge", "Fan", "Venom" },
            Power(0),
            Haste(0, 1, "power"),
            Piercing(0, "haste", "knives_edge"),
            Ricochet(0, "haste", "knives_edge"),
            Velocity(1, 0),
            Lesser("spread", "Wide Fan", 1, 1, new[] { 0.1f, 0.2f, 0.3f }, v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, "velocity"),
            ExtraProjectile(1, "spread"),
            Laceration(2, 0),
            Efficiency(2, 1, "laceration"),
            Convert("poisoned_tips", "Elemental Tips", 2, SkillTags.Lightning, "efficiency", "knives_venom"),
            KillBlast(2, "efficiency", "knives_venom")),

        Tree("frost_nova", new[] { "Winter", "Reach", "Rhythm" },
            Power(0),
            Rime(0, 1, "power"),
            Brutal(0, "rime"),
            Reach(1, 0),
            Lesser("knock", "Gale", 1, 1, new[] { 0.1f, 0.2f, 0.3f }, v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, "reach"),
            Wide(1, "knock"),
            Patience(2, 0),
            Efficiency(2, 1, "patience"),
            Echo(2, "efficiency", "nova_rhythm"),
            Convert("static_nova", "Static Nova", 2, SkillTags.Lightning, "efficiency", "nova_rhythm")),

        Tree("meteor", new[] { "Impact", "Crater", "Sky" },
            Power(0),
            Kindle(0, 1, "power"),
            Brutal(0, "kindle"),
            Reach(1, 0),
            Searing(1, 1, "reach"),
            KillBlast(1, "searing"),
            Patience(2, 0),
            Efficiency(2, 1, "patience"),
            Echo(2, "efficiency", "meteor_sky"),
            Convert("comet", "Comet", 2, SkillTags.Cold, "efficiency", "meteor_sky")),

        Tree("blades", new[] { "Steel", "Storm", "Time" },
            Power(0),
            Laceration(0, 1, "power"),
            Brutal(0, "laceration"),
            Reach(1, 0),
            Lesser("more_blades", "Blade Count", 1, 1, new[] { 1f, 2f }, v => $"+{v:0} blade{(v > 1f ? "s" : "")}", (c, v) => c.ExtraCount += (int)v, "reach", 10),
            Convert("ember_blades", "Ember Blades", 1, SkillTags.Fire, "more_blades", "blades_storm"),
            Convert("frost_blades", "Frost Blades", 1, SkillTags.Cold, "more_blades", "blades_storm"),
            Lingering(2, 0),
            Patience(2, 1, "lingering"),
            KillBlast(2, "patience")),

        Tree("cleave", new[] { "Might", "Arc", "Blood" },
            Power(0),
            Haste(0, 1, "power"),
            Brutal(0, "haste"),
            Reach(1, 0),
            Efficiency(1, 1, "reach"),
            Wide(1, "efficiency", "cleave_arc"),
            Flurry(1, "efficiency", "cleave_arc"),
            Laceration(2, 0),
            Lesser("rend", "Rend", 2, 1, new[] { 0.1f, 0.2f, 0.3f }, v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, "laceration"),
            Convert("flame_cleave", "Flame Edge", 2, SkillTags.Fire, "rend", "cleave_blood"),
            KillBlast(2, "rend", "cleave_blood")),

        Tree("double_strike", new[] { "Precision", "Tempo", "Wounds" },
            Power(0),
            Reach(0, 1, "power"),
            Brutal(0, "reach"),
            Haste(1, 0),
            Efficiency(1, 1, "haste"),
            Flurry(1, "efficiency"),
            Laceration(2, 0),
            Lesser("deep", "Deep Cuts", 2, 1, new[] { 0.1f, 0.2f, 0.3f }, v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, "laceration"),
            Convert("venom_strike", "Storm Edge", 2, SkillTags.Lightning, "deep", "ds_wounds"),
            KillBlast(2, "deep", "ds_wounds")),

        Tree("storm_strike", new[] { "Thunder", "Tempo", "Conduit" },
            Power(0),
            Static(0, 1, "power"),
            Brutal(0, "static"),
            Haste(1, 0),
            Efficiency(1, 1, "haste"),
            Flurry(1, "efficiency"),
            Reach(2, 0),
            Lesser("charged", "Charged", 2, 1, new[] { 0.1f, 0.2f, 0.3f }, v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, "reach"),
            KillBlast(2, "charged", "storm_conduit"),
            Convert("thunder_steel", "Bare Steel", 2, SkillTags.None, "charged", "storm_conduit")),

        Tree("glacial_hammer", new[] { "Glacier", "Weight", "Rhythm" },
            Power(0),
            Rime(0, 1, "power"),
            Brutal(0, "rime"),
            Reach(1, 0),
            Lesser("heavy", "Heavy Head", 1, 1, new[] { 0.1f, 0.2f, 0.3f }, v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, "reach"),
            KillBlast(1, "heavy", "hammer_weight"),
            Wide(1, "heavy", "hammer_weight"),
            Haste(2, 0),
            Efficiency(2, 1, "haste"),
            Flurry(2, "efficiency")),

        Tree("ground_slam", new[] { "Quake", "Fault", "Rhythm" },
            Power(0),
            Reach(0, 1, "power"),
            Wide(0, "reach"),
            Laceration(1, 0),
            Lesser("fault", "Fault Line", 1, 1, new[] { 0.1f, 0.2f, 0.3f }, v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, "laceration"),
            Convert("magma", "Magma", 1, SkillTags.Fire, "fault", "slam_fault"),
            KillBlast(1, "fault", "slam_fault"),
            Patience(2, 0),
            Efficiency(2, 1, "patience"),
            Echo(2, "efficiency")),

        Tree("sunder", new[] { "Rift", "Length", "Rhythm" },
            Power(0),
            Laceration(0, 1, "power"),
            Brutal(0, "laceration"),
            Reach(1, 0),
            Lesser("split", "Split Earth", 1, 1, new[] { 0.1f, 0.2f, 0.3f }, v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, "reach"),
            Convert("volcanic", "Volcanic Rift", 1, SkillTags.Fire, "split", "sunder_length"),
            KillBlast(1, "split", "sunder_length"),
            Patience(2, 0),
            Efficiency(2, 1, "patience"),
            Echo(2, "efficiency")),

        // The Witch's curses (any class may use them).
        Tree("corruption", new[] { "Rot", "Plague", "Reach" },
            Power(0),
            Lingering(0, 1, "power"),
            Brutal(0, "lingering"),
            Efficiency(1, 0),
            Lesser("virulence", "Virulence", 1, 1, new[] { 1f, 2f }, v => $"Spreads to {v:0} enem{(v > 1f ? "ies" : "y")} when its bearer dies", (c, v) => c.CurseSpread = Math.Max(c.CurseSpread, (int)v), "efficiency", 10),
            Notable("pandemic", "Pandemic", 1, "Spreads to 3 enemies, and on from them", c => c.CurseSpread = Math.Max(c.CurseSpread, 3), "virulence", null, 11),
            Haste(2, 0),
            Lesser("multi", "Many Hosts", 2, 1, new[] { 1f, 2f }, v => $"Marks {v:0} more enem{(v > 1f ? "ies" : "y")} near the first", (c, v) => c.CurseExtraTargets += (int)v, "haste", 10),
            Notable("blight_field", "Blight Field", 2, "Marks every enemy in a wide circle, 20% less damage over time", c => { c.CurseExtraTargets += 20; c.AreaMultiplier *= 2.2f; c.DamageMultiplier *= 0.8f; }, "multi", null, 12)),

        Tree("curse_elements", new[] { "Weakness", "Reach", "Ruin" },
            Lesser("weakness", "Weakness", 0, 0, new[] { 5f, 10f, 15f }, v => $"Resistances drop {v:0} more", (c, v) => c.CursePotencyBonus += v),
            Lingering(0, 1, "weakness"),
            Notable("exposure", "Exposure", 0, "Cursed enemies also take 15% more damage of every kind", c => c.CurseVulnerability = Math.Max(c.CurseVulnerability, 15f), "lingering"),
            Reach(1, 0),
            Efficiency(1, 1, "reach"),
            Notable("hex_contagion", "Contagion", 1, "Spreads to 2 enemies when its bearer dies", c => c.CurseSpread = Math.Max(c.CurseSpread, 2), "efficiency", null, 10),
            Patience(2, 0),
            Haste(2, 1, "patience"),
            Notable("hex_burst", "Ruin", 2, "Cursed enemies burst when they die, for 10% of their maximum life", c => c.KillBlastFraction = Math.Max(c.KillBlastFraction, 0.1f), "haste")),

        Tree("curse_time", new[] { "Stillness", "Reach", "Echo" },
            Lesser("drag", "Drag", 0, 0, new[] { 5f, 10f, 15f }, v => $"{v:0}% stronger slow", (c, v) => c.CursePotencyBonus += v),
            Lingering(0, 1, "drag"),
            Notable("stasis", "Stasis", 0, "Slow +20% more, and the curse is 20% shorter", c => { c.CursePotencyBonus += 20f; c.DurationMultiplier *= 0.8f; }, "lingering"),
            Reach(1, 0),
            Efficiency(1, 1, "reach"),
            Notable("time_contagion", "Ripple", 1, "Spreads to 2 enemies when its bearer dies", c => c.CurseSpread = Math.Max(c.CurseSpread, 2), "efficiency", null, 10),
            Patience(2, 0),
            Haste(2, 1, "patience"),
            Notable("time_frailty", "Frailty", 2, "Slowed enemies also take 10% more damage", c => c.CurseVulnerability = Math.Max(c.CurseVulnerability, 10f), "haste")),
    }.ToDictionary(t => t.SkillId);

    public static SkillTree? For(string skillId) => All.TryGetValue(skillId, out SkillTree? t) ? t : null;

    /// <summary>The cast transforms for a gem's allocated nodes (ranks clamped to the node).</summary>
    public static IEnumerable<CastModifier> ModifiersFor(string skillId, IReadOnlyDictionary<string, int> ranks)
    {
        if (For(skillId) is not { } tree)
        {
            yield break;
        }

        foreach ((string id, int rank) in ranks)
        {
            if (rank > 0 && tree.Node(id) is { } node)
            {
                yield return new SkillTreeModifier(node, Math.Min(rank, node.MaxRank));
            }
        }
    }
}

/// <summary>The rules of spending points in a gem's tree (node-free, so tests can run them).</summary>
public static class SkillTreeRules
{
    public static int RankOf(Characters.GemData gem, string nodeId) => gem.Tree.TryGetValue(nodeId, out int r) ? r : 0;

    /// <summary>Why a rank can't be added to this node now, or null.</summary>
    public static string? CannotRaise(Characters.GemData gem, SkillTree tree, SkillTreeNode node)
    {
        int rank = RankOf(gem, node.Id);
        if (rank >= node.MaxRank)
        {
            return "Already at its highest rank";
        }

        if (gem.TreePointsFree <= 0)
        {
            return gem.Level >= Characters.GemData.MaxLevel ? "No points left" : "No points left: raise the skill's level for more";
        }

        if (node.Requires is { } parent && RankOf(gem, parent) < node.RequiresRanks)
        {
            return $"Needs {node.RequiresRanks} point{(node.RequiresRanks == 1 ? "" : "s")} in {tree.Node(parent)?.Name ?? parent} first";
        }

        if (node.Exclusive is { } group && tree.Nodes.FirstOrDefault(n => n.Exclusive == group && n.Id != node.Id && RankOf(gem, n.Id) > 0) is { } taken)
        {
            return $"Excludes {taken.Name}: only one of them";
        }

        return null;
    }

    /// <summary>Why a rank can't be taken back from this node now, or null.</summary>
    public static string? CannotLower(Characters.GemData gem, SkillTree tree, SkillTreeNode node)
    {
        int rank = RankOf(gem, node.Id);
        if (rank <= 0)
        {
            return "No points in it";
        }

        if (tree.Nodes.FirstOrDefault(n => n.Requires == node.Id && RankOf(gem, n.Id) > 0 && rank - 1 < n.RequiresRanks) is { } child)
        {
            return $"{child.Name} needs it: take that back first";
        }

        return null;
    }

    public static bool Raise(Characters.GemData gem, SkillTree tree, SkillTreeNode node)
    {
        if (CannotRaise(gem, tree, node) is not null)
        {
            return false;
        }

        gem.Tree[node.Id] = RankOf(gem, node.Id) + 1;
        return true;
    }

    public static bool Lower(Characters.GemData gem, SkillTree tree, SkillTreeNode node)
    {
        if (CannotLower(gem, tree, node) is not null)
        {
            return false;
        }

        int rank = RankOf(gem, node.Id) - 1;
        if (rank <= 0)
        {
            gem.Tree.Remove(node.Id);
        }
        else
        {
            gem.Tree[node.Id] = rank;
        }

        return true;
    }

    /// <summary>A loaded gem keeps only ranks its tree has, within the nodes' limits and
    /// its points; anything else (an old or edited save) clears the tree, refunding it.</summary>
    public static void Sanitize(Characters.GemData gem)
    {
        if (gem.IsSupport || SkillTrees.For(gem.Id) is not { } tree)
        {
            gem.Tree.Clear();
            return;
        }

        bool valid = gem.Tree.All(kv => tree.Node(kv.Key) is { } n && kv.Value >= 1 && kv.Value <= n.MaxRank
                && (n.Requires is null || RankOf(gem, n.Requires) >= n.RequiresRanks)
                && (n.Exclusive is null || !tree.Nodes.Any(o => o.Exclusive == n.Exclusive && o.Id != n.Id && RankOf(gem, o.Id) > 0)))
            && gem.TreePointsSpent <= gem.Level - 1;
        if (!valid)
        {
            gem.Tree.Clear();
        }
    }
}
