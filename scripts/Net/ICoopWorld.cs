#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using ArpgGame.Run;

namespace ArpgGame.Net;

/// <summary>
/// What the co-op replication (<see cref="CoopHost"/>/<see cref="CoopClient"/>)
/// needs from the activity it replicates — docs/design/multiplayer.md ->
/// "Replication layers". The Survival Rift's <see cref="RunState"/> and an
/// Altered Reality's <see cref="World.RealityParty"/> both provide it: the
/// party's players, whether the activity ended, toasts, and the instanced-loot
/// hooks the host listens to (nothing listens offline).
/// </summary>
public interface ICoopWorld
{
    IReadOnlyList<RunPlayer> Players { get; }

    int PartySize { get; }

    bool Ended { get; }

    /// <summary>True when every party member is down.</summary>
    bool AllDown { get; }

    RunPlayer? PlayerOf(Node? node);

    void RemovePlayer(RunPlayer player);

    /// <summary>A short line on this machine's HUD.</summary>
    void ShowToast(string text, Color color);

    /// <summary>A drop was put on the floor (owner set).</summary>
    event Action<Pickup>? PickupSpawned;

    /// <summary>A drop was collected: pickup, by whom, and a detail (Cataclysm kills).</summary>
    event Action<Pickup, Player, int>? PickupCollected;

    /// <summary>Drops were pulled in (null = everyone's).</summary>
    event Action<Player?>? Magnetized;

    /// <summary>Gear rolled for a remote player: the host sends it, then frees the node.</summary>
    event Action<Player, Items.EquipmentItem, Vector2>? RemoteItemDropped;
}
