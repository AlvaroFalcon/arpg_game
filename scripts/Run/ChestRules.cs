#nullable enable
using Godot;

namespace ArpgGame.Run;

/// <summary>
/// Every number of the Rift's chests — docs/design/survival-roadmap.md -> M2. A chest drops
/// from elites and bosses, is opened by walking over it (no pause: the horde keeps coming,
/// and co-op cannot stop for one player), and raises 1, 3 or 5 things the build already
/// owns: a skill level or a power-up rank, or a ready evolution first (M5). New skills,
/// supports and keystones stay level-up choices. With nothing left to raise, a slot pays gold.
/// </summary>
public static class ChestRules
{
    /// <summary>An announced elite ("An elite approaches").</summary>
    public const float AnnouncedEliteChance = 0.35f;

    /// <summary>An elite that rolled out of an ordinary spawn.</summary>
    public const float EliteChance = 0.08f;

    /// <summary>The size cascade (Vampire Survivors' rule): a 5 first, then a 3, else a 1;
    /// each chance multiplied by Luck.</summary>
    public const float FiveChance = 0.03f;
    public const float ThreeChance = 0.12f;

    public static int RollSize(float luck, RandomNumberGenerator rng)
    {
        luck = Mathf.Max(0f, luck);
        if (rng.Randf() < FiveChance * luck)
        {
            return 5;
        }

        return rng.Randf() < ThreeChance * luck ? 3 : 1;
    }

    /// <summary>A boss's chest: the Bone Herald 3, the Ember Colossus and every endless-phase boss 5.</summary>
    public static int BossSize(bool colossus, bool endless) => colossus || endless ? 5 : 3;

    /// <summary>Weights inside a chest: a skill level, a power-up rank.</summary>
    public const float SkillLevelWeight = 3f;
    public const float PowerUpWeight = 2f;

    /// <summary>Gold for a slot with nothing to raise: 60 a minute, at least 50.</summary>
    public static long FallbackGold(float elapsed) => (long)Mathf.Max(50f, 60f * elapsed / 60f);

    /// <summary>At most this many chests wait on the floor per player; the oldest opens itself beyond.</summary>
    public const int MaxOnFloor = 6;

    /// <summary>Chests are opened by touch, not pulled by the pickup radius.</summary>
    public const float OpenDistance = 30f;

    /// <summary>Key of a gold slot ("gold:120"): paid by the run, not by the build.</summary>
    public const string GoldPrefix = "gold:";
}
