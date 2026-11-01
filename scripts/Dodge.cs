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
    private System.Action? _onArrive;
    private Color _ghostTint = new(0.5f, 0.9f, 1f, 0.55f);

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

            if (_dashRemaining <= 0f && _onArrive is { } arrive)
            {
                _onArrive = null;
                _dashVelocity = Vector2.Zero;
                arrive();
            }
        }

        if (_body is Player { Command: { } command })
        {
            // A remote co-op player (or a test): the press arrives as a counter.
            if (command.ConsumeDodge() && InputEnabled)
            {
                SpacePressed(command.DodgeDir, command.Aim);
            }
        }
        else if (InputEnabled && _body is not Player { IsLocal: false } && !UI.LevelUpScreen.BlocksInput && Input.IsActionJustPressed("dodge"))
        {
            Vector2 input = Input.GetVector("move_left", "move_right", "move_up", "move_down");
            Vector2 mouse = _body.GetGlobalMousePosition();
            Vector2 direction = input != Vector2.Zero
                ? input.Normalized()
                : _body.GlobalPosition.DirectionTo(mouse);
            if (SpaceSkill is not null && Net.NetMode.ClientSide)
            {
                // A co-op guest: the host casts the movement skill on its copy and the
                // position comes back with the snapshots (Net.CoopClient sends the press).
                _dashVelocity = Vector2.Zero;
                EmitSignal(SignalName.Dodged);
            }
            else
            {
                SpacePressed(direction, mouse);
            }
        }
    }

    /// <summary>The skill in the Space slot (docs/design/skills.md -> "The Space slot"), or
    /// null: an empty Space slot is the dodge roll.</summary>
    public Skills.SkillInstance? SpaceSkill => _body.GetNodeOrNull<Skills.SkillUser>("SkillUser")?.GetSkill(Skills.SkillSlotId.Space);

    /// <summary>Space: the movement skill in the Space slot toward <paramref name="aimPoint"/>,
    /// or a roll in <paramref name="direction"/> when the slot is empty.</summary>
    public void SpacePressed(Vector2 direction, Vector2 aimPoint)
    {
        if (SpaceSkill is not null && _body.GetNodeOrNull<Skills.SkillUser>("SkillUser") is { } user)
        {
            if (!IsDashing && !(_health?.IsDead ?? false))
            {
                user.TryCast(Skills.SkillSlotId.Space, _body.GlobalPosition.DirectionTo(aimPoint), aimPoint);
            }

            return;
        }

        TryDodge(direction);
    }

    /// <summary>
    /// Moves the body by <paramref name="offset"/> over <paramref name="time"/> seconds
    /// (walls stop it), invulnerable for <paramref name="invulnerable"/> seconds, then calls
    /// <paramref name="onArrive"/> — Blink and Leap Slam (<see cref="Skills.MovementSkillDefinition"/>).
    /// Spends no dodge charge.
    /// </summary>
    public void Travel(Vector2 offset, float time, float invulnerable, Color tint, System.Action? onArrive)
    {
        // Whole physics frames, so the body covers exactly the offset (the body moves before
        // this node counts down, so a fractional last frame would overshoot by a frame).
        float step = 1f / Mathf.Max(1, Engine.PhysicsTicksPerSecond);
        int frames = Mathf.Max(1, Mathf.RoundToInt(time / step));
        time = frames * step;
        _dashRemaining = time - step * 0.5f;
        _dashVelocity = offset / time;
        _onArrive = onArrive;
        _ghostTint = tint with { A = 0.55f };
        _ghostTimer = 0f;
        _health?.GrantInvulnerability(invulnerable);
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
        _ghostTint = new Color(0.5f, 0.9f, 1f, 0.55f);
        _onArrive = null;
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
            Modulate = _ghostTint,
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
