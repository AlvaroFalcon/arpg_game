#nullable enable
using Godot;
using Godot.Collections;
using PathToExile.Combat;

namespace PathToExile.Items;

/// <summary>
/// Spawns an <see cref="EquipmentItem"/> drop when the entity this component is
/// attached to dies — the equipment sibling to <see cref="DropSpawner"/> (which
/// stays currency-only). A sibling component rather than extending
/// <see cref="DropSpawner"/> itself, per the milestone brief's "extend it
/// minimally, or add a sibling spawner": <see cref="DropSpawner"/> already has a
/// working smoke test asserting its exact currency-only shape, and currency and
/// equipment roll along genuinely different axes (an amount range vs. a base +
/// rarity + affix roll) — merging them would mean every currency-only call site
/// (target dummies today) grows unused equipment fields.
///
/// <b>Not the drop table.</b> Same caveat <see cref="DropSpawner"/> documents:
/// this is a single-entry stand-in (pick a random base, roll a rarity from a
/// placeholder weight table) so the roller can be exercised end-to-end before
/// the real weighted <c>DropContext</c>-driven table exists — explicitly out of
/// scope for this milestone. When it lands, this node's job shrinks to "ask the
/// table, spawn what it returns."
/// </summary>
[GlobalClass]
public partial class EquipmentDropSpawner : Node
{
    /// <summary>Expected to have an <see cref="EquipmentItem"/> at its root
    /// (res://scenes/items/EquipmentDrop.tscn).</summary>
    [Export]
    public PackedScene? DropScene;

    /// <summary>Which base items this spawner can produce. A plain array rather
    /// than a wrapping "library" resource, mirroring how <see cref="DropSpawner"/>
    /// takes its single <see cref="CurrencyDefinition"/> directly.</summary>
    [Export]
    public Array<BaseItemDefinition> Bases = new();

    [Export]
    public AffixLibrary? Affixes;

    /// <summary>Fixed per-spawner item level, standing in for area/monster level
    /// until a real leveling system exists (docs/design/affixes.md's Open
    /// Questions #1: "there is no character leveling system").</summary>
    [Export]
    public int ItemLevel = 1;

    [Export]
    public float DropChance = 0.5f;

    [Export]
    public float ScatterRadiusPx = 18f;

    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _rng.Randomize();
        GetParent().GetNode<Health>("Health").Died += OnDied;
    }

    private void OnDied()
    {
        if (DropScene is null || Affixes is null || Bases.Count == 0 || _rng.Randf() > DropChance)
        {
            return;
        }

        BaseItemDefinition baseItem = Bases[_rng.RandiRange(0, Bases.Count - 1)];
        Rarity rarity = RollRarity();

        // Spawn into the current scene, not as a child of the dying entity —
        // same reasoning as DropSpawner.OnDied: TargetDummy.OnDied QueueFrees
        // itself, which would take a child drop down with it.
        Vector2 origin = GetParent<Node2D>().GlobalPosition;
        var drop = DropScene.Instantiate<EquipmentItem>();
        GetTree().CurrentScene.AddChild(drop);

        drop.GlobalPosition = origin + new Vector2(
            _rng.RandfRange(-ScatterRadiusPx, ScatterRadiusPx),
            _rng.RandfRange(-ScatterRadiusPx, ScatterRadiusPx));

        drop.Initialize(baseItem, rarity, ItemLevel, Affixes.Entries, _rng);
    }

    /// <summary>
    /// Placeholder rarity weights — balance tuning is explicitly out of scope
    /// (docs/design/items.md's Open Questions #4/#5). Majestic and Set are
    /// deliberately excluded here: no majestic effect pool or
    /// <c>SetDefinition</c> exists yet (milestone 4), so rolling either rarity
    /// from this spawner would produce an item with a rarity colour and nothing
    /// behind it. <see cref="AffixRoller.Roll"/> still supports both for when
    /// that milestone lands.
    /// </summary>
    private Rarity RollRarity()
    {
        float roll = _rng.Randf() * 100f;
        if (roll < 45f)
        {
            return Rarity.Common;
        }

        if (roll < 80f)
        {
            return Rarity.Magic;
        }

        return Rarity.Rare;
    }
}
