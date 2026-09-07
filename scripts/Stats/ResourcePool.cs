using Godot;

namespace PathToExile.Stats;

/// <summary>
/// A single resource pool an actor owns: current amount, max, and an optional flat
/// regen per second. An actor that doesn't have a given resource simply doesn't
/// allocate a pool for it at all — see docs/design/skills.md -> "Resources are
/// per-actor, and enemies mostly don't have them" for why a missing pool and an
/// empty pool are different things.
/// </summary>
public sealed class ResourcePool
{
    public float Current { get; set; }
    public float Max { get; set; }
    public float RegenPerSecond { get; set; }

    public ResourcePool(float current, float max, float regenPerSecond)
    {
        Current = current;
        Max = max;
        RegenPerSecond = regenPerSecond;
    }

    public void Regen(float delta)
    {
        if (RegenPerSecond <= 0f)
        {
            return;
        }

        Current = Mathf.Min(Max, Current + RegenPerSecond * delta);
    }
}
