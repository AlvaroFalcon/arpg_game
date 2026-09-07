using Godot;
using PathToExile.Stats;

namespace PathToExile;

/// <summary>
/// Top-down player controller. Supports two input modes that share one velocity:
/// - Direct (WASD): immediate directional movement, always takes priority.
/// - PathFollow (hold right mouse button): retargets a NavigationAgent2D to the
///   mouse position every physics frame while held, then coasts the existing
///   path to completion after release (so a quick click-and-release still works).
/// See docs/design/movement.md for the full rationale.
/// </summary>
public partial class Player : CharacterBody2D
{
    private NavigationAgent2D _navigationAgent = null!;
    private PlayerStats _playerStats = null!;

    public override void _Ready()
    {
        _navigationAgent = GetNode<NavigationAgent2D>("NavigationAgent2D");
        _playerStats = GetNode<PlayerStats>("PlayerStats");
    }

    public override void _PhysicsProcess(double delta)
    {
        float speed = _playerStats.MovementSpeed.Value;

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

        if (Input.IsActionPressed("move_to"))
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
