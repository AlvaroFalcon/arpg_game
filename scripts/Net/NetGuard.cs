#nullable enable
using System;
using System.Collections.Generic;

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

    /// <summary>Messages per peer: a token bucket refilled at <see cref="MessagesPerSecond"/>,
    /// holding up to <see cref="MessageBurst"/> (inputs arrive at 60/s, bunched after a lag spike).</summary>
    public const float MessagesPerSecond = 150f;
    public const float MessageBurst = 300f;

    /// <summary>Wrong or malformed join codes from one address before it is ignored…</summary>
    public const int FailedAttempts = 5;

    /// <summary>…within this window, and for how long it is then ignored.</summary>
    public const double FailedWindowSeconds = 60.0;
    public const double BlockSeconds = 60.0;

    /// <summary>Seconds a connecting peer has to send its code (the engine drops it after).</summary>
    public const double AuthTimeout = 8.0;

    // character sanity bounds (docs/design/multiplayer.md -> "Security" -> "What the host checks")
    public const int NameChars = 32;
    public const int IdChars = 64;
    public const int MaxItems = 160;
    public const int MaxAffixesPerItem = 16;
    public const int MaxBaseStatsPerItem = 16;
    public const int MaxGems = 200;
    public const int MaxPassives = 600;
    public const int MaxAscendancyNodes = 64;
    public const int MaxItemLevel = 200;
    public const float MaxStatValue = 1_000_000f;
}

/// <summary>
/// Failed join attempts per address: after <see cref="NetLimits.FailedAttempts"/>
/// failures within <see cref="NetLimits.FailedWindowSeconds"/>, the address is
/// ignored for <see cref="NetLimits.BlockSeconds"/>. Clock in seconds, passed in
/// (tests drive it by hand).
/// </summary>
public sealed class AttemptLimiter
{
    private readonly Dictionary<string, List<double>> _failures = new();
    private readonly Dictionary<string, double> _blockedUntil = new();

    public bool IsBlocked(string address, double now)
    {
        if (_blockedUntil.TryGetValue(address, out double until))
        {
            if (now < until)
            {
                return true;
            }

            _blockedUntil.Remove(address);
            _failures.Remove(address);
        }

        return false;
    }

    /// <summary>Records a failure; true if the address is now blocked.</summary>
    public bool Fail(string address, double now)
    {
        if (!_failures.TryGetValue(address, out List<double>? times))
        {
            // Bounded memory: a flood of distinct addresses forgets the oldest.
            if (_failures.Count > 1024)
            {
                _failures.Clear();
            }

            times = new List<double>();
            _failures[address] = times;
        }

        times.RemoveAll(t => now - t > NetLimits.FailedWindowSeconds);
        times.Add(now);
        if (times.Count >= NetLimits.FailedAttempts)
        {
            _blockedUntil[address] = now + NetLimits.BlockSeconds;
            times.Clear();
            return true;
        }

        return false;
    }

    public void Clear()
    {
        _failures.Clear();
        _blockedUntil.Clear();
    }
}

/// <summary>A per-peer message budget (token bucket). Clock in seconds, passed in.</summary>
public sealed class PeerBudget
{
    private float _tokens = NetLimits.MessageBurst;
    private double _last = double.NaN;

    /// <summary>Spends one message; false if the peer is over its budget.</summary>
    public bool Spend(double now)
    {
        if (!double.IsNaN(_last))
        {
            _tokens = Math.Min(NetLimits.MessageBurst, _tokens + (float)(now - _last) * NetLimits.MessagesPerSecond);
        }

        _last = now;
        _tokens -= 1f;
        return _tokens >= 0f;
    }
}
