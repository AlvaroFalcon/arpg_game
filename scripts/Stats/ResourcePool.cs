using Godot;

namespace ArpgGame.Stats;

/// <summary>
/// A single resource pool an actor owns: a plain mutable <see cref="Current"/>,
/// plus <see cref="Max"/>/<see cref="RegenPerSecond"/> backed by modifiable
/// <see cref="Stat"/>s rather than plain floats — docs/design/stats.md ->
/// "Resource pools become stat-backed". <c>Current</c> stays plain state (it is
/// not a modifier stack); only the ceiling and the regen rate are stats, which is
/// what makes "+40 to Maximum Life" and "+1 Life Regeneration per second"
/// expressible as ordinary <see cref="StatModifier"/>s.
///
/// An actor that doesn't have a given resource simply doesn't allocate a pool
/// for it at all — see docs/design/skills.md -> "Resources are per-actor, and
/// enemies mostly don't have them" for why a missing pool and an empty pool are
/// different things.
/// </summary>
public sealed class ResourcePool
{
    private readonly Stat _maxStat;
    private readonly Stat _regenStat;

    public float Current { get; set; }

    public float Max => NetMax ?? _maxStat.Value;

    /// <summary>
    /// Co-op client: the host's maximum for this pool, from snapshots (docs/design/multiplayer.md
    /// -> "Replication"), which overrides the local stat. The host simulates every player, and a
    /// client's copy of an ally never takes that ally's run cards (each machine applies only its
    /// own), so the local stat could be a stale ceiling: an ally with Vitality showed full at 420
    /// of 503 (owner's report 2026-10-24). Null everywhere else (offline, on the host).
    /// </summary>
    public float? NetMax { get; set; }

    public float RegenPerSecond => _regenStat.Value;

    public ResourcePool(Stat maxStat, Stat regenStat, float startingCurrent)
    {
        _maxStat = maxStat;
        _regenStat = regenStat;
        Current = startingCurrent;
    }

    public void Regen(float delta)
    {
        float regen = RegenPerSecond;
        if (regen <= 0f)
        {
            return;
        }

        Current = Mathf.Min(Max, Current + regen * delta);
    }

    /// <summary>
    /// Re-clamps <see cref="Current"/> after <see cref="Max"/> may have changed
    /// underneath it (e.g. a modifier was added to or removed from the backing
    /// Max stat). docs/design/stats.md -> "What happens to Current when Max
    /// changes": <c>Current</c> is preserved *absolutely*, never as a
    /// percentage — equipping +40 Maximum Life is not a free heal, and
    /// unequipping it clamps you back down. The clamp itself must never be
    /// lethal, so it floors at 1, not 0: "dying from taking off a hat is a bug
    /// report, not a mechanic."
    ///
    /// Only fires when <c>Current</c> has actually drifted above the (possibly
    /// lower) new <c>Max</c>, so it can never resurrect a pool that reached 0
    /// through ordinary damage — <see cref="Combat.Health.TakeDamage"/> owns
    /// that path and this method never runs it backwards.
    /// </summary>
    public void ClampToMax()
    {
        float max = Max;
        if (Current > max)
        {
            Current = Mathf.Max(1f, max);
        }
    }
}
