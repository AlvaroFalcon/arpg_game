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
/// Eight nodes per ascendancy, six points (four from the tier-5 trial, two from
/// tier 12): every character skips two, so two Assassins can still differ —
/// docs/design/classes.md -> "Ascendancies".
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

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<AscendancyNode>> Nodes = new Dictionary<string, IReadOnlyList<AscendancyNode>>
    {
        ["assassin"] = new[]
        {
            N("assassin_executioner", "Executioner", "Hits deal 40% more damage to enemies below 35% life.", true),
            N("assassin_venom", "Venom", "Critical strikes poison: 60% of the hit as damage over 2 s. Poisons stack.", true),
            N("assassin_mark", "Mark the Prey", "30% more damage against rare, unique-rare and boss enemies.", true),
            N("assassin_deadly", "Deadly", "+40% critical strike multiplier, 30% increased critical chance.", false, Flat(StatId.CritMultiplier, 0.4f), Inc(StatId.CritChance, 30f)),
            N("assassin_toxic", "Toxic Blades", "Poisons deal 50% more damage (and last 1 s longer).", false),
            N("assassin_ruthless", "Ruthless", "12% increased attack speed.", false, Inc(StatId.AttackSpeed, 12f)),
            N("assassin_cold_blood", "Cold Blood", "+20% to all elemental resistances.", false, Flat(StatId.FireResistance, 20f), Flat(StatId.ColdResistance, 20f), Flat(StatId.LightningResistance, 20f)),
            N("assassin_predator", "Predator", "Killing a rare or unique-rare enemy restores 25% of your life.", false),
        },
        ["ninja"] = new[]
        {
            N("ninja_clone", "Shadow Clone", "Dodging leaves a clone that draws enemies for 2.5 s, then explodes.", true),
            N("ninja_shadowstep", "Shadowstep", "Your first hit within 2 s of a dodge deals 60% more damage.", true),
            N("ninja_evasive", "Evasive", "15% chance to avoid hits entirely.", true),
            N("ninja_charges", "Endless Roll", "+1 dodge charge.", false, Flat(StatId.DodgeCharges, 1f)),
            N("ninja_recovery", "Light Feet", "40% faster dodge recharge.", false, Inc(StatId.DodgeRecovery, 40f)),
            N("ninja_swift", "Swift", "10% increased movement and attack speed.", false, Inc(StatId.MovementSpeed, 10f), Inc(StatId.AttackSpeed, 10f)),
            N("ninja_multishot", "Shuriken Rain", "+1 projectile.", false, Flat(StatId.ExtraProjectiles, 1f)),
            N("ninja_fleet", "Fleeting Kill", "Kills advance your dodge recharge by 10%.", false),
        },
        ["barbarian"] = new[]
        {
            N("barb_rage_cap", "Unending Rage", "+20 maximum Rage.", true),
            N("barb_leech", "Bloodthirst", "While above 15 Rage, 3% of damage you deal is leeched as life.", true),
            N("barb_fissure", "Fissures", "Melee area skills tear a fissure forward for 60% of their damage.", true),
            N("barb_bloodlust", "Bloodlust", "20% more melee damage.", false, More(StatId.Damage, 20f, SkillTags.Melee)),
            N("barb_thick", "Thick Skin", "15% increased maximum life.", false, Inc(StatId.MaxLife, 15f)),
            N("barb_frenzy", "Frenzy", "15% increased attack speed.", false, Inc(StatId.AttackSpeed, 15f)),
            N("barb_wrath", "Wrath", "Rage also grants 1% attack speed per point.", false),
            N("barb_warcry", "War Cry", "Every 8 s your next melee hit deals 80% more damage.", false),
        },
        ["paladin"] = new[]
        {
            N("pal_block", "Bulwark of Faith", "25% chance to block hits.", true),
            N("pal_consecrate", "Consecration", "Standing still for 1 s consecrates the ground: 4% life per second, enemies on it take 15% more damage.", true),
            N("pal_radiance", "Radiance", "Melee hits release a burst of holy fire (30% of the hit).", true),
            N("pal_fortress", "Fortress", "+150 armour, 10% increased maximum life.", false, Flat(StatId.Armour, 150f), Inc(StatId.MaxLife, 10f)),
            N("pal_resist", "Purity", "+25% to all elemental resistances.", false, Flat(StatId.FireResistance, 25f), Flat(StatId.ColdResistance, 25f), Flat(StatId.LightningResistance, 25f)),
            N("pal_aura", "Holy Aura", "Enemies within 140 px take 12% more damage from you.", false),
            N("pal_renewal", "Renewal", "Regenerate 1.5% of maximum life per second.", false),
            N("pal_martyr", "Martyr", "15% less damage taken.", false, More(StatId.DamageTaken, -15f)),
        },
        ["samurai"] = new[]
        {
            N("sam_parry", "Perfect Parry", "Dodging a hit in the first 0.2 s of a roll refunds the charge and makes your next hit deal 100% more damage.", true),
            N("sam_iaido", "Iaido", "Your first hit after 2 s without attacking deals 80% more damage.", true),
            N("sam_bushido", "Bushido", "20% more damage while above 90% life.", true),
            N("sam_focus", "Focus", "30% increased critical chance, +30% critical multiplier.", false, Inc(StatId.CritChance, 30f), Flat(StatId.CritMultiplier, 0.3f)),
            N("sam_discipline", "Discipline", "10% increased attack and movement speed.", false, Inc(StatId.AttackSpeed, 10f), Inc(StatId.MovementSpeed, 10f)),
            N("sam_bladestorm", "Wide Arc", "20% increased area of effect.", false, Inc(StatId.AreaOfEffect, 20f)),
            N("sam_steel", "Folded Steel", "+100 armour, 10% less damage taken.", false, Flat(StatId.Armour, 100f), More(StatId.DamageTaken, -10f)),
            N("sam_flow", "Flowing Water", "+1 dodge charge.", false, Flat(StatId.DodgeCharges, 1f)),
        },
        ["ascendant"] = new[]
        {
            N("asc_overload", "Elemental Overload", "Hitting with a different element than your last hit grants a stack of 8% more elemental damage (max 5, 4 s).", true),
            N("asc_prismatic", "Prismatic", "Elemental hits have a 20% chance to also inflict every other elemental ailment.", true),
            N("asc_penetration", "Piercing Elements", "Your elemental hits penetrate 15% resistance.", true, Flat(StatId.FirePenetration, 15f), Flat(StatId.ColdPenetration, 15f), Flat(StatId.LightningPenetration, 15f)),
            N("asc_mastery", "Mastery", "30% increased elemental damage.", false, new PassiveMod(StatId.Damage, ModifierType.Increased, 30f, SkillTags.None, Progression.UpgradeCatalog.Elements)),
            N("asc_resist", "Attunement", "+20% to all elemental resistances.", false, Flat(StatId.FireResistance, 20f), Flat(StatId.ColdResistance, 20f), Flat(StatId.LightningResistance, 20f)),
            N("asc_conflux", "Conflux", "+15% chance to ignite and to shock.", false, Flat(StatId.IgniteChance, 0.15f), Flat(StatId.ShockChance, 0.15f)),
            N("asc_battery", "Battery", "+40 maximum mana, +5 mana regenerated per second.", false, Flat(StatId.MaxMana, 40f), Flat(StatId.ManaRegen, 5f)),
            N("asc_surge", "Surge", "12% increased cast speed.", false, Inc(StatId.CastSpeed, 12f)),
        },
        ["warlock"] = new[]
        {
            N("war_hex", "Hex", "Your hits curse enemies with Vulnerability: 20% more damage taken for 4 s.", true),
            N("war_familiar", "Familiar", "A familiar circles you and fires a shadow bolt at a nearby enemy every second.", true),
            N("war_burn", "Hellfire", "Ignites deal 50% more damage; +10% chance to ignite.", true, Flat(StatId.IgniteChance, 0.1f)),
            N("war_pact", "Dark Pact", "30% more spell damage, 15% less maximum life.", false, More(StatId.Damage, 30f, SkillTags.Spell), More(StatId.MaxLife, -15f)),
            N("war_siphon", "Siphon", "Kills restore 3% of your life and mana.", false),
            N("war_decay", "Decay", "Enemies you kill have a 25% chance to explode for 10% of their maximum life.", false),
            N("war_wither", "Wither", "Cursed enemies are also chilled.", false),
            N("war_soul", "Soul Hunger", "10% increased cast speed, +20 maximum mana.", false, Inc(StatId.CastSpeed, 10f), Flat(StatId.MaxMana, 20f)),
        },
        ["sage"] = new[]
        {
            N("sage_insight", "Insight", "+1 support socket for every skill.", true),
            N("sage_echo", "Resonance", "Every spell repeats one additional time.", true),
            N("sage_time", "Time Field", "Enemies within 180 px of you are slowed by 30%.", true),
            N("sage_cooldown", "Patience", "30% increased cooldown recovery.", false, Inc(StatId.CooldownRecovery, 30f)),
            N("sage_wisdom", "Wisdom", "20% increased experience gained, +15 intelligence.", false, Inc(StatId.ExperienceGain, 20f), Flat(StatId.Intelligence, 15f)),
            N("sage_focus", "Focus", "25% increased area of effect, 15% increased skill duration.", false, Inc(StatId.AreaOfEffect, 25f), Inc(StatId.SkillDuration, 15f)),
            N("sage_clarity", "Clarity", "+6 mana regenerated per second, 15% reduced mana costs.", false, Flat(StatId.ManaRegen, 6f), Inc(StatId.ManaCostMultiplier, -15f)),
            N("sage_ward", "Ward", "+15% to all elemental resistances, 10% increased maximum life.", false, Flat(StatId.FireResistance, 15f), Flat(StatId.ColdResistance, 15f), Flat(StatId.LightningResistance, 15f), Inc(StatId.MaxLife, 10f)),
        },
    };

    public static IReadOnlyList<AscendancyNode> For(string? ascendancy) =>
        ascendancy is not null && Nodes.TryGetValue(ascendancy, out IReadOnlyList<AscendancyNode>? list) ? list : System.Array.Empty<AscendancyNode>();

    public static AscendancyNode? Find(string id) => Nodes.Values.SelectMany(l => l).FirstOrDefault(n => n.Id == id);
}
