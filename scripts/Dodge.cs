#nullable enable
using Godot;
using ArpgGame.Combat;
using ArpgGame.Stats;

namespace ArpgGame;

/// <summary>
/// The dodge roll — docs/design/dodge.md. Space dashes a short, fixed distance
/// and grants a brief invulnerability window that covers the dash plus a small
/// tail. Dodges are <b>charges</b>, not a cooldown: the actor holds up to
/// <see cref="StatId.DodgeCharges"/> (base 2) and each spent charge refills on
/// its own timer (<see cref="RechargeTime"/>, sped up by
/// <see cref="StatId.DodgeRecovery"/>). Charges let a player chain two dodges
/// through a boss combo, while the refill rate keeps "spam dodge forever" off
/// the table — the escape is a resource you manage, not a reflex you mash.
///
/// Direction: the movement input if any (you dodge where you are steering),
/// otherwise toward the mouse cursor. Movement during a dash is owned here;
/// <see cref="Player"/> defers to <see cref="IsDashing"/>.
/// </summary>
[GlobalClass]
public partial class Dodge : Node
{
    [Signal]
    public delegate void DodgedEventHandler();

    [Export]
    public float DashDistance = 150f;

    [Export]
    public float DashTime = 0.16f;

    /// <summary>Invulnerability lasts the dash plus this many seconds.</summary>
    [Export]
    public float InvulnerabilityTail = 0.14f;

    [Export]
    public float RechargeTime = 2.4f;

    private CharacterBody2D _body = null!;
    private ActorStats _stats = null!;
    private Health? _health;
    private AnimatedSprite2D? _sprite;

    private float _dashRemaining;
    private Vector2 _dashVelocity;
    private float _ghostTimer;

    /// <summary>Charges currently banked (0..<see cref="MaxCharges"/>).</summary>
    public int Charges { get; private set; }

    /// <summary>Progress (0..1) of the charge currently refilling.</summary>
    public float RechargeProgress { get; private set; }

    public int MaxCharges => Mathf.Max(1, Mathf.RoundToInt(_stats.Get(StatId.DodgeCharges).Value));

    public bool IsDashing => _dashRemaining > 0f;

    /// <summary>Engine time (s) the last dodge started — ascendancy timing windows.</summary>
    public double LastDodgeTime { get; private set; } = -100.0;

    /// <summary>Gives a charge back (Samurai's Perfect Parry).</summary>
    public void AddCharge() => Charges = Mathf.Min(MaxCharges, Charges + 1);

    /// <summary>Advances the refilling charge by a fraction (Ninja's Fleeting Kill).</summary>
    public void AdvanceRecharge(float fraction)
    {
        if (Charges >= MaxCharges)
        {
            return;
        }

        RechargeProgress += fraction;
        if (RechargeProgress >= 1f)
        {
            RechargeProgress = 0f;
            Charges++;
        }
    }

    public Vector2 DashVelocity => _dashVelocity;

    /// <summary>When false (menus, cutscenes, the hideout's dialogue), input is ignored.</summary>
    public bool InputEnabled { get; set; } = true;

    public override void _Ready()
    {
        _body = GetParent<CharacterBody2D>();
        _stats = _body.GetNode<ActorStats>("Stats");
        _health = _body.GetNodeOrNull<Health>("Health");
        _sprite = _body.GetNodeOrNull<AnimatedSprite2D>("Sprite");
        Charges = MaxCharges;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        TickRecharge(dt);

        if (_dashRemaining > 0f)
        {
            _dashRemaining -= dt;
            _ghostTimer -= dt;
            if (_ghostTimer <= 0f)
            {
                _ghostTimer = 0.035f;
                SpawnGhost();
            }
        }

        if (_body is Player { Command: { } command })
        {
            // A remote co-op player (or a test): the press arrives as a counter.
            if (command.ConsumeDodge() && InputEnabled)
            {
                TryDodge(command.DodgeDir);
            }
        }
        else if (InputEnabled && _body is not Player { IsLocal: false } && Input.IsActionJustPressed("dodge"))
        {
            Vector2 input = Input.GetVector("move_left", "move_right", "move_up", "move_down");
            Vector2 direction = input != Vector2.Zero
                ? input.Normalized()
                : _body.GlobalPosition.DirectionTo(_body.GetGlobalMousePosition());
            TryDodge(direction);
        }
    }

    /// <summary>Spends a charge and starts a dash. Public so tests and AI can drive it.</summary>
    public bool TryDodge(Vector2 direction)
    {
        if (Charges <= 0 || IsDashing || (_health?.IsDead ?? false))
        {
            return false;
        }

        if (direction == Vector2.Zero)
        {
            direction = Vector2.Right;
        }

        Charges--;
        LastDodgeTime = Time.GetTicksMsec() / 1000.0;
        _dashRemaining = DashTime;
        _dashVelocity = direction.Normalized() * (DashDistance / DashTime);
        _health?.GrantInvulnerability(DashTime + InvulnerabilityTail);
        _ghostTimer = 0f;
        Core.Sfx.Play(this, "dodge");
        EmitSignal(SignalName.Dodged);
        return true;
    }

    private void TickRecharge(float dt)
    {
        int max = MaxCharges;
        if (Charges > max)
        {
            Charges = max;
        }

        if (Charges >= max)
        {
            RechargeProgress = 0f;
            return;
        }

        float recovery = Mathf.Max(0.1f, _stats.Get(StatId.DodgeRecovery).Value);
        RechargeProgress += dt * recovery / RechargeTime;
        if (RechargeProgress >= 1f)
        {
            RechargeProgress = 0f;
            Charges++;
        }
    }

    /// <summary>Afterimage: a fading copy of the current sprite frame.</summary>
    private void SpawnGhost()
    {
        if (_sprite?.SpriteFrames is not { } frames || !IsInsideTree())
        {
            return;
        }

        var ghost = new Sprite2D
        {
            Texture = frames.GetFrameTexture(_sprite.Animation, _sprite.Frame),
            FlipH = _sprite.FlipH,
            Scale = _sprite.Scale,
            Modulate = new Color(0.5f, 0.9f, 1f, 0.55f),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            ZIndex = -1,
        };
        GetTree().CurrentScene.AddChild(ghost);
        ghost.GlobalPosition = _sprite.GlobalPosition;
        Tween tween = ghost.CreateTween();
        tween.TweenProperty(ghost, "modulate:a", 0f, 0.25f);
        tween.TweenCallback(Callable.From(ghost.QueueFree));
    }
}
