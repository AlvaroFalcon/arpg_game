#nullable enable
using System;
using Godot;
using ArpgGame.Enemies;
using ArpgGame.Run;
using ArpgGame.World;

namespace ArpgGame.Net;

/// <summary>
/// A co-op client's half of an Altered Reality — docs/design/multiplayer.md ->
/// "Co-op realities". The cavern is built here from the same seed as on the host;
/// monsters, effects and drops come through the shared replication
/// (<see cref="CoopClient"/>). This adds: the skill buttons held (realities cast on
/// press, the host casts), the reality's numbers and this player's mana from
/// every snapshot, gem drops, and the reality's moments (teleports, the gate,
/// the clear, the exit portal, the end), which the <see cref="RealityController"/>
/// shows. Rewards land in this machine's profile only.
/// </summary>
public partial class RealityClient : CoopClient
{
    public RealityController Reality { get; set; } = null!;

    protected override Player Local => Reality.Player;

    protected override ICoopWorld World => Reality.Party;

    protected override bool SendsHeldSlots => true;

    /// <summary>Test hook: these skill buttons count as held (tests have no mouse or keyboard).</summary>
    public static byte? TestHeld { get; set; }

    /// <summary>Teleports the host sent this player (tests, diagnostics).</summary>
    public int Teleports { get; private set; }

    protected override byte HeldSlots()
    {
        if (TestHeld is { } held)
        {
            return held;
        }

        if (UI.PanelStack.HasOpen || !Local.ControlsEnabled)
        {
            return 0;
        }

        return Local.GetNodeOrNull<PlayerSkillInput>("PlayerSkillInput")?.HeldMask() ?? 0;
    }

    protected override void ReadModeShared(NetReader r, double hostTime)
    {
        int kills = r.U16();
        int total = r.U16();
        int portals = r.U8();
        byte flags = r.U8();
        float mana = r.U16();
        Reality.ApplyShared(kills, total, portals, (flags & RealityProtocol.FlagBossFight) != 0);
        if (Local.Stats.Mana is { } m)
        {
            m.Current = Mathf.Min(mana, m.Max);
        }
    }

    protected override void Collected(PickupKind kind, float value, string key, int detail) =>
        Reality.ApplyReward(Local, kind, value, key, detail == 1, fromHost: true);

    protected override void Announce(string title, string subtitle, Color color) =>
        Reality.EmitSignal(RealityController.SignalName.Announced, title, subtitle, color);

    protected override void BossReplicaSpawned(Enemy boss) => Reality.AdoptBossReplica(boss);

    protected override bool ApplyModeEvent(byte type, NetReader r)
    {
        switch (type)
        {
            case RealityProtocol.PickupExtra:
            {
                uint id = r.U32();
                bool support = r.Bool();
                if (PickupById(id) is { } gem)
                {
                    gem.GemSupport = support;
                    gem.QueueRedraw();
                }

                return true;
            }

            case RealityProtocol.Teleport:
            {
                Vector2 at = r.Pos();
                if (Reality.Raster.Walkable(Reality.Field.CellOf(at)))
                {
                    SnapLocal(at);
                    Teleports++;
                }

                return true;
            }

            case RealityProtocol.RealityEnd:
                Reality.OnRemoteEnd(r.U8());
                return true;
            case RealityProtocol.GateState:
                Reality.OnRemoteGate(r.Bool());
                return true;
            case RealityProtocol.RealityCleared:
                Reality.OnRemoteCleared(Math.Clamp((int)r.U8(), 1, RealityTiers.MaxTier));
                return true;
            case RealityProtocol.ExitPortal:
            {
                Vector2 at = r.Pos();
                if (Reality.Raster.Walkable(Reality.Field.CellOf(at)) || Reality.Raster.BossRegion.Contains(Reality.Field.CellOf(at)))
                {
                    Reality.OnRemoteExit(at);
                }

                return true;
            }
        }

        return false;
    }

    protected override void ModeExit() => TestHeld = null;

    public override void OnHostLost(string reason)
    {
        base.OnHostLost(reason);
        Reality.OnHostLost(reason);
    }
}
