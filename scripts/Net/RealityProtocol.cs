#nullable enable
namespace ArpgGame.Net;

/// <summary>
/// Host → client event ids of a co-op Altered Reality — docs/design/multiplayer.md ->
/// "Co-op realities". They travel in the same reliable batches as the shared ids of
/// <see cref="RiftProtocol"/> and live in their own range, 64–79, so they never
/// collide with the Rift's 1–31.
/// </summary>
public static class RealityProtocol
{
    /// <summary>After a gem drop's spawn record: drop id u32, support u8.</summary>
    public const byte PickupExtra = 64;

    /// <summary>The host moved the receiver's player (respawn, boss pull-in): position.</summary>
    public const byte Teleport = 65;

    /// <summary>The reality is over for everyone: reason u8 (<see cref="EndCollapsed"/>, <see cref="EndHostClosed"/>).</summary>
    public const byte RealityEnd = 66;

    /// <summary>The boss gate: closed u8 (the fight started) or open (won, or everyone in it fell).</summary>
    public const byte GateState = 67;

    /// <summary>The boss fell: tier u8. Each participant records the clear on its own profile.</summary>
    public const byte RealityCleared = 68;

    /// <summary>The exit portal home appeared: position.</summary>
    public const byte ExitPortal = 69;

    public const byte EndCollapsed = 1;
    public const byte EndHostClosed = 2;

    // snapshot flags (RealityHost.WriteModeShared)
    public const byte FlagBossFight = 1;
    public const byte FlagCompleted = 2;
    public const byte FlagCollapsed = 4;
}
