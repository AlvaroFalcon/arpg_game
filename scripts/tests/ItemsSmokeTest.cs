#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using PathToExile.Items;
using PathToExile.UI;

namespace PathToExile.Tests;

/// <summary>
/// Headless functional smoke test for the M1 item/loot slice
/// (docs/design/items.md -> "Build order" item 1: "drop pipeline, ItemDescriptor,
/// FilterChain, labels, and currency/gold"), extended in milestone 2d for
/// inventory usability (docs/design/items.md -> "Pickup is manual...", "Closing
/// panels", "UI must consume its own input", "Item tooltips"). Run with:
///   dotnet build PathToExile.csproj
///   ./scripts/run_headless.sh res://scenes/tests/ItemsSmokeTest.tscn --quit-after 60
///
/// Descriptor construction and filter-chain evaluation are pure C# and exercised
/// by direct calls in <see cref="_Ready"/> — there's no input or physics
/// dependency for that part, so a direct call is the real production path, not a
/// bypass (same rationale SkillsSmokeTest gives for calling TryCast directly).
///
/// The currency pickup path is deliberately tested through *real* physics rather
/// than a direct <c>OnPickup</c> call: the scene places a <see cref="CurrencyDrop"/>
/// exactly on the player's spawn position so <c>Area2D.BodyEntered</c> fires for
/// real within the first few physics frames. That is what proves "filters
/// control display, never behaviour" end to end — a hidden-styled drop is still
/// collected by the actual walk-over signal path, not just by an OnPickup method
/// that happens to ignore style in isolation.
///
/// The milestone-2d additions use the same "real production path" philosophy but
/// pick the cheapest real path per concern, matching <c>InventorySmokeTest</c>'s
/// own precedent ("tests Inventory's public methods directly rather than
/// simulating mouse drag-and-drop"):
/// - Manual pickup: <see cref="EquipmentItem.OnPickup"/> called directly — the
///   exact method both the ground-label click and the sprite's
///   <see cref="Area2D.InputEvent"/> handler call internally.
/// - UI input consumption and Escape: these two are specifically about the
///   *input-routing* plumbing itself, so they go through the real thing —
///   <see cref="Viewport.PushInput"/> injecting a genuine
///   <see cref="InputEventMouseButton"/> at the grid Control's actual screen
///   rect, and <see cref="Godot.Input.ActionPress"/> for
///   <c>toggle_inventory</c>/<c>ui_cancel</c> — because a direct method call
///   would bypass exactly the dispatch-order question this milestone's bug was
///   about.
///
///   <b><c>Viewport.PushInput</c>, not <c>Input.ParseInputEvent</c>.</b> The
///   two look interchangeable but are not, in headless mode specifically:
///   <c>Input.ParseInputEvent</c> queues onto the OS-level input pump, which
///   headless's display driver never runs, so the event silently never
///   reaches any node's <c>_UnhandledInput</c> and — this is the trap —
///   <c>Input.IsActionPressed</c> never reflects it either, so a check
///   written against <c>ParseInputEvent</c> "passes" whether or not the fix
///   under test actually works. <c>Viewport.PushInput</c> feeds the
///   viewport's own dispatch directly (the same mechanism Godot's own docs
///   use to drive UI from code, e.g. XR composition-layer pointer input) and
///   does reach <c>_GuiInput</c>/<c>_UnhandledInput</c> headless. It does not,
///   however, update the <c>Input</c> singleton's own held-state — production
///   code and real hardware do both at once, so a held-button check needs
///   <c>Input.ActionPress</c> alongside it to reproduce that; see
///   <see cref="InjectClickOverGrid"/>.
/// </summary>
public partial class ItemsSmokeTest : Node2D
{
    private Player _player = null!;
    private CurrencyDrop _drop = null!;
    private ItemDescriptor _dropDescriptorAtSpawn = null!;

    private EquipmentItem _equipmentWalkOver = null!;
    private EquipmentItem _equipmentForClick = null!;
    private EquipmentItem _equipmentFullInventory = null!;

    private AffixLibrary _library = null!;
    private BaseItemDefinition _ironWand = null!;
    private BaseItemDefinition _copperRing = null!;
    private PackedScene _equipmentDropScene = null!;
    private readonly RandomNumberGenerator _rng = new();
    private readonly List<EquipmentItem> _fillerItems = new();

    private NavigationAgent2D _navAgent = null!;
    private Control _inventoryPanel = null!;
    private Control _gridControl = null!;

    private int _frame;
    private bool _failed;

    public override void _Ready()
    {
        _player = GetNode<Player>("Player");
        _drop = GetNode<CurrencyDrop>("CurrencyDrop");

        _equipmentWalkOver = GetNode<EquipmentItem>("EquipmentDropWalkOver");
        _equipmentForClick = GetNode<EquipmentItem>("EquipmentDropForClick");
        _equipmentFullInventory = GetNode<EquipmentItem>("EquipmentDropFullInventory");

        _navAgent = _player.GetNode<NavigationAgent2D>("NavigationAgent2D");
        _inventoryPanel = _player.GetNode<Control>("InventoryUI/Panel");
        _gridControl = _player.GetNode<Control>("InventoryUI/Panel/GridControl");

        _library = GD.Load<AffixLibrary>("res://resources/items/AffixLibrary.tres");
        _ironWand = GD.Load<BaseItemDefinition>("res://resources/items/bases/iron_wand.tres");
        _copperRing = GD.Load<BaseItemDefinition>("res://resources/items/bases/copper_ring.tres");
        _equipmentDropScene = GD.Load<PackedScene>("res://scenes/items/EquipmentDrop.tscn");
        _rng.Randomize();

        _equipmentWalkOver.Initialize(_ironWand, Rarity.Common, 1, System.Array.Empty<AffixDefinition>(), _rng);
        _equipmentForClick.Initialize(_ironWand, Rarity.Magic, 60, SinglePool("vigorous"), _rng);
        _equipmentFullInventory.Initialize(_ironWand, Rarity.Common, 1, System.Array.Empty<AffixDefinition>(), _rng);

        // Captured now, before physics has had a chance to collect the drop, so
        // the later HIDDEN-DROP-STILL-AUTO-COLLECTED check can evaluate a filter
        // against the exact descriptor this drop rolled even after the node has
        // freed itself.
        _dropDescriptorAtSpawn = _drop.Descriptor;

        GD.Print("[ITEMSTEST] Ready.");

        CheckDescriptorConstruction();
        CheckFilterChainFirstMatchWins();
        CheckHiddenRuleStillEvaluatesHidden();
        CheckWalletIncrementDirect();
        CheckTooltipTextIncludesRarityColourBaseAndTiers();
    }

    private void Check(string name, bool passed, string detail)
    {
        GD.Print($"[ITEMSTEST] {name}: {(passed ? "PASS" : "FAIL")} — {detail}");
        if (!passed)
        {
            _failed = true;
        }
    }

    private List<AffixDefinition> SinglePool(string affixId) =>
        new() { _library.Entries.First(a => a.Id == affixId) };

    /// <summary>
    /// Builds a descriptor exercising every field, including the ones with no
    /// milestone-1 producer (AffixTiers, SetId, MajesticEffectId, SocketCount,
    /// ...), and confirms every one round-trips through the init-only properties
    /// unchanged. This is "the vocabulary is fixed before items exist," checked
    /// mechanically rather than just asserted in prose.
    /// </summary>
    private void CheckDescriptorConstruction()
    {
        var descriptor = new ItemDescriptor
        {
            ItemClass = ItemClass.Currency,
            BaseTypeId = "iron_wand",
            Rarity = Rarity.Rare,
            ItemLevel = 42,
            AreaLevel = 40,
            AffixCount = 3,
            AffixTiers = new List<int> { 2, 4, 6 },
            HighestAffixTier = 6,
            SetId = "vestige_of_the_hollow",
            MajesticEffectId = "emberwake",
            SocketCount = 2,
            StackSize = 1,
            Width = 1,
            Height = 2,
            DisplayName = "Test Item",
            CurrencyId = "gold",
            Amount = 250,
            Tags = ItemTags.None,
        };

        bool ok = descriptor.ItemClass == ItemClass.Currency
            && descriptor.BaseTypeId == "iron_wand"
            && descriptor.Rarity == Rarity.Rare
            && descriptor.ItemLevel == 42
            && descriptor.AreaLevel == 40
            && descriptor.AffixCount == 3
            && descriptor.AffixTiers.Count == 3
            && descriptor.AffixTiers[1] == 4
            && descriptor.HighestAffixTier == 6
            && descriptor.SetId == "vestige_of_the_hollow"
            && descriptor.MajesticEffectId == "emberwake"
            && descriptor.SocketCount == 2
            && descriptor.StackSize == 1
            && descriptor.Width == 1
            && descriptor.Height == 2
            && descriptor.DisplayName == "Test Item"
            && descriptor.CurrencyId == new CurrencyId("gold")
            && descriptor.Amount == 250
            && descriptor.Tags == ItemTags.None;

        Check("DESCRIPTOR-CONSTRUCTION", ok, "every field round-trips through init-only properties, including no-producer-yet fields");

        // What every milestone-1 currency drop's descriptor actually looks like:
        // everything left at its default. Confirm those defaults are sane
        // (Rarity.Common, an empty-not-null tiers list) rather than leaving it
        // implicit.
        var bare = new ItemDescriptor();
        Check("DESCRIPTOR-DEFAULTS", bare.Rarity == Rarity.Common && bare.AffixTiers.Count == 0 && bare.DisplayName == "",
            $"Rarity={bare.Rarity}, AffixTiers.Count={bare.AffixTiers.Count}, DisplayName='{bare.DisplayName}'");
    }

    /// <summary>
    /// Two rules that both match the same descriptor — the case that actually
    /// distinguishes "first match wins" from "some match wins," which a chain
    /// with no overlapping rules cannot do.
    /// </summary>
    private void CheckFilterChainFirstMatchWins()
    {
        var descriptor = new ItemDescriptor { Rarity = Rarity.Rare };
        var first = new DisplayStyle { TextColor = Colors.Red };
        var second = new DisplayStyle { TextColor = Colors.Blue };

        var chain = new FilterChain(new[]
        {
            new FilterRule(d => d.Rarity == Rarity.Rare, first),
            new FilterRule(d => d.Rarity == Rarity.Rare, second), // also matches — must never win
        });

        DisplayStyle result = chain.Evaluate(descriptor);
        Check("FILTER-FIRST-MATCH-WINS", ReferenceEquals(result, first) && result.TextColor == Colors.Red,
            $"resolved TextColor={result.TextColor} (expected Red from the first rule, not Blue from the second)");

        var emptyChain = new FilterChain();
        DisplayStyle baseline = emptyChain.Evaluate(descriptor);
        Check("FILTER-NO-MATCH-IS-BASELINE", ReferenceEquals(baseline, DisplayStyle.Baseline),
            "an empty chain evaluates to DisplayStyle.Baseline, not an exception or a null");

        // The built-in default filter: the currency rule must resolve before the
        // rarity rules, since every descriptor (including currency) carries a
        // default Rarity.Common — see BuiltInDefaultRules' ordering note.
        var currencyDescriptor = new ItemDescriptor { CurrencyId = "gold", Rarity = Rarity.Common };
        DisplayStyle currencyStyle = FilterChain.Default.Evaluate(currencyDescriptor);
        DisplayStyle commonStyle = FilterChain.Default.Evaluate(new ItemDescriptor { Rarity = Rarity.Common });
        Check("DEFAULT-FILTER-CURRENCY-BEFORE-RARITY", currencyStyle.TextColor != commonStyle.TextColor,
            $"currency style {currencyStyle.TextColor} vs common-rarity style {commonStyle.TextColor} (must differ)");
    }

    /// <summary>
    /// Builds a filter that would hide this exact drop and confirms the filter
    /// layer correctly reports Hidden=true. The complementary half of the
    /// guarantee — that Hidden has zero effect on pickup — is proven by real
    /// physics in <see cref="_PhysicsProcess"/> below, through a code path that
    /// never touches this or any other FilterChain at all.
    /// </summary>
    private void CheckHiddenRuleStillEvaluatesHidden()
    {
        FilterChain userChain = FilterChain.CreateDefault();
        userChain.PrependRules(new[]
        {
            new FilterRule(d => d.CurrencyId == _dropDescriptorAtSpawn.CurrencyId, new DisplayStyle { Hidden = true }),
        });

        DisplayStyle style = userChain.Evaluate(_dropDescriptorAtSpawn);
        Check("HIDDEN-RULE-EVALUATES-HIDDEN", style.Hidden == true,
            $"a 'hide this gold' rule prepended ahead of the defaults resolves Hidden={style.Hidden}");
    }

    private void CheckWalletIncrementDirect()
    {
        var wallet = new Wallet();
        var silver = new CurrencyId("silver");

        Check("WALLET-STARTS-AT-ZERO", wallet.GetBalance(silver) == 0L, $"balance={wallet.GetBalance(silver)}");

        wallet.Add(silver, 10);
        wallet.Add(silver, 5);
        Check("WALLET-INCREMENT-ACCUMULATES", wallet.GetBalance(silver) == 15L,
            $"balance after +10, +5 = {wallet.GetBalance(silver)} (expected 15)");

        wallet.Add("gold", 1);
        Check("WALLET-BALANCES-ARE-PER-CURRENCY", wallet.GetBalance(silver) == 15L && wallet.GetBalance("gold") == 1L,
            $"silver={wallet.GetBalance(silver)}, gold={wallet.GetBalance("gold")}");
    }

    /// <summary>
    /// docs/design/items.md -&gt; "Item tooltips": name in rarity colour, base
    /// type, item level, rolled affixes with tiers. Pure logic — no scene
    /// dependency — so it's checked the same way <c>ItemNaming</c>/<c>AffixRoller</c>
    /// already are elsewhere: a direct call against a real rolled item.
    /// </summary>
    private void CheckTooltipTextIncludesRarityColourBaseAndTiers()
    {
        string text = ItemTooltipText.Build(_equipmentForClick);
        DisplayStyle expectedStyle = FilterChain.Default.Evaluate(_equipmentForClick.Descriptor);
        string expectedColorTag = $"[color=#{(expectedStyle.TextColor ?? Colors.White).ToHtml(false)}]";

        Check("TOOLTIP-NAME-LINE-USES-FILTERCHAIN-COLOUR", text.StartsWith(expectedColorTag),
            $"tooltip must open with the exact colour FilterChain.Default resolved for this item's descriptor " +
            $"(expected prefix '{expectedColorTag}'), not a hardcoded rarity->colour mapping — got: {text.Split('\n')[0]}");

        Check("TOOLTIP-INCLUDES-BASE-TYPE", text.Contains(_equipmentForClick.Base!.DisplayName),
            $"tooltip must include the base type name '{_equipmentForClick.Base.DisplayName}' — got:\n{text}");

        Check("TOOLTIP-INCLUDES-ITEM-LEVEL", text.Contains($"Item Level {_equipmentForClick.Descriptor.ItemLevel}"),
            $"tooltip must state the item level ({_equipmentForClick.Descriptor.ItemLevel}) — got:\n{text}");

        Check("TOOLTIP-INCLUDES-AFFIX-TIER", _equipmentForClick.RolledAffixes.Count > 0
            && text.Contains($"T{_equipmentForClick.RolledAffixes[0].Tier} "),
            $"a rolled affix's tier number must be visible per docs/design/affixes.md -> 'Tiers are player-visible' " +
            $"— got:\n{text}");
    }

    // ------------------------------------------------------------------
    // Manual pickup: docs/design/items.md -> "Pickup is manual for items,
    // automatic for currency".
    // ------------------------------------------------------------------

    private void CheckEquipmentDoesNotAutoPickupOnWalkOver()
    {
        bool stillOnGround = IsInstanceValid(_equipmentWalkOver) && _equipmentWalkOver.IsInsideTree();
        bool notInInventory = !_player.Inventory.Placements.ContainsKey(_equipmentWalkOver);

        Check("EQUIPMENT-DOES-NOT-AUTO-PICKUP-ON-WALKOVER", stillOnGround && notInInventory,
            $"an EquipmentItem sitting exactly on the player's spawn point for several physics frames must NOT be " +
            $"collected — stillOnGround={stillOnGround}, notInInventory={notInInventory} (contrast currency, which " +
            "auto-collects from the exact same overlap below)");
    }

    /// <summary>
    /// Calls the exact method both click affordances (ground label, sprite)
    /// invoke — see this type's class doc on why a direct call is the right
    /// "real path" for this concern specifically.
    /// </summary>
    private void CheckClickPicksUpEquipment()
    {
        bool hadRoomBefore = true; // fresh inventory, nothing added yet.

        // Hovering before the click is the real sequence: you cannot click an item
        // you are not pointing at, so the tooltip is always showing when pickup
        // happens.
        // Reach, per docs/design/items.md -> "Pickup has a reach". Checked before the
        // pickup below moves the item, and from both sides: a radius that is always
        // true would pass a near-only assertion.
        Vector2 itemPos = _equipmentForClick.GlobalPosition;
        Vector2 playerHome = _player.GlobalPosition;

        _player.GlobalPosition = itemPos + new Vector2(600f, 0f);
        bool farRejected = !_equipmentForClick.IsWithinPickupReach(_player);

        _player.GlobalPosition = itemPos + new Vector2(40f, 0f);
        bool nearAccepted = _equipmentForClick.IsWithinPickupReach(_player);

        Check("PICKUP-REACH-REJECTS-DISTANT-CLICK", farRejected,
            $"a player 600px away must be out of reach of a {_equipmentForClick.PickupRadiusPx}px radius");
        Check("PICKUP-REACH-ACCEPTS-ADJACENT-CLICK", nearAccepted,
            "a player 40px away must be within reach — otherwise the check above passes trivially");

        _player.GlobalPosition = playerHome;

        var tooltip = GetNode<UI.ItemTooltipControl>("ItemTooltip");
        tooltip.Show(_equipmentForClick, _equipmentForClick);
        bool tooltipShownBefore = tooltip.Visible;

        _equipmentForClick.OnPickup(_player);

        bool leftGround = !_equipmentForClick.IsInsideTree();
        bool inInventory = _player.Inventory.Placements.ContainsKey(_equipmentForClick);

        Check("CLICK-PICKS-UP-EQUIPMENT", hadRoomBefore && leftGround && inInventory,
            $"OnPickup with room available must remove the item from the ground and place it in the grid — " +
            $"leftGround={leftGround}, inInventory={inInventory}");

        Check("PICKUP-CLEARS-ITS-OWN-TOOLTIP", tooltipShownBefore && !tooltip.Visible,
            $"the tooltip must not survive the item being picked up — shown before={tooltipShownBefore}, " +
            $"still visible after={tooltip.Visible}. MouseExited never fires on pickup (the item leaves the tree " +
            "in the same frame the click lands), so the tooltip sat on screen describing an item already in the bag.");
    }

    /// <summary>
    /// Fills the grid completely (mirrors <c>InventorySmokeTest</c>'s own
    /// full-grid setup), then confirms a click on a still-on-ground item fails
    /// safely: nothing moves, nothing is destroyed, the label is untouched. Then
    /// frees one filler cell and retries, proving the item was never
    /// half-detached by the failed attempt — docs/design/items.md's watch-out:
    /// "do not leave the item half-detached on a failed pickup."
    /// </summary>
    private IEnumerable<Vector2I> EnumerateFreeCells()
    {
        bool[,] mask = _player.Inventory.BuildOccupancyMask();
        for (int y = 0; y < Items.Inventory.Height; y++)
        {
            for (int x = 0; x < Items.Inventory.Width; x++)
            {
                if (!mask[x, y])
                {
                    yield return new Vector2I(x, y);
                }
            }
        }
    }

    private void CheckClickWithFullInventoryFailsSafely()
    {
        int guard = 0;
        while (guard++ < 100)
        {
            var filler = _equipmentDropScene.Instantiate<EquipmentItem>();
            AddChild(filler);
            filler.Initialize(_copperRing, Rarity.Common, 1, System.Array.Empty<AffixDefinition>(), _rng);
            if (!_player.Inventory.TryAdd(filler))
            {
                filler.QueueFree();
                break;
            }

            _fillerItems.Add(filler);
        }

        // Placements.Count is an *item* count, not a cell count — the already-
        // equipped-into-the-grid 1x2 wand is one entry covering two cells, so
        // the right way to confirm "completely full" is zero free cells in the
        // occupancy mask, not entry count == 60.
        int countBeforeAttempt = _player.Inventory.Placements.Count;
        int freeCellsBeforeAttempt = EnumerateFreeCells().Count();
        Check("FULL-INVENTORY-SETUP-GRID-IS-FULL", freeCellsBeforeAttempt == 0,
            $"setup must completely fill the 60-cell grid before this check means anything — freeCells={freeCellsBeforeAttempt}, " +
            $"placementEntries={countBeforeAttempt} (1 wand covering 2 cells + {_fillerItems.Count} 1x1 fillers)");

        bool hadLabelBefore = _equipmentFullInventory.HasGroundLabel;

        _equipmentFullInventory.OnPickup(_player);

        bool stillOnGround = IsInstanceValid(_equipmentFullInventory) && _equipmentFullInventory.IsInsideTree();
        bool stillNotInInventory = !_player.Inventory.Placements.ContainsKey(_equipmentFullInventory);
        bool countUnchanged = _player.Inventory.Placements.Count == countBeforeAttempt;
        bool labelStillIntact = _equipmentFullInventory.HasGroundLabel == hadLabelBefore;

        Check("CLICK-WITH-FULL-INVENTORY-FAILS-SAFELY",
            stillOnGround && stillNotInInventory && countUnchanged && labelStillIntact,
            $"a click with no room must leave the item exactly where it was, label intact — stillOnGround={stillOnGround}, " +
            $"stillNotInInventory={stillNotInInventory}, countUnchanged={countUnchanged} (before={countBeforeAttempt}, " +
            $"after={_player.Inventory.Placements.Count}), labelStillIntact={labelStillIntact}");

        // Free exactly enough room and retry — the "not half-detached" proof.
        // _equipmentFullInventory is a 1x2 Iron Wand, so freeing a single 1x1
        // filler cell is not enough (its neighbours are still all occupied) —
        // free the two fillers occupying a vertically-adjacent pair instead.
        // Column 0 is where _equipmentForClick's own 1x2 footprint already
        // landed (first-fit reading order), so column 1's rows 0-1 are
        // guaranteed to be two separate 1x1 fillers rather than part of that
        // footprint — looked up by actual placement rather than by list index,
        // since the wand's footprint shifts where in reading order the fillers
        // themselves landed.
        var freeA = new Vector2I(1, 0);
        var freeB = new Vector2I(1, 1);
        EquipmentItem fillerTop = _fillerItems.First(f => _player.Inventory.Placements[f] == freeA);
        EquipmentItem fillerBottom = _fillerItems.First(f => _player.Inventory.Placements[f] == freeB);
        _player.Inventory.Remove(fillerTop);
        _player.Inventory.Remove(fillerBottom);
        fillerTop.QueueFree();
        fillerBottom.QueueFree();
        _fillerItems.Remove(fillerTop);
        _fillerItems.Remove(fillerBottom);

        _equipmentFullInventory.OnPickup(_player);
        bool succeededOnceRoomExists = !_equipmentFullInventory.IsInsideTree()
            && _player.Inventory.Placements.ContainsKey(_equipmentFullInventory);
        Check("RETRY-AFTER-FREEING-SPACE-SUCCEEDS", succeededOnceRoomExists,
            "the same item, once a cell is free, must still be pickable — proving the earlier failed attempt never " +
            "half-detached it from the ground");
    }

    // ------------------------------------------------------------------
    // docs/design/items.md -> "UI must consume its own input" and "Closing
    // panels".
    // ------------------------------------------------------------------

    /// <summary>Opens the inventory the same way the player does — a real
    /// <c>toggle_inventory</c> action press — rather than reaching into
    /// <see cref="InventoryUI"/>'s private panel field, so this also exercises
    /// <see cref="InventoryUI"/>'s own toggle path (and its <see cref="PanelStack"/>
    /// registration, which the later Escape check depends on).</summary>
    private void OpenInventoryViaToggleKey() => Input.ActionPress("toggle_inventory");

    private void ReleaseToggleKey() => Input.ActionRelease("toggle_inventory");

    private void CheckPanelOpened()
    {
        Check("PANEL-OPENS-VIA-TOGGLE-KEY", _inventoryPanel.Visible,
            "InventoryUI must open its panel in response to a real toggle_inventory press");
    }

    /// <summary>
    /// The positive control for the check below: with no panel open, a real
    /// left-click press must still path-follow exactly as
    /// <c>MovementSmokeTest</c>'s own LMB-DEFAULTS-TO-MOVE already covers —
    /// repeated here because it is the necessary baseline for
    /// <see cref="InjectClickOverGrid"/> to mean anything. Without this half,
    /// "the player didn't move" is satisfied just as well by a click that
    /// silently failed to dispatch at all as by one the GUI genuinely
    /// consumed — see this type's class doc on <c>PushInput</c> vs
    /// <c>ParseInputEvent</c> for exactly that trap.
    /// </summary>
    private void InjectClickAwayFromAnyControl()
    {
        _navAgent.TargetPosition = _player.GlobalPosition;
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        Input.ActionPress("skill_slot_lmb");
    }

    private void ReleaseClickAwayFromAnyControl()
    {
        Input.ActionRelease("skill_slot_lmb");
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
    }

    private void CheckClickAwayFromAnyControlMovesPlayer()
    {
        Vector2 expectedMouseWorld = _player.GetGlobalMousePosition();
        float error = _navAgent.TargetPosition.DistanceTo(expectedMouseWorld);
        Check("LMB-MOVES-PLAYER-WHEN-NO-PANEL-OPEN", error < 5f,
            $"a real left-click with no panel open must path-follow toward the mouse — " +
            $"TargetPosition={_navAgent.TargetPosition}, expected≈{expectedMouseWorld} (error {error:0.00}px)");
    }

    /// <summary>
    /// The milestone's headline bug: "Dragging an item in the inventory also
    /// issues a move order to the character underneath." Injects a genuine
    /// <see cref="InputEventMouseButton"/> press at the grid Control's actual
    /// screen rect via <see cref="Viewport.PushInput"/> — the real dispatch
    /// path, not a direct method call, because this check is specifically
    /// about whether the GUI's consumption (<see cref="Control.MouseFilterEnum.Stop"/>)
    /// stops the event before <see cref="Player._UnhandledInput"/> ever sees
    /// it. See this type's class doc for why <c>PushInput</c> — not
    /// <c>ParseInputEvent</c> — is what actually exercises that path headless,
    /// and why <see cref="InjectClickAwayFromAnyControl"/> above is what makes
    /// this check meaningful rather than vacuous.
    /// </summary>
    private void InjectClickOverGrid()
    {
        Rect2 rect = _gridControl.GetGlobalRect();
        Vector2 clickPos = rect.Position + rect.Size * 0.5f;

        GetViewport().PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = clickPos,
            GlobalPosition = clickPos,
        });
        Input.ActionPress("skill_slot_lmb");
    }

    private void ReleaseClickOverGrid()
    {
        Input.ActionRelease("skill_slot_lmb");

        Rect2 rect = _gridControl.GetGlobalRect();
        Vector2 clickPos = rect.Position + rect.Size * 0.5f;

        GetViewport().PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = clickPos,
            GlobalPosition = clickPos,
        });
    }

    private Vector2 _navTargetBeforeClick;

    private void CaptureNavTargetBeforeClick() => _navTargetBeforeClick = _navAgent.TargetPosition;

    private void CheckClickOverPanelDidNotIssueMoveOrder()
    {
        Check("UI-CLICK-DOES-NOT-REACH-MOVEMENT", _navAgent.TargetPosition == _navTargetBeforeClick,
            $"a left-click landing on the open inventory grid must never reach Player's movement — " +
            $"NavigationAgent2D.TargetPosition before={_navTargetBeforeClick}, after={_navAgent.TargetPosition} " +
            "(must be equal)");
    }

    private void PressEscape() => Input.ActionPress("ui_cancel");

    private void ReleaseEscape() => Input.ActionRelease("ui_cancel");

    private void CheckEscapeClosedTopmostPanel()
    {
        Check("ESCAPE-CLOSES-TOPMOST-PANEL", !_inventoryPanel.Visible,
            "ui_cancel (Escape) must close the currently-open inventory panel via PanelStack.CloseTopmost, " +
            "with no per-panel Escape binding of its own");
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;

        // By frame 10 the CurrencyDrop (spawned exactly on the player's position)
        // should have overlapped the player's body, fired OnPickup for real
        // through Area2D.BodyEntered, credited the player's Wallet, and freed
        // itself — despite the ad-hoc filter above resolving Hidden=true for its
        // descriptor. This is the "filters control display, never behaviour"
        // guarantee, exercised end to end rather than merely asserted.
        if (_frame == 10)
        {
            bool collected = !IsInstanceValid(_drop);
            long balance = _player.Wallet.GetBalance(_dropDescriptorAtSpawn.CurrencyId!.Value);
            Check("HIDDEN-DROP-STILL-AUTO-COLLECTED",
                collected && balance == _dropDescriptorAtSpawn.Amount,
                $"drop freed={collected}, wallet balance={balance} (expected {_dropDescriptorAtSpawn.Amount})");

            CheckEquipmentDoesNotAutoPickupOnWalkOver();
        }

        if (_frame == 12)
        {
            CheckClickPicksUpEquipment();
        }

        if (_frame == 14)
        {
            CheckClickWithFullInventoryFailsSafely();
        }

        if (_frame == 16)
        {
            InjectClickAwayFromAnyControl();
        }

        if (_frame == 17)
        {
            CheckClickAwayFromAnyControlMovesPlayer();
            ReleaseClickAwayFromAnyControl();
        }

        if (_frame == 18)
        {
            OpenInventoryViaToggleKey();
        }

        if (_frame == 19)
        {
            ReleaseToggleKey();
        }

        if (_frame == 20)
        {
            CheckPanelOpened();
            CaptureNavTargetBeforeClick();
            InjectClickOverGrid();
        }

        if (_frame == 21)
        {
            ReleaseClickOverGrid();
        }

        if (_frame == 22)
        {
            CheckClickOverPanelDidNotIssueMoveOrder();
            PressEscape();
        }

        if (_frame == 23)
        {
            ReleaseEscape();
        }

        if (_frame == 24)
        {
            CheckEscapeClosedTopmostPanel();
        }

        if (_frame >= 30)
        {
            GD.Print(_failed ? "[ITEMSTEST] === RESULT: FAIL ===" : "[ITEMSTEST] === RESULT: PASS ===");
            GetTree().Quit(_failed ? 1 : 0);
        }
    }
}
