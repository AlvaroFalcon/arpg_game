#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace ArpgGame.Characters;

/// <summary>An ascendancy: the class's endgame specialisation (docs/design/classes.md).</summary>
public sealed class AscendancyDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ClassId { get; init; } = "";
    public string Tagline { get; init; } = "";

    /// <summary>Sprite set (assets/sprites/player/&lt;Sprite&gt;.tres).</summary>
    public string Sprite { get; init; } = "";
}

/// <summary>A base class — docs/design/classes.md -> "The base classes".</summary>
public sealed class ClassDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Mechanic { get; init; } = "";
    public float Strength { get; init; } = 10f;
    public float Dexterity { get; init; } = 10f;
    public float Intelligence { get; init; } = 10f;
    public float BonusLife { get; init; }
    public float BonusMana { get; init; }
    public int BonusDodgeCharges { get; init; }
    public string StartingSkill { get; init; } = "";

    /// <summary>A second gem every new character of this class gets, so the
    /// first reality is not a one-button fight.</summary>
    public string StartingSupport { get; init; } = "";

    public string Sprite { get; init; } = "";

    /// <summary>The Survival Rift skills that fit the class (docs/design/progression.md ->
    /// "Starting skills"): a skill-less character's first pick comes from here, and
    /// later new-skill cards favour them (<see cref="Progression.OfferRules.ClassSkillMultiplier"/>).
    /// The first entry is the class's starting skill.</summary>
    public IReadOnlyList<string> RiftSkills { get; init; } = new List<string>();

    /// <summary>Starting node id on the shared passive tree.</summary>
    public string TreeStart { get; init; } = "";

    public ClassResourceKind Resource { get; init; }

    public IReadOnlyList<string> Ascendancies { get; init; } = new List<string>();
}

public enum ClassResourceKind
{
    Combo,
    Rage,
    ArcaneCharges,
}

/// <summary>
/// The three base classes and their six evolutions (docs/design/classes.md, owner's
/// rework of 2026-10-30): each base class evolves into one of two ascendancies.
/// The Arcanist is now the <b>Thaumaturge</b> (its id stays "arcanist" so saves keep
/// working). Skills are not tied to a class: every class can use every skill, and
/// a class only decides the one it starts with. Sprites reuse what exists.
/// </summary>
public static class ClassCatalog
{
    public static readonly IReadOnlyList<ClassDefinition> Classes = new[]
    {
        new ClassDefinition
        {
            Id = "rogue", Name = "Rogue",
            Description = "Quick, precise and hard to pin down. Critical strikes, speed and the best dodge in the game.",
            Mechanic = "Combo — consecutive hits build stacks (attack speed and critical chance). Stop hitting and they fall away.",
            Strength = 12, Dexterity = 22, Intelligence = 10, BonusLife = 10, BonusDodgeCharges = 1,
            StartingSkill = "double_strike", StartingSupport = "faster_casting", Sprite = "rogue", TreeStart = "start_rogue",
            RiftSkills = new[] { "double_strike", "throwing_knives", "blades", "storm_strike" },
            Resource = ClassResourceKind.Combo,
            Ascendancies = new[] { "samurai", "hunter" },
        },
        new ClassDefinition
        {
            Id = "warrior", Name = "Warrior",
            Description = "Armour, axes and stubbornness. Wades into the pack and hits everything in front of it.",
            Mechanic = "Rage — dealing and taking hits fills Rage, which grants damage and reduces damage taken. It fades out of combat.",
            Strength = 22, Dexterity = 10, Intelligence = 8, BonusLife = 35,
            StartingSkill = "cleave", StartingSupport = "increased_area", Sprite = "warrior", TreeStart = "start_warrior",
            RiftSkills = new[] { "cleave", "ground_slam", "sunder", "double_strike", "glacial_hammer" },
            Resource = ClassResourceKind.Rage,
            Ascendancies = new[] { "juggernaut", "paladin" },
        },
        new ClassDefinition
        {
            Id = "arcanist", Name = "Thaumaturge",
            Description = "A worker of wonders. Fragile up close, devastating from range with the elements and with curses.",
            Mechanic = "Arcane Charges — every spell cast grants a charge (cast speed and spell damage). Charges drain slowly.",
            Strength = 8, Dexterity = 10, Intelligence = 22, BonusMana = 25,
            StartingSkill = "fireball", StartingSupport = "spell_echo", Sprite = "player", TreeStart = "start_arcanist",
            RiftSkills = new[] { "fireball", "spark", "frost_nova", "meteor", "blades", "frost_shards" },
            Resource = ClassResourceKind.ArcaneCharges,
            Ascendancies = new[] { "witch", "elementalist" },
        },
    };

    public static readonly IReadOnlyList<AscendancyDefinition> Ascendancies = new[]
    {
        new AscendancyDefinition { Id = "samurai", Name = "Samurai", ClassId = "rogue", Sprite = "samurai", Tagline = "Attack speed, critical strikes and swift feet. The duellist." },
        new AscendancyDefinition { Id = "hunter", Name = "Hunter", ClassId = "rogue", Sprite = "ninja", Tagline = "More projectiles, faster and deadlier. Nothing escapes the volley." },
        new AscendancyDefinition { Id = "juggernaut", Name = "Juggernaut", ClassId = "warrior", Sprite = "barbarian", Tagline = "Resistances, armour, life and regeneration. The wall that walks." },
        new AscendancyDefinition { Id = "paladin", Name = "Paladin", ClassId = "warrior", Sprite = "paladin", Tagline = "Holy light, healing and elemental wards. Faith with a hammer." },
        new AscendancyDefinition { Id = "witch", Name = "Witch", ClassId = "arcanist", Sprite = "warlock", Tagline = "Curses that hurt, spread and break resistances." },
        new AscendancyDefinition { Id = "elementalist", Name = "Elementalist", ClassId = "arcanist", Sprite = "ascendant", Tagline = "Fire, cold and lightning mastered one by one, and the mana to cast them." },
    };

    /// <summary>The evolutions of the old roster (before 2026-10-30), for saves: a
    /// character whose ascendancy no longer exists, or belongs to another class now,
    /// takes the closest one of its class, and its ascendancy points are refunded.</summary>
    public static string? MigrateAscendancy(string classId, string? ascendancy)
    {
        if (ascendancy is null || (GetAscendancy(ascendancy) is { } a && a.ClassId == classId))
        {
            return ascendancy;
        }

        return (classId, ascendancy) switch
        {
            ("rogue", "ninja") => "hunter",
            ("rogue", _) => "samurai",
            ("warrior", "paladin") => "paladin",
            ("warrior", _) => "juggernaut",
            ("arcanist", "ascendant") => "elementalist",
            ("arcanist", _) => "witch",
            _ => null,
        };
    }

    public static ClassDefinition Get(string id) => Classes.FirstOrDefault(c => c.Id == id) ?? Classes[2];

    public static AscendancyDefinition? GetAscendancy(string? id) => id is null ? null : Ascendancies.FirstOrDefault(a => a.Id == id);
}
