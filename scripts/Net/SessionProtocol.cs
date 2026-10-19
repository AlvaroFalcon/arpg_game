#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ArpgGame.World;

namespace ArpgGame.Net;

/// <summary>Where the party as a whole is (host-authoritative, sent in every
/// <see cref="SessionProtocol.Roster"/>) — docs/design/multiplayer.md -> "Party session".</summary>
public enum SessionPhase : byte
{
    /// <summary>The main-menu lobby: nobody has loaded a world yet.</summary>
    Lobby = 0,

    /// <summary>Everyone free is in the host's hideout.</summary>
    Hideout = 1,

    /// <summary>A rift is running on the host (its participants are in it; the others wait in the hideout).</summary>
    Run = 2,

    /// <summary>The host's rift ended; the phase is Hideout again once the host is back home.</summary>
    Returning = 3,
}

/// <summary>Where one member's machine is. Each machine reports its own
/// (<see cref="SessionProtocol.Place"/>); the host is authoritative for itself.</summary>
public enum MemberPlace : byte
{
    Menu = 0,
    Loading = 1,
    Hideout = 2,
    Run = 3,
}

/// <summary>What a rift proposal opens.</summary>
public enum RiftKind : byte
{
    SurvivalRift = 1,

    /// <summary>An Altered Reality of a tier (co-op realities: phase B).</summary>
    Reality = 2,
}

/// <summary>
/// What a rift is: the kind, the reality tier (0 for the Survival Rift), a
/// pinned layout ("" = by seed), the map seed (drawn by the host at the start;
/// 0 in a proposal) and up to three shard modifiers. Travels in proposals and
/// in <see cref="SessionProtocol.StartRift"/>; both ends sanitise it.
/// </summary>
public sealed class RiftSpec
{
    public const int MaxModifiers = 3;

    public RiftKind Kind { get; set; } = RiftKind.SurvivalRift;
    public int Tier { get; set; }
    public string LayoutId { get; set; } = "";
    public ulong Seed { get; set; }
    public List<string> Modifiers { get; set; } = new();

    public static RiftSpec SurvivalRift() => new() { Kind = RiftKind.SurvivalRift };

    public static RiftSpec Reality(int tier, IEnumerable<string>? modifiers = null) =>
        new() { Kind = RiftKind.Reality, Tier = tier, Modifiers = modifiers?.ToList() ?? new List<string>() };

    public RiftSpec Copy() => new() { Kind = Kind, Tier = Tier, LayoutId = LayoutId, Seed = Seed, Modifiers = new List<string>(Modifiers) };

    public void Write(NetWriter w)
    {
        w.U8((byte)Kind);
        w.U8((byte)Math.Clamp(Tier, 0, 255));
        w.Str(LayoutId);
        w.U32((uint)(Seed & 0xFFFFFFFF));
        w.U32((uint)(Seed >> 32));
        w.U8((byte)Math.Min(Modifiers.Count, MaxModifiers));
        foreach (string id in Modifiers.Take(MaxModifiers))
        {
            w.Str(id);
        }
    }

    /// <summary>Reads a spec and sanitises it; null when it isn't a valid rift.</summary>
    public static RiftSpec? Read(NetReader r)
    {
        var spec = new RiftSpec
        {
            Kind = (RiftKind)r.U8(),
            Tier = r.U8(),
            LayoutId = r.Str(),
        };
        uint low = r.U32();
        uint high = r.U32();
        spec.Seed = low | ((ulong)high << 32);
        int count = r.U8();
        if (count > MaxModifiers)
        {
            return null;
        }

        for (int i = 0; i < count; i++)
        {
            spec.Modifiers.Add(r.Str());
        }

        return spec.Sanitize() ? spec : null;
    }

    /// <summary>
    /// Is this a rift the game knows? Known kind; the Survival Rift has tier 0, no
    /// layout and no modifiers; a reality has a tier 1..<see cref="RealityTiers.MaxTier"/>,
    /// known distinct modifier ids and a known layout (or none).
    /// </summary>
    public bool Sanitize()
    {
        switch (Kind)
        {
            case RiftKind.SurvivalRift:
                Tier = 0;
                LayoutId = "";
                Modifiers.Clear();
                return true;
            case RiftKind.Reality:
                if (Tier < 1 || Tier > RealityTiers.MaxTier || Modifiers.Count > MaxModifiers)
                {
                    return false;
                }

                if (Modifiers.Any(m => m.Length > NetLimits.IdChars || RealityModifier.Find(m) is null) || Modifiers.Distinct().Count() != Modifiers.Count)
                {
                    return false;
                }

                return LayoutId.Length == 0 || (LayoutId.Length <= NetLimits.IdChars && LayoutCatalog.Find(LayoutId) is not null);
            default:
                return false;
        }
    }

    /// <summary>"the Survival Rift" / "an Altered Reality — Tier 20".</summary>
    public string Describe() => Kind == RiftKind.Reality ? $"an Altered Reality — Tier {Tier}" : "the Survival Rift";

    /// <summary>"Survival Rift" / "Tier 20 reality" (banners).</summary>
    public string Short() => Kind == RiftKind.Reality ? $"Tier {Tier} reality" : "Survival Rift";
}

/// <summary>The rift the party is running: an id, what it is, and who is in it.</summary>
public sealed class PartyRun
{
    public int Id { get; init; }
    public RiftSpec Spec { get; init; } = RiftSpec.SurvivalRift();
    public List<long> Participants { get; init; } = new();

    /// <summary>Host clock (seconds) when it started (clients: their own clock when told).</summary>
    public double StartedAt { get; init; }

    /// <summary>Who proposed it (their shard pays for a reality).</summary>
    public long Proposer { get; init; } = 1;

    public bool Includes(long peer) => Participants.Contains(peer);
}

/// <summary>
/// Message kinds of the party session — docs/design/multiplayer.md -> "Protocol 5".
/// Each byte payload of <c>RpcPartyToClient</c> / <c>RpcPartyToHost</c> is one
/// message: its kind, then its fields, written with <see cref="NetWriter"/>.
/// </summary>
public static class SessionProtocol
{
    // host → client
    public const byte Roster = 1;
    public const byte Proposal = 2;
    public const byte ProposalStatus = 3;
    public const byte ProposalClosed = 4;
    public const byte StartRift = 5;
    public const byte RiftOver = 6;
    public const byte EditGranted = 7;
    public const byte EditRefused = 8;
    public const byte EditRevoked = 9;
    public const byte Notice = 10;
    public const byte TestAsk = 11;

    // client → host
    public const byte Place = 1;
    public const byte Propose = 2;
    public const byte Answer = 3;
    public const byte CancelProposal = 4;
    public const byte EditRequest = 5;
    public const byte EditCancel = 6;
    public const byte TestAnswer = 7;

    /// <summary>Presence flags (low bits of the presence byte, after the 8-way direction).</summary>
    public const byte PresenceMoving = 1;
    public const byte PresenceSprinting = 2;
    public const byte PresenceDodging = 4;

    /// <summary>Longest notice, reason or test text accepted from the wire.</summary>
    public const int TextChars = 200;

    public static string Cut(string text, int max = TextChars) => text.Length > max ? text[..max] : text;
}
