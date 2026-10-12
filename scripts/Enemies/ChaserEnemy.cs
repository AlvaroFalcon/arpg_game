#nullable enable
using Godot;

namespace ArpgGame.Enemies;

/// <summary>
/// Walks straight at the player — the slime and the bat. <see cref="Weave"/>
/// adds a sideways sine wobble so a swarm of bats reads as a swarm rather than
/// a conga line, and makes them slightly harder to line up shots on.
/// </summary>
[GlobalClass]
public partial class ChaserEnemy : Enemy
{
    /// <summary>Sideways wobble as a fraction of speed (0 = straight line).</summary>
    [Export]
    public float Weave;

    [Export]
    public float WeaveFrequency = 4f;

    private float _phase = GD.Randf() * Mathf.Tau;

    protected override void Think(float delta)
    {
        _phase += delta * WeaveFrequency;
        Vector2 dir = DirectionToTarget;
        Vector2 side = new(-dir.Y, dir.X);
        DesiredVelocity = (dir + side * Mathf.Sin(_phase) * Weave).Normalized() * Speed;
    }
}
