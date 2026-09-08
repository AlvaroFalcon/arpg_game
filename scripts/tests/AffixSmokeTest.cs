#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using PathToExile.Combat;
using PathToExile.Items;
using PathToExile.Stats;

namespace PathToExile.Tests;

/// <summary>
/// Headless functional smoke test for milestone 2a (base items + affix rolling).
/// Run with:
///   dotnet build PathToExile.csproj
///   ./scripts/run_headless.sh res://scenes/tests/AffixSmokeTest.tscn --quit-after 20
///
/// Covers: the 2 Temper / 2 Sigil cap, affix count per rarity, no duplicate stat
/// on one item (including "of Resilience"'s three-stat claim), tier gating by
/// item level, conditional modifiers actually filtering through
/// <see cref="ActorStats.GetModifiers"/> (both the all-of Condition and the
/// any-of ConditionAny forms), item-class gating (Plated/of Swiftness), attribute
/// derivations recomputing correctly, Thorns not recursing, and one full
/// <see cref="EquipmentItem"/> roll end to end.
///
/// Loads the real seeded resources (<c>res://resources/items/AffixLibrary.tres</c>
/// and the nine base `.tres` files) rather than constructing affixes in code —
/// this is what actually proves the seeded data round-trips through the roller
/// correctly, not just the roller's logic in isolation.
/// </summary>
public partial class AffixSmokeTest : Node2D
{
    private AffixLibrary _library = null!;
    private BaseItemDefinition _ironWand = null!;
    private BaseItemDefinition _copperRing = null!;
    private BaseItemDefinition _rawhideBoots = null!;
    private BaseItemDefinition _oakBuckler = null!;

    private ActorStats _conditionalStats = null!;
    private ActorStats _attributeStats = null!;
    private ActorStats _attackerAStats = null!;
    private ActorStats _targetBStats = null!;
    private Health _targetBHealth = null!;

    private bool _failed;

    public override void _Ready()
    {
        _library = GD.Load<AffixLibrary>("res://resources/items/AffixLibrary.tres");
        _ironWand = GD.Load<BaseItemDefinition>("res://resources/items/bases/iron_wand.tres");
        _copperRing = GD.Load<BaseItemDefinition>("res://resources/items/bases/copper_ring.tres");
        _rawhideBoots = GD.Load<BaseItemDefinition>("res://resources/items/bases/rawhide_boots.tres");
        _oakBuckler = GD.Load<BaseItemDefinition>("res://resources/items/bases/oak_buckler.tres");

        _conditionalStats = GetNode<ActorStats>("ConditionalStats/Stats");
        _attributeStats = GetNode<ActorStats>("AttributeStats/Stats");
        _attackerAStats = GetNode<ActorStats>("AttackerA/Stats");
        _targetBStats = GetNode<ActorStats>("TargetB/Stats");
        _targetBHealth = GetNode<Health>("TargetB/Health");

        GD.Print("[AFFIXTEST] Ready.");

        Check("LIBRARY-LOADED-28-AFFIXES", _library.Entries.Count == 28,
            $"AffixLibrary.Entries.Count={_library.Entries.Count}, expected 28");

        CheckAffixCountPerRarity();
        CheckTemperSigilCap();
        CheckNoDuplicateStatPerItem();
        CheckAllResistancesMutualExclusion();
        CheckTierGatingByItemLevel();
        CheckItemClassGating();
        CheckConditionalModifierFiltering();
        CheckAttributeDerivations();
        CheckThornsRecursionGuard();
        CheckEquipmentItemEndToEnd();

        GD.Print(_failed ? "[AFFIXTEST] === RESULT: FAIL ===" : "[AFFIXTEST] === RESULT: PASS ===");
        GetTree().Quit(_failed ? 1 : 0);
    }

    private void Check(string name, bool passed, string detail)
    {
        GD.Print($"[AFFIXTEST] {name}: {(passed ? "PASS" : "FAIL")} — {detail}");
        if (!passed)
        {
            _failed = true;
        }
    }

    private static bool Approx(float a, float b, float epsilon = 0.01f) => Mathf.Abs(a - b) <= epsilon;

    private RandomNumberGenerator NewRng()
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        return rng;
    }

    // ------------------------------------------------------------------
    // Rarity -> affix count.
    // ------------------------------------------------------------------

    private void CheckAffixCountPerRarity()
    {
        RandomNumberGenerator rng = NewRng();

        bool commonAlwaysZero = true;
        bool magicInRange = true;
        bool rareInRange = true;

        for (int i = 0; i < 100; i++)
        {
            commonAlwaysZero &= AffixRoller.Roll(_ironWand, Rarity.Common, 120, _library.Entries, rng).Count == 0;

            int magicCount = AffixRoller.Roll(_ironWand, Rarity.Magic, 120, _library.Entries, rng).Count;
            magicInRange &= magicCount is >= 1 and <= 2;

            int rareCount = AffixRoller.Roll(_ironWand, Rarity.Rare, 120, _library.Entries, rng).Count;
            rareInRange &= rareCount is >= 3 and <= 4;
        }

        Check("AFFIX-COUNT-COMMON-ALWAYS-ZERO", commonAlwaysZero, "100 Common rolls, all should have 0 affixes");
        Check("AFFIX-COUNT-MAGIC-ONE-OR-TWO", magicInRange, "100 Magic rolls, all should have 1-2 affixes");
        Check("AFFIX-COUNT-RARE-THREE-OR-FOUR", rareInRange, "100 Rare rolls, all should have 3-4 affixes");
    }

    // ------------------------------------------------------------------
    // 2 Temper / 2 Sigil cap.
    // ------------------------------------------------------------------

    private void CheckTemperSigilCap()
    {
        RandomNumberGenerator rng = NewRng();
        bool capRespected = true;

        for (int i = 0; i < 100; i++)
        {
            List<AffixRoller.RolledAffix> rolled = AffixRoller.Roll(_copperRing, Rarity.Rare, 120, _library.Entries, rng);
            int temper = rolled.Count(a => a.Definition.Pool == AffixPoolKind.Temper);
            int sigil = rolled.Count(a => a.Definition.Pool == AffixPoolKind.Sigil);
            capRespected &= temper <= AffixRoller.MaxPerPool && sigil <= AffixRoller.MaxPerPool;
        }

        Check("TEMPER-SIGIL-CAP-RESPECTED", capRespected,
            $"100 Rare rolls at ilvl 120, none should exceed {AffixRoller.MaxPerPool} Temper or {AffixRoller.MaxPerPool} Sigil");
    }

    // ------------------------------------------------------------------
    // No duplicate stat on one item, including of Resilience's 3-stat claim.
    // ------------------------------------------------------------------

    private void CheckNoDuplicateStatPerItem()
    {
        RandomNumberGenerator rng = NewRng();
        bool noDuplicates = true;

        for (int i = 0; i < 100; i++)
        {
            List<AffixRoller.RolledAffix> rolled = AffixRoller.Roll(_copperRing, Rarity.Rare, 120, _library.Entries, rng);
            var claimed = new HashSet<StatId>();
            foreach (AffixRoller.RolledAffix affix in rolled)
            {
                List<StatId> stats = affix.Definition.IsAllResistances
                    ? new List<StatId> { StatId.FireResistance, StatId.ColdResistance, StatId.LightningResistance }
                    : new List<StatId> { affix.Definition.Stat };

                foreach (StatId stat in stats)
                {
                    if (!claimed.Add(stat))
                    {
                        noDuplicates = false;
                    }
                }
            }
        }

        Check("NO-DUPLICATE-STAT-PER-ITEM", noDuplicates,
            "100 Rare rolls, no item should claim the same stat twice (accounting for of Resilience's 3-stat claim)");
    }

    /// <summary>
    /// Direct, narrower proof of the multi-stat rule: restrict the pool to just
    /// the four resistance affixes. <b>Only "of Resilience" collides with every
    /// other candidate</b> — the three single-element resistances target three
    /// genuinely different stats (Fire/Cold/Lightning Resistance) and may
    /// legitimately co-occur on the same item, so "at most one resistance affix
    /// ever rolls" would be the wrong invariant to assert here. What must hold:
    /// whenever of Resilience is present, it's the *only* resistance affix on
    /// that item.
    /// </summary>
    private void CheckAllResistancesMutualExclusion()
    {
        List<AffixDefinition> resistancePool = _library.Entries
            .Where(a => a.Id is "of_resilience" or "of_warding" or "of_insulation" or "of_grounding")
            .ToList();

        Check("RESISTANCE-POOL-HAS-FOUR-ENTRIES", resistancePool.Count == 4,
            $"filtered pool has {resistancePool.Count} entries, expected 4");

        RandomNumberGenerator rng = NewRng();
        bool resilienceExclusive = true;
        bool sawTwoSinglesTogether = false;

        for (int i = 0; i < 100; i++)
        {
            List<AffixRoller.RolledAffix> rolled = AffixRoller.Roll(_copperRing, Rarity.Rare, 120, resistancePool, rng);
            bool hasResilience = rolled.Any(a => a.Definition.IsAllResistances);
            if (hasResilience)
            {
                resilienceExclusive &= rolled.Count == 1;
            }
            else if (rolled.Count == 2)
            {
                // Two singles co-occurring is the positive control: it proves the
                // exclusion is specific to of Resilience, not an accidental
                // "only one resistance affix ever" restriction. Capped at 2
                // (not 3) because all four resistance affixes are Sigil-pool —
                // the 2-per-pool cap (docs/design/affixes.md -> "The two pools")
                // is a second, independent reason the third single can't join,
                // so this positive control targets 2, the actual achievable max.
                sawTwoSinglesTogether = true;
            }
        }

        Check("RESILIENCE-EXCLUSIVE-WHEN-PRESENT", resilienceExclusive,
            "100 Rare rolls against the 4 resistance affixes: whenever of Resilience rolls, it must be the only affix on the item");
        Check("SINGLE-RESISTANCES-CAN-CO-OCCUR", sawTwoSinglesTogether,
            "positive control: two of the 3 single-element resistances (different stats) must be able to roll together " +
            "at least once across 100 tries — otherwise the exclusion above could just be a blanket 'one resistance affix max' bug");
    }

    // ------------------------------------------------------------------
    // Tier gating by item level.
    // ------------------------------------------------------------------

    private void CheckTierGatingByItemLevel()
    {
        AffixDefinition ofRuin = _library.Entries.First(a => a.Id == "of_ruin");

        // MinIlvl 20, interpolated to MaxItemLevel 120: T1 gate=20, T7 gate=120.
        Check("TIER-GATE-BELOW-MINILVL-IS-ZERO", ofRuin.MaxReachableTier(1) == 0,
            $"of Ruin (MinIlvl=20) at item level 1: MaxReachableTier={ofRuin.MaxReachableTier(1)}, expected 0 (not eligible at all)");
        Check("TIER-GATE-AT-MINILVL-IS-ONE", ofRuin.MaxReachableTier(20) == 1,
            $"of Ruin at item level 20 (exactly MinIlvl): MaxReachableTier={ofRuin.MaxReachableTier(20)}, expected 1");
        Check("TIER-GATE-T7-UNREACHABLE-BELOW-ITS-OWN-THRESHOLD",
            ofRuin.MaxReachableTier(119) == 6 && ofRuin.MaxReachableTier(120) == 7,
            $"MaxReachableTier(119)={ofRuin.MaxReachableTier(119)} (expected 6), MaxReachableTier(120)={ofRuin.MaxReachableTier(120)} (expected 7 — reachable at max item level)");

        // A low-ilvl item can never roll T7, exercised through the full roller:
        // at item level 1, every MinIlvl=1 affix can itself only ever reach Tier 1
        // (its own T2 gate is 1+17=18), so a batch of Rare rolls at ilvl 1 should
        // never produce any tier above 1.
        RandomNumberGenerator rng = NewRng();
        bool neverAboveTierOne = true;
        for (int i = 0; i < 50; i++)
        {
            List<AffixRoller.RolledAffix> rolled = AffixRoller.Roll(_ironWand, Rarity.Rare, 1, _library.Entries, rng);
            neverAboveTierOne &= rolled.All(a => a.Tier == 1);
        }

        Check("LOW-ILVL-ITEM-NEVER-ROLLS-ABOVE-TIER-ONE", neverAboveTierOne,
            "50 Rare rolls at item level 1: every rolled affix must land at Tier 1 (T7, and every tier above 1, is unreachable)");
    }

    // ------------------------------------------------------------------
    // AllowedItemClasses gating (Plated: armour slots only; of Swiftness: boots only).
    // ------------------------------------------------------------------

    private void CheckItemClassGating()
    {
        RandomNumberGenerator rng = NewRng();
        bool platedNeverOnRing = true;
        bool swiftnessNeverOnRing = true;
        bool swiftnessOnlyOnBoots = true;

        for (int i = 0; i < 100; i++)
        {
            List<AffixRoller.RolledAffix> onRing = AffixRoller.Roll(_copperRing, Rarity.Rare, 120, _library.Entries, rng);
            platedNeverOnRing &= onRing.All(a => a.Definition.Id != "plated");
            swiftnessNeverOnRing &= onRing.All(a => a.Definition.Id != "of_swiftness");

            List<AffixRoller.RolledAffix> onBoots = AffixRoller.Roll(_rawhideBoots, Rarity.Rare, 120, _library.Entries, rng);
            _ = onBoots; // of Swiftness *can* appear here — nothing to assert beyond "doesn't error".
        }

        Check("PLATED-NEVER-ROLLS-ON-RING", platedNeverOnRing, "100 Rare rolls on Copper Ring, 'Plated' (armour-slot-only) must never appear");
        Check("SWIFTNESS-NEVER-ROLLS-ON-RING", swiftnessNeverOnRing, "100 Rare rolls on Copper Ring, 'of Swiftness' (Boots-only) must never appear");

        AffixDefinition swiftness = _library.Entries.First(a => a.Id == "of_swiftness");
        swiftnessOnlyOnBoots = swiftness.AllowedItemClasses.Count == 1 && swiftness.AllowedItemClasses[0] == ItemClass.Boots;
        Check("SWIFTNESS-AUTHORED-BOOTS-ONLY", swiftnessOnlyOnBoots,
            $"of Swiftness AllowedItemClasses={string.Join(",", swiftness.AllowedItemClasses)}, expected exactly [Boots]");

        AffixDefinition plated = _library.Entries.First(a => a.Id == "plated");
        Check("PLATED-AUTHORED-ARMOUR-SLOTS-ONLY",
            plated.AllowedItemClasses.Count == 5 && !plated.AllowedItemClasses.Contains(ItemClass.Ring) && !plated.AllowedItemClasses.Contains(ItemClass.Wand),
            $"Plated AllowedItemClasses={string.Join(",", plated.AllowedItemClasses)}, expected the 5 armour slots, no Ring/Wand");
    }

    // ------------------------------------------------------------------
    // Conditional modifiers actually filtering — docs/design/affixes.md ->
    // "Condition semantics", the load-bearing seam-4 activation.
    // ------------------------------------------------------------------

    private void CheckConditionalModifierFiltering()
    {
        // All-of: a Blazing-style modifier (Condition = Fire) must apply to a
        // Fire-tagged cast and not to a Cold-tagged one.
        var blazing = new StatModifier(ModifierType.Flat, 10f, source: "blazing-test", condition: SkillTags.Fire);
        _conditionalStats.Get(StatId.Damage).AddModifier(blazing);

        IReadOnlyList<StatModifier> fireHit = _conditionalStats.GetModifiers(
            new ModifierQuery(StatId.Damage, SkillTags.Fire | SkillTags.Projectile | SkillTags.Spell));
        IReadOnlyList<StatModifier> coldHit = _conditionalStats.GetModifiers(
            new ModifierQuery(StatId.Damage, SkillTags.Cold | SkillTags.Projectile | SkillTags.Spell));

        Check("CONDITION-ALL-OF-FIRE-TAGGED-SKILL-GETS-BLAZING", fireHit.Contains(blazing),
            $"Fire-tagged query returned {fireHit.Count} modifier(s), expected it to include the Blazing-style Flat+Fire modifier");
        Check("CONDITION-ALL-OF-COLD-TAGGED-SKILL-EXCLUDED", !coldHit.Contains(blazing),
            $"Cold-tagged query returned {coldHit.Count} modifier(s), expected it to exclude the Fire-conditioned modifier");

        _conditionalStats.Get(StatId.Damage).RemoveModifier(blazing);

        // Any-of: a Volatile-style modifier (ConditionAny = Fire|Cold|Lightning)
        // must apply to any one elemental tag and exclude a cast with none of them.
        var volatile_ = new StatModifier(
            ModifierType.Increased, 12f, source: "volatile-test",
            conditionAny: SkillTags.Fire | SkillTags.Cold | SkillTags.Lightning);
        _conditionalStats.Get(StatId.Damage).AddModifier(volatile_);

        IReadOnlyList<StatModifier> coldSpell = _conditionalStats.GetModifiers(new ModifierQuery(StatId.Damage, SkillTags.Cold | SkillTags.Spell));
        IReadOnlyList<StatModifier> physicalMelee = _conditionalStats.GetModifiers(new ModifierQuery(StatId.Damage, SkillTags.Melee | SkillTags.Attack));

        Check("CONDITION-ANY-OF-ELEMENTAL-TAG-INCLUDED", coldSpell.Contains(volatile_),
            $"a Cold-tagged query must include a ConditionAny=Fire|Cold|Lightning modifier ({coldSpell.Count} matched)");
        Check("CONDITION-ANY-OF-NON-ELEMENTAL-EXCLUDED", !physicalMelee.Contains(volatile_),
            $"a Melee/Attack (no elemental tag) query must exclude it ({physicalMelee.Count} matched)");

        _conditionalStats.Get(StatId.Damage).RemoveModifier(volatile_);

        // Unconditional modifier applies regardless of tags.
        var unconditional = new StatModifier(ModifierType.Flat, 3f, source: "unconditional-test");
        _conditionalStats.Get(StatId.Damage).AddModifier(unconditional);
        Check("UNCONDITIONAL-MODIFIER-APPLIES-TO-ANY-TAGS",
            _conditionalStats.GetModifiers(new ModifierQuery(StatId.Damage, SkillTags.None)).Contains(unconditional)
            && _conditionalStats.GetModifiers(new ModifierQuery(StatId.Damage, SkillTags.Fire | SkillTags.Minion)).Contains(unconditional),
            "an unconditional (Condition=None, ConditionAny=None) modifier must apply regardless of query tags");
        _conditionalStats.Get(StatId.Damage).RemoveModifier(unconditional);
    }

    // ------------------------------------------------------------------
    // Attribute derivations.
    // ------------------------------------------------------------------

    private void CheckAttributeDerivations()
    {
        Check("ATTRIBUTES-NOT-AUTO-DERIVED-AT-READY",
            _attributeStats.Armour.Value == 0f && _attributeStats.Get(StatId.MaxLife).Value == 50f,
            $"Armour={_attributeStats.Armour.Value} (expected 0), MaxLife={_attributeStats.Get(StatId.MaxLife).Value} (expected 50) — " +
            "RecomputeAttributeDerivations is not called automatically (see its doc comment); default Str/Dex/Int=10 grants nothing until called");

        _attributeStats.Get(StatId.Strength).BaseValue = 20f;
        _attributeStats.Get(StatId.Intelligence).BaseValue = 30f;
        _attributeStats.Get(StatId.Dexterity).BaseValue = 40f;
        _attributeStats.RecomputeAttributeDerivations();

        Check("STRENGTH-GRANTS-LIFE-AND-ARMOUR",
            Approx(_attributeStats.Get(StatId.MaxLife).Value, 50f + 20f * 2f) && Approx(_attributeStats.Armour.Value, 20f * 1f),
            $"20 Strength: MaxLife={_attributeStats.Get(StatId.MaxLife).Value} (expected 90), Armour={_attributeStats.Armour.Value} (expected 20)");

        Check("INTELLIGENCE-GRANTS-MANA-AND-REGEN",
            Approx(_attributeStats.Get(StatId.MaxMana).Value, 50f + 30f * 2f) && Approx(_attributeStats.Get(StatId.ManaRegen).Value, 5f + 30f * 0.1f),
            $"30 Intelligence: MaxMana={_attributeStats.Get(StatId.MaxMana).Value} (expected 110), ManaRegen={_attributeStats.Get(StatId.ManaRegen).Value} (expected 8)");

        float expectedCrit = 0.05f * (1f + 40f * 0.5f / 100f);
        Check("DEXTERITY-GRANTS-INCREASED-CRIT-CHANCE",
            Approx(_attributeStats.CritChance.Value, expectedCrit),
            $"40 Dexterity: CritChance={_attributeStats.CritChance.Value} (expected {expectedCrit}, i.e. base 5% * 1.20)");

        // Recompute again with a different Strength to prove "remove by source,
        // re-add" — a stale modifier left behind would double-count instead of
        // replacing.
        _attributeStats.Get(StatId.Strength).BaseValue = 10f;
        _attributeStats.RecomputeAttributeDerivations();
        Check("RECOMPUTE-REPLACES-RATHER-THAN-ACCUMULATES",
            Approx(_attributeStats.Armour.Value, 10f),
            $"Armour after re-recomputing with Strength=10: {_attributeStats.Armour.Value} (expected exactly 10, not 20+10=30)");
    }

    // ------------------------------------------------------------------
    // Thorns recursion guard.
    // ------------------------------------------------------------------

    private void CheckThornsRecursionGuard()
    {
        _attackerAStats.Get(StatId.CritChance).BaseValue = 0f;
        _targetBStats.Get(StatId.CritChance).BaseValue = 0f;
        _attackerAStats.Get(StatId.ThornsDamage).BaseValue = 7f;
        _targetBStats.Get(StatId.ThornsDamage).BaseValue = 5f;

        float lifeABefore = _attackerAStats.Life!.Current;
        float lifeBBefore = _targetBStats.Life!.Current;

        // A hits B for 20 physical. B has Thorns (reflects 5 back to A, stamped).
        // A also has Thorns (7), but must NOT get a chance to reflect the
        // reflection — that's the guard under test.
        var hit = new DamagePayload(20f, DamageType.Physical, GetNode<Node2D>("AttackerA"), Faction.Player, SkillTags.None, DamageDeliveryKind.Hit);
        _targetBHealth.TakeDamage(hit);

        float lifeALost = lifeABefore - _attackerAStats.Life.Current;
        float lifeBLost = lifeBBefore - _targetBStats.Life.Current;

        // Both have 0 Armour, so mitigation is a no-op and the numbers are exact.
        Check("THORNS-TARGET-TAKES-HIT-DAMAGE", Approx(lifeBLost, 20f),
            $"B lost {lifeBLost} life (expected exactly 20 — B's own Thorns reflects but doesn't reduce B's own life)");
        Check("THORNS-REFLECTS-EXACTLY-ONCE-NOT-RECURSIVELY", Approx(lifeALost, 5f),
            $"A lost {lifeALost} life (expected exactly 5 = B's Thorns damage; " +
            "if the guard failed this would be 5 + 7 or worse — A's own Thorns re-reflecting back at B, " +
            "or an unbounded loop that would have hung the test entirely)");
    }

    // ------------------------------------------------------------------
    // One full EquipmentItem roll, end to end.
    // ------------------------------------------------------------------

    private void CheckEquipmentItemEndToEnd()
    {
        var dropScene = GD.Load<PackedScene>("res://scenes/items/EquipmentDrop.tscn");
        EquipmentItem item = dropScene.Instantiate<EquipmentItem>();
        AddChild(item);

        RandomNumberGenerator rng = NewRng();
        item.Initialize(_oakBuckler, Rarity.Rare, 120, _library.Entries, rng);

        ItemDescriptor d = item.Descriptor;
        Check("EQUIPMENT-DESCRIPTOR-POPULATED",
            d.ItemClass == ItemClass.Offhand
                && d.BaseTypeId == "oak_buckler"
                && d.Rarity == Rarity.Rare
                && d.ItemLevel == 120
                && d.AffixCount is >= 3 and <= 4
                && d.AffixTiers.Count == d.AffixCount
                && d.HighestAffixTier is not null
                && d.Width == 2 && d.Height == 2
                && !string.IsNullOrEmpty(d.DisplayName),
            $"class={d.ItemClass}, base={d.BaseTypeId}, rarity={d.Rarity}, ilvl={d.ItemLevel}, " +
            $"affixCount={d.AffixCount}, tiers=[{string.Join(",", d.AffixTiers)}], highest={d.HighestAffixTier}, " +
            $"size={d.Width}x{d.Height}, name='{d.DisplayName}'");

        Check("EQUIPMENT-ROLLED-AFFIXES-MATCH-DESCRIPTOR",
            item.RolledAffixes.Count == d.AffixCount,
            $"RolledAffixes.Count={item.RolledAffixes.Count}, Descriptor.AffixCount={d.AffixCount}");

        Check("EQUIPMENT-BASE-STATS-ROLLED",
            item.RolledBaseStats.Count == 1 && Approx(item.RolledBaseStats[0].Value, 12f),
            $"Oak Buckler's fixed 12 Armour base stat: rolled {item.RolledBaseStats.Count} entries, " +
            $"value={(item.RolledBaseStats.Count > 0 ? item.RolledBaseStats[0].Value : -1f)} (expected 12)");

        item.QueueFree();
    }
}
