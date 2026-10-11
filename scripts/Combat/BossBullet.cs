#nullable enable
using Godot;

namespace PathToExile.Combat;

/// <summary>
/// A boss pattern bullet — docs/design/bosses.md -> "Bullet patterns". Moves in
/// a straight line (optionally curving), hits the first hostile within
/// <see cref="Radius"/>, and expires at <see cref="Range"/>. Distance-checked
/// against the hostile group rather than a physics body, because patterns put
/// dozens on screen at once and only ever target the player. Slow and large
/// by design: patterns are about reading gaps, not reflexes.
/// </summary>
public partial class BossBullet : Node2D
{
    public Vector2 Velocity;
    public float AngularVelocity;
    public float Radius = 10f;
    public float Range = 900f;
    public HitProfile Hit = new();

    private float _travelled;
    private AnimatedSprite2D? _sprite;

    public static BossBullet Spawn(Node parent, Vector2 origin, Vector2 velocity, HitProfile hit, SpriteFrames? frames, float scale = 2f)
    {
        var bullet = new BossBullet { Velocity = velocity, Hit = hit, ZIndex = 2 };
        if (frames is not null)
        {
            bullet._sprite = new AnimatedSprite2D { SpriteFrames = frames, Scale = Vector2.One * scale, TextureFilter = TextureFilterEnum.Nearest };
            bullet.AddChild(bullet._sprite);
            bullet._sprite.Play();
        }

        bullet.Radius = 5f * scale;
        parent.AddChild(bullet);
        bullet.GlobalPosition = origin;
        bullet.Rotation = velocity.Angle();
        return bullet;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (AngularVelocity != 0f)
        {
            Velocity = Velocity.Rotated(AngularVelocity * dt);
            Rotation = Velocity.Angle();
        }

        Vector2 step = Velocity * dt;
        GlobalPosition += step;
        _travelled += step.Length();
        if (_travelled >= Range)
        {
            QueueFree();
            return;
        }

        foreach (Node node in GetTree().GetNodesInGroup(CombatQuery.HostileGroupFor(Hit.Faction)))
        {
            if (node is Node2D target
                && target.GlobalPosition.DistanceSquaredTo(GlobalPosition) <= (Radius + 10f) * (Radius + 10f)
                && target.GetNodeOrNull<Health>("Health") is { IsDead: false } health)
            {
                if (health.IsInvulnerable)
                {
                    // Dodged through: the bullet passes, it is not consumed.
                    continue;
                }

                health.TakeDamage(Hit.BuildPayload(Velocity.Normalized()));
                QueueFree();
                return;
            }
        }
    }
}
