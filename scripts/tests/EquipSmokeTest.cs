#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PathToExile.Combat;
using PathToExile.Items;
using PathToExile.Stats;

namespace PathToExile.Tests;

/// <summary>
/// Headless functional smoke test for milestone 2b (equip/unequip and modifier
/// application). Run with:
///   dotnet build PathToExile.csproj
///   ./scripts/run_headless.sh res://scenes/tests/EquipSmokeTest.tscn --quit-after 20
///
/// Covers: equip raising the expected stats and unequip reverting them exactly;
/// unequipping a large Maximum Life roll clamping current life without killing;
/// slot validation rejecting a mismatched <see cref="ItemClass"/>; a Ring
/// occupying either ring slot; equipping into an occupied slot returning the
/// displaced item rather than destroying it; a Strength-granting item also
/// granting its derived Life/Armour in the same operation; equip/unequip cycles
/// not accumulating; and a Fire-conditioned affix applying only to a
/// matching-tagged <see cref="ActorStats.GetModifiers"/> query.
///
/// Loads the real seeded resources (<c>AffixLibrary.tres</c>, the base
/// `.tres` files) the same way <c>AffixSmokeTest</c> does, and forces
/// deterministic single-affix rolls by rolling against a one-entry pool — the
/// roller can only pick the one eligible affix, so <em>which</em> affix landed
/// is deterministic even though its tier/value still comes from real RNG. Every
/// assertion below reads the rolled value back off the item rather than
/// hard-coding it, so it stays correct regardless of which tier the RNG lands
/// on.
///
/// <b>Rig design:</b> "GearActor" runs most checks in sequence, sharing one
/// <see cref="ActorStats"/>/<see cref="Equipment"/> pair across Boots/Ring1/
/// Ring2/Weapon slots that never conflict, after a "warm-up" equip/unequip
/// cycle that locks in the constant attribute-derived offset every equip now
/// applies (see <see cref="ActorStats.RecomputeAttributeDerivations"/>) — every
/// subsequent before/after comparison on that rig reads the captured baseline
/// rather than hard-coding the derived Strength/Intelligence contribution.
/// "ClampActor" and "StrengthActor" get their own rigs because both need to
/// mutate <see cref="StatId.MaxLife"/>/<see cref="StatId.Strength"/> directly,
/// which would corrupt GearActor's stable baseline.
/// </summary>
public partial class EquipSmokeTest : Node2D
{
    private AffixLibrary _library = null!;
    private BaseItemDefinition _rawhideBoots = null!;
    private BaseItemDefinition _copperRing = null!;
    private BaseItemDefinition _oakBuckler = null!;
    private BaseItemDefinition _ironWand = null!;
    private PackedScene _dropScene = null!;
    private readonly RandomNumberGenerator _rng = new();

    private ActorStats _gearStats = null!;
    private Equipment _gearEquipment = null!;

    private ActorStats _clampStats = null!;
    private Equipment _clampEquipment = null!;

    private ActorStats _strengthStats = null!;
    private Equipment _strengthEquipment = null!;

    private ActorStats _autoEquipStats = null!;
    private Equipment _autoEquipEquipment = null!;

    private bool _failed;

    public override void _Ready()
    {
        _library = GD.Load<AffixLibrary>("res://resources/items/AffixLibrary.tres");
        _rawhideBoots = GD.Load<BaseItemDefinition>("res://resources/items/bases/rawhide_boots.tres");
        _copperRing = GD.Load<BaseItemDefinition>("res://resources/items/bases/copper_ring.tres");
        _oakBuckler = GD.Load<BaseItemDefinition>("res://resources/items/bases/oak_buckler.tres");
        _ironWand = GD.Load<BaseItemDefinition>("res://resources/items/bases/iron_wand.tres");
        _dropScene = GD.Load<PackedScene>("res://scenes/items/EquipmentDrop.tscn");
        _rng.Randomize();

        _gearStats = GetNode<ActorStats>("GearActor/Stats");
        _gearEquipment = GetNode<Equipment>("GearActor/Equipment");

        _clampStats = GetNode<ActorStats>("ClampActor/Stats");
        _clampEquipment = GetNode<Equipment>("ClampActor/Equipment");

        _strengthStats = GetNode<ActorStats>("StrengthActor/Stats");
        _strengthEquipment = GetNode<Equipment>("StrengthActor/Equipment");

        _autoEquipStats = GetNode<ActorStats>("AutoEquipActor/Stats");
        _autoEquipEquipment = GetNode<Equipment>("AutoEquipActor/Equipment");

        GD.Print("[EQUIPTEST] Ready.");

        (float baselineArmour, float baselineMaxLife) = WarmUpGearActorBaseline();
        EquipmentItem bootsItem = CheckEquipRaisesExpectedStatsAndUnequipReverts(baselineArmour, baselineMaxLife);
        CheckSlotValidationRejectsMismatchedClass(bootsItem);
        CheckRingCanOccupyEitherSlot();
        CheckEquipIntoOccupiedSlotReturnsDisplaced();
        CheckEquipUnequipCyclesDoNotAccumulate(baselineArmour, baselineMaxLife);
        CheckConditionalFireAffixAppliesOnlyToMatchingQuery();

        CheckUnequipLargeLifeRollClampsWithoutKilling();
        CheckStrengthItemGrantsDerivedLifeAndArmour();
        CheckAutoEquipBridgeFillsFreeSlotElseLeavesItemUnequipped();

        GD.Print(_failed ? "[EQUIPTEST] === RESULT: FAIL ===" : "[EQUIPTEST] === RESULT: PASS ===");
        GetTree().Quit(_failed ? 1 : 0);
    }

    private void Check(string name, bool passed, string detail)
    {
        GD.Print($"[EQUIPTEST] {name}: {(passed ? "PASS" : "FAIL")} — {detail}");
        if (!passed)
        {
            _failed = true;
        }
    }

    private static bool Approx(float a, float b, float epsilon = 0.01f) => Mathf.Abs(a - b) <= epsilon;

    private List<AffixDefinition> SinglePool(string affixId) =>
        new() { _library.Entries.First(a => a.Id == affixId) };

    private EquipmentItem RollItem(BaseItemDefinition baseItem, Rarity rarity, int itemLevel, IReadOnlyList<AffixDefinition> pool)
    {
        var item = _dropScene.Instantiate<EquipmentItem>();
        AddChild(item);
        item.Initialize(baseItem, rarity, itemLevel, pool, _rng);
        return item;
    }

    // ------------------------------------------------------------------
    // Warm-up: locks in the constant attribute-derived offset every equip now
    // applies, so every later before/after comparison on GearActor can just
    // diff against a captured baseline instead of reasoning about Strength/
    // Intelligence's derived contribution by hand.
    // ------------------------------------------------------------------

    private (float armour, float maxLife) WarmUpGearActorBaseline()
    {
        Check("ATTRIBUTES-DERIVED-BEFORE-ANY-EQUIP",
            _gearStats.Armour.Value == 10f && _gearStats.Get(StatId.MaxLife).Value == 70f,
            $"a bare, never-equipped actor should already read Armour={_gearStats.Armour.Value} (expected 10) and " +
            $"MaxLife={_gearStats.Get(StatId.MaxLife).Value} (expected 70) from its base Strength 10 — " +
            "ActorStats._Ready derives, so equipping is not what makes attributes take effect");

        EquipmentItem warmup = RollItem(_copperRing, Rarity.Common, 1, Array.Empty<AffixDefinition>());
        bool equipped = _gearEquipment.TryEquip(EquipmentSlot.Ring1, warmup, out EquipmentItem? displaced);
        Check("WARMUP-EQUIP-SUCCEEDS", equipped && displaced is null, $"equipped={equipped}, displaced={displaced}");

        EquipmentItem? removed = _gearEquipment.Unequip(EquipmentSlot.Ring1);
        Check("WARMUP-UNEQUIP-RETURNS-SAME-ITEM", ReferenceEquals(removed, warmup), "Unequip must return the exact item that was equipped");

        float baselineArmour = _gearStats.Armour.Value;
        float baselineMaxLife = _gearStats.Get(StatId.MaxLife).Value;

        // A Common-rarity copper ring has no base stats and no affixes, so a full
        // equip/unequip cycle with it must be a complete no-op. Derivations are
        // already applied at _Ready, so this is specifically checking that the
        // recompute Equipment triggers on both ends of the cycle *replaces*
        // rather than accumulates — a second helping of base Strength's +10
        // armour / +20 life would show up here as 20 / 90.
        Check("STATLESS-EQUIP-CYCLE-IS-A-NO-OP",
            baselineArmour == 10f && baselineMaxLife == 70f,
            $"after an equip/unequip cycle with a stat-less item, Armour={baselineArmour} (expected exactly 10) and " +
            $"MaxLife={baselineMaxLife} (expected exactly 70) — unchanged from the derived-at-_Ready baseline");

        return (baselineArmour, baselineMaxLife);
    }

    // ------------------------------------------------------------------
    // Equip raises the expected stats; unequip reverts them exactly.
    // ------------------------------------------------------------------

    private EquipmentItem CheckEquipRaisesExpectedStatsAndUnequipReverts(float baselineArmour, float baselineMaxLife)
    {
        EquipmentItem boots = RollItem(_rawhideBoots, Rarity.Magic, 60, SinglePool("vigorous"));
        Check("EQUIP-ROLLED-EXACTLY-VIGOROUS",
            boots.RolledAffixes.Count == 1 && boots.RolledAffixes[0].Definition.Id == "vigorous"
            && boots.RolledBaseStats.Count == 1 && boots.RolledBaseStats[0].Stat == StatId.Armour,
            $"a one-entry affix pool must roll exactly that affix — got {boots.RolledAffixes.Count} affix(es), " +
            $"base stats on {boots.RolledBaseStats.Count} stat(s)");

        float expectedArmour = baselineArmour + boots.RolledBaseStats[0].Value;
        float expectedMaxLife = baselineMaxLife + boots.RolledAffixes[0].Value;

        bool equipped = _gearEquipment.TryEquip(EquipmentSlot.Boots, boots, out EquipmentItem? displaced);
        Check("EQUIP-INTO-FREE-SLOT-SUCCEEDS", equipped && displaced is null, $"equipped={equipped}, displaced={displaced}");

        Check("EQUIP-RAISES-EXPECTED-STATS",
            Approx(_gearStats.Armour.Value, expectedArmour) && Approx(_gearStats.Get(StatId.MaxLife).Value, expectedMaxLife),
            $"Armour={_gearStats.Armour.Value} (expected {expectedArmour} = baseline {baselineArmour} + base-stat " +
            $"{boots.RolledBaseStats[0].Value}), MaxLife={_gearStats.Get(StatId.MaxLife).Value} (expected {expectedMaxLife} " +
            $"= baseline {baselineMaxLife} + affix {boots.RolledAffixes[0].Value})");

        EquipmentItem? removed = _gearEquipment.Unequip(EquipmentSlot.Boots);
        Check("UNEQUIP-RETURNS-SAME-ITEM", ReferenceEquals(removed, boots), "Unequip must return the exact item that was equipped, not a copy or null");

        Check("UNEQUIP-REVERTS-STATS-EXACTLY",
            Approx(_gearStats.Armour.Value, baselineArmour) && Approx(_gearStats.Get(StatId.MaxLife).Value, baselineMaxLife),
            $"Armour={_gearStats.Armour.Value} (expected exactly the baseline {baselineArmour}), " +
            $"MaxLife={_gearStats.Get(StatId.MaxLife).Value} (expected exactly the baseline {baselineMaxLife})");

        return boots;
    }

    // ------------------------------------------------------------------
    // Slot validation: ItemClass must match the slot.
    // ------------------------------------------------------------------

    private void CheckSlotValidationRejectsMismatchedClass(EquipmentItem bootsItem)
    {
        float armourBefore = _gearStats.Armour.Value;

        bool wrongSlotForBoots = _gearEquipment.TryEquip(EquipmentSlot.Weapon, bootsItem, out EquipmentItem? displacedA);
        Check("SLOT-VALIDATION-REJECTS-BOOTS-INTO-WEAPON",
            !wrongSlotForBoots && displacedA is null && _gearEquipment.GetEquipped(EquipmentSlot.Weapon) is null,
            $"a Boots-class item must not fit the Weapon slot — TryEquip returned {wrongSlotForBoots}");

        EquipmentItem offhand = RollItem(_oakBuckler, Rarity.Common, 1, Array.Empty<AffixDefinition>());
        bool wrongSlotForOffhand = _gearEquipment.TryEquip(EquipmentSlot.Boots, offhand, out EquipmentItem? displacedB);
        Check("SLOT-VALIDATION-REJECTS-OFFHAND-INTO-BOOTS",
            !wrongSlotForOffhand && displacedB is null && _gearEquipment.GetEquipped(EquipmentSlot.Boots) is null,
            $"an Offhand-class item must not fit the Boots slot — TryEquip returned {wrongSlotForOffhand}");

        Check("SLOT-VALIDATION-REJECTION-APPLIES-NO-MODIFIERS",
            Approx(_gearStats.Armour.Value, armourBefore),
            $"Armour must be untouched by two rejected equips: before={armourBefore}, after={_gearStats.Armour.Value}");
    }

    // ------------------------------------------------------------------
    // A Ring may occupy either ring slot.
    // ------------------------------------------------------------------

    private void CheckRingCanOccupyEitherSlot()
    {
        EquipmentItem ringA = RollItem(_copperRing, Rarity.Common, 1, Array.Empty<AffixDefinition>());
        EquipmentItem ringB = RollItem(_copperRing, Rarity.Common, 1, Array.Empty<AffixDefinition>());

        bool intoRing1 = _gearEquipment.TryEquip(EquipmentSlot.Ring1, ringA, out _);
        bool intoRing2 = _gearEquipment.TryEquip(EquipmentSlot.Ring2, ringB, out _);

        Check("RING-OCCUPIES-EITHER-SLOT",
            intoRing1 && intoRing2
            && ReferenceEquals(_gearEquipment.GetEquipped(EquipmentSlot.Ring1), ringA)
            && ReferenceEquals(_gearEquipment.GetEquipped(EquipmentSlot.Ring2), ringB),
            $"a Ring-class item must fit both Ring1 (succeeded={intoRing1}) and Ring2 (succeeded={intoRing2}) " +
            "simultaneously, each holding its own distinct item");

        _gearEquipment.Unequip(EquipmentSlot.Ring1);
        _gearEquipment.Unequip(EquipmentSlot.Ring2);
    }

    // ------------------------------------------------------------------
    // Equipping into an occupied slot returns the displaced item.
    // ------------------------------------------------------------------

    private void CheckEquipIntoOccupiedSlotReturnsDisplaced()
    {
        EquipmentItem itemA = RollItem(_rawhideBoots, Rarity.Common, 1, Array.Empty<AffixDefinition>());
        EquipmentItem itemB = RollItem(_rawhideBoots, Rarity.Common, 1, Array.Empty<AffixDefinition>());

        _gearEquipment.TryEquip(EquipmentSlot.Boots, itemA, out _);
        bool secondEquip = _gearEquipment.TryEquip(EquipmentSlot.Boots, itemB, out EquipmentItem? displaced);

        Check("OCCUPIED-SLOT-DISPLACES-RATHER-THAN-DESTROYS",
            secondEquip && ReferenceEquals(displaced, itemA) && IsInstanceValid(itemA)
            && ReferenceEquals(_gearEquipment.GetEquipped(EquipmentSlot.Boots), itemB),
            $"equipping into an occupied Boots slot must return the previous item ({(displaced is null ? "null" : "itemA")}) " +
            $"rather than freeing it, and the slot must now hold the new item");

        bool armourHasNoItemAModifier = !_gearStats.Get(StatId.Armour).Modifiers.Any(m => ReferenceEquals(m.Source, itemA));
        bool armourHasItemBModifier = _gearStats.Get(StatId.Armour).Modifiers.Any(m => ReferenceEquals(m.Source, itemB));
        Check("DISPLACED-ITEM-MODIFIERS-REMOVED-NEW-ITEM-APPLIED",
            armourHasNoItemAModifier && armourHasItemBModifier,
            $"Armour's modifier list must contain itemB's contribution and none of itemA's — " +
            $"noItemA={armourHasNoItemAModifier}, hasItemB={armourHasItemBModifier}");

        _gearEquipment.Unequip(EquipmentSlot.Boots);
    }

    // ------------------------------------------------------------------
    // Equip/unequip cycles do not accumulate.
    // ------------------------------------------------------------------

    private void CheckEquipUnequipCyclesDoNotAccumulate(float baselineArmour, float baselineMaxLife)
    {
        EquipmentItem item = RollItem(_rawhideBoots, Rarity.Magic, 60, SinglePool("vigorous"));
        float expectedArmour = baselineArmour + item.RolledBaseStats[0].Value;
        float expectedMaxLife = baselineMaxLife + item.RolledAffixes[0].Value;

        bool allCyclesConsistent = true;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            _gearEquipment.TryEquip(EquipmentSlot.Boots, item, out _);
            allCyclesConsistent &= Approx(_gearStats.Armour.Value, expectedArmour) && Approx(_gearStats.Get(StatId.MaxLife).Value, expectedMaxLife);

            _gearEquipment.Unequip(EquipmentSlot.Boots);
            allCyclesConsistent &= Approx(_gearStats.Armour.Value, baselineArmour) && Approx(_gearStats.Get(StatId.MaxLife).Value, baselineMaxLife);
        }

        Check("EQUIP-UNEQUIP-CYCLES-DO-NOT-ACCUMULATE", allCyclesConsistent,
            $"3 repeated equip/unequip cycles of the same item must each land exactly on baseline+item and back to " +
            $"baseline — a growing delta across cycles would mean modifiers or the attribute recompute are " +
            $"accumulating instead of being replaced (final Armour={_gearStats.Armour.Value}, MaxLife=" +
            $"{_gearStats.Get(StatId.MaxLife).Value}, baseline Armour={baselineArmour}, MaxLife={baselineMaxLife})");
    }

    // ------------------------------------------------------------------
    // A conditional (Fire-tagged) affix applies only to a matching query.
    // ------------------------------------------------------------------

    private void CheckConditionalFireAffixAppliesOnlyToMatchingQuery()
    {
        EquipmentItem wand = RollItem(_ironWand, Rarity.Magic, 60, SinglePool("blazing"));
        Check("CONDITIONAL-CHECK-ROLLED-EXACTLY-BLAZING",
            wand.RolledAffixes.Count == 1 && wand.RolledAffixes[0].Definition.Id == "blazing",
            $"expected exactly one rolled Blazing affix, got {wand.RolledAffixes.Count}");

        _gearEquipment.TryEquip(EquipmentSlot.Weapon, wand, out _);

        // Iron Wand's own base stat is a Spell-conditioned Damage modifier (see
        // BaseItemStatEntry's doc comment), so querying with Fire-only (no
        // Spell tag) isolates Blazing's Fire-conditioned modifier from it —
        // neither query below satisfies the base stat's Condition=Spell.
        IReadOnlyList<StatModifier> fireQuery = _gearStats.GetModifiers(new ModifierQuery(StatId.Damage, SkillTags.Fire));
        IReadOnlyList<StatModifier> coldQuery = _gearStats.GetModifiers(new ModifierQuery(StatId.Damage, SkillTags.Cold));

        bool fireIncludesBlazing = fireQuery.Any(m => ReferenceEquals(m.Source, wand) && Approx(m.Value, wand.RolledAffixes[0].Value));
        bool coldExcludesBlazing = !coldQuery.Any(m => ReferenceEquals(m.Source, wand));

        Check("CONDITIONAL-FIRE-AFFIX-APPLIES-TO-FIRE-QUERY", fireIncludesBlazing,
            $"a Fire-tagged Damage query must include Blazing's modifier (rolled value {wand.RolledAffixes[0].Value}) — " +
            $"{fireQuery.Count} modifier(s) matched");
        Check("CONDITIONAL-FIRE-AFFIX-EXCLUDED-FROM-COLD-QUERY", coldExcludesBlazing,
            $"a Cold-tagged Damage query must exclude Blazing's Fire-conditioned modifier — {coldQuery.Count} modifier(s) matched");

        _gearEquipment.Unequip(EquipmentSlot.Weapon);
    }

    // ------------------------------------------------------------------
    // Unequipping a large Maximum Life roll clamps current life without killing.
    // ------------------------------------------------------------------

    private void CheckUnequipLargeLifeRollClampsWithoutKilling()
    {
        // Force the worst case: base MaxLife and base Strength both zeroed, so
        // after unequip Max drops all the way to 0 (no leftover attribute-
        // derived cushion) — the floor-at-1 clamp is the only thing standing
        // between this and a "died from taking off a ring" bug.
        _clampStats.Get(StatId.MaxLife).BaseValue = 0f;
        _clampStats.Get(StatId.Strength).BaseValue = 0f;

        EquipmentItem ring = RollItem(_copperRing, Rarity.Magic, 1, SinglePool("vigorous"));
        bool equipped = _clampEquipment.TryEquip(EquipmentSlot.Ring1, ring, out EquipmentItem? displaced);
        Check("CLAMP-SETUP-EQUIP-SUCCEEDS", equipped && displaced is null, $"equipped={equipped}, displaced={displaced}");

        float maxAfterEquip = _clampStats.Get(StatId.MaxLife).Value;
        Check("CLAMP-SETUP-MAX-EQUALS-ROLLED-VALUE", Approx(maxAfterEquip, ring.RolledAffixes[0].Value),
            $"with base MaxLife/Strength both 0, Max after equip should equal the rolled Vigorous value exactly: " +
            $"Max={maxAfterEquip}, rolled={ring.RolledAffixes[0].Value}");

        // Top off to full, simulating a character who healed while wearing the ring.
        _clampStats.Life!.Current = _clampStats.Life.Max;
        float currentBeforeUnequip = _clampStats.Life.Current;

        EquipmentItem? removed = _clampEquipment.Unequip(EquipmentSlot.Ring1);
        Check("CLAMP-UNEQUIP-RETURNS-SAME-ITEM", ReferenceEquals(removed, ring), "Unequip must return the exact item that was equipped");

        float maxAfterUnequip = _clampStats.Get(StatId.MaxLife).Value;
        float currentAfterUnequip = _clampStats.Life.Current;

        Check("CLAMP-NEVER-KILLS", currentAfterUnequip >= 1f,
            $"Life.Current={currentAfterUnequip} after unequip (Max dropped from {currentBeforeUnequip} to " +
            $"{maxAfterUnequip}) — must floor at 1, never 0, per docs/design/stats.md's absolute-preserve-with-floor rule");
        Check("CLAMP-ACTUALLY-RAN-SYNCHRONOUSLY-INSIDE-UNEQUIP", currentAfterUnequip < currentBeforeUnequip,
            $"Life.Current must already reflect the clamp immediately after Unequip returns (no physics frame waited " +
            $"for): before={currentBeforeUnequip}, after={currentAfterUnequip}");
        Check("CLAMP-FLOORS-EXACTLY-AT-ONE-NOT-ZERO", Approx(maxAfterUnequip, 0f) && Approx(currentAfterUnequip, 1f),
            $"with base MaxLife/Strength forced to 0, Max after unequip is {maxAfterUnequip} (expected 0), so " +
            $"Current must clamp to exactly 1 (expected 1, never 0): got {currentAfterUnequip}");
    }

    // ------------------------------------------------------------------
    // An item granting Strength also grants its derived Life/Armour in the
    // same operation.
    // ------------------------------------------------------------------

    private void CheckStrengthItemGrantsDerivedLifeAndArmour()
    {
        Check("STRENGTH-CHECK-STARTS-FROM-DERIVED-BASELINE",
            _strengthStats.Armour.Value == 10f && _strengthStats.Get(StatId.MaxLife).Value == 70f,
            $"a fresh, never-equipped actor: Armour={_strengthStats.Armour.Value} (expected 10), " +
            $"MaxLife={_strengthStats.Get(StatId.MaxLife).Value} (expected 70) — base Strength 10, already derived at _Ready");

        EquipmentItem ring = RollItem(_copperRing, Rarity.Magic, 1, SinglePool("mighty"));
        Check("STRENGTH-CHECK-ROLLED-EXACTLY-MIGHTY",
            ring.RolledAffixes.Count == 1 && ring.RolledAffixes[0].Definition.Id == "mighty",
            $"expected exactly one rolled Mighty affix, got {ring.RolledAffixes.Count}");

        bool equipped = _strengthEquipment.TryEquip(EquipmentSlot.Ring1, ring, out _);
        Check("STRENGTH-CHECK-EQUIP-SUCCEEDS", equipped, $"equipped={equipped}");

        float mightyValue = ring.RolledAffixes[0].Value;
        float expectedStrength = 10f + mightyValue; // base Strength 10 + the item's flat grant.
        float expectedArmour = expectedStrength * 1f; // +1 Armour per point (docs/design/stats.md).
        float expectedMaxLife = 50f + expectedStrength * 2f; // +2 Maximum Life per point.

        Check("STRENGTH-ITEM-STRENGTH-VALUE-INCLUDES-ITEM-GRANT",
            Approx(_strengthStats.Strength.Value, expectedStrength),
            $"Strength.Value={_strengthStats.Strength.Value} (expected {expectedStrength} = base 10 + rolled {mightyValue})");

        // The point of this check: both derived bonuses must already be present
        // in the same TryEquip call that granted the Strength — not one physics
        // frame later. Equipment applies the item's own modifiers before calling
        // RecomputeAttributeDerivations, so Strength.Value already includes the
        // item's grant by the time the derivation reads it.
        Check("STRENGTH-ITEM-ALSO-GRANTS-DERIVED-ARMOUR",
            Approx(_strengthStats.Armour.Value, expectedArmour),
            $"Armour={_strengthStats.Armour.Value} (expected {expectedArmour} = {expectedStrength} Strength * 1 Armour/point)");
        Check("STRENGTH-ITEM-ALSO-GRANTS-DERIVED-LIFE",
            Approx(_strengthStats.Get(StatId.MaxLife).Value, expectedMaxLife),
            $"MaxLife={_strengthStats.Get(StatId.MaxLife).Value} (expected {expectedMaxLife} = 50 base + " +
            $"{expectedStrength} Strength * 2 Life/point)");

        _strengthEquipment.Unequip(EquipmentSlot.Ring1);
    }

    // ------------------------------------------------------------------
    // Milestone 2b bridge: auto-equip on pickup fills a free matching slot,
    // otherwise leaves the item unequipped (the ground-drop equivalent of
    // "leaves it on the ground") — see EquipmentItem.OnPickup.
    // ------------------------------------------------------------------

    private void CheckAutoEquipBridgeFillsFreeSlotElseLeavesItemUnequipped()
    {
        EquipmentItem ringA = RollItem(_copperRing, Rarity.Common, 1, Array.Empty<AffixDefinition>());
        EquipmentItem ringB = RollItem(_copperRing, Rarity.Common, 1, Array.Empty<AffixDefinition>());
        EquipmentItem ringC = RollItem(_copperRing, Rarity.Common, 1, Array.Empty<AffixDefinition>());

        bool fillsRing1 = _autoEquipEquipment.TryEquip(EquipmentSlot.Ring1, ringA, out _);
        Check("AUTOEQUIP-SETUP-RING1-FILLED", fillsRing1, "setup: manually fill Ring1 first");

        bool autoFillsRing2 = _autoEquipEquipment.TryAutoEquip(ringB);
        Check("AUTOEQUIP-FILLS-REMAINING-FREE-RING-SLOT",
            autoFillsRing2 && ReferenceEquals(_autoEquipEquipment.GetEquipped(EquipmentSlot.Ring2), ringB),
            $"with Ring1 occupied, auto-equip must land a second ring in the still-free Ring2: succeeded={autoFillsRing2}");

        bool autoEquipFailsWhenBothRingsFull = _autoEquipEquipment.TryAutoEquip(ringC);
        Check("AUTOEQUIP-LEAVES-ITEM-UNEQUIPPED-WHEN-NO-SLOT-FREE",
            !autoEquipFailsWhenBothRingsFull
            && !ReferenceEquals(_autoEquipEquipment.GetEquipped(EquipmentSlot.Ring1), ringC)
            && !ReferenceEquals(_autoEquipEquipment.GetEquipped(EquipmentSlot.Ring2), ringC),
            $"with both ring slots occupied, auto-equip must fail rather than displace anything — succeeded={autoEquipFailsWhenBothRingsFull}");

        _autoEquipEquipment.Unequip(EquipmentSlot.Ring1);
        _autoEquipEquipment.Unequip(EquipmentSlot.Ring2);
    }
}
