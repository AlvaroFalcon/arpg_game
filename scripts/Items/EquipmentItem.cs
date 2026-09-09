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
/// grid inventory." Milestone 2c wires that up for real — see
/// <see cref="OnPickup"/>.
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

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
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
        _labelHandle = _labelPool.Acquire(() => GlobalPosition, Descriptor.DisplayName, style);
    }

    private void ReleaseLabel()
    {
        if (_labelHandle is { } handle)
        {
            _labelPool?.Release(handle);
            _labelHandle = null;
        }
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is Player player)
        {
            OnPickup(player);
        }
    }

    /// <summary>
    /// Milestone 2c, replacing milestone 2b's temporary auto-equip bridge (which
    /// this method used to call and which no longer exists —
    /// docs/design/items.md -> "Inventory interaction": "Removes the milestone-2b
    /// auto-equip-on-pickup bridge, which existed only so gear was usable before
    /// this screen existed."). Walking over an equipment drop now routes it into
    /// <see cref="Player.Inventory"/> (<see cref="Items.Inventory.TryAdd"/>):
    /// first free space in reading order. If nothing fits, the item is left
    /// exactly where it is on the ground — still visible, still pickable once
    /// space frees up — per "If nothing fits, the item stays on the ground
    /// rather than being destroyed or silently swallowed."
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
