#nullable enable
using Godot;
using ArpgGame.Combat;

namespace ArpgGame.Items;

/// <summary>
/// Spawns a drop when the entity this component is attached to dies.
///
/// A component node hanging off a combat entity, exactly like
/// <see cref="Combat.Health"/> and <see cref="Stats.ActorStats"/>: it finds a
/// sibling "Health" on its parent and listens to <see cref="Health.Died"/>. This
/// is deliberately *not* code inside <see cref="Combat.TargetDummy"/> — a target
/// dummy is a combat entity and has no business knowing what a currency is. Any
/// future enemy scene gets drops by adding this node, with no change to its own
/// script.
///
/// <b>This is not the drop table.</b> docs/design/items.md -> "Drop pipeline"
/// specifies drop tables as "weighted entries plus tags, resolved against a
/// <c>DropContext</c> (monster level, area level, magic find, player)". This is a
/// single-entry stand-in that exists so milestone 1's pipeline can be exercised in
/// the playable scene before that machinery is designed. When the real table lands,
/// this node's job shrinks to "ask the table, spawn what it returns" — the death
/// hook and the spawn-into-scene mechanics below stay as they are.
/// </summary>
[GlobalClass]
public partial class DropSpawner : Node
{
    /// <summary>The drop to instance. Expected to have a <see cref="CurrencyDrop"/>
    /// at its root (res://scenes/items/CurrencyDrop.tscn).</summary>
    [Export]
    public PackedScene? DropScene;

    [Export]
    public CurrencyDefinition? Currency;

    [Export]
    public int MinAmount = 1;

    [Export]
    public int MaxAmount = 1;

    /// <summary>0..1. Left at 1 for target dummies, which should always drop so the
    /// pipeline is observable every time you kill one.</summary>
    [Export]
    public float DropChance = 1f;

    /// <summary>Max random offset from the dying entity, in px. Keeps two drops from
    /// landing exactly on top of each other and makes the label pool's stacking
    /// behaviour visible.</summary>
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
        if (DropScene is null || Currency is null || _rng.Randf() > DropChance)
        {
            return;
        }

        // Spawn into the current scene rather than as a child of the dying entity:
        // TargetDummy.OnDied calls QueueFree on itself, which would take the drop
        // with it. (QueueFree is deferred to end-of-frame, so reading the parent's
        // GlobalPosition here is still valid regardless of which Died handler ran
        // first.)
        var origin = GetParent<Node2D>().GlobalPosition;
        Node scene = GetTree().CurrentScene;
        var drop = DropScene.Instantiate<CurrencyDrop>();
        Vector2 scatter = new(
            _rng.RandfRange(-ScatterRadiusPx, ScatterRadiusPx),
            _rng.RandfRange(-ScatterRadiusPx, ScatterRadiusPx));
        int amount = _rng.RandiRange(MinAmount, MaxAmount);
        CurrencyDefinition currency = Currency;

        // Died is raised from inside a physics callback (a projectile's overlap signal), where
        // adding the drop's Area2D and shape is refused ("can't change this state while
        // flushing queries"): add it at the end of the frame instead.
        Callable.From(() =>
        {
            if (!IsInstanceValid(scene))
            {
                drop.Free();
                return;
            }

            scene.AddChild(drop);
            drop.GlobalPosition = origin + scatter;

            // AddChild first, then Initialize: _Ready runs on AddChild with Currency
            // still null, which CurrencyDrop.RegisterLabel treats as "no label yet".
            // Initialize then computes the one real descriptor and registers the label.
            // The drop is never observed carrying the placeholder descriptor, which is
            // what "immutable after drop" requires.
            drop.Initialize(currency, amount);
        }).CallDeferred();
    }
}
