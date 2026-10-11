#nullable enable
using Godot;
using PathToExile.Skills;

namespace PathToExile.Enemies;

/// <summary>
/// The bone caster — docs/design/enemies.md -> "Caster". Keeps between
/// <see cref="PreferredMin"/> and <see cref="PreferredMax"/> from the player
/// and shoots its skill (a slow bolt) through the very same
/// <see cref="SkillUser"/> pipeline the player uses — the "one skill
/// definition serves both sides" promise of docs/design/skills.md, finally
/// cashed in. Glows for <see cref="WindupTime"/> before each shot so the
/// player can see it coming in a crowd.
/// </summary>
[GlobalClass]
public partial class RangedEnemy : Enemy
{
    [Export]
    public SkillDefinition? Attack;

    [Export]
    public float PreferredMin = 200f;

    [Export]
    public float PreferredMax = 320f;

    [Export]
    public float AttackInterval = 2.6f;

    [Export]
    public float WindupTime = 0.45f;

    private SkillUser _skills = null!;
    private float _attackTimer;
    private float _windup = -1f;

    public override void _Ready()
    {
        base._Ready();
        _skills = GetNode<SkillUser>("SkillUser");
        _attackTimer = AttackInterval * (0.5f + GD.Randf() * 0.5f);
        if (Attack is not null)
        {
            _skills.EquipSkill(SkillSlotId.Slot1, new SkillInstance(Attack));
        }
    }

    protected override void Think(float delta)
    {
        float d = DistanceToTarget;
        Vector2 dir = DirectionToTarget;
        if (d > PreferredMax)
        {
            DesiredVelocity = dir * Speed;
        }
        else if (d < PreferredMin)
        {
            DesiredVelocity = -dir * Speed * 0.8f;
        }
        else
        {
            DesiredVelocity = new Vector2(-dir.Y, dir.X) * Speed * 0.35f;
        }

        if (_windup >= 0f)
        {
            DesiredVelocity = Vector2.Zero;
            _windup -= delta;
            if (_windup < 0f && Target is not null)
            {
                _skills.TryCast(SkillSlotId.Slot1, DirectionToTarget);
            }

            return;
        }

        _attackTimer -= delta;
        if (_attackTimer <= 0f && d < PreferredMax + 120f)
        {
            _attackTimer = AttackInterval;
            _windup = WindupTime;
        }
    }

    protected override void UpdateVisual(float delta)
    {
        base.UpdateVisual(delta);
        if (_windup >= 0f && Sprite is not null)
        {
            Sprite.Modulate = new Color(1.6f, 1.1f, 2.2f);
        }
    }
}
