#nullable enable
using System;
using System.Diagnostics;

namespace ArpgGame.Tools;

/// <summary>
/// Stopwatch probes for the frame-budget benches — docs/design/performance.md ->
/// "Frame budget pass". Off unless <see cref="PerfBench"/> turns them on: a probe
/// site is then two static calls that return at once (about a branch each).
/// <code>
/// long t0 = Perf.Begin();            // or: long b0 = 0, t0 = Perf.BeginAlloc(ref b0);
/// ...work...
/// Perf.End(Perf.K.VoxStage, t0);     // or: Perf.EndAlloc(Perf.K.VoxActor, t0, b0);
/// </code>
/// </summary>
public static class Perf
{
    /// <summary>One counter per wired probe; add an entry when a probe is added.</summary>
    public enum K
    {
        EnemyPhys,
        MoveSlide,
        FindTarget,
        EnemyTicks,
        Crowd,
        VoxActor,
        VoxMeshFor,
        VoxStage,
        TargetFrame,
        Director,
        Nuke,
        Count,
    }

    /// <summary>Set by PerfBench only. Shipped builds never turn it on.</summary>
    public static bool Enabled;

    public static readonly long[] Ticks = new long[(int)K.Count];
    public static readonly long[] Calls = new long[(int)K.Count];
    public static readonly long[] Bytes = new long[(int)K.Count];

    public static long Begin() => Enabled ? Stopwatch.GetTimestamp() : 0;

    public static void End(K k, long t0)
    {
        if (!Enabled || t0 == 0)
        {
            return;
        }

        Ticks[(int)k] += Stopwatch.GetTimestamp() - t0;
        Calls[(int)k]++;
    }

    /// <summary>Like <see cref="Begin"/>, also noting this thread's allocated bytes in <paramref name="b0"/>.</summary>
    public static long BeginAlloc(out long b0)
    {
        if (!Enabled)
        {
            b0 = 0;
            return 0;
        }

        b0 = GC.GetAllocatedBytesForCurrentThread();
        return Stopwatch.GetTimestamp();
    }

    public static void EndAlloc(K k, long t0, long b0)
    {
        if (!Enabled || t0 == 0)
        {
            return;
        }

        Bytes[(int)k] += GC.GetAllocatedBytesForCurrentThread() - b0;
        End(k, t0);
    }

    public static void Reset()
    {
        Array.Clear(Ticks);
        Array.Clear(Calls);
        Array.Clear(Bytes);
    }

    public static double Ms(K k) => Ticks[(int)k] * 1000.0 / Stopwatch.Frequency;
}
