using Godot;
using PathToExile.Stats;

namespace PathToExile.Combat;

/// <summary>
/// The damage receiver. Deliberately dumb, per docs/design/skills.md milestone 1
/// scope: takes a <see cref="DamagePayload"/>, mutates the owning actor's
/// <see cref="ActorStats.Life"/> pool, and fires <see cref="Died"/> at zero. No AI,
/// no movement, no retaliation — that belongs to whatever is attached to the
/// entity this component lives on, not to this component.
///
/// Expects a sibling "Stats" node (an <see cref="ActorStats"/>) on the same parent,
/// mirroring the convention used by <see cref="Skills.SkillUser"/>. Life itself
/// lives on ActorStats (not duplicated here) so cost-resolution code and the
/// damage receiver share one source of truth for an actor's life pool.
/// </summary>
[GlobalClass]
public partial class Health : Node
{
    [Signal]
    public delegate void DiedEventHandler();

    [Signal]
    public delegate void DamagedEventHandler(float amount);

    private ActorStats _stats = null!;

    public bool IsDead { get; private set; }

    public Faction Faction => _stats.Faction;

    public override void _Ready()
    {
        _stats = GetParent().GetNode<ActorStats>("Stats");
    }

    public void TakeDamage(DamagePayload payload)
    {
        if (IsDead)
        {
            return;
        }

        // Defense in depth: collision layers already keep same-faction hits from
        // ever reaching here in the normal case, but this check is what makes
        // correctness independent of getting the physics layers exactly right.
        if (payload.SourceFaction == _stats.Faction)
        {
            return;
        }

        ResourcePool life = _stats.Life!;
        life.Current = Mathf.Max(0f, life.Current - payload.Amount);
        EmitSignal(SignalName.Damaged, payload.Amount);

        if (life.Current <= 0f)
        {
            IsDead = true;
            EmitSignal(SignalName.Died);
        }
    }
}
