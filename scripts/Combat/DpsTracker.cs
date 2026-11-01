#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ArpgGame.Combat;

/// <summary>
/// The numbers behind the playground's DPS meter (docs/design/playground.md -> "DPS meter"),
/// node-free so tests can feed it. Every piece of damage the player deals arrives with a
/// time and a label (the skill, or "Bleed", "Ignite"...). A "fight" runs from its first hit
/// to its last; a gap longer than <see cref="IdleReset"/> starts a new one, so walking to the
/// next pack never dilutes the number.
/// </summary>
public sealed class DpsTracker
{
    /// <summary>Seconds without damage after which the next hit starts a new fight.</summary>
    public const float IdleReset = 5f;

    /// <summary>The rolling window of <see cref="Recent"/>.</summary>
    public const float Window = 5f;

    private readonly Dictionary<string, float> _bySource = new();
    private readonly Dictionary<int, float> _perSecond = new();

    public float Total { get; private set; }
    public float First { get; private set; } = -1f;
    public float Last { get; private set; } = -1f;
    public int Hits { get; private set; }
    public int Crits { get; private set; }

    public bool Started => First >= 0f;

    /// <summary>Seconds from the first to the last damage (at least one, so one hit isn't infinite DPS).</summary>
    public float FightSeconds => Started ? Mathf.Max(1f, Last - First) : 0f;

    /// <summary>Damage per second over the whole fight.</summary>
    public float FightDps => Started ? Total / FightSeconds : 0f;

    public void Reset()
    {
        _bySource.Clear();
        _perSecond.Clear();
        Total = 0f;
        First = Last = -1f;
        Hits = Crits = 0;
    }

    /// <summary>Counts damage dealt at <paramref name="now"/> (seconds); a hit counts towards hits and crits.</summary>
    public void Add(float now, float amount, string label, bool hit = false, bool crit = false)
    {
        if (amount <= 0f || !float.IsFinite(amount))
        {
            return;
        }

        if (Started && now - Last > IdleReset)
        {
            Reset();
        }

        if (!Started)
        {
            First = now;
        }

        Last = Mathf.Max(Last, now);
        Total += amount;
        _bySource[label] = (_bySource.TryGetValue(label, out float had) ? had : 0f) + amount;
        int second = Mathf.FloorToInt(now);
        _perSecond[second] = (_perSecond.TryGetValue(second, out float s) ? s : 0f) + amount;
        if (hit)
        {
            Hits++;
            if (crit)
            {
                Crits++;
            }
        }
    }

    /// <summary>Damage per second over the last <see cref="Window"/> seconds before <paramref name="now"/>.</summary>
    public float Recent(float now)
    {
        if (!Started)
        {
            return 0f;
        }

        int to = Mathf.FloorToInt(now);
        float sum = 0f;
        for (int s = to - (int)Window + 1; s <= to; s++)
        {
            sum += _perSecond.TryGetValue(s, out float v) ? v : 0f;
        }

        // The buckets cover from the start of the oldest whole second to now (the current second
        // is only partly over); a fight younger than that is averaged over its own length.
        float covered = now - (to - (int)Window + 1);
        float span = Mathf.Clamp(Mathf.Min(covered, now - First), 1f, Window);
        return sum / span;
    }

    /// <summary>The most damage dealt in any one second of the fight.</summary>
    public float PeakSecond => _perSecond.Count == 0 ? 0f : _perSecond.Values.Max();

    /// <summary>Each source's share, biggest first.</summary>
    public IEnumerable<(string Label, float Total)> Breakdown => _bySource.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value));
}
