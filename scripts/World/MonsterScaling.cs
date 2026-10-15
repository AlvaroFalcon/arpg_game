#nullable enable
using Godot;
using ArpgGame.Combat;
using ArpgGame.Enemies;
using ArpgGame.Stats;

namespace ArpgGame.World;

/// <summary>
/// Monster stats by monster level — docs/design/endgame.md -> "Monster scaling".
/// THE place to tune how tough reality monsters are: the tables below are
/// mirrored in that doc, so change both together.
///
/// Instead of a free-standing multiplier per tier, monster life follows the
/// damage a <i>reference character</i> deals at that monster level (its level,
/// gems, supports, ascendancy and typical rare gear for the tier) times a
/// <b>time to kill</b> per rarity. If the player's power curve changes (new
/// affixes, a stronger tree), retune <see cref="ExpectedDps"/> and every tier
/// follows. Defences (elemental resistances, armour) rise with level on top, so
/// damage type and penetration matter; damage keeps its own gentler curve.
/// </summary>
public static class MonsterScaling
{
    /// <summary>Seconds the reference character takes to kill one monster of
    /// each rarity at any level (single target, before defences).</summary>
    public const float TtkNormal = 0.4f;
    public const float TtkMagic = 1.5f;
    public const float TtkRare = 5f;
    public const float TtkUnique = 10f;
    public const float TtkBoss = 45f;

    /// <summary>The base life a "standard" monster is authored with (bats are
    /// lighter, brutes heavier: species keep their ratio to this).</summary>
    public const float StandardBaseLife = 30f;

    /// <summary>The reference character's single-target damage per second by
    /// monster level, log-interpolated between anchors. Calibrated with the
    /// autoplay bot's reference character (<c>--gear=typical --level=auto</c>).</summary>
    public static readonly (int Level, float Dps)[] ExpectedDpsTable =
    {
        (1, 130f), (10, 500f), (19, 850f), (28, 1300f), (37, 1900f), (46, 2900f),
        (54, 3700f), (63, 4300f), (72, 4900f), (85, 5500f), (100, 6500f),
    };

    /// <summary>Elemental resistances every monster has: 0 at level 1, this at level 85.</summary>
    public const float MaxResistance = 30f;

    /// <summary>Share of the reference character's hit that armour stops: 0 at level 1, this at level 85.</summary>
    public const float MaxArmourReduction = 0.15f;

    /// <summary>A theme's affinity on top (its own element, or armour for Physical).</summary>
    public const float AffinityResistance = 25f;
    public const float AffinityArmourReduction = 0.05f;

    /// <summary>
    /// The reference character's maximum life by monster level (measured on the
    /// bot's reference character, gear included; log-interpolated). Bosses aim
    /// their hits at it: see <see cref="BossDamageMultiplier"/>.
    /// </summary>
    public static readonly (int Level, float Life)[] ReferenceLifeTable =
    {
        (1, 250f), (19, 300f), (41, 480f), (63, 700f), (85, 1260f), (100, 1500f),
    };

    /// <summary>Share of the reference character's life one typical boss hit takes
    /// (a telegraphed slam; the biggest ring attacks take about twice this).</summary>
    public const float BossHitShare = 0.3f;

    /// <summary>The boss hit the authored numbers are written around (Herald and
    /// Nereth slams, the Colossus's quake: 13–22).</summary>
    public const float AuthoredBossHit = 16f;

    /// <summary>Damage multiplier for a reality boss: its typical hit takes
    /// <see cref="BossHitShare"/> of the reference character's life. (Owner
    /// feedback: standing still in front of the Herald never killed you — on the
    /// ordinary monster curve a slam took ~6 % of your life.)</summary>
    public static float BossDamageMultiplier(int level) => BossHitShare * ReferenceLife(level) / AuthoredBossHit;

    public static float ReferenceLife(int level) => Interpolate(ReferenceLifeTable, level);

    /// <summary>Monster damage: ×1 at level 1, ×5 at level 85 (player life grows far less than player damage).</summary>
    public static float DamageMultiplier(int level) => Mathf.Pow(5f, Progress(level));

    public static float ExpectedDps(int level) => Interpolate(ExpectedDpsTable, level);

    /// <summary>Log-linear interpolation between (level, value) anchors, clamped at both ends.</summary>
    private static float Interpolate((int Level, float Value)[] t, int level)
    {
        if (level <= t[0].Level)
        {
            return t[0].Value;
        }

        for (int i = 1; i < t.Length; i++)
        {
            if (level <= t[i].Level)
            {
                float f = (level - t[i - 1].Level) / (float)(t[i].Level - t[i - 1].Level);
                return Mathf.Exp(Mathf.Lerp(Mathf.Log(t[i - 1].Value), Mathf.Log(t[i].Value), f));
            }
        }

        return t[^1].Value;
    }

    /// <summary>Life multiplier on a normal monster's authored base life: a
    /// <see cref="StandardBaseLife"/> monster dies in <see cref="TtkNormal"/>.</summary>
    public static float LifeMultiplier(int level) => TtkNormal * ExpectedDps(level) / StandardBaseLife;

    /// <summary>Total life multiplier for a rarity relative to a normal monster (the TTK ratio).</summary>
    public static float RarityLife(MonsterRarity rarity) => rarity switch
    {
        MonsterRarity.Magic => TtkMagic / TtkNormal,
        MonsterRarity.Rare => TtkRare / TtkNormal,
        MonsterRarity.Unique => TtkUnique / TtkNormal,
        _ => 1f,
    };

    /// <summary>A boss's life multiplier on its authored life (theme multiplier
    /// included): it lasts <see cref="TtkBoss"/> against the reference character.</summary>
    public static float BossLifeMultiplier(int level, float authoredLife) => TtkBoss * ExpectedDps(level) / Mathf.Max(1f, authoredLife);

    public static float Resistance(int level) => MaxResistance * Progress(level);

    public static float ArmourReduction(int level) => MaxArmourReduction * Progress(level);

    /// <summary>The armour that stops <paramref name="reduction"/> of the reference
    /// character's hit (PoE formula armour / (armour + 10·hit)).</summary>
    public static float ArmourFor(int level, float reduction)
    {
        float hit = ExpectedDps(level) * ReferenceUseTime;
        reduction = Mathf.Clamp(reduction, 0f, 0.9f);
        return reduction / (1f - reduction) * 10f * hit;
    }

    /// <summary>The reference skill's time between hits (a Fireball, 0.45 s).</summary>
    public const float ReferenceUseTime = 0.45f;

    /// <summary>
    /// Resistances and armour for this level (plus the theme's affinity). Added
    /// as Flat modifiers on top of anything the species or a monster mod
    /// already has ("Armoured", "Fire-touched").
    /// </summary>
    public static void ApplyDefences(Enemy enemy, int level, DamageType? affinity)
    {
        float res = Resistance(level);
        float fire = res + (affinity == DamageType.Fire ? AffinityResistance : 0f);
        float cold = res + (affinity == DamageType.Cold ? AffinityResistance : 0f);
        float lightning = res + (affinity == DamageType.Lightning ? AffinityResistance : 0f);
        float armourReduction = ArmourReduction(level) + (affinity == DamageType.Physical ? AffinityArmourReduction : 0f);
        AddFlat(enemy.Stats.FireResistance, fire);
        AddFlat(enemy.Stats.ColdResistance, cold);
        AddFlat(enemy.Stats.LightningResistance, lightning);
        AddFlat(enemy.Stats.Armour, ArmourFor(level, armourReduction));
    }

    private static readonly object DefenceSource = new();

    private static void AddFlat(Stat stat, float value)
    {
        if (value > 0f)
        {
            stat.AddModifier(new StatModifier(ModifierType.Flat, value, DefenceSource));
        }
    }

    // --- the reference character ----------------------------------------------------

    /// <summary>The character level expected at a monster level: 8 at level 1 (after
    /// the introduction), 70 at level 85.</summary>
    public static int ReferenceCharacterLevel(int monsterLevel) =>
        Mathf.Clamp(Mathf.RoundToInt(8f + (monsterLevel - 1) * 62f / 84f), 1, 70);

    /// <summary>Gem level at a character level (equipped gems take half of all XP).</summary>
    public static int ReferenceGemLevel(int characterLevel) => Mathf.Clamp(1 + characterLevel / 2, 1, 20);

    /// <summary>Item level of the gear the reference character wears (a few levels behind).</summary>
    public static int ReferenceItemLevel(int monsterLevel) => Mathf.Max(1, monsterLevel - 5);

    private static float Progress(int level) => Mathf.Clamp((level - 1) / 84f, 0f, 1.25f);
}
