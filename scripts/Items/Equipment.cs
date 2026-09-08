#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PathToExile.Combat;
using PathToExile.Stats;

namespace PathToExile.Items;

/// <summary>
/// The equip/unequip component — milestone 2b, docs/design/items.md's build
/// order item 2 ("equip -&gt; ActorStats modifiers via Source tagging"). Sibling
/// to <see cref="ActorStats"/>/<see cref="Combat.Health"/>/<see cref="Skills.SkillUser"/>,
/// found the same convention-over-configuration way they are: a child node
/// named "Equipment", located via <c>GetParent().GetNode&lt;Equipment&gt;("Equipment")</c>
/// where a caller needs it (see <see cref="EquipmentItem.OnPickup"/>), and this
/// node itself reaching its own "Stats" sibling the way <see cref="Skills.SkillUser"/>
/// does.
///
/// Owns <see cref="EquipmentSlot"/> -&gt; <see cref="EquipmentItem"/> and nothing
/// else — no grid inventory, no equipment panel UI (both explicitly out of scope
/// this milestone; see the milestone brief).
/// </summary>
[GlobalClass]
public partial class Equipment : Node
{
    private readonly Dictionary<EquipmentSlot, EquipmentItem> _slots = new();

    private ActorStats _stats = null!;

    public override void _Ready()
    {
        _stats = GetParent().GetNode<ActorStats>("Stats");
    }

    /// <summary>Query: what's in a slot, or null if it's empty.</summary>
    public EquipmentItem? GetEquipped(EquipmentSlot slot) =>
        _slots.TryGetValue(slot, out EquipmentItem? item) ? item : null;

    /// <summary>
    /// Which slots <paramref name="itemClass"/> may occupy — docs/design/items.md
    /// -> "Equipment slots": one slot per class, except Ring, which may go in
    /// either of the two ring slots. The single source of truth for slot
    /// validation (<see cref="IsValidSlot"/>) and auto-equip's candidate order
    /// (<see cref="TryAutoEquip"/>) both read this, so the two can never disagree
    /// about which slots a class is allowed into.
    /// </summary>
    private static IReadOnlyList<EquipmentSlot> CandidateSlots(ItemClass itemClass) => itemClass switch
    {
        ItemClass.Wand => new[] { EquipmentSlot.Weapon },
        ItemClass.Offhand => new[] { EquipmentSlot.Offhand },
        ItemClass.Helmet => new[] { EquipmentSlot.Helmet },
        ItemClass.BodyArmour => new[] { EquipmentSlot.BodyArmour },
        ItemClass.Gloves => new[] { EquipmentSlot.Gloves },
        ItemClass.Boots => new[] { EquipmentSlot.Boots },
        ItemClass.Belt => new[] { EquipmentSlot.Belt },
        ItemClass.Amulet => new[] { EquipmentSlot.Amulet },
        ItemClass.Ring => new[] { EquipmentSlot.Ring1, EquipmentSlot.Ring2 },

        // Currency (and any future non-equippable class) has no valid slot at all.
        _ => Array.Empty<EquipmentSlot>(),
    };

    private static bool IsValidSlot(ItemClass itemClass, EquipmentSlot slot) =>
        CandidateSlots(itemClass).Contains(slot);

    /// <summary>
    /// Equips <paramref name="item"/> into <paramref name="slot"/>. Returns
    /// <c>false</c> and does nothing if the item's <see cref="ItemClass"/> can't
    /// occupy that slot (docs/design/items.md -> "Equipment slots": "an item's
    /// ItemClass must match the slot"). On success, whatever was previously in
    /// the slot is displaced out through <paramref name="displaced"/> rather than
    /// destroyed — the caller decides what happens to it (drop it, hand it back,
    /// swap it into inventory once one exists).
    ///
    /// Order of operations matters and is the point of this milestone beyond
    /// equipping itself (docs/design/affixes.md -> "Derivations need a recompute
    /// hook"): the displaced item's modifiers are removed, the new item's
    /// modifiers are applied, and only *then* is
    /// <see cref="ActorStats.RecomputeAttributeDerivations"/> called — so an item
    /// granting +20 Strength feeds its derived Life/Armour bonus in the same
    /// operation, and a displaced item's Strength grant is gone before the
    /// derivation reads it.
    /// </summary>
    public bool TryEquip(EquipmentSlot slot, EquipmentItem item, out EquipmentItem? displaced)
    {
        displaced = null;

        if (item.Base is null || !IsValidSlot(item.Base.ItemClass, slot))
        {
            return false;
        }

        if (_slots.TryGetValue(slot, out EquipmentItem? previous))
        {
            _stats.RemoveModifiersFromSource(previous);
            displaced = previous;
        }

        _slots[slot] = item;
        ApplyItemModifiers(item);

        _stats.RecomputeAttributeDerivations();
        ClampResourcePools();

        return true;
    }

    /// <summary>
    /// Removes and returns whatever is in <paramref name="slot"/>, or null if it
    /// was already empty. Strips the item's modifiers via
    /// <see cref="ActorStats.RemoveModifiersFromSource"/> — never by tracking
    /// which individual <see cref="StatModifier"/>s the item added, per
    /// docs/design/affixes.md -> "How affixes reach stats" — then recomputes
    /// attribute derivations and re-clamps the resource pools.
    ///
    /// <b>Must never kill.</b> Unequipping a large Maximum Life roll can drop
    /// <c>MaxLife</c> below current life; <see cref="ResourcePool.ClampToMax"/>
    /// already preserves <c>Current</c> absolutely and floors the clamp at 1, not
    /// 0 (docs/design/stats.md -> "What happens to Current when Max changes").
    /// That clamp would also run on the next <c>_PhysicsProcess</c> tick
    /// regardless, but this calls it immediately so the actor is never
    /// momentarily over its new Max even for a single synchronous read (e.g. a
    /// test asserting life right after calling this method, with no physics
    /// frame in between).
    /// </summary>
    public EquipmentItem? Unequip(EquipmentSlot slot)
    {
        if (!_slots.Remove(slot, out EquipmentItem? item))
        {
            return null;
        }

        _stats.RemoveModifiersFromSource(item);
        _stats.RecomputeAttributeDerivations();
        ClampResourcePools();

        return item;
    }

    /// <summary>
    /// Milestone-2b bridge (docs/design/items.md's build order item 2, ahead of
    /// the grid inventory in item 2c's scope): equips <paramref name="item"/>
    /// into the first free slot its <see cref="ItemClass"/> is valid for — Ring1
    /// before Ring2, everything else has exactly one candidate — or does nothing
    /// and returns false if every valid slot is already occupied. Never displaces
    /// an existing item; auto-equip only ever fills a gap. See
    /// <see cref="EquipmentItem.OnPickup"/> for the walk-over trigger and why this
    /// whole method is marked for removal once a real inventory exists.
    /// </summary>
    public bool TryAutoEquip(EquipmentItem item)
    {
        if (item.Base is null)
        {
            return false;
        }

        foreach (EquipmentSlot slot in CandidateSlots(item.Base.ItemClass))
        {
            if (GetEquipped(slot) is not null)
            {
                continue;
            }

            return TryEquip(slot, item, out _);
        }

        return false;
    }

    /// <summary>
    /// Grants every <see cref="StatModifier"/> <paramref name="item"/> carries —
    /// its rolled base stats (<see cref="EquipmentItem.RolledBaseStats"/>) and its
    /// rolled affixes (<see cref="EquipmentItem.RolledAffixes"/>) — tagged with
    /// the item instance itself as <see cref="StatModifier.Source"/>. Deliberately
    /// a *different* source object from
    /// <see cref="ActorStats"/>'s own attribute-derivation source: mixing the two
    /// would mean unequipping one item's Strength roll could also wipe every
    /// other source's derived Life/Armour, or vice versa (docs/design/affixes.md's
    /// milestone brief -> "keep those distinct, or removing one will strip the
    /// other").
    ///
    /// <see cref="AffixDefinition.IsAllResistances"/> ("of Resilience") is the one
    /// affix that writes to three stats at once — mirrors
    /// <c>AffixRoller.ClaimedStatsOf</c>'s handling of the same case, since this
    /// is the other place in the codebase that has to know an affix can target
    /// more than the one <see cref="AffixDefinition.Stat"/> field names.
    /// </summary>
    private void ApplyItemModifiers(EquipmentItem item)
    {
        foreach ((StatId stat, ModifierType type, float value, SkillTags condition) in item.RolledBaseStats)
        {
            _stats.Get(stat).AddModifier(new StatModifier(type, value, item, condition));
        }

        foreach (AffixRoller.RolledAffix affix in item.RolledAffixes)
        {
            AffixDefinition definition = affix.Definition;

            if (definition.IsAllResistances)
            {
                var modifier = new StatModifier(definition.Type, affix.Value, item, definition.Condition, definition.ConditionAny);
                _stats.Get(StatId.FireResistance).AddModifier(modifier);
                _stats.Get(StatId.ColdResistance).AddModifier(modifier);
                _stats.Get(StatId.LightningResistance).AddModifier(modifier);
                continue;
            }

            _stats.Get(definition.Stat).AddModifier(
                new StatModifier(definition.Type, affix.Value, item, definition.Condition, definition.ConditionAny));
        }
    }

    private void ClampResourcePools()
    {
        _stats.Life?.ClampToMax();
        _stats.Mana?.ClampToMax();
    }
}
