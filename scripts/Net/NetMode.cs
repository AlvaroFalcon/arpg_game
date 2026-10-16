#nullable enable
using Godot;

namespace ArpgGame.Net;

/// <summary>
/// Process-wide co-op flags — docs/design/multiplayer.md -> "Architecture".
/// <see cref="ClientSide"/> is the one switch that makes a co-op client a pure
/// view: nothing it runs locally can deal damage (<see cref="Combat.Health"/>
/// ignores every hit), so every number the player sees comes from the host.
/// Set by <see cref="RiftClient"/> while it lives and cleared when it leaves the
/// tree; always false offline and on the host.
/// </summary>
public static class NetMode
{
    public static bool ClientSide { get; set; }
}

/// <summary>
/// What a player wants this frame, as sent over the network — move direction,
/// aim point, sprint, and counters for one-shot presses (a counter survives a
/// lost unreliable packet; a "pressed" bit would not). On the host it drives a
/// remote player's <see cref="Player"/>, <see cref="Dodge"/> and
/// <see cref="Items.Flasks"/> instead of the keyboard; null on a
/// <see cref="Player"/> means "read the local keyboard and mouse".
/// </summary>
public sealed class PlayerCommand
{
    public Vector2 Move;
    public Vector2 Aim;
    public bool Sprint;

    /// <summary>The player has the level-up cards open (warded while choosing).</summary>
    public bool Choosing;

    public ushort Seq;
    public byte DodgeCount;
    public Vector2 DodgeDir;
    public byte LifeFlaskCount;
    public byte ManaFlaskCount;

    /// <summary>Host clock (msec) when the last packet arrived; a stale command stops the player.</summary>
    public ulong ReceivedMsec;

    private byte _dodgeSeen;
    private byte _lifeSeen;
    private byte _manaSeen;
    private bool _primed;

    /// <summary>Takes the counters' current values as "already handled" (first packet).</summary>
    public void Prime()
    {
        _dodgeSeen = DodgeCount;
        _lifeSeen = LifeFlaskCount;
        _manaSeen = ManaFlaskCount;
        _primed = true;
    }

    public bool ConsumeDodge() => Consume(DodgeCount, ref _dodgeSeen);

    public bool ConsumeLifeFlask() => Consume(LifeFlaskCount, ref _lifeSeen);

    public bool ConsumeManaFlask() => Consume(ManaFlaskCount, ref _manaSeen);

    private bool Consume(byte count, ref byte seen)
    {
        if (!_primed)
        {
            Prime();
            return false;
        }

        if (count == seen)
        {
            return false;
        }

        seen = count;
        return true;
    }
}
