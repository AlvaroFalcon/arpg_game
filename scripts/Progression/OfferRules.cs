#nullable enable
namespace ArpgGame.Progression;

/// <summary>How strong a keystone is — docs/design/progression.md -> "Keystone cadence".</summary>
public enum KeystoneTier
{
    /// <summary>A narrow trade: one stat bent, a modest cost (levels 5, 15, 25…).</summary>
    Lesser,

    /// <summary>Build-defining: changes how the whole build works (levels 10, 20, 30…).</summary>
    Great,
}

/// <summary>What a card is, for the composition table (docs/design/progression.md -> "Offer composition").</summary>
public enum OfferCategory
{
    /// <summary>A new skill, or a level for one already owned.</summary>
    Skill,

    /// <summary>A support gem for one owned skill.</summary>
    Support,

    /// <summary>A stat bonus (damage, life, pickup radius, ward…), rolled at a rarity.</summary>
    PowerUp,

    Keystone,
}

/// <summary>
/// Every number that shapes the Survival Rift's level-up cards, in one place —
/// docs/design/progression.md -> "Offer composition" is the same table in prose.
/// <see cref="RunBuild"/> reads these; nothing else should hard-code them.
/// </summary>
public static class OfferRules
{
    /// <summary>Cards per level-up (and in the start pick).</summary>
    public const int CardsPerOffer = 3;

    /// <summary>Per-card rerolls for the whole run.</summary>
    public const int RerollsPerRun = 2;

    // --- pick control (docs/design/survival-roadmap.md -> M1) ---------------------------

    /// <summary>Skips per run: close a row without a card, for part of its experience back.</summary>
    public const int SkipsPerRun = 3;

    /// <summary>Fraction of the skipped level's experience given back.</summary>
    public const float SkipRefund = 0.3f;

    /// <summary>Banishes per run: a card's subject leaves the pool for the rest of the run.</summary>
    public const int BanishesPerRun = 2;

    /// <summary>Once the free rerolls are spent, a reroll costs gold: base × growthⁿ
    /// (n = gold rerolls already bought this run): 25, 40, 64, 102…</summary>
    public const int GoldRerollBase = 25;
    public const float GoldRerollGrowth = 1.6f;

    public static long GoldRerollPrice(int bought) => (long)Godot.Mathf.Round(GoldRerollBase * Godot.Mathf.Pow(GoldRerollGrowth, bought));

    /// <summary>Chance of a 4th card on an ordinary row: Luck above 1 (VS's rule: 3 cards, 4 with luck).</summary>
    public static float FourthCardChance(float luck) => Godot.Mathf.Clamp(luck - 1f, 0f, 1f);

    // --- keystone cadence: lesser at 5, 15, 25…; great at 10, 20, 30… -----------------

    public const int KeystoneInterval = 10;
    public const int FirstLesserKeystone = 5;
    public const int FirstGreatKeystone = 10;

    /// <summary>The keystone tier offered on reaching <paramref name="level"/>, or
    /// null for an ordinary level. Keystone levels offer keystones of that tier only.</summary>
    public static KeystoneTier? KeystoneTierAt(int level)
    {
        if (level >= FirstGreatKeystone && (level - FirstGreatKeystone) % KeystoneInterval == 0)
        {
            return KeystoneTier.Great;
        }

        if (level >= FirstLesserKeystone && (level - FirstLesserKeystone) % KeystoneInterval == 0)
        {
            return KeystoneTier.Lesser;
        }

        return null;
    }

    // --- ordinary levels: a category per card, then an entry by its own weight ---------

    /// <summary>Category weights for an ordinary level's cards. A category with
    /// nothing left to offer drops out and the others share its weight.</summary>
    public static float CategoryWeight(OfferCategory category) => category switch
    {
        OfferCategory.Skill => 35f,
        OfferCategory.PowerUp => 50f,
        OfferCategory.Support => 15f,
        _ => 0f,
    };

    /// <summary>An ordinary level always shows at least one skill card while a skill
    /// can still be learned or levelled.</summary>
    public const bool GuaranteeSkillCard = true;

    /// <summary>Inside the skill category: weight of one new skill vs one skill level.
    /// New skills are favoured while the build has fewer than two.</summary>
    public const float NewSkillWeightFewSkills = 3f;
    public const float NewSkillWeight = 1f;
    public const float SkillLevelWeight = 1.6f;

    /// <summary>A new skill from the character's class pool (ClassDefinition.RiftSkills)
    /// weighs this much more than an off-class one — coherent, never locked.</summary>
    public const float ClassSkillMultiplier = 3f;

    /// <summary>Within the support category every legal support weighs the same.</summary>
    public const float SupportWeight = 1f;

    // --- power-up rarity --------------------------------------------------------------

    public const float RareChance = 0.07f;
    public const float MagicChance = 0.25f;
    public const float MagicMultiplier = 1.5f;
    public const float RareMultiplier = 2.2f;
}
