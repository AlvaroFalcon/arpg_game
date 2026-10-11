#nullable enable
using Godot;

namespace PathToExile.Combat;

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

    private Health _health = null!;
    private Polygon2D? _visual;
    private Color _baseColor;
    private float _flashTimer;

    public override void _Ready()
    {
        _health = GetNode<Health>("Health");
        _visual = GetNodeOrNull<Polygon2D>("Visual");
        _baseColor = _visual?.Color ?? Colors.White;

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
    }

    private void OnDamaged(float amount)
    {
        _flashTimer = 0.1f;
        if (_visual is not null)
        {
            _visual.Color = Colors.White;
        }

        SpawnDamageNumber(amount);
    }

    private void OnDied()
    {
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
