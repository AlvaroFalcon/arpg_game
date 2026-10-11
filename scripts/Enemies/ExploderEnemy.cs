#nullable enable
using Godot;
using PathToExile.Combat;

namespace PathToExile.Enemies;

/// <summary>
/// The volatile imp — docs/design/enemies.md -> "Imp". Sprints at the player;
/// in range it stops and primes for <see cref="PrimeTime"/> seconds, drawing
/// its blast radius, then explodes (and dies). Killing it while primed
/// cancels the blast — a small reward for focus fire.
/// </summary>
[GlobalClass]
public partial class ExploderEnemy : Enemy
{
    [Export]
    public float TriggerRange = 70f;

    [Export]
    public float PrimeTime = 0.9f;

    [Export]
    public float BlastRadius = 80f;

    [Export]
    public float BlastDamage = 22f;

    private float _prime = -1f;

    protected override void Think(float delta)
    {
        if (_prime >= 0f)
        {
            _prime -= delta;
            QueueRedraw();
            if (_prime < 0f)
            {
                AreaBurst.Spawn(GetTree().CurrentScene, GlobalPosition, BlastRadius, 0f, new Color(1f, 0.4f, 0.1f), MakeHit(BlastDamage, DamageType.Fire, SkillTags.Area | SkillTags.Fire));
                ExperienceValue = 0;
                Health.ApplyResolvedDamage(float.MaxValue / 4f, null);
            }

            return;
        }

        DesiredVelocity = DirectionToTarget * Speed;
        if (DistanceToTarget < TriggerRange)
        {
            _prime = PrimeTime;
            Sprite?.Play("primed");
        }
    }

    public override void _Draw()
    {
        if (_prime < 0f)
        {
            return;
        }

        float t = 1f - Mathf.Clamp(_prime / PrimeTime, 0f, 1f);
        float r = BlastRadius / Scale.X;
        DrawCircle(Vector2.Zero, r, new Color(1f, 0.15f, 0.1f, 0.15f));
        DrawCircle(Vector2.Zero, r * t, new Color(1f, 0.2f, 0.1f, 0.35f));
        DrawArc(Vector2.Zero, r, 0f, Mathf.Tau, 40, new Color(1f, 0.2f, 0.1f, 0.9f), 2f);
    }
}
