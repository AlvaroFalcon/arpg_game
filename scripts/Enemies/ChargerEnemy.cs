#nullable enable
using Godot;

namespace ArpgGame.Enemies;

/// <summary>
/// The ember brute — docs/design/enemies.md -> "Brute". Lumbers toward the
/// player; within <see cref="ChargeRange"/> it stops, draws its charge lane on
/// the ground for <see cref="WindupTime"/> seconds, then charges along that
/// locked lane. The lane is the whole point: every big hit in the game is
/// announced where it will land, so the counterplay (step sideways, or dodge
/// through) is always available to a player who is paying attention.
/// </summary>
[GlobalClass]
public partial class ChargerEnemy : Enemy
{
    [Export]
    public float ChargeRange = 280f;

    [Export]
    public float WindupTime = 0.8f;

    [Export]
    public float ChargeSpeed = 560f;

    [Export]
    public float ChargeDistance = 380f;

    [Export]
    public float ChargeCooldown = 3.5f;

    private enum State { Walk, Windup, Charge, Recover }

    private State _state = State.Walk;
    private float _timer;
    private float _cooldown = 1.5f;
    private Vector2 _lane;

    protected override float ContactDamageScale() => _state == State.Charge ? 1.6f : 1f;

    /// <summary>True while the charge lane is drawn (AI/tools can read it).</summary>
    public bool IsWindingUp => _state == State.Windup;

    public bool IsCharging => _state == State.Charge;

    public Vector2 LaneDirection => _lane;

    public float TimeToCharge => _state == State.Windup ? Mathf.Max(0f, _timer) : 0f;

    protected override void Think(float delta)
    {
        _cooldown -= delta;
        _timer -= delta;
        switch (_state)
        {
            case State.Walk:
                DesiredVelocity = DirectionToTarget * Speed;
                if (_cooldown <= 0f && DistanceToTarget < ChargeRange)
                {
                    _state = State.Windup;
                    _timer = WindupTime;
                    _lane = DirectionToTarget;
                }

                break;
            case State.Windup:
                if (_timer <= 0f)
                {
                    _state = State.Charge;
                    _timer = ChargeDistance / ChargeSpeed;
                }

                break;
            case State.Charge:
                DesiredVelocity = _lane * ChargeSpeed;
                if (_timer <= 0f)
                {
                    _state = State.Recover;
                    _timer = 0.6f;
                }

                break;
            case State.Recover:
                if (_timer <= 0f)
                {
                    _state = State.Walk;
                    _cooldown = ChargeCooldown;
                }

                break;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_state != State.Windup)
        {
            return;
        }

        float t = 1f - Mathf.Clamp(_timer / WindupTime, 0f, 1f);
        float width = 30f;
        Vector2 local = _lane.Rotated(-GlobalRotation) / Scale;
        float length = ChargeDistance / Scale.X;
        Vector2 side = new Vector2(-local.Y, local.X) * width / 2f;
        Vector2[] full = { side, local * length + side, local * length - side, -side };
        DrawColoredPolygon(full, new Color(1f, 0.15f, 0.1f, 0.18f));
        Vector2[] fill = { side, local * length * t + side, local * length * t - side, -side };
        DrawColoredPolygon(fill, new Color(1f, 0.2f, 0.1f, 0.4f));
        DrawPolyline(new[] { side, local * length + side, local * length - side, -side, side }, new Color(1f, 0.2f, 0.1f, 0.9f), 2f);
    }
}
