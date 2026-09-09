using Godot;
using PathToExile.Items;
using PathToExile.Skills;
using PathToExile.Stats;

namespace PathToExile;

/// <summary>
/// Top-down player controller. Supports two input modes that share one velocity:
/// - Direct (WASD): immediate directional movement, always takes priority.
/// - PathFollow (hold a move input): retargets a NavigationAgent2D to the mouse
///   position every physics frame while held, then coasts the existing path to
///   completion after release (so a quick click-and-release still works).
/// See docs/design/movement.md for the full rationale.
///
/// Path-follow's trigger changed with the combat pass (docs/design/skills.md ->
/// "The right-click conflict — resolved"): it is no longer a hardcoded
/// `move_to` action. LMB is now a loadout slot that *defaults* to the built-in
/// Move occupant, and a separate `force_move` action is the escape hatch once a
/// real skill is bound over LMB. This class asks <see cref="SkillUser"/> what's
/// in the LMB slot rather than knowing loadout internals itself — see
/// <see cref="PlayerSkillInput"/>'s doc comment for the matching split on the
/// casting side.
/// </summary>
public partial class Player : CharacterBody2D
{
    private NavigationAgent2D _navigationAgent = null!;
    private ActorStats _stats = null!;
    private SkillUser _skillUser = null!;

    /// <summary>
    /// docs/design/items.md -&gt; "UI must consume its own input": the fix for
    /// "dragging an item in the inventory also issues a move order" lives here,
    /// not as an "is a panel open?" check bolted onto <see cref="_PhysicsProcess"/>
    /// — see <see cref="MouseActionGate"/>'s class doc for why a raw
    /// <c>Input.IsActionPressed</c> poll cannot tell a world click from a
    /// GUI-consumed one, and why the fix belongs at the input-handling layer
    /// instead.
    /// </summary>
    private readonly MouseActionGate _lmbGate = new("skill_slot_lmb");

    /// <summary>docs/design/items.md -> "Currency": <c>Player.Wallet</c>. A sibling
    /// component node, exposed here the same way <see cref="ActorStats"/>/
    /// <see cref="SkillUser"/> are, so pickup code (<see cref="Items.CurrencyDrop"/>)
    /// can reach it off the player it hit.</summary>
    public Wallet Wallet { get; private set; } = null!;

    /// <summary>
    /// Another sibling component, same convention as <see cref="Wallet"/>.
    /// Exposed publicly so <see cref="Items.EquipmentItem.OnPickup"/> can reach
    /// the player's slots the same way <see cref="Items.CurrencyDrop.OnPickup"/>
    /// reaches <see cref="Wallet"/>.
    /// </summary>
    public Items.Equipment Equipment { get; private set; } = null!;

    /// <summary>
    /// Milestone 2c: the grid inventory (docs/design/items.md -> "Grid
    /// inventory"). Same sibling-component convention as
    /// <see cref="Wallet"/>/<see cref="Equipment"/>; exposed publicly so
    /// <see cref="Items.EquipmentItem.OnPickup"/> can route a pickup into it.
    /// </summary>
    public Items.Inventory Inventory { get; private set; } = null!;

    public override void _Ready()
    {
        _navigationAgent = GetNode<NavigationAgent2D>("NavigationAgent2D");
        _stats = GetNode<ActorStats>("Stats");
        _skillUser = GetNode<SkillUser>("SkillUser");
        Wallet = GetNode<Wallet>("Wallet");
        Equipment = GetNode<Items.Equipment>("Equipment");
        Inventory = GetNode<Items.Inventory>("Inventory");
    }

    /// <summary>
    /// Only the rising edge of <c>skill_slot_lmb</c> is fed here (see
    /// <see cref="MouseActionGate"/>) — a press the GUI already consumed
    /// (e.g. starting a drag over the inventory grid, which sets
    /// <c>MouseFilter.Stop</c>) never reaches <c>_UnhandledInput</c> at all, so
    /// <see cref="_lmbGate"/> never latches on for it.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event) => _lmbGate.HandleUnhandledInput(@event);

    public override void _PhysicsProcess(double delta)
    {
        float speed = _stats.MovementSpeed.Value;

        Vector2 wasdDir = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        if (wasdDir != Vector2.Zero)
        {
            // WASD cancels a pending click-to-move destination outright instead of
            // pausing it — otherwise releasing WASD would resume walking to a target
            // the player has already steered away from. Godot 4.1's NavigationAgent2D
            // has no explicit cancel, so retargeting to the current position is what
            // marks the path finished.
            if (!_navigationAgent.IsNavigationFinished())
            {
                _navigationAgent.TargetPosition = GlobalPosition;
            }

            Velocity = wasdDir * speed;
            MoveAndSlide();
            return;
        }

        // Path-follow triggers on either: the LMB slot still holds the built-in
        // Move sentinel and skill_slot_lmb is held; or force_move is held
        // regardless of what's bound. Both retarget to the same mouse world
        // position, so this collapses to one boolean feeding one retarget line.
        bool pathFollow = (_skillUser.SlotIsBuiltInMove(SkillSlotId.Lmb) && _lmbGate.Poll())
            || Input.IsActionPressed("force_move");
        if (pathFollow)
        {
            _navigationAgent.TargetPosition = GetGlobalMousePosition();
        }

        // Navigation map may not be active yet (e.g. first physics frame(s) after
        // the scene loads, before the NavigationRegion2D registers with the
        // server) — nothing to path against yet, just wait.
        // Note: NavigationServer2D.MapGetIterationId() (the more precise "has this
        // map finished syncing" check) isn't available in Godot 4.1.1's C# API —
        // it was added in a later 4.x release. MapIsActive() is the closest
        // equivalent guard on this version; see docs/design/movement.md.
        if (!NavigationServer2D.MapIsActive(_navigationAgent.GetNavigationMap()))
        {
            return;
        }

        if (_navigationAgent.IsNavigationFinished())
        {
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        Vector2 nextPathPosition = _navigationAgent.GetNextPathPosition();
        Velocity = GlobalPosition.DirectionTo(nextPathPosition) * speed;
        MoveAndSlide();
    }
}
