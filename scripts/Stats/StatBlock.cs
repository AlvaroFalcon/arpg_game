using Godot;
using Godot.Collections;

namespace PathToExile.Stats;

/// <summary>
/// Per-entity-archetype base stat values — docs/design/stats.md -> "Base values
/// move to a StatBlock resource". Godot 4.1 cannot export a dictionary keyed by
/// an enum, and one <c>[Export] float BaseX</c> per stat does not survive
/// eighteen stats, so this is an array of <see cref="StatBlockEntry"/> instead.
///
/// Assign to <see cref="ActorStats.Bases"/>. Any <see cref="StatId"/> not listed
/// here falls back to <see cref="ActorStats.DefaultBase"/>. A side benefit: an
/// enemy/dummy archetype becomes a <c>.tres</c> asset rather than a hand-tuned
/// per-instance scene override.
/// </summary>
[GlobalClass]
public partial class StatBlock : Resource
{
    [Export]
    public Array<StatBlockEntry> Entries = new();
}
