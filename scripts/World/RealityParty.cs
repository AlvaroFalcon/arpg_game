#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using ArpgGame.Net;
using ArpgGame.Run;

namespace ArpgGame.World;

/// <summary>
/// The party in a co-op Altered Reality — docs/design/endgame.md -> "Co-op
/// realities". A plain list of <see cref="RunPlayer"/> entries (the local player
/// first) and the instanced-loot hooks <see cref="RealityHost"/> listens to: what
/// the replication layer needs from a reality (<see cref="ICoopWorld"/>). Solo
/// realities have one entry and nobody listens.
/// </summary>
public sealed class RealityParty : ICoopWorld
{
    private readonly List<RunPlayer> _players = new();
    private readonly Action<string, Color> _toast;

    public RealityParty(Action<string, Color> toast) => _toast = toast;

    public IReadOnlyList<RunPlayer> Players => _players;

    public int PartySize => Mathf.Max(1, _players.Count);

    /// <summary>Set when the reality is over for everyone (collapsed, closed by the host).</summary>
    public bool Ended { get; set; }

    public bool AllDown
    {
        get
        {
            foreach (RunPlayer rp in _players)
            {
                if (!rp.Downed)
                {
                    return false;
                }
            }

            return _players.Count > 0;
        }
    }

    public RunPlayer Add(Player player)
    {
        var rp = new RunPlayer(player, player.PeerId);
        if (player.IsLocal)
        {
            _players.Insert(0, rp);
        }
        else
        {
            _players.Add(rp);
        }

        return rp;
    }

    public RunPlayer? PlayerOf(Node? node)
    {
        foreach (RunPlayer rp in _players)
        {
            if (node is not null && rp.Player == node)
            {
                return rp;
            }
        }

        return null;
    }

    public RunPlayer? ByPeer(long peer) => _players.Find(rp => rp.PeerId == peer);

    public void RemovePlayer(RunPlayer player) => _players.Remove(player);

    public void ShowToast(string text, Color color) => _toast(text, color);

    public event Action<Pickup>? PickupSpawned;

    public event Action<Pickup, Player, int>? PickupCollected;

#pragma warning disable CS0067 // nothing in a reality pulls drops in (the Rift's Lodestone)
    public event Action<Player?>? Magnetized;
#pragma warning restore CS0067

    public event Action<Player, Items.EquipmentItem, Vector2>? RemoteItemDropped;

    /// <summary>Marks a fresh drop as <paramref name="owner"/>'s (null = the local player) and tells the co-op layer.</summary>
    public Pickup Spawned(Pickup pickup, Player? owner)
    {
        pickup.OwnerPlayer = owner;
        PickupSpawned?.Invoke(pickup);
        return pickup;
    }

    public void Collected(Pickup pickup, Player player, int detail) => PickupCollected?.Invoke(pickup, player, detail);

    public void ItemForRemote(Player owner, Items.EquipmentItem item, Vector2 at) => RemoteItemDropped?.Invoke(owner, item, at);
}
