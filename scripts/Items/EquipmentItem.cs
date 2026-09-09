#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PathToExile.Combat;
using PathToExile.Stats;

namespace PathToExile.Items;

/// <summary>
/// The equipment <see cref="IDroppable"/> — docs/design/items.md -> "Drop
/// pipeline": on-ground is "label + sprite" (shared with currency via
/// <see cref="GroundLabelPool"/>/<see cref="FilterChain"/>), on-pickup is "into
/// grid inventory."
///
/// <b>Pickup is click-based, not walk-over</b> — docs/design/items.md ->
/// "Pickup is manual for items, automatic for currency": milestone 2c's
/// walk-over auto-pickup for equipment was a genuine design bug (it fills a
/// 60-cell grid with vendor trash and takes the is-this-worth-carrying decision
/// away from the player), fixed in milestone 2d by retiring the
/// <see cref="Area2D.BodyEntered"/> path entirely for this type (contrast
/// <see cref="CurrencyDrop"/>, which keeps it — currency really is still
/// auto-collected). The click affordance is wired two ways, both funnelling
/// into the same <see cref="OnPickup"/> (via <see cref="TryPickupByPlayer"/>):
/// the ground label (<see cref="RegisterLabel"/>, via
/// <see cref="GroundLabel.OnClicked"/>) and this node's own sprite, via
/// <see cref="Area2D.InputEvent"/> — see <see cref="_Ready"/>. "The sprite
/// stays clickable even when its label is
/// hidden" (same section) falls out for free: the two paths are entirely
/// independent, and a hidden label is simply an invisible
/// <see cref="Control"/> that Godot never routes input to, while the sprite's
/// physics-based picking neither knows nor cares about label visibility.
///
/// Mirrors <see cref="CurrencyDrop"/>'s shape closely: a runtime
/// <see cref="Initialize"/> entry point that computes one final, immutable
/// <see cref="Descriptor"/> before anything else can observe this node (per
/// docs/design/items.md -> "The descriptor is immutable after drop"), plus label
/// registration through the same pool/filter path currency already uses.
/// </summary>
[GlobalClass]
public partial class EquipmentItem : Area2D, IDroppable
{
    /// <summary>Same optional-wiring convention as <see cref="CurrencyDrop.GroundLabelPoolPath"/>.</summary>
    [Export]
    public NodePath GroundLabelPoolPath = new();

    /// <summary>Same optional-wiring convention, for the shared hover tooltip
    /// (docs/design/items.md -> "Item tooltips") — see
    /// <see cref="UI.ItemTooltipControl"/>'s class doc for the fallback lookup.</summary>
    [Export]
    public NodePath ItemTooltipPath = new();

    /// <summary>Which base this item rolled on. Null only in the brief window
    /// before <see cref="Initialize"/> runs.</summary>
    public BaseItemDefinition? Base { get; private set; }

    public Rarity Rarity { get; private set; }

    /// <summary>
    /// The affixes this item actually rolled — richer than
    /// <see cref="ItemDescriptor.AffixTiers"/> (which only exposes tiers for
    /// filtering), this is the full <see cref="AffixDefinition"/> + tier + value
    /// a future equip step needs to grant real <see cref="Stats.StatModifier"/>s.
    /// Not part of the descriptor: docs/design/items.md's filter vocabulary only
    /// needs to see tiers, not the underlying stats — see that type's own class
    /// doc.
    /// </summary>
    public IReadOnlyList<AffixRoller.RolledAffix> RolledAffixes { get; private set; } = Array.Empty<AffixRoller.RolledAffix>();

    /// <summary>The base item's intrinsic stats, rolled once at drop time (a
    /// range like Iron Wand's "+3-6 Damage" resolves to one concrete value, the
    /// same way an affix's tier range does) — see
    /// <see cref="BaseItemStatEntry"/>'s doc comment on why weapon base damage is
    /// modelled as a rollable range rather than a fixed number.</summary>
    public IReadOnlyList<(StatId Stat, ModifierType Type, float Value, SkillTags Condition)> RolledBaseStats { get; private set; }
        = Array.Empty<(StatId, ModifierType, float, SkillTags)>();

    public ItemDescriptor Descriptor { get; private set; } = new();

    private GroundLabelPool? _labelPool;
    private int? _labelHandle;
    private UI.ItemTooltipControl? _tooltip;

    /// <summary>Public so a test can confirm a failed pickup left the label
    /// intact (docs/design/items.md: "the click fails and leaves the item
    /// exactly where it was, label intact") without reaching into the private
    /// <see cref="_labelHandle"/> field itself.</summary>
    public bool HasGroundLabel => _labelHandle is not null;

    public override void _Ready()
    {
        // Physics-based click/hover picking for the sprite half of manual
        // pickup and hover tooltips — see this type's class doc. Off by default
        // per-Viewport (Godot 4.1: physics_object_picking defaults false), and
        // every EquipmentItem needs it, so each one turning it on here (rather
        // than a one-off setting buried in Main.tscn or every test scene) is
        // what makes "instance an EquipmentDrop anywhere and it's clickable"
        // true without extra per-scene wiring. Idempotent: harmless to set true
        // repeatedly as more items spawn.
        GetViewport().PhysicsObjectPicking = true;
        InputEvent += OnAreaInputEvent;
        MouseEntered += () => _tooltip?.Show(this, this);
        MouseExited += () => _tooltip?.Clear(this);

        _tooltip = GetNodeOrNull<UI.ItemTooltipControl>(ItemTooltipPath)
            ?? GetTree().CurrentScene.GetNodeOrNull<UI.ItemTooltipControl>("ItemTooltip");
    }

    /// <summary>
    /// Runtime configuration path, called exactly once by a spawner
    /// (<see cref="EquipmentDropSpawner"/>) immediately after instancing and
    /// before this drop is visible to anything else — same contract as
    /// <see cref="CurrencyDrop.Initialize"/>. Rolls base stats and affixes
    /// (<see cref="AffixRoller.Roll"/>), generates the display name
    /// (<see cref="ItemNaming.Generate"/>), and builds the one, final,
    /// <see cref="Descriptor"/> — nothing about this item changes after this
    /// call returns.
    /// </summary>
    public void Initialize(
        BaseItemDefinition baseItem,
        Rarity rarity,
        int itemLevel,
        IReadOnlyList<AffixDefinition> affixPool,
        RandomNumberGenerator rng)
    {
        Base = baseItem;
        Rarity = rarity;

        RolledBaseStats = baseItem.BaseStats
            .Select(entry => (
                entry.Stat,
                entry.Type,
                entry.Min >= entry.Max ? entry.Min : rng.RandfRange(entry.Min, entry.Max),
                entry.Condition))
            .ToList();

        List<AffixRoller.RolledAffix> rolled = AffixRoller.Roll(baseItem, rarity, itemLevel, affixPool, rng);
        RolledAffixes = rolled;

        string displayName = ItemNaming.Generate(rarity, baseItem.DisplayName, rolled, rng);
        List<int> tiers = rolled.Select(a => a.Tier).ToList();

        Descriptor = new ItemDescriptor
        {
            ItemClass = baseItem.ItemClass,
            BaseTypeId = baseItem.Id,
            Rarity = rarity,
            ItemLevel = itemLevel,

            // No separate area-level concept exists yet (the weighted DropContext
            // table is explicitly out of scope this milestone) — equal to
            // ItemLevel by construction until one does.
            AreaLevel = itemLevel,

            AffixCount = rolled.Count,
            AffixTiers = tiers,
            HighestAffixTier = tiers.Count > 0 ? tiers.Max() : (int?)null,
            Width = baseItem.Width,
            Height = baseItem.Height,
            DisplayName = displayName,
        };

        RegisterLabel();
    }

    private void RegisterLabel()
    {
        ReleaseLabel();

        _labelPool = GetNodeOrNull<GroundLabelPool>(GroundLabelPoolPath)
            ?? GetTree().CurrentScene.GetNodeOrNull<GroundLabelPool>("GroundLabelPool");
        if (_labelPool is null || Base is null)
        {
            return;
        }

        // DisplayStyle comes from the same FilterChain/rarity-colouring path
        // currency uses — never read Rarity -> colour directly here, per
        // docs/design/items.md -> "Rarity colour is filter output, not label
        // state".
        DisplayStyle style = FilterChain.Default.Evaluate(Descriptor);
        _labelHandle = _labelPool.Acquire(
            () => GlobalPosition,
            Descriptor.DisplayName,
            style,
            onClicked: () => TryPickupByPlayer(),
            onHoverChanged: hovering =>
            {
                if (hovering)
                {
                    _tooltip?.Show(this, this);
                }
                else
                {
                    _tooltip?.Clear(this);
                }
            });
    }

    private void ReleaseLabel()
    {
        if (_labelHandle is { } handle)
        {
            _labelPool?.Release(handle);
            _labelHandle = null;
        }

        // Surrendering the tooltip belongs here, with the label, because the two
        // are lost for the same reasons. Tooltip ownership is normally released by
        // MouseExited, and on pickup that signal never arrives: the item is
        // detached from the tree in the same frame the click lands, so the cursor
        // never "leaves" it. The tooltip then stayed on screen describing an item
        // that was already in the player's bag. ReleaseLabel is the one chokepoint
        // both pickup and _ExitTree already pass through.
        _tooltip?.Clear(this);
    }

    /// <summary>
    /// Resolves the single local player the same convention-over-configuration
    /// way <see cref="RegisterLabel"/> resolves its <see cref="GroundLabelPool"/>
    /// — a well-known name under the current scene root — and forwards to
    /// <see cref="OnPickup"/> if found. The click handlers below are the only
    /// callers; a click with no resolvable player (shouldn't happen outside a
    /// misconfigured scene) is simply a no-op, same failure mode as any other
    /// rejected pickup.
    /// </summary>
    private void TryPickupByPlayer()
    {
        if (GetTree().CurrentScene.GetNodeOrNull<Player>("Player") is { } player)
        {
            OnPickup(player);
        }
    }

    /// <summary>The sprite half of manual pickup (docs/design/items.md ->
    /// "Pickup is manual...": "its ground label or its sprite") — requires
    /// <see cref="CollisionObject2D.InputPickable"/> (true by default) and a
    /// nonzero <see cref="CollisionObject2D.CollisionLayer"/>, both set on
    /// <c>EquipmentDrop.tscn</c>, plus <see cref="Viewport.PhysicsObjectPicking"/>,
    /// turned on in <see cref="_Ready"/>.</summary>
    private void OnAreaInputEvent(Node viewport, InputEvent @event, long shapeIdx)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            TryPickupByPlayer();
        }
    }

    /// <summary>
    /// <see cref="IDroppable.OnPickup"/> — milestone 2d changed only <i>what
    /// triggers this</i> (a click, via <see cref="TryPickupByPlayer"/>), never
    /// what it does. Routes into <see cref="Player.Inventory"/>
    /// (<see cref="Items.Inventory.TryAdd"/>): first free space in reading
    /// order. If nothing fits, the item is left exactly where it is on the
    /// ground — still visible, still pickable once space frees up, label still
    /// registered — per docs/design/items.md: "the click fails and leaves the
    /// item exactly where it was, label intact." <b>Nothing below this check
    /// runs on failure</b> — see this type's class doc on why a failed pickup
    /// must never half-detach the item.
    /// </summary>
    public void OnPickup(Player player)
    {
        if (!player.Inventory.TryAdd(this))
        {
            return;
        }

        ReleaseLabel();

        // Ownership transfers to Inventory as pure data (Base/RolledAffixes/
        // RolledBaseStats are still read from this same instance whenever it's
        // equipped or its stats are recomputed) rather than as a ground
        // presence. Detached, not freed: Inventory's dictionary is now the only
        // remaining reference keeping this node alive, and QueueFree-ing it
        // would collect the very object that reference points at.
        GetParent()?.RemoveChild(this);
    }

    public override void _ExitTree() => ReleaseLabel();
}
