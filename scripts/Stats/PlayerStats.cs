using Godot;

namespace PathToExile.Stats;

/// <summary>
/// Holds the player's live stats. Currently only movement speed exists; future
/// stats (damage, life, resistances, etc.) should follow the same pattern:
/// an [Export] base value plus a computed <see cref="Stat"/> built in _Ready().
/// </summary>
[GlobalClass]
public partial class PlayerStats : Node
{
    [Export]
    public float BaseMovementSpeed = 130f;

    public Stat MovementSpeed { get; private set; } = null!;

    public override void _Ready()
    {
        MovementSpeed = new Stat(BaseMovementSpeed);
    }
}
