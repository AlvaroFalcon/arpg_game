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
/// The three base classes and eight ascendancies (docs/design/classes.md).
/// Names: Rogue and Arcanist were proposed in the design pass and approved
/// with the rest of the plan; Warrior was given.
/// </summary>
public static class ClassCatalog
{
    public static readonly IReadOnlyList<ClassDefinition> Classes = new[]
    {
        new ClassDefinition
        {
            Id = "rogue", Name = "Rogue",
            Description = "Quick, precise and hard to pin down. Daggers, knives and critical strikes; the best dodger in the game.",
            Mechanic = "Combo — consecutive hits build stacks (attack speed and critical chance). Stop hitting and they fall away.",
            Strength = 12, Dexterity = 22, Intelligence = 10, BonusLife = 10, BonusDodgeCharges = 1,
            StartingSkill = "double_strike", StartingSupport = "faster_casting", Sprite = "rogue", TreeStart = "start_rogue",
            Resource = ClassResourceKind.Combo,
            Ascendancies = new[] { "assassin", "ninja" },
        },
        new ClassDefinition
        {
            Id = "warrior", Name = "Warrior",
            Description = "Armour, axes and stubbornness. Wades into the pack and hits everything in front of it.",
            Mechanic = "Rage — dealing and taking hits fills Rage, which grants damage and reduces damage taken. It fades out of combat.",
            Strength = 22, Dexterity = 10, Intelligence = 8, BonusLife = 35,
            StartingSkill = "cleave", StartingSupport = "increased_area", Sprite = "warrior", TreeStart = "start_warrior",
            Resource = ClassResourceKind.Rage,
            Ascendancies = new[] { "barbarian", "paladin", "samurai" },
        },
        new ClassDefinition
        {
            Id = "arcanist", Name = "Arcanist",
            Description = "A student of raw arcane power. Fragile up close, devastating from range with the elements.",
            Mechanic = "Arcane Charges — every spell cast grants a charge (cast speed and spell damage). Charges drain slowly.",
            Strength = 8, Dexterity = 10, Intelligence = 22, BonusMana = 25,
            StartingSkill = "fireball", StartingSupport = "spell_echo", Sprite = "player", TreeStart = "start_arcanist",
            Resource = ClassResourceKind.ArcaneCharges,
            Ascendancies = new[] { "ascendant", "warlock", "sage" },
        },
    };

    public static readonly IReadOnlyList<AscendancyDefinition> Ascendancies = new[]
    {
        new AscendancyDefinition { Id = "assassin", Name = "Assassin", ClassId = "rogue", Sprite = "assassin", Tagline = "Crits, poison and executions. The single-target specialist." },
        new AscendancyDefinition { Id = "ninja", Name = "Ninja", ClassId = "rogue", Sprite = "ninja", Tagline = "Shadow clones and endless mobility." },
        new AscendancyDefinition { Id = "barbarian", Name = "Barbarian", ClassId = "warrior", Sprite = "barbarian", Tagline = "More Rage, life leech and fissures that follow every slam." },
        new AscendancyDefinition { Id = "paladin", Name = "Paladin", ClassId = "warrior", Sprite = "paladin", Tagline = "Block, healing ground and holy fire. The unkillable path." },
        new AscendancyDefinition { Id = "samurai", Name = "Samurai", ClassId = "warrior", Sprite = "samurai", Tagline = "Perfect parries on the dodge and devastating first strikes." },
        new AscendancyDefinition { Id = "ascendant", Name = "Ascendant", ClassId = "arcanist", Sprite = "ascendant", Tagline = "Elemental mastery: alternate elements to overload." },
        new AscendancyDefinition { Id = "warlock", Name = "Warlock", ClassId = "arcanist", Sprite = "warlock", Tagline = "Curses, burning and a familiar that fights beside you." },
        new AscendancyDefinition { Id = "sage", Name = "Sage", ClassId = "arcanist", Sprite = "sage", Tagline = "More supports per skill, echoes and a field that slows time." },
    };

    public static ClassDefinition Get(string id) => Classes.FirstOrDefault(c => c.Id == id) ?? Classes[2];

    public static AscendancyDefinition? GetAscendancy(string? id) => id is null ? null : Ascendancies.FirstOrDefault(a => a.Id == id);
}
