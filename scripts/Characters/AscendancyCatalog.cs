#nullable enable
using System.Collections.Generic;
using System.Linq;
using ArpgGame.Combat;
using ArpgGame.Stats;

namespace ArpgGame.Characters;

/// <summary>One ascendancy node. Stat lines are applied generically; behaviour
/// nodes are keyed by id in <see cref="AscendancyEffects"/>.</summary>
public sealed class AscendancyNode
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Notable { get; init; }
    public IReadOnlyList<PassiveMod> Mods { get; init; } = System.Array.Empty<PassiveMod>();
}

/// <summary>
/// Eight or nine nodes per ascendancy, six points (four from the tier-5 trial, two
/// from tier 12): every character skips some, so two Samurai can still differ —
/// docs/design/classes.md -> "Ascendancies" (six evolutions since 2026-10-30).
/// </summary>
public static class AscendancyCatalog
{
    public const int Tier1Unlock = 5;
    public const int Tier2Unlock = 12;
    public const int FirstPoints = 4;
    public const int SecondPoints = 2;

    private static PassiveMod Inc(StatId s, float v, SkillTags c = SkillTags.None) => new(s, ModifierType.Increased, v, c);
    private static PassiveMod Flat(StatId s, float v) => new(s, ModifierType.Flat, v);
    private static PassiveMod More(StatId s, float v, SkillTags c = SkillTags.None) => new(s, ModifierType.More, v, c);

    private static AscendancyNode N(string id, string name, string desc, bool notable = false, params PassiveMod[] mods) =>
        new() { Id = id, Name = name, Description = desc, Notable = notable, Mods = mods };

    private static PassiveMod[] AllRes(float v) => new[] { Flat(StatId.FireResistance, v), Flat(StatId.ColdResistance, v), Flat(StatId.LightningResistance, v) };

    // Behaviour nodes keep the ids their code is keyed by in AscendancyEffects (war_hex,
    // asc_overload, sam_iaido, pal_radiance...), even where they moved to a new ascendancy.
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<AscendancyNode>> Nodes = new Dictionary<string, IReadOnlyList<AscendancyNode>>
    {
        // --- Rogue -------------------------------------------------------------------
        // Samurai: attack speed, critical damage and chance, movement speed.
        ["samurai"] = new[]
        {
            N("sam_iaido", "Iaido", "Your first hit after 2 s without attacking deals 80% more damage.", true),
            N("sam_parry", "Perfect Parry", "Dodging a hit in the first 0.2 s of a roll refunds the charge and makes your next hit deal 100% more damage.", true),
            N("sam_bushido", "Bushido", "20% more damage while above 90% life.", true),
            N("sam_tempo", "Tempo", "15% increased attack speed.", false, Inc(StatId.AttackSpeed, 15f)),
            N("sam_edge", "Keen Edge", "+50% critical strike multiplier.", false, Flat(StatId.CritMultiplier, 0.5f)),
            N("sam_focus", "Focus", "30% increased critical chance.", false, Inc(StatId.CritChance, 30f)),
            N("sam_wind", "Wind Step", "12% increased movement speed.", false, Inc(StatId.MovementSpeed, 12f)),
            N("sam_discipline", "Discipline", "8% increased attack and movement speed.", false, Inc(StatId.AttackSpeed, 8f), Inc(StatId.MovementSpeed, 8f)),
        },
        // Hunter: projectile damage, critical damage, more projectiles, projectile speed.
        ["hunter"] = new[]
        {
            N("hunt_volley", "Volley", "+2 projectiles, 15% less projectile damage.", true, Flat(StatId.ExtraProjectiles, 2f), More(StatId.Damage, -15f, SkillTags.Projectile)),
            N("hunt_ricochet", "Ricochet", "Projectiles chain to one more enemy and pierce one more.", true, Flat(StatId.ExtraChains, 1f), Flat(StatId.ExtraPierce, 1f)),
            N("assassin_mark", "Mark the Prey", "30% more damage against rare, unique-rare and boss enemies.", true),
            N("hunt_power", "Broadheads", "30% increased projectile damage.", false, Inc(StatId.Damage, 30f, SkillTags.Projectile)),
            N("hunt_velocity", "Velocity", "30% increased projectile speed.", false, Inc(StatId.ProjectileSpeed, 30f)),
            N("hunt_lethal", "Lethal Shot", "+40% critical strike multiplier.", false, Flat(StatId.CritMultiplier, 0.4f)),
            N("hunt_quiver", "Full Quiver", "+1 projectile.", false, Flat(StatId.ExtraProjectiles, 1f)),
            N("hunt_eye", "Hawk Eye", "25% increased critical chance.", false, Inc(StatId.CritChance, 25f)),
        },

        // --- Warrior -----------------------------------------------------------------
        // Juggernaut: resistance to everything, defence, life and life regeneration.
        ["juggernaut"] = new[]
        {
            N("jugg_unbreakable", "Unbreakable", "+300 armour and 15% less damage taken.", true, Flat(StatId.Armour, 300f), More(StatId.DamageTaken, -15f)),
            N("jugg_bastion", "Bastion", "+30% to all elemental resistances and 10% less physical damage taken.", true,
                Flat(StatId.FireResistance, 30f), Flat(StatId.ColdResistance, 30f), Flat(StatId.LightningResistance, 30f), More(StatId.PhysicalDamageTaken, -10f)),
            N("jugg_undying", "Undying", "Regenerate 2% of maximum life per second, twice as fast below 35% life.", true),
            N("jugg_vitality", "Vitality", "15% increased maximum life.", false, Inc(StatId.MaxLife, 15f)),
            N("jugg_plate", "Plated", "+150 armour, 10% increased maximum life.", false, Flat(StatId.Armour, 150f), Inc(StatId.MaxLife, 10f)),
            N("jugg_regen", "Mending", "+8 life regenerated per second.", false, Flat(StatId.LifeRegen, 8f)),
            N("jugg_ward", "Warded", "+15% to all elemental resistances.", false, AllRes(15f)),
            N("barb_rage_cap", "Unending Rage", "+20 maximum Rage.", false),
        },
        // Paladin: holy (light) damage, healing, elemental resistances, life.
        ["paladin"] = new[]
        {
            N("pal_radiance", "Radiance", "Melee hits release a burst of holy light (30% of the hit).", true),
            N("pal_consecrate", "Consecration", "Standing still for 1 s consecrates the ground: 4% life per second, enemies on it take 15% more damage.", true),
            N("pal_block", "Bulwark of Faith", "25% chance to block hits.", true),
            N("pal_light", "Light Bearer", "30% increased holy damage.", false, Inc(StatId.Damage, 30f, SkillTags.Holy)),
            N("pal_renewal", "Renewal", "Regenerate 1.5% of maximum life per second.", false),
            N("pal_resist", "Purity", "+25% to all elemental resistances.", false, AllRes(25f)),
            N("pal_life", "Devotion", "15% increased maximum life.", false, Inc(StatId.MaxLife, 15f)),
            N("pal_aura", "Holy Aura", "Enemies within 140 px take 12% more damage from you.", false),
        },

        // --- Thaumaturge ---------------------------------------------------------------
        // Witch: curse damage, elemental resistances, curses that spread.
        ["witch"] = new[]
        {
            N("witch_contagion", "Contagion", "When a cursed enemy dies, its curses spread to up to 2 enemies within 220 px.", true),
            N("war_hex", "Hex", "Your hits curse enemies with Vulnerability: 20% more damage taken for 4 s.", true),
            N("witch_malediction", "Malediction", "Curses last 50% longer and deal 30% more damage.", true, Inc(StatId.SkillDuration, 50f, SkillTags.Curse), More(StatId.Damage, 30f, SkillTags.Curse)),
            N("witch_dark", "Dark Arts", "30% increased curse damage.", false, Inc(StatId.Damage, 30f, SkillTags.Curse)),
            N("witch_reach", "Long Shadow", "30% increased area of effect for curses.", false, Inc(StatId.AreaOfEffect, 30f, SkillTags.Curse)),
            N("war_wither", "Wither", "Cursed enemies are also chilled.", false),
            N("witch_ward", "Hedge Ward", "+20% to all elemental resistances.", false, AllRes(20f)),
            N("war_siphon", "Siphon", "Kills restore 3% of your life and mana.", false),
        },
        // Elementalist: elemental damage and resistances, damage per element, mana and its recovery.
        ["elementalist"] = new[]
        {
            N("asc_overload", "Elemental Overload", "Hitting with a different element than your last hit grants a stack of 8% more elemental damage (max 5, 4 s).", true),
            N("asc_penetration", "Piercing Elements", "Your elemental hits penetrate 15% resistance.", true, Flat(StatId.FirePenetration, 15f), Flat(StatId.ColdPenetration, 15f), Flat(StatId.LightningPenetration, 15f)),
            N("asc_prismatic", "Prismatic", "Elemental hits have a 20% chance to also inflict every other elemental ailment.", true),
            N("elem_pyro", "Pyromancy", "30% increased fire damage.", false, Inc(StatId.Damage, 30f, SkillTags.Fire)),
            N("elem_cryo", "Cryomancy", "30% increased cold damage.", false, Inc(StatId.Damage, 30f, SkillTags.Cold)),
            N("elem_storm", "Stormcalling", "30% increased lightning damage.", false, Inc(StatId.Damage, 30f, SkillTags.Lightning)),
            N("asc_battery", "Battery", "+40 maximum mana, +3 mana regenerated per second.", false, Flat(StatId.MaxMana, 40f), Flat(StatId.ManaRegen, 3f)),
            N("elem_flow", "Mana Flow", "40% increased mana regeneration, 10% reduced mana costs.", false, Inc(StatId.ManaRegen, 40f), Inc(StatId.ManaCostMultiplier, -10f)),
            N("asc_resist", "Attunement", "+20% to all elemental resistances.", false, AllRes(20f)),
        },
    };

    public static IReadOnlyList<AscendancyNode> For(string? ascendancy) =>
        ascendancy is not null && Nodes.TryGetValue(ascendancy, out IReadOnlyList<AscendancyNode>? list) ? list : System.Array.Empty<AscendancyNode>();

    public static AscendancyNode? Find(string id) => Nodes.Values.SelectMany(l => l).FirstOrDefault(n => n.Id == id);
}
