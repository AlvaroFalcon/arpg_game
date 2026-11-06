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

    /// <summary>More ways in (Escaldón's Workbench, 2026-11-01): a node may be reached from
    /// several nodes; any one path whose points are met opens it.</summary>
    public IReadOnlyList<(string Id, int Ranks)> AlsoFrom { get; init; } = Array.Empty<(string, int)>();

    /// <summary>Every way into the node (empty = a starting node).</summary>
    public IEnumerable<(string Id, int Ranks)> Parents =>
        Requires is { } r ? AlsoFrom.Prepend((r, RequiresRanks)) : AlsoFrom;

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
    // Owner, 2026-11-04: 8/12/20 (was 8/16/25) — damage must come from gear too, not only from the trees.
    private static readonly float[] PowerRanks = { 0.08f, 0.12f, 0.2f };

    // Balance matrix, 2026-11-05 (docs/design/balance-testing.md): the second "more damage"
    // node of a branch (Rend, Fault Line, Heavy Head...) was 10/20/30%, and stacked with Power,
    // Overwhelm and Flurry/Echo a full tree multiplied a melee skill's damage by 4–6.5.
    private static readonly float[] SecondaryDamage = { 0.06f, 0.1f, 0.15f };

    private static SkillTreeNode Power(int branch) => Power(branch, 0, null);

    private static SkillTreeNode Power(int branch, int depth, string? requires, float[]? values = null) => Lesser("power", "Power", branch, depth, values ?? PowerRanks,
        v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, requires);

    private static SkillTreeNode Haste(int branch, int depth, string? requires = null) => Lesser("haste", "Haste", branch, depth, new[] { 0.05f, 0.1f, 0.15f },
        v => $"{Pct(v)} faster to use", (c, v) => c.UseTimeMultiplier *= 1f - v, requires);

    private static SkillTreeNode Efficiency(int branch, int depth, string? requires = null, float[]? values = null) => Lesser("efficiency", "Efficiency", branch, depth, values ?? new[] { 0.08f, 0.16f, 0.25f },
        v => $"{Pct(v)} less mana cost", (c, v) => c.CostMultiplier *= 1f - v, requires);

    private static SkillTreeNode Patience(int branch, int depth, string? requires = null, float[]? values = null) => Lesser("patience", "Patience", branch, depth, values ?? new[] { 0.1f, 0.2f, 0.3f },
        v => $"{Pct(v)} shorter cooldown", (c, v) => c.CooldownMultiplier *= 1f - v, requires);

    private static SkillTreeNode Reach(int branch, int depth, string? requires = null) => Lesser("reach", "Reach", branch, depth, new[] { 0.1f, 0.2f, 0.3f },
        v => $"{Pct(v)} more area", (c, v) => c.AreaMultiplier *= 1f + v, requires);

    private static SkillTreeNode Velocity(int branch, int depth, string? requires = null, float[]? values = null) => Lesser("velocity", "Velocity", branch, depth, values ?? new[] { 0.1f, 0.2f, 0.35f },
        v => $"{Pct(v)} faster projectiles", (c, v) => { if (c.Projectile is { } p) { p.Speed *= 1f + v; } }, requires);

    private static SkillTreeNode Lingering(int branch, int depth, string? requires = null) => Lesser("lingering", "Lingering", branch, depth, new[] { 0.15f, 0.3f, 0.5f },
        v => $"{Pct(v)} longer duration", (c, v) => c.DurationMultiplier *= 1f + v, requires);

    private static SkillTreeNode Kindle(int branch, int depth, string? requires = null) => Lesser("kindle", "Kindle", branch, depth, new[] { 0.1f, 0.2f, 0.3f },
        v => $"+{Pct(v)} chance to ignite", (c, v) => c.IgniteChanceBonus += v, requires);

    private static SkillTreeNode Searing(int branch, int depth, string? requires = null, float[]? values = null) => Lesser("searing", "Searing", branch, depth, values ?? new[] { 0.2f, 0.4f, 0.7f },
        v => $"Ignites deal {Pct(v)} more damage", (c, v) => c.IgniteDamageMultiplier *= 1f + v, requires);

    private static SkillTreeNode Static(int branch, int depth, string? requires = null) => Lesser("static", "Static", branch, depth, new[] { 0.1f, 0.2f, 0.3f },
        v => $"+{Pct(v)} chance to shock", (c, v) => c.ShockChanceBonus += v, requires);

    private static SkillTreeNode Rime(int branch, int depth, string? requires = null) => Lesser("rime", "Rime", branch, depth, new[] { 0.05f, 0.1f, 0.15f },
        v => $"+{Pct(v)} chance to freeze", (c, v) => c.FreezeChanceBonus += v, requires);

    private static SkillTreeNode Laceration(int branch, int depth, string? requires = null) => Lesser("laceration", "Laceration", branch, depth, new[] { 0.1f, 0.2f, 0.3f },
        v => $"{Pct(v)} chance to cause bleeding", (c, v) => c.BleedChance += v, requires);

    // Aura helpers (docs/design/skills.md -> "Auras"): nodes write into CastContext.Aura.
    private static void Aura(CastContext c, Action<AuraParams> act)
    {
        if (c.Aura is { } a)
        {
            act(a);
        }
    }

    private static SkillTreeNode AuraLesser(string id, string name, int branch, int depth, float[] values, Func<float, string> text, Action<AuraParams, float> apply, string? requires = null) =>
        Lesser(id, name, branch, depth, values, text, (c, v) => Aura(c, a => apply(a, v)), requires);

    private static SkillTreeNode AuraNotable(string id, string name, int branch, string text, Action<AuraParams> apply, string requires, string? exclusive = null) =>
        Notable(id, name, branch, text, c => Aura(c, apply), requires, exclusive);

    private static SkillTreeNode Resolve(int branch, int depth, string? requires = null) => AuraLesser("resolve", "Resolve", branch, depth, new[] { 0.1f, 0.2f, 0.3f },
        v => $"The passive is {Pct(v)} stronger", (a, v) => a.PassiveMultiplier *= 1f + v, requires);

    private static SkillTreeNode Surge(int branch, int depth, string? requires = null) => AuraLesser("surge", "Surge", branch, depth, new[] { 0.1f, 0.2f, 0.3f },
        v => $"The active is {Pct(v)} stronger", (a, v) => a.ActiveMultiplier *= 1f + v, requires);

    private static AuraStat Res(Stats.StatId id, float v) => new(id, Stats.ModifierType.Flat, v);

    private static SkillTreeNode Potency(int branch, int depth, string? requires = null, float[]? values = null) => Lesser("potency", "Potency", branch, depth, values ?? new[] { 10f, 20f, 35f },
        v => $"+{v:0}% to its effect", (c, v) => c.AfflictionPotency += v, requires);

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
        "The skill repeats once, 35% less damage", c => { c.Repeats += 1; c.DamageMultiplier *= 0.65f; }, requires, exclusive, 20);

    private static SkillTreeNode Brutal(int branch, string requires, string? exclusive = null, float damage = 0.25f, float cost = 0.2f) => Notable("brutal", "Overwhelm", branch,
        $"{Pct(damage)} more damage, {Pct(cost)} more mana cost", c => { c.DamageMultiplier *= 1f + damage; c.CostMultiplier *= 1f + cost; }, requires, exclusive);

    private static SkillTreeNode ExtraProjectile(int branch, string requires, string? exclusive = null) => Notable("volley", "Volley", branch,
        "+1 projectile, 10% less damage", c => { c.ExtraCount += 1; c.DamageMultiplier *= 0.9f; }, requires, exclusive, 10);

    private static SkillTreeNode Piercing(int branch, string requires, string? exclusive = null) => Notable("piercing", "Piercing", branch,
        "Projectiles pass through 2 more enemies", c => { if (c.Projectile is { } p) { p.Pierce += 2; } }, requires, exclusive, 10);

    private static SkillTreeNode Ricochet(int branch, string requires, string? exclusive = null) => Notable("ricochet", "Ricochet", branch,
        "Projectiles chain to 2 more enemies", c => { if (c.Projectile is { } p) { p.Chains += 2; } }, requires, exclusive, 10);

    private static SkillTreeNode Flurry(int branch, string requires, string? exclusive = null) => Notable("flurry", "Flurry", branch,
        "Strikes one more time, 35% less damage", c => { c.ExtraCount += 1; c.DamageMultiplier *= 0.65f; }, requires, exclusive, 10);

    private static SkillTreeNode Wide(int branch, string requires, string? exclusive = null) => Notable("wide", "Shockwave", branch,
        "40% more area, 10% less damage", c => { c.AreaMultiplier *= 1.4f; c.DamageMultiplier *= 0.9f; }, requires, exclusive);

    // --- the trees ------------------------------------------------------------------------

    private static SkillTree Tree(string skill, string[] branches, params SkillTreeNode[] nodes) => new() { SkillId = skill, Branches = branches, Nodes = nodes };

    public static readonly IReadOnlyDictionary<string, SkillTree> All = new[]
    {
        Tree("fireball", new[] { "Inferno", "Wildfire", "Form" },
            Power(0),
            // Owner's sheet (2026-11-01): Fireball no longer bursts by default; this node adds it.
            Lesser("blast", "Impact Burst", 0, 1, new[] { 40f, 55f, 70f }, v => $"Bursts on impact: {v:0} px, half the hit", (c, v) => { if (c.Projectile is { } p) { p.ExplodeRadius = Math.Max(p.ExplodeRadius, v); p.ExplodeDamageFraction = Math.Max(p.ExplodeDamageFraction, 0.5f); } }, "power"),
            Notable("firestorm", "Firestorm", 0, "The burst deals 75% of the hit instead of half", c => { if (c.Projectile is { } p) { p.ExplodeDamageFraction = Math.Max(p.ExplodeDamageFraction, 0.75f); } }, "blast"),
            Kindle(1, 0),
            Searing(1, 1, "kindle", new[] { 0.1f, 0.15f, 0.25f }),
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
            Efficiency(1, 1, "haste", new[] { 0.08f, 0.12f, 0.2f }),
            ExtraProjectile(1, "efficiency", "spark_storm"),
            Piercing(1, "efficiency", "spark_storm"),
            // Owner, 2026-11-04: "Rebound" (+wall bounces) is gone, sparks already bounce.
            Lesser("cold_current", "Cold Current", 2, 0, new[] { 0.08f, 0.12f, 0.2f }, v => $"Converts {Pct(v)} of the damage to cold (can chill)", (c, v) => c.ColdConversion += v),
            Lesser("range", "Long Arc", 2, 1, new[] { 0.15f, 0.3f, 0.5f }, v => $"Sparks fly {Pct(v)} farther", (c, v) => { if (c.Projectile is { } p) { p.Range *= 1f + v; } }, "cold_current"),
            Convert("grounded", "Grounded", 2, SkillTags.None, "range", "spark_path"),
            KillBlast(2, "range", "spark_path")),

        Tree("frost_shards", new[] { "Ice", "Volley", "Shatter" },
            Power(0),
            Rime(0, 1, "power"),
            Brutal(0, "rime", null, 0.25f, 0.2f),
            Velocity(1, 0),
            Efficiency(1, 1, "velocity"),
            ExtraProjectile(1, "efficiency", "shards_volley"),
            Piercing(1, "efficiency", "shards_volley"),
            Haste(2, 0),
            Lesser("brittle", "Brittle", 2, 1, SecondaryDamage, v => $"{Pct(v)} more damage, shards fly {Pct(v / 2f)} slower", (c, v) => { c.DamageMultiplier *= 1f + v; if (c.Projectile is { } p) { p.Speed *= 1f - v / 2f; } }, "haste"),
            KillBlast(2, "brittle")),

        Tree("throwing_knives", new[] { "Edge", "Fan", "Venom" },
            Power(0),
            Haste(0, 1, "power"),
            Piercing(0, "haste", "knives_edge"),
            Ricochet(0, "haste", "knives_edge"),
            Velocity(1, 0),
            Lesser("spread", "Wide Fan", 1, 1, SecondaryDamage, v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, "velocity"),
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
            // Owner's sheet (2026-11-01): no push back by default; this node adds it.
            Lesser("knock", "Gale", 1, 1, new[] { 120f, 180f, 260f }, v => $"Pushes enemies back ({v:0} force), {Math.Round(v / 24f)}% more damage", (c, v) => { c.ExtraKnockback += v; c.DamageMultiplier *= 1f + v / 2400f; }, "reach"),
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
            Lesser("rend", "Rend", 2, 1, SecondaryDamage, v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, "laceration"),
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
            Lesser("deep", "Deep Cuts", 2, 1, SecondaryDamage, v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, "laceration"),
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
            Lesser("charged", "Charged", 2, 1, SecondaryDamage, v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, "reach"),
            KillBlast(2, "charged", "storm_conduit"),
            Convert("thunder_steel", "Bare Steel", 2, SkillTags.None, "charged", "storm_conduit")),

        Tree("glacial_hammer", new[] { "Glacier", "Weight", "Rhythm" },
            Power(0),
            Rime(0, 1, "power"),
            Brutal(0, "rime"),
            Reach(1, 0),
            Lesser("heavy", "Heavy Head", 1, 1, SecondaryDamage, v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, "reach"),
            KillBlast(1, "heavy", "hammer_weight"),
            Wide(1, "heavy", "hammer_weight"),
            Haste(2, 0),
            Efficiency(2, 1, "haste"),
            Flurry(2, "efficiency")),

        // Leap Slam since 2026-11-01 (owner's notes): a jump that slams on landing.
        Tree("ground_slam", new[] { "Quake", "Fault", "Bound" },
            Power(0),
            Reach(0, 1, "power"),
            Notable("aftershock", "Aftershock", 0, "The landing slams again 0.35 s later for 60% of the hit", c => c.Aftershocks += 1, "reach", null, 20),
            Laceration(1, 0),
            Lesser("fault", "Fault Line", 1, 1, SecondaryDamage, v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, "laceration"),
            Convert("magma", "Magma", 1, SkillTags.Fire, "fault", "slam_fault"),
            KillBlast(1, "fault", "slam_fault"),
            Lesser("long_jump", "Long Jump", 2, 0, new[] { 0.15f, 0.3f, 0.5f }, v => $"Leaps {Pct(v)} farther", (c, v) => c.RangeMultiplier *= 1f + v),
            Patience(2, 1, "long_jump"),
            Notable("earthshaker", "Earthshaker", 2, "Lands harder: +150 knockback and 25% more area", c => { c.ExtraKnockback += 150f; c.AreaMultiplier *= 1.25f; }, "patience")),

        Tree("blink", new[] { "Rift", "Distance", "Arcane" },
            Patience(0, 0),
            Efficiency(0, 1, "patience"),
            Notable("flicker", "Flicker", 0, "40% shorter cooldown, 30% shorter range", c => { c.CooldownMultiplier *= 0.6f; c.RangeMultiplier *= 0.7f; }, "efficiency"),
            // Owner, 2026-11-04: two new notables.
            Notable("rewind", "Rewind", 0, "Press again within 3 s to go back to where you blinked from, as if time ran backwards", c => c.RewindWindow = Math.Max(c.RewindWindow, 3f), "efficiency"),
            Lesser("far_step", "Far Step", 1, 0, new[] { 0.15f, 0.3f, 0.5f }, v => $"Blinks {Pct(v)} farther", (c, v) => c.RangeMultiplier *= 1f + v),
            Lesser("phasing", "Phasing", 1, 1, new[] { 0.1f, 0.2f, 0.3f }, v => $"Untouchable {v:0.0} s longer after arriving", (c, v) => c.ExtraInvulnerability += v, "far_step"),
            Notable("elusive", "Elusive", 1, "30% more movement speed for 3 s after arriving", c => c.AfterMoveSpeed = Math.Max(c.AfterMoveSpeed, 30f), "phasing"),
            Notable("second_wind", "Second Wind", 1, "+1 charge: blink twice before the cooldown", c => c.ExtraCharges = Math.Max(c.ExtraCharges, 1), "phasing"),
            Lesser("static_wake", "Static Wake", 2, 0, new[] { 0.3f, 0.6f, 1f }, v => $"The point you leave bursts for {Pct(v)} of the skill's damage", (c, v) => c.DepartBurst = Math.Max(c.DepartBurst, v)),
            Power(2, 1, "static_wake"),
            Notable("frozen_arrival", "Frozen Arrival", 2, "Where you arrive bursts too, as cold (chills)", c => { c.ArriveBurst = true; UpgradeCatalog.Convert(c, SkillTags.Cold); }, "power", null, 5)),

        Tree("sunder", new[] { "Rift", "Length", "Rhythm" },
            Power(0),
            Laceration(0, 1, "power"),
            Brutal(0, "laceration"),
            Reach(1, 0),
            Lesser("split", "Split Earth", 1, 1, SecondaryDamage, v => $"{Pct(v)} more damage", (c, v) => c.DamageMultiplier *= 1f + v, "reach"),
            Convert("volcanic", "Volcanic Rift", 1, SkillTags.Fire, "split", "sunder_length"),
            KillBlast(1, "split", "sunder_length"),
            Patience(2, 0),
            Efficiency(2, 1, "patience"),
            Echo(2, "efficiency")),

        // The Witch's curses (any class may use them).
        Tree("corruption", new[] { "Rot", "Plague", "Reach" },
            Power(0),
            Lingering(0, 1, "power"),
            Brutal(0, "lingering", null, 0.25f, 0.2f),
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

        // --- auras (owner's brief, 2026-11-01; docs/design/skills.md -> "Auras") -----------

        Tree("immolation", new[] { "Inferno", "Sacrifice", "Wildfire" },
            Power(0),
            Reach(0, 1, "power"),
            Notable("kindling_heart", "Kindling Heart", 0, "Its burns have +40% chance to ignite", c => c.IgniteChanceBonus += 0.4f, "reach"),
            AuraLesser("tempered", "Tempered Flesh", 1, 0, new[] { 0.15f, 0.3f, 0.45f }, v => $"Drains {Pct(v)} less life", (a, v) => a.LifeDrainMultiplier *= 1f - v),
            AuraLesser("pyre_ward", "Pyre Ward", 1, 1, new[] { 10f, 20f, 30f }, v => $"While on: +{v:0} fire resistance", (a, v) => a.Active.Add(Res(Stats.StatId.FireResistance, v)), "tempered"),
            Notable("blood_pyre", "Blood Pyre", 1, "40% more damage, drains twice the life", c => { c.DamageMultiplier *= 1.4f; Aura(c, a => a.LifeDrainMultiplier *= 2f); }, "pyre_ward", "immo_sacrifice"),
            AuraNotable("cinder_skin", "Cinder Skin", 1, "While on: 15% less damage taken", a => a.Active.Add(new AuraStat(Stats.StatId.DamageTaken, Stats.ModifierType.More, -15f)), "pyre_ward", "immo_sacrifice"),
            Kindle(2, 0),
            Searing(2, 1, "kindle"),
            Notable("smoulder", "Smoulder", 2, "Its ignites last 50% longer", c => c.IgniteDurationMultiplier *= 1.5f, "searing")),

        Tree("elemental_ward", new[] { "Bulwark", "Surge", "Rhythm" },
            AuraLesser("warding", "Warding", 0, 0, new[] { 5f, 10f, 25f }, v => $"Passive: +{v:0} more to all three resistances", (a, v) => { a.Passive.Add(Res(Stats.StatId.FireResistance, v)); a.Passive.Add(Res(Stats.StatId.ColdResistance, v)); a.Passive.Add(Res(Stats.StatId.LightningResistance, v)); }),
            Resolve(0, 1, "warding"),
            AuraNotable("elemental_body", "Elemental Body", 0, "Passive: also 8% less elemental damage taken", a => a.Passive.Add(new AuraStat(Stats.StatId.ElementalDamageTaken, Stats.ModifierType.More, -8f)), "resolve"),
            Surge(1, 0),
            Lingering(1, 1, "surge"),
            AuraNotable("elemental_shell", "Elemental Shell", 1, "Active: also 20% less elemental damage taken", a => a.Active.Add(new AuraStat(Stats.StatId.ElementalDamageTaken, Stats.ModifierType.More, -20f)), "lingering"),
            Patience(2, 0),
            Efficiency(2, 1, "patience"),
            Notable("steady_ward", "Steady Ward", 2, "The active lasts 60% longer, 20% weaker", c => { c.DurationMultiplier *= 1.6f; Aura(c, a => a.ActiveMultiplier *= 0.8f); }, "efficiency")),

        // Three branches, one per element; each ends in a focus that excludes the other two.
        Tree("elemental_storm", new[] { "Fire", "Frost", "Thunder" },
            AuraLesser("embers", "Embers", 0, 0, new[] { 5f, 10f, 15f }, v => $"Passive: {v:0}% increased fire damage", (a, v) => a.Passive.Add(new AuraStat(Stats.StatId.Damage, Stats.ModifierType.Increased, v, SkillTags.Fire))),
            AuraLesser("wildfire", "Wildfire", 0, 1, new[] { 0.05f, 0.1f, 0.15f }, v => $"Passive: +{Pct(v)} chance to ignite", (a, v) => a.Passive.Add(new AuraStat(Stats.StatId.IgniteChance, Stats.ModifierType.Flat, v)), "embers"),
            AuraNotable("fire_focus", "Inferno Focus", 0, "The storm is all fire: twice its bonus, to fire damage only; the active also +25% chance to ignite", a => { a.Focus = SkillTags.Fire; a.Active.Add(new AuraStat(Stats.StatId.IgniteChance, Stats.ModifierType.Flat, 0.25f)); }, "wildfire", "storm_focus"),
            AuraLesser("frostbite", "Frostbite", 1, 0, new[] { 5f, 10f, 15f }, v => $"Passive: {v:0}% increased cold damage", (a, v) => a.Passive.Add(new AuraStat(Stats.StatId.Damage, Stats.ModifierType.Increased, v, SkillTags.Cold))),
            AuraLesser("hoarfrost", "Hoarfrost", 1, 1, new[] { 0.03f, 0.06f, 0.1f }, v => $"Passive: +{Pct(v)} chance to freeze", (a, v) => a.Passive.Add(new AuraStat(Stats.StatId.FreezeChance, Stats.ModifierType.Flat, v)), "frostbite"),
            AuraNotable("cold_focus", "Glacier Focus", 1, "The storm is all cold: twice its bonus, to cold damage only; the active also +10% chance to freeze", a => { a.Focus = SkillTags.Cold; a.Active.Add(new AuraStat(Stats.StatId.FreezeChance, Stats.ModifierType.Flat, 0.1f)); }, "hoarfrost", "storm_focus"),
            AuraLesser("static_charge", "Static Charge", 2, 0, new[] { 5f, 10f, 15f }, v => $"Passive: {v:0}% increased lightning damage", (a, v) => a.Passive.Add(new AuraStat(Stats.StatId.Damage, Stats.ModifierType.Increased, v, SkillTags.Lightning))),
            AuraLesser("conduction", "Conduction", 2, 1, new[] { 0.05f, 0.1f, 0.15f }, v => $"Passive: +{Pct(v)} chance to shock", (a, v) => a.Passive.Add(new AuraStat(Stats.StatId.ShockChance, Stats.ModifierType.Flat, v)), "static_charge"),
            AuraNotable("lightning_focus", "Tempest Focus", 2, "The storm is all lightning: twice its bonus, to lightning damage only; the active also +25% chance to shock", a => { a.Focus = SkillTags.Lightning; a.Active.Add(new AuraStat(Stats.StatId.ShockChance, Stats.ModifierType.Flat, 0.25f)); }, "conduction", "storm_focus")),

        Tree("precision", new[] { "Focus", "Lethality", "Rhythm" },
            AuraLesser("keen", "Keen Eye", 0, 0, new[] { 0.01f, 0.02f, 0.03f }, v => $"Passive: +{Pct(v)} critical strike chance", (a, v) => a.Passive.Add(new AuraStat(Stats.StatId.CritChance, Stats.ModifierType.Flat, v))),
            AuraLesser("deadly", "Deadly", 0, 1, new[] { 0.1f, 0.2f, 0.3f }, v => $"Passive: +{Pct(v)} critical strike multiplier", (a, v) => a.Passive.Add(new AuraStat(Stats.StatId.CritMultiplier, Stats.ModifierType.Flat, v)), "keen"),
            AuraNotable("sharpshooter", "Sharpshooter", 0, "The passive is 50% stronger, the active 20% weaker", a => { a.PassiveMultiplier *= 1.5f; a.ActiveMultiplier *= 0.8f; }, "deadly"),
            Surge(1, 0),
            Lingering(1, 1, "surge"),
            AuraNotable("opening_strike", "Opening Strike", 1, "Active: also 20% increased attack and cast speed", a => { a.Active.Add(new AuraStat(Stats.StatId.AttackSpeed, Stats.ModifierType.Increased, 20f)); a.Active.Add(new AuraStat(Stats.StatId.CastSpeed, Stats.ModifierType.Increased, 20f)); }, "lingering"),
            Patience(2, 0),
            Efficiency(2, 1, "patience"),
            AuraNotable("flow", "Flow", 2, "Active: also 15% increased movement speed", a => a.Active.Add(new AuraStat(Stats.StatId.MovementSpeed, Stats.ModifierType.Increased, 15f)), "efficiency")),

        Tree("bloodthirst", new[] { "Wounds", "Frenzy", "Rhythm" },
            AuraLesser("deep_wounds", "Deep Wounds", 0, 0, new[] { 10f, 20f, 35f }, v => $"Passive: bleeds last {v:0}% longer", (a, v) => a.Passive.Add(new AuraStat(Stats.StatId.BleedDuration, Stats.ModifierType.Increased, v))),
            AuraLesser("hemophilia", "Hemophilia", 0, 1, new[] { 0.05f, 0.1f, 0.15f }, v => $"Passive: +{Pct(v)} chance to cause bleeding", (a, v) => a.Passive.Add(new AuraStat(Stats.StatId.BleedChance, Stats.ModifierType.Flat, v)), "deep_wounds"),
            AuraNotable("haemorrhage", "Haemorrhage", 0, "Passive: every bleed adds one more stack, but bleeds last 25% shorter", a => { a.Passive.Add(new AuraStat(Stats.StatId.BleedStacksPerHit, Stats.ModifierType.Flat, 1f)); a.Passive.Add(new AuraStat(Stats.StatId.BleedDuration, Stats.ModifierType.Increased, -25f)); }, "hemophilia"),
            Surge(1, 0),
            Lingering(1, 1, "surge"),
            AuraNotable("blood_frenzy", "Blood Frenzy", 1, "Active: also 20% increased attack speed", a => a.Active.Add(new AuraStat(Stats.StatId.AttackSpeed, Stats.ModifierType.Increased, 20f)), "lingering"),
            Patience(2, 0),
            Efficiency(2, 1, "patience"),
            AuraNotable("sanguine", "Sanguine Thirst", 2, "Active: also 2% of damage leeched as life", a => a.Active.Add(new AuraStat(Stats.StatId.LifeLeech, Stats.ModifierType.Flat, 2f)), "efficiency")),

        Tree("titan", new[] { "Bastion", "Colossus", "Rhythm" },
            AuraLesser("plating", "Plating", 0, 0, new[] { 10f, 20f, 30f }, v => $"Passive: {v:0}% more increased armour", (a, v) => a.Passive.Add(new AuraStat(Stats.StatId.Armour, Stats.ModifierType.Increased, v))),
            AuraLesser("girth", "Girth", 0, 1, new[] { 3f, 6f, 10f }, v => $"Passive: {v:0}% increased maximum life", (a, v) => a.Passive.Add(new AuraStat(Stats.StatId.MaxLife, Stats.ModifierType.Increased, v)), "plating"),
            AuraNotable("immovable", "Immovable", 0, "The passive alone stops every knockback", a => a.PassiveUnstoppable = true, "girth"),
            Surge(1, 0),
            Lingering(1, 1, "surge"),
            AuraNotable("stone_skin", "Stone Skin", 1, "Active: also 15% less physical damage taken", a => a.Active.Add(new AuraStat(Stats.StatId.PhysicalDamageTaken, Stats.ModifierType.More, -15f)), "lingering"),
            Patience(2, 0),
            Efficiency(2, 1, "patience"),
            AuraNotable("retaliation", "Retaliation", 2, "Active: also +30 thorns damage", a => a.Active.Add(new AuraStat(Stats.StatId.ThornsDamage, Stats.ModifierType.Flat, 30f)), "efficiency")),

        Tree("vitality", new[] { "Vigour", "Mending", "Rhythm" },
            AuraLesser("vigour", "Vigour", 0, 0, new[] { 3f, 6f, 10f }, v => $"Passive: {v:0}% more increased maximum life", (a, v) => a.Passive.Add(new AuraStat(Stats.StatId.MaxLife, Stats.ModifierType.Increased, v))),
            AuraLesser("regrowth", "Regrowth", 0, 1, new[] { 1f, 2f, 4f }, v => $"Passive: +{v:0} life regeneration per second", (a, v) => a.Passive.Add(new AuraStat(Stats.StatId.LifeRegen, Stats.ModifierType.Flat, v)), "vigour"),
            AuraNotable("colossal_heart", "Colossal Heart", 0, "The passive is 50% stronger; the active heals 40% less", a => { a.PassiveMultiplier *= 1.5f; a.HealFraction *= 0.6f; }, "regrowth"),
            AuraLesser("mending", "Mending", 1, 0, new[] { 0.05f, 0.1f, 0.15f }, v => $"The active heals {Pct(v)} more of your life", (a, v) => a.HealFraction += v),
            AuraLesser("fortify", "Fortify", 1, 1, new[] { 5f, 10f, 15f }, v => $"Active: {v:0}% less damage taken on top", (a, v) => a.Active.Add(new AuraStat(Stats.StatId.DamageTaken, Stats.ModifierType.More, -v)), "mending"),
            AuraNotable("second_wind", "Second Wind", 1, "Active: also 30% increased movement speed", a => a.Active.Add(new AuraStat(Stats.StatId.MovementSpeed, Stats.ModifierType.Increased, 30f)), "fortify"),
            Patience(2, 0),
            Efficiency(2, 1, "patience"),
            Notable("renewal", "Renewal", 2, "20% shorter cooldown, and the active heals 10% more of your life", c => { c.CooldownMultiplier *= 0.8f; Aura(c, a => a.HealFraction += 0.1f); }, "efficiency")),

        // --- totem, oil and detonations (docs/design/skills.md -> "Totems", "Oil and detonations") ---

        // The totem's nodes apply to its own cast (duration, speed, count) and to every cast of
        // the skill socketed into it (damage, forks, projectiles).
        Tree("totem", new[] { "Elements", "Form", "Ritual" },
            Power(0),
            Lesser("attunement", "Attunement", 0, 1, SecondaryDamage, v => $"{Pct(v)} more elemental damage", (c, v) => { if ((c.Tags & UpgradeCatalog.Elements) != 0) { c.DamageMultiplier *= 1f + v; } }, "power"),
            Notable("conduit", "Conduit", 0, "+15% chance to ignite and shock, +5% to freeze", c => { c.IgniteChanceBonus += 0.15f; c.ShockChanceBonus += 0.15f; c.FreezeChanceBonus += 0.05f; }, "attunement"),
            Lesser("splitting", "Splitting", 1, 0, new[] { 1f, 2f }, v => $"Its projectiles fork {v:0} time{(v > 1f ? "s" : "")} on hit", (c, v) => { if (c.Projectile is { } p) { p.Forks += (int)v; } }, null, 10),
            Velocity(1, 1, "splitting", new[] { 0.08f, 0.12f, 0.2f }),
            ExtraProjectile(1, "velocity", "totem_form"),
            Wide(1, "velocity", "totem_form"),
            Lesser("quickened", "Quickened", 2, 0, new[] { 0.08f, 0.16f, 0.25f }, v => $"The totem casts {Pct(v)} faster", (c, v) => c.UseTimeMultiplier *= 1f - v),
            Lingering(2, 1, "quickened"),
            Notable("ancestral_ring", "Ancestral Ring", 2, "+1 totem at once, 15% less damage", c => { c.ExtraTotems += 1; c.DamageMultiplier *= 0.85f; }, "lingering")),

        Tree("douse", new[] { "Slick", "Spill", "Rhythm" },
            Potency(0, 0, null, new[] { 5f, 8f, 12f }),
            Lingering(0, 1, "potency"),
            Notable("tar", "Tar", 0, "The oil also slows by 30%", c => c.OilSlow = Math.Max(c.OilSlow, 30f), "lingering"),
            Reach(1, 0),
            Efficiency(1, 1, "reach"),
            Wide(1, "efficiency"),
            Patience(2, 0),
            Haste(2, 1, "patience"),
            Notable("accelerant", "Accelerant", 2, "Ignites on oiled enemies last 50% longer", c => c.OilIgniteDuration = Math.Max(c.OilIgniteDuration, 0.5f), "haste")),

        Tree("combust", new[] { "Blaze", "Reach", "Rhythm" },
            Potency(0, 0, null, new[] { 5f, 10f, 20f }),
            Power(0, 1, "potency", new[] { 0.05f, 0.08f, 0.12f }),
            Notable("chain_reaction", "Chain Reaction", 0, "Each detonation splashes 35% of its damage around and ignites what it hits", c => c.DetonationSplash = Math.Max(c.DetonationSplash, 0.35f), "power"),
            Reach(1, 0),
            Efficiency(1, 1, "reach"),
            Notable("embers_remain", "Embers Remain", 1, "Each detonated enemy keeps 3 of its ignite stacks", c => c.StacksKept = Math.Max(c.StacksKept, 3), "efficiency"),
            Patience(2, 0, null, new[] { 0.1f, 0.14f, 0.2f }),
            Haste(2, 1, "patience"),
            Brutal(2, "haste", null, 0.2f)),

        Tree("exsanguinate", new[] { "Hemorrhage", "Reach", "Rhythm" },
            Potency(0, 0, null, new[] { 5f, 10f, 20f }),
            Power(0, 1, "potency", new[] { 0.05f, 0.08f, 0.12f }),
            Notable("blood_nova", "Blood Nova", 0, "Each detonation splashes 35% of its damage around and makes what it hits bleed", c => c.DetonationSplash = Math.Max(c.DetonationSplash, 0.35f), "power"),
            Reach(1, 0),
            Efficiency(1, 1, "reach"),
            Notable("open_wounds", "Open Wounds", 1, "Each detonated enemy keeps 3 of its bleed stacks", c => c.StacksKept = Math.Max(c.StacksKept, 3), "efficiency"),
            Patience(2, 0, null, new[] { 0.1f, 0.14f, 0.2f }),
            Haste(2, 1, "patience"),
            Brutal(2, "haste", null, 0.2f)),
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

        if (!Reachable(gem, node, _ => 0))
        {
            string ways = string.Join(" or ", node.Parents.Select(p => $"{p.Ranks} point{(p.Ranks == 1 ? "" : "s")} in {tree.Node(p.Id)?.Name ?? p.Id}"));
            return $"Needs {ways} first";
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

        // A child that would lose every way in blocks the refund; one still reached another way doesn't.
        if (tree.Nodes.FirstOrDefault(n => RankOf(gem, n.Id) > 0 && n.Parents.Any(p => p.Id == node.Id)
                && !Reachable(gem, n, id => id == node.Id ? 1 : 0)) is { } child)
        {
            return $"{child.Name} needs it: take that back first";
        }

        return null;
    }

    /// <summary>Whether any way into <paramref name="node"/> is met (a starting node always is);
    /// <paramref name="minus"/> takes ranks away first, to ask "after a refund?".</summary>
    public static bool Reachable(Characters.GemData gem, SkillTreeNode node, Func<string, int> minus)
    {
        bool any = false;
        foreach ((string id, int ranks) in node.Parents)
        {
            any = true;
            if (RankOf(gem, id) - minus(id) >= ranks)
            {
                return true;
            }
        }

        return !any;
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
                && Reachable(gem, n, _ => 0)
                && (n.Exclusive is null || !tree.Nodes.Any(o => o.Exclusive == n.Exclusive && o.Id != n.Id && RankOf(gem, o.Id) > 0)))
            && gem.TreePointsSpent <= gem.Level - 1;
        if (!valid)
        {
            gem.Tree.Clear();
        }
    }
}
