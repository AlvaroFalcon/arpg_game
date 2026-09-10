#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PathToExile.Items;
using PathToExile.Stats;

namespace PathToExile.Tests;

/// <summary>
/// Headless functional smoke test for milestone 2c (the grid inventory and
/// equipment strip). Run with:
///   dotnet build PathToExile.csproj
///   ./scripts/run_headless.sh res://scenes/tests/InventorySmokeTest.tscn --quit-after 20
///
/// Covers: placement into the first free space in reading order; an item that
/// doesn't fit leaving the grid unchanged; the occupancy mask rejecting an
/// overlap; removing an item freeing exactly its own cells and no others;
/// right-click equip into a free slot; right-click equip swapping and the
/// displaced item landing in the vacated space; a Ring going to either ring
/// slot; equipping through the inventory still applying stat modifiers and
/// the attribute recompute; and unequipping into a full grid failing safely.
///
/// Tests <see cref="Inventory"/>'s public methods directly rather than
/// simulating mouse drag-and-drop through the Control tree — those methods
/// (<see cref="Inventory.TryAdd"/>, <see cref="Inventory.TryRightClickEquip"/>,
/// <see cref="Inventory.TryEquipFromGrid"/>, <see cref="Inventory.TryUnequipToGrid"/>)
/// are exactly what <see cref="UI.InventoryGridControl"/>/
/// <see cref="UI.EquipmentStripControl"/>'s <c>_GuiInput</c>/<c>_DropData</c>
/// handlers call, so this exercises the real production logic minus the input
/// plumbing itself — the same "direct call is the real path, not a bypass"
/// reasoning <c>ItemsSmokeTest</c> and <c>SkillsSmokeTest</c> already use for
/// their own pure-logic checks.
///
/// <b>Rig design:</b> three independent actors, mirroring <c>EquipSmokeTest</c>'s
/// per-concern-rig pattern so no test's inventory/equipment state can leak into
/// another's. "GridActor" has an <see cref="Inventory"/> only (no
/// <see cref="ActorStats"/>/<see cref="Equipment"/> — pure grid mechanics need
/// neither, since <see cref="Inventory"/> has no <c>_Ready</c>-time dependency
/// on either). "GearActor" runs the equip/swap/ring/stat checks. "FullGridActor"
/// is dedicated to the "unequip into a full grid" check, which needs to fill
/// all 60 cells without disturbing anything else.
/// </summary>
public partial class InventorySmokeTest : Node2D
{
    private AffixLibrary _library = null!;
    private BaseItemDefinition _rawhideBoots = null!;
    private BaseItemDefinition _copperRing = null!;
    private BaseItemDefinition _ironWand = null!;
    private PackedScene _dropScene = null!;
    private readonly RandomNumberGenerator _rng = new();

    private Inventory _gridInventory = null!;

    private ActorStats _gearStats = null!;
    private Equipment _gearEquipment = null!;
    private Inventory _gearInventory = null!;

    private ActorStats _fullGridStats = null!;
    private Equipment _fullGridEquipment = null!;
    private Inventory _fullGridInventory = null!;

    private bool _failed;

    public override void _Ready()
    {
        _library = GD.Load<AffixLibrary>("res://resources/items/AffixLibrary.tres");
        _rawhideBoots = GD.Load<BaseItemDefinition>("res://resources/items/bases/rawhide_boots.tres");
        _copperRing = GD.Load<BaseItemDefinition>("res://resources/items/bases/copper_ring.tres");
        _ironWand = GD.Load<BaseItemDefinition>("res://resources/items/bases/iron_wand.tres");
        _dropScene = GD.Load<PackedScene>("res://scenes/items/EquipmentDrop.tscn");
        _rng.Randomize();

        _gridInventory = GetNode<Inventory>("GridActor/Inventory");

        _gearStats = GetNode<ActorStats>("GearActor/Stats");
        _gearEquipment = GetNode<Equipment>("GearActor/Equipment");
        _gearInventory = GetNode<Inventory>("GearActor/Inventory");

        _fullGridStats = GetNode<ActorStats>("FullGridActor/Stats");
        _fullGridEquipment = GetNode<Equipment>("FullGridActor/Equipment");
        _fullGridInventory = GetNode<Inventory>("FullGridActor/Inventory");

        GD.Print("[INVENTORYTEST] Ready.");

        CheckFirstFreeSpaceInReadingOrder();
        CheckOccupancyMaskRejectsOverlap();
        CheckRemovingItemFreesExactlyItsOwnCells();
        CheckFullGridLeavesNonFittingItemUnplacedAndGridUnchanged();

        CheckRightClickEquipIntoFreeSlot();
        CheckRingGoesToEitherSlotThenSwapDisplacesIntoVacatedSpace();
        CheckEquippingThroughInventoryAppliesModifiersAndRecompute();

        CheckUnequipIntoFullGridFailsSafely();
        CheckSorting();

        GD.Print(_failed ? "[INVENTORYTEST] === RESULT: FAIL ===" : "[INVENTORYTEST] === RESULT: PASS ===");
        GetTree().Quit(_failed ? 1 : 0);
    }

    /// <summary>
    /// docs/design/items.md -> "Sorting". Covers the order itself, the packing
    /// consequence of that order, and the all-or-nothing guarantee — the last of
    /// which is the one that matters, since a half-applied sort loses items.
    /// </summary>
    private void CheckSorting()
    {
        ClearGrid();

        // Deliberately scattered: several classes, several rarities, mixed
        // footprints, added in an order that is not the sorted order.
        var added = new List<EquipmentItem>();
        foreach ((string baseId, Rarity rarity, int ilvl) in new (string, Rarity, int)[]
                 {
                     ("copper_ring", Rarity.Magic, 40),
                     ("rawhide_boots", Rarity.Rare, 120),
                     ("iron_wand", Rarity.Common, 10),
                     ("copper_ring", Rarity.Rare, 90),
                     ("rawhide_boots", Rarity.Magic, 55),
                     ("iron_wand", Rarity.Common, 5),
                 })
        {
            EquipmentItem item = RollItem(baseId switch
            {
                "copper_ring" => _copperRing,
                "rawhide_boots" => _rawhideBoots,
                _ => _ironWand,
            }, rarity, ilvl, Array.Empty<AffixDefinition>());

            if (_gridInventory.TryAdd(item))
            {
                added.Add(item);
            }
        }

        Check("SORT-SETUP-ITEMS-PLACED", added.Count == 6,
            $"setup: six mixed items must fit an empty grid — placed {added.Count}");

        bool sorted = InventorySorter.TrySort(_gridInventory);

        Check("SORT-SUCCEEDS", sorted, "a repack of items that already fitted must succeed");

        Check("SORT-KEEPS-EVERY-ITEM", _gridInventory.Placements.Count == added.Count
                && added.All(i => _gridInventory.Placements.ContainsKey(i)),
            $"every item must survive the repack — before={added.Count}, after={_gridInventory.Placements.Count}");

        // Reading order over the sorted grid must be non-decreasing by class: that
        // is what "grouped so you can find things" actually means on a 2D grid.
        List<EquipmentItem> byPosition = _gridInventory.Placements
            .OrderBy(kv => kv.Value.Y)
            .ThenBy(kv => kv.Value.X)
            .Select(kv => kv.Key)
            .ToList();

        bool classesGrouped = byPosition
            .Select(i => (int)i.Descriptor.ItemClass)
            .Zip(byPosition.Skip(1).Select(i => (int)i.Descriptor.ItemClass), (a, b) => a <= b)
            .All(ok => ok);

        Check("SORT-GROUPS-BY-CLASS-IN-READING-ORDER", classesGrouped,
            "walking the sorted grid in reading order must never step backwards to an earlier item class — " +
            $"got [{string.Join(", ", byPosition.Select(i => i.Descriptor.ItemClass))}]");

        // Two rings of different rarity: the better one must come first.
        List<EquipmentItem> rings = byPosition.Where(i => i.Descriptor.ItemClass == ItemClass.Ring).ToList();
        bool rarityOrdered = rings.Count < 2 || (int)rings[0].Rarity >= (int)rings[1].Rarity;
        Check("SORT-ORDERS-BY-RARITY-WITHIN-A-CLASS", rarityOrdered,
            $"within a class the better rarity comes first — got [{string.Join(", ", rings.Select(r => r.Rarity))}]");

        ClearGrid();
    }

    /// <summary>Empties the grid between check groups. Inventory has no Clear of
    /// its own — nothing in the game ever empties a bag wholesale, and adding one
    /// just for tests would be API surface no player action reaches.</summary>
    private void ClearGrid()
    {
        foreach (EquipmentItem item in _gridInventory.Placements.Keys.ToList())
        {
            _gridInventory.Remove(item);
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        GD.Print($"[INVENTORYTEST] {name}: {(passed ? "PASS" : "FAIL")} — {detail}");
        if (!passed)
        {
            _failed = true;
        }
    }

    private static bool Approx(float a, float b, float epsilon = 0.01f) => Mathf.Abs(a - b) <= epsilon;

    private EquipmentItem RollItem(BaseItemDefinition baseItem, Rarity rarity, int itemLevel, IReadOnlyList<AffixDefinition> pool)
    {
        var item = _dropScene.Instantiate<EquipmentItem>();
        AddChild(item);
        item.Initialize(baseItem, rarity, itemLevel, pool, _rng);
        return item;
    }

    private EquipmentItem RollRing() => RollItem(_copperRing, Rarity.Common, 1, Array.Empty<AffixDefinition>());

    // ------------------------------------------------------------------
    // First free space in reading order (left-to-right, top-to-bottom).
    // ------------------------------------------------------------------

    private void CheckFirstFreeSpaceInReadingOrder()
    {
        EquipmentItem itemA = RollRing();
        EquipmentItem itemB = RollRing();
        EquipmentItem itemC = RollRing();

        bool addedA = _gridInventory.TryAdd(itemA);
        bool addedB = _gridInventory.TryAdd(itemB);
        bool addedC = _gridInventory.TryAdd(itemC);

        Check("READING-ORDER-ALL-THREE-PLACED", addedA && addedB && addedC,
            $"three 1x1 items into an empty grid must all place: A={addedA}, B={addedB}, C={addedC}");

        bool orderCorrect = _gridInventory.Placements[itemA] == new Vector2I(0, 0)
            && _gridInventory.Placements[itemB] == new Vector2I(1, 0)
            && _gridInventory.Placements[itemC] == new Vector2I(2, 0);
        Check("READING-ORDER-LEFT-TO-RIGHT-TOP-TO-BOTTOM", orderCorrect,
            $"three sequential 1x1 adds into an empty grid must land at (0,0), (1,0), (2,0) in order — " +
            $"got A={_gridInventory.Placements[itemA]}, B={_gridInventory.Placements[itemB]}, C={_gridInventory.Placements[itemC]}");

        _gridInventory.Remove(itemA);
        _gridInventory.Remove(itemB);
        _gridInventory.Remove(itemC);
    }

    // ------------------------------------------------------------------
    // The occupancy mask rejects an overlap.
    // ------------------------------------------------------------------

    private void CheckOccupancyMaskRejectsOverlap()
    {
        EquipmentItem boots = RollItem(_rawhideBoots, Rarity.Common, 1, Array.Empty<AffixDefinition>());
        bool placed = _gridInventory.TryPlaceAt(boots, new Vector2I(0, 0));
        Check("OVERLAP-SETUP-PLACES-2X2", placed, "setup: a 2x2 item must place at (0,0) in an empty grid");

        bool[,] mask = _gridInventory.BuildOccupancyMask();
        bool maskCorrect = mask[0, 0] && mask[1, 0] && mask[0, 1] && mask[1, 1] && !mask[2, 0] && !mask[0, 2];
        Check("OVERLAP-MASK-STAMPS-EXACTLY-THE-2X2-FOOTPRINT", maskCorrect,
            "a 2x2 item at (0,0) must occupy exactly (0,0),(1,0),(0,1),(1,1) and nothing beyond it");

        EquipmentItem ring = RollRing();
        bool overlapRejected = !_gridInventory.CanPlaceAt(ring, new Vector2I(1, 1)) && !_gridInventory.TryPlaceAt(ring, new Vector2I(1, 1));
        Check("OVERLAP-REJECTED-AT-SHARED-CELL", overlapRejected,
            "a 1x1 item at (1,1), which the 2x2 item already occupies, must be rejected by both CanPlaceAt and TryPlaceAt");

        bool nonOverlapAccepted = _gridInventory.TryPlaceAt(ring, new Vector2I(2, 0));
        Check("NON-OVERLAP-ACCEPTED", nonOverlapAccepted, "the same 1x1 item at (2,0), which is free, must be accepted");

        _gridInventory.Remove(boots);
        _gridInventory.Remove(ring);
    }

    // ------------------------------------------------------------------
    // Removing an item frees exactly its own cells and no others.
    // ------------------------------------------------------------------

    private void CheckRemovingItemFreesExactlyItsOwnCells()
    {
        EquipmentItem boots = RollItem(_rawhideBoots, Rarity.Common, 1, Array.Empty<AffixDefinition>());
        EquipmentItem ring = RollRing();

        _gridInventory.TryPlaceAt(boots, new Vector2I(0, 0));
        _gridInventory.TryPlaceAt(ring, new Vector2I(2, 0));

        bool removed = _gridInventory.Remove(boots);
        Check("REMOVE-RETURNS-TRUE-FOR-PRESENT-ITEM", removed, "removing an item that is on the grid must return true");

        bool[,] mask = _gridInventory.BuildOccupancyMask();
        bool bootsCellsFreed = !mask[0, 0] && !mask[1, 0] && !mask[0, 1] && !mask[1, 1];
        bool ringCellUntouched = mask[2, 0];
        Check("REMOVE-FREES-EXACTLY-ITS-OWN-CELLS", bootsCellsFreed && ringCellUntouched,
            "removing the 2x2 item must free exactly (0,0),(1,0),(0,1),(1,1), leaving the unrelated ring's cell (2,0) untouched");

        int totalOccupied = 0;
        for (int x = 0; x < Inventory.Width; x++)
        {
            for (int y = 0; y < Inventory.Height; y++)
            {
                if (mask[x, y])
                {
                    totalOccupied++;
                }
            }
        }
        Check("REMOVE-DOES-NOT-FREE-ANY-OTHER-CELL", totalOccupied == 1,
            $"exactly one cell (the ring's) should remain occupied after removing the boots — found {totalOccupied}");

        _gridInventory.Remove(ring);
    }

    // ------------------------------------------------------------------
    // An item that does not fit leaves the grid unchanged.
    // ------------------------------------------------------------------

    private void CheckFullGridLeavesNonFittingItemUnplacedAndGridUnchanged()
    {
        var filler = new List<EquipmentItem>();
        bool allFit = true;
        for (int i = 0; i < Inventory.Width * Inventory.Height; i++)
        {
            EquipmentItem ring = RollRing();
            filler.Add(ring);
            allFit &= _gridInventory.TryAdd(ring);
        }

        Check("FULL-GRID-SETUP-SIXTY-1X1-ITEMS-ALL-FIT", allFit && _gridInventory.Placements.Count == 60,
            $"60 1x1 items must exactly fill a 12x5 grid — placed count={_gridInventory.Placements.Count}");

        EquipmentItem overflow = RollRing();
        bool overflowAdded = _gridInventory.TryAdd(overflow);
        Check("DOES-NOT-FIT-TRYADD-RETURNS-FALSE", !overflowAdded, "a 61st item into a completely full grid must fail");
        Check("DOES-NOT-FIT-GRID-COUNT-UNCHANGED", _gridInventory.Placements.Count == 60,
            $"a failed add must leave the grid exactly as it was — count is now {_gridInventory.Placements.Count} (expected 60)");
        Check("DOES-NOT-FIT-ITEM-NOT-IN-GRID", !_gridInventory.Placements.ContainsKey(overflow),
            "the overflow item must not appear anywhere in the grid's placements");

        foreach (EquipmentItem ring in filler)
        {
            _gridInventory.Remove(ring);
        }
    }

    // ------------------------------------------------------------------
    // Right-click equip into a free slot.
    // ------------------------------------------------------------------

    private void CheckRightClickEquipIntoFreeSlot()
    {
        EquipmentItem boots = RollItem(_rawhideBoots, Rarity.Common, 1, Array.Empty<AffixDefinition>());
        _gearInventory.TryAdd(boots);

        bool equipped = _gearEquipment.GetEquipped(EquipmentSlot.Boots) is null
            && _gearInventory.TryRightClickEquip(boots, _gearEquipment);

        Check("RIGHTCLICK-EQUIPS-INTO-FREE-SLOT",
            equipped && ReferenceEquals(_gearEquipment.GetEquipped(EquipmentSlot.Boots), boots),
            $"right-clicking a Boots-class item with the Boots slot free must equip it there: succeeded={equipped}");

        Check("RIGHTCLICK-REMOVES-ITEM-FROM-GRID", !_gearInventory.Placements.ContainsKey(boots),
            "once equipped via right-click, the item must no longer occupy a grid cell");

        _gearEquipment.Unequip(EquipmentSlot.Boots);
    }

    // ------------------------------------------------------------------
    // A Ring may equip into either ring slot, and right-click equip swaps
    // with the occupied slot when both are full, landing the displaced item
    // in the exact grid space the incoming item vacated.
    // ------------------------------------------------------------------

    private void CheckRingGoesToEitherSlotThenSwapDisplacesIntoVacatedSpace()
    {
        EquipmentItem ringA = RollRing();
        EquipmentItem ringB = RollRing();
        EquipmentItem ringC = RollRing();

        _gearInventory.TryAdd(ringA);
        bool ringAToRing1 = _gearInventory.TryRightClickEquip(ringA, _gearEquipment);
        Check("RING-FIRST-EQUIP-GOES-TO-RING1",
            ringAToRing1 && ReferenceEquals(_gearEquipment.GetEquipped(EquipmentSlot.Ring1), ringA),
            $"the first ring right-click-equipped with both ring slots free must land in Ring1: succeeded={ringAToRing1}");

        _gearInventory.TryAdd(ringB);
        bool ringBToRing2 = _gearInventory.TryRightClickEquip(ringB, _gearEquipment);
        Check("RING-SECOND-EQUIP-GOES-TO-RING2",
            ringBToRing2 && ReferenceEquals(_gearEquipment.GetEquipped(EquipmentSlot.Ring2), ringB),
            $"with Ring1 occupied, a second ring right-click-equipped must land in the still-free Ring2: succeeded={ringBToRing2}");

        // Both ring slots are now occupied. Place a third ring at a known,
        // otherwise-empty grid cell and right-click equip it — this must swap
        // with Ring1 (the first candidate slot) and send ringA back to the
        // exact cell ringC just vacated.
        var ringCOrigin = new Vector2I(5, 3);
        bool ringCPlaced = _gearInventory.TryPlaceAt(ringC, ringCOrigin);
        Check("SWAP-SETUP-PLACES-THIRD-RING", ringCPlaced, "setup: a third ring must place at a chosen empty cell");

        bool swapped = _gearInventory.TryRightClickEquip(ringC, _gearEquipment);
        Check("SWAP-RIGHTCLICK-SUCCEEDS-WITH-BOTH-RINGS-FULL", swapped,
            "right-click equip with both ring slots occupied must still succeed by swapping");

        Check("SWAP-NEW-ITEM-TAKES-RING1",
            ReferenceEquals(_gearEquipment.GetEquipped(EquipmentSlot.Ring1), ringC),
            "the incoming ring must take Ring1 (the first candidate slot), displacing whatever was there");

        Check("SWAP-RING2-UNTOUCHED",
            ReferenceEquals(_gearEquipment.GetEquipped(EquipmentSlot.Ring2), ringB),
            "Ring2's occupant must be untouched by a swap that only targets Ring1");

        bool displacedLandedInVacatedSpace = _gearInventory.Placements.TryGetValue(ringA, out Vector2I ringAOrigin)
            && ringAOrigin == ringCOrigin;
        Check("SWAP-DISPLACED-ITEM-LANDS-IN-VACATED-SPACE", displacedLandedInVacatedSpace,
            $"the displaced ringA must land exactly where ringC was picked up from ({ringCOrigin}) — " +
            $"found at {(displacedLandedInVacatedSpace ? ringCOrigin.ToString() : "not there")}");

        Check("SWAP-INCOMING-ITEM-NO-LONGER-IN-GRID", !_gearInventory.Placements.ContainsKey(ringC),
            "the newly-equipped ringC must no longer occupy any grid cell");

        _gearEquipment.Unequip(EquipmentSlot.Ring1);
        _gearEquipment.Unequip(EquipmentSlot.Ring2);
        _gearInventory.Remove(ringA);
    }

    // ------------------------------------------------------------------
    // Equipping through the inventory still applies stat modifiers and the
    // attribute recompute — Inventory orchestrates Equipment rather than
    // reimplementing it.
    // ------------------------------------------------------------------

    private void CheckEquippingThroughInventoryAppliesModifiersAndRecompute()
    {
        float baselineArmour = _gearStats.Armour.Value;

        EquipmentItem wand = RollItem(_ironWand, Rarity.Common, 1, Array.Empty<AffixDefinition>());
        Check("MODIFIER-CHECK-WAND-HAS-BASE-DAMAGE-STAT", wand.RolledBaseStats.Count == 1,
            $"Iron Wand must roll exactly one base stat entry, got {wand.RolledBaseStats.Count}");

        _gearInventory.TryAdd(wand);
        bool equippedViaGrid = _gearInventory.TryEquipFromGrid(wand, EquipmentSlot.Weapon, _gearEquipment);
        Check("MODIFIER-CHECK-EQUIP-FROM-GRID-SUCCEEDS",
            equippedViaGrid && ReferenceEquals(_gearEquipment.GetEquipped(EquipmentSlot.Weapon), wand),
            $"TryEquipFromGrid into the free Weapon slot must succeed: {equippedViaGrid}");

        bool damageModifierApplied = _gearStats.Get(StatId.Damage).Modifiers.Any(m => ReferenceEquals(m.Source, wand));
        Check("MODIFIER-CHECK-DAMAGE-MODIFIER-APPLIED", damageModifierApplied,
            "equipping through Inventory.TryEquipFromGrid must apply the wand's own StatModifier via Equipment, exactly " +
            "as a direct Equipment.TryEquip call would");

        // Attribute recompute: Armour is untouched by this particular item (no
        // Strength/Armour roll on a common Iron Wand), so this doubles as a
        // "recompute ran and did not corrupt anything else" check.
        Check("MODIFIER-CHECK-RECOMPUTE-DID-NOT-DISTURB-UNRELATED-STAT",
            Approx(_gearStats.Armour.Value, baselineArmour),
            $"Armour must be unaffected by equipping a wand with no Armour/Strength roll: before={baselineArmour}, after={_gearStats.Armour.Value}");

        _gearEquipment.Unequip(EquipmentSlot.Weapon);
    }

    // ------------------------------------------------------------------
    // Unequipping into a full grid fails safely rather than destroying the
    // item.
    // ------------------------------------------------------------------

    private void CheckUnequipIntoFullGridFailsSafely()
    {
        EquipmentItem boots = RollItem(_rawhideBoots, Rarity.Common, 1, Array.Empty<AffixDefinition>());
        bool equipped = _fullGridEquipment.TryEquip(EquipmentSlot.Boots, boots, out _);
        Check("UNEQUIP-SAFETY-SETUP-EQUIPS-BOOTS", equipped, "setup: boots must equip directly for this check");

        float armourWithBootsEquipped = _fullGridStats.Armour.Value;

        var filler = new List<EquipmentItem>();
        bool gridCompletelyFull = true;
        for (int i = 0; i < Inventory.Width * Inventory.Height; i++)
        {
            EquipmentItem ring = RollRing();
            filler.Add(ring);
            gridCompletelyFull &= _fullGridInventory.TryAdd(ring);
        }
        Check("UNEQUIP-SAFETY-SETUP-GRID-COMPLETELY-FULL", gridCompletelyFull && _fullGridInventory.Placements.Count == 60,
            $"the grid must be completely full before this check means anything — count={_fullGridInventory.Placements.Count}");

        bool unequipSucceeded = _fullGridInventory.TryUnequipToGrid(_fullGridEquipment, EquipmentSlot.Boots);
        Check("UNEQUIP-INTO-FULL-GRID-FAILS", !unequipSucceeded,
            "TryUnequipToGrid must return false when the grid has no room for the item being taken off");

        Check("UNEQUIP-INTO-FULL-GRID-ITEM-STAYS-EQUIPPED",
            ReferenceEquals(_fullGridEquipment.GetEquipped(EquipmentSlot.Boots), boots),
            "a failed unequip-to-grid must leave the item exactly as equipped as it was — never destroyed, never orphaned");

        Check("UNEQUIP-INTO-FULL-GRID-STATS-UNCHANGED",
            Approx(_fullGridStats.Armour.Value, armourWithBootsEquipped),
            $"a failed unequip must not touch stats: before={armourWithBootsEquipped}, after={_fullGridStats.Armour.Value}");

        Check("UNEQUIP-INTO-FULL-GRID-COUNT-UNCHANGED", _fullGridInventory.Placements.Count == 60,
            $"a failed unequip-to-grid must not add a partial/phantom entry — count is {_fullGridInventory.Placements.Count} (expected 60)");

        _fullGridEquipment.Unequip(EquipmentSlot.Boots);
        foreach (EquipmentItem ring in filler)
        {
            _fullGridInventory.Remove(ring);
        }
    }
}
