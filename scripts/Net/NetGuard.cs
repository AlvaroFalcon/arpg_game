#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArpgGame.Net;

/// <summary>
/// The host's limits on what a client may send — docs/design/multiplayer.md ->
/// "Security". One table of numbers, used by <see cref="NetSession"/>. Every
/// limit is far above what the real game sends (measured: input 14 bytes per
/// physics frame, a level-30 character with full gear about 40 KB of JSON),
/// so only a broken or hostile client ever reaches one.
/// </summary>
public static class NetLimits
{
    /// <summary>The authentication message (magic, protocol, code).</summary>
    public const int AuthBytes = 64;

    /// <summary>The hello: the lobby entry with the whole character as JSON (characters, i.e. UTF-16 units).</summary>
    public const int HelloChars = 256 * 1024;

    /// <summary>One input packet (14 bytes today).</summary>
    public const int InputBytes = 64;

    /// <summary>One batch of client events (ready, card pick, test answers).</summary>
    public const int ClientEventBytes = 4096;

    /// <summary>One party-session message (place, proposal, answer, edit lock, test answer; under 100 bytes).</summary>
    public const int PartyMessageBytes = 2048;

    /// <summary>One hideout presence packet (sequence, position, flags: 7 bytes).</summary>
    public const int PresenceBytes = 32;

    /// <summary>A hideout layout as JSON (the default town is about 6 K characters with every
    /// prop; 400 props and a fully repaved town stay under this).</summary>
    public const int LayoutChars = 96 * 1024;

    /// <summary>A member may resend their character (gear changed in the hideout) at most this often.</summary>
    public const double CharacterUpdateSeconds = 2.0;

    /// <summary>Messages per peer: a token bucket refilled at <see cref="MessagesPerSecond"/>,
    /// holding up to <see cref="MessageBurst"/> (inputs arrive at 60/s, bunched after a lag spike).</summary>
    public const float MessagesPerSecond = 150f;
    public const float MessageBurst = 300f;

    /// <summary>Revive intents (start or stop a revive: docs/design/multiplayer.md -> "Deaths and
    /// revives") per peer: a bucket refilled at this rate, holding up to <see cref="ReviveIntentBurst"/>.
    /// Holding E sends one start, then one again every 0.4 s at most while it is refused;
    /// releasing sends one stop. More than this is refused and counted.</summary>
    public const float ReviveIntentsPerSecond = 4f;
    public const float ReviveIntentBurst = 6f;

    /// <summary>Wrong or malformed join codes from one address before it is ignored…</summary>
    public const int FailedAttempts = 5;

    /// <summary>…within this window, and for how long it is then ignored.</summary>
    public const double FailedWindowSeconds = 60.0;
    public const double BlockSeconds = 60.0;

    /// <summary>Wrong, malformed or missing codes from all addresses together, per
    /// <see cref="FailedWindowSeconds"/>, before every new connection is turned away
    /// unanswered until the window drains: a ceiling for many-address brute force.</summary>
    public const int GlobalFailedAttempts = 120;

    /// <summary>Connections from one address still waiting for their code at once.</summary>
    public const int PendingPerAddress = 4;

    /// <summary>Seconds a connecting peer has to send its code (the engine drops it
    /// after, and it counts as a failed attempt). A real client sends it at once.</summary>
    public const double AuthTimeout = 5.0;

    /// <summary>A turned-away peer is told why, then dropped for good after this
    /// many seconds even if it never acknowledges the answer.</summary>
    public const double RefuseGraceSeconds = 2.0;

    // character sanity bounds (docs/design/multiplayer.md -> "Security" -> "What the host checks")
    public const int NameChars = 32;
    public const int IdChars = 64;
    public const int MaxItems = 160;
    public const int MaxAffixesPerItem = 16;
    public const int MaxBaseStatsPerItem = 16;
    public const int MaxGems = 200;
    public const int MaxPassives = 600;
    public const int MaxAscendancyNodes = 64;
    public const int MaxQuests = 32;
    public const int MaxQuestCount = 999;
    public const int MaxItemLevel = 200;
    public const float MaxStatValue = 1_000_000f;
}

/// <summary>
/// Failed join attempts per address: after <see cref="NetLimits.FailedAttempts"/>
/// failures within <see cref="NetLimits.FailedWindowSeconds"/>, the address is
/// ignored for <see cref="NetLimits.BlockSeconds"/>. IPv6 addresses count per /64
/// (one home connection owns a whole /64, so per-address would be free to dodge).
/// Past <see cref="NetLimits.GlobalFailedAttempts"/> failures from everyone in a
/// window, <see cref="GloballyBlocked"/> turns every newcomer away. Memory is
/// bounded: stale entries are pruned first, then the oldest goes (never a wholesale
/// reset an attacker could trigger by rotating addresses). Clock in seconds,
/// passed in (tests drive it by hand).
/// </summary>
public sealed class AttemptLimiter
{
    public const int MaxTracked = 1024;

    private readonly Dictionary<string, List<double>> _failures = new();
    private readonly Dictionary<string, double> _blockedUntil = new();
    private readonly Queue<double> _global = new();

    /// <summary>The limiter's key for an address: IPv4 as is, IPv6 by its /64.</summary>
    public static string Key(string address)
    {
        if (System.Net.IPAddress.TryParse(address, out System.Net.IPAddress? ip))
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                return ip.MapToIPv4().ToString();
            }

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                byte[] b = ip.GetAddressBytes();
                Array.Clear(b, 8, 8);
                return new System.Net.IPAddress(b) + "/64";
            }
        }

        return address;
    }

    public bool IsBlocked(string address, double now)
    {
        string key = Key(address);
        if (_blockedUntil.TryGetValue(key, out double until))
        {
            if (now < until)
            {
                return true;
            }

            _blockedUntil.Remove(key);
            _failures.Remove(key);
        }

        return false;
    }

    /// <summary>Too many failures from everyone lately: turn every newcomer away.</summary>
    public bool GloballyBlocked(double now)
    {
        while (_global.Count > 0 && now - _global.Peek() > NetLimits.FailedWindowSeconds)
        {
            _global.Dequeue();
        }

        return _global.Count >= NetLimits.GlobalFailedAttempts;
    }

    /// <summary>Records a failure; true if the address is now blocked.</summary>
    public bool Fail(string address, double now)
    {
        string key = Key(address);
        _global.Enqueue(now);
        GloballyBlocked(now);
        if (!_failures.TryGetValue(key, out List<double>? times))
        {
            if (_failures.Count >= MaxTracked)
            {
                Prune(now);
            }

            times = new List<double>();
            _failures[key] = times;
        }

        times.RemoveAll(t => now - t > NetLimits.FailedWindowSeconds);
        times.Add(now);
        if (times.Count >= NetLimits.FailedAttempts)
        {
            if (_blockedUntil.Count >= MaxTracked)
            {
                foreach (string k in _blockedUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
                {
                    _blockedUntil.Remove(k);
                }

                if (_blockedUntil.Count >= MaxTracked)
                {
                    _blockedUntil.Remove(_blockedUntil.MinBy(kv => kv.Value).Key);
                }
            }

            _blockedUntil[key] = now + NetLimits.BlockSeconds;
            times.Clear();
            return true;
        }

        return false;
    }

    /// <summary>Addresses tracked (tests: memory stays bounded).</summary>
    public int Tracked => _failures.Count + _blockedUntil.Count;

    private void Prune(double now)
    {
        foreach (string k in _failures.Where(kv => kv.Value.Count == 0 || now - kv.Value[^1] > NetLimits.FailedWindowSeconds).Select(kv => kv.Key).ToList())
        {
            _failures.Remove(k);
        }

        while (_failures.Count >= MaxTracked)
        {
            _failures.Remove(_failures.MinBy(kv => kv.Value[^1]).Key);
        }
    }

    public void Clear()
    {
        _failures.Clear();
        _blockedUntil.Clear();
        _global.Clear();
    }
}

/// <summary>A per-peer message budget (token bucket). Clock in seconds, passed in. By default
/// the message budget (<see cref="NetLimits.MessagesPerSecond"/>); other limits pass their own.</summary>
public sealed class PeerBudget
{
    private readonly float _rate;
    private readonly float _burst;
    private float _tokens;
    private double _last = double.NaN;

    public PeerBudget(float rate = NetLimits.MessagesPerSecond, float burst = NetLimits.MessageBurst)
    {
        _rate = rate;
        _burst = burst;
        _tokens = burst;
    }

    /// <summary>Spends one message; false if the peer is over its budget.</summary>
    public bool Spend(double now)
    {
        if (!double.IsNaN(_last))
        {
            _tokens = Math.Min(_burst, _tokens + (float)(now - _last) * _rate);
        }

        _last = now;
        _tokens -= 1f;
        return _tokens >= 0f;
    }
}
