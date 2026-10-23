#nullable enable
using Godot;
using ArpgGame.Stats;

namespace ArpgGame.Combat;

/// <summary>
/// The inert target dummy required by docs/design/skills.md milestone 1: "has
/// life, takes damage, dies. No AI, no movement, no retaliation, no pathing."
/// This script only wires visual feedback (a hit flash + a floating damage
/// number) to its child <see cref="Health"/>'s signals and frees itself on
/// death — it never reads input, never moves, and never attacks back.
/// </summary>
[GlobalClass]
public partial class TargetDummy : Area2D
{
    [Export]
    public PackedScene? DamageNumberScene;

    /// <summary>The hideout's training dummies: a hit never takes them below full life, so
    /// they never die, give no kill events and cannot drop anything. The Training Grounds
    /// dummies stay mortal (docs/design/hideout.md -> "Training dummies").</summary>
    [Export]
    public bool Immortal;

    private Health _health = null!;
    private Polygon2D? _visual;

    /// <summary>Optional pixel-art body (the hideout's training dummies). When
    /// present the placeholder polygon is hidden and the flash goes on this.</summary>
    private CanvasItem? _sprite;
    private Color _baseColor;
    private float _flashTimer;

    public override void _Ready()
    {
        _health = GetNode<Health>("Health");
        _visual = GetNodeOrNull<Polygon2D>("Visual");
        _baseColor = _visual?.Color ?? Colors.White;
        _sprite = GetNodeOrNull<CanvasItem>("Sprite");
        if (_sprite is not null && _visual is not null)
        {
            _visual.Visible = false;
        }

        // Targetable by chains/auto-aim like any enemy (docs/design/progression.md).
        AddToGroup(CombatQuery.EnemyGroup);

        _health.Damaged += OnDamaged;
        _health.Died += OnDied;
    }

    public override void _Process(double delta)
    {
        if (_flashTimer <= 0f)
        {
            return;
        }

        _flashTimer -= (float)delta;
        if (_flashTimer <= 0f && _visual is not null)
        {
            _visual.Color = _baseColor;
        }

        if (_flashTimer <= 0f && _sprite is not null)
        {
            _sprite.Modulate = Colors.White;
        }
    }

    private void OnDamaged(float amount)
    {
        _flashTimer = 0.1f;

        // Raised before Health checks for death, so a hit of any size leaves them standing.
        if (Immortal && GetNodeOrNull<ActorStats>("Stats")?.Life is { } life)
        {
            life.Current = life.Max;
        }

        if (_visual is not null)
        {
            _visual.Color = Colors.White;
        }

        if (_sprite is not null)
        {
            _sprite.Modulate = new Color(3f, 3f, 3f);
        }

        SpawnDamageNumber(amount);
    }

    private void OnDied()
    {
        if (Immortal)
        {
            _health.Revive();
            return;
        }

        QueueFree();
    }

    private void SpawnDamageNumber(float amount)
    {
        if (DamageNumberScene is null)
        {
            return;
        }

        var instance = DamageNumberScene.Instantiate<UI.DamageNumber>();
        GetTree().CurrentScene.AddChild(instance);
        instance.GlobalPosition = GlobalPosition + new Vector2(0f, -20f);
        instance.Display(amount);
    }
}
