#nullable enable
using System;
using System.Collections.Generic;

namespace ArpgGame.Items;

/// <summary>
/// Spreads loot spawning over frames — docs/design/performance.md -> "Mass
/// deaths". Drops are still rolled on the death frame (same chances, same
/// positions, same order); only the node creation waits here. The host pumps it
/// once per physics frame with a budget: an item (instance, affix roll, icon,
/// label) costs <see cref="ItemCost"/>, a pickup <see cref="PickupCost"/>, so a
/// frame spawns at most two items or eight pickups. Nothing is ever dropped.
/// </summary>
public sealed class LootQueue
{
    public const float ItemCost = 1f;
    public const float PickupCost = 0.25f;
    public const float FrameBudget = 2f;

    private readonly Queue<(float Cost, Action Spawn)> _pending = new();

    public int Count => _pending.Count;

    public void Add(float cost, Action spawn) => _pending.Enqueue((cost, spawn));

    /// <summary>Spawns queued drops in order until the next one would exceed the budget.</summary>
    public void Pump(float budget = FrameBudget)
    {
        float spent = 0f;
        while (_pending.Count > 0 && spent + _pending.Peek().Cost <= budget + 1e-4f)
        {
            (float cost, Action spawn) = _pending.Dequeue();
            spent += cost;
            spawn();
        }
    }

    /// <summary>Spawns everything still waiting.</summary>
    public void Flush()
    {
        while (_pending.Count > 0)
        {
            _pending.Dequeue().Spawn();
        }
    }
}
