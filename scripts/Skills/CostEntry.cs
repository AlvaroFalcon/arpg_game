using Godot;
using PathToExile.Stats;

namespace PathToExile.Skills;

/// <summary>
/// How a <see cref="CostEntry"/>'s <see cref="CostEntry.Amount"/> is interpreted.
/// See docs/design/skills.md -> "Resource costs" for why three modes exist:
/// Flat suits Mana (pools grow modestly, a flat number stays a real budget);
/// PercentOfMax suits Life (pools scale hugely with gear, so a flat cost would be
/// lethal early and free late); PercentOfCurrent is the self-limiting variant
/// (shrinks as you get hurt, so it can never spiral).
/// </summary>
public enum CostMode
{
    Flat,
    PercentOfMax,
    PercentOfCurrent,
}

/// <summary>
/// One entry in a skill's cost list. A definition carries a *list* of these
/// (never a single scalar cost) because hybrid costs and cost-converting
/// modifiers ("your spells cost Life instead of Mana") both need a collection to
/// operate over — see docs/design/skills.md -> "Why a list and not a single cost".
///
/// A Godot `Resource` (not a plain class) so it can be authored inline as a
/// sub-resource inside a skill's `.tres` file from the editor.
/// </summary>
[GlobalClass]
public partial class CostEntry : Resource
{
    [Export]
    public ResourceType Resource = ResourceType.Mana;

    [Export]
    public float Amount;

    [Export]
    public CostMode Mode = CostMode.Flat;
}
