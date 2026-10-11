#nullable enable
using System;
using System.Threading.Tasks;
using Godot;
using PathToExile.Combat;

namespace PathToExile.Enemies;

/// <summary>
/// Shared machinery for bosses — docs/design/bosses.md. A boss is an
/// <see cref="Enemy"/> that runs scripted <b>attacks</b> (async sequences of
/// telegraph → wait → impact) chosen from a per-phase pool, and changes phase
/// at life thresholds.
///
/// The rules every boss follows, enforced here rather than per boss:
/// <list type="bullet">
/// <item>Every attack is <b>named</b> (<see cref="CurrentAttack"/>, shown under
/// the boss bar) and every damaging hit is drawn on the ground before it lands
/// (<see cref="Slam"/>, <see cref="Lane"/>) — no unmarked damage.</item>
/// <item>Phase changes stop the fight for a beat: the boss is invulnerable for
/// <see cref="PhaseTransitionTime"/> seconds and the change is announced.</item>
/// <item>Attacks never overlap: one runs at a time, followed by a recovery gap.</item>
/// </list>
/// </summary>
[GlobalClass]
public abstract partial class BossEnemy : Enemy
{
    public const float PhaseTransitionTime = 2f;

    public override bool IsBoss => true;

    protected override float LifeScalingExponent => 0.5f;

    protected override float DamageScalingExponent => 0f;

    /// <summary>Raised for big, must-read callouts ("GET OUT!").</summary>
    public event Action<string, Color>? MechanicCalled;

    /// <summary>Raised when a new phase begins (2, 3...).</summary>
    public event Action<int, string>? PhaseChanged;

    public string CurrentAttack { get; protected set; } = "";

    public int Phase { get; private set; } = 1;

    /// <summary>Life fractions at which phases 2, 3... begin (descending).</summary>
    public abstract float[] PhaseThresholds { get; }

    protected bool Busy { get; private set; }

    /// <summary>Movement applied while an attack runs (usually zero).</summary>
    protected Vector2 BusyVelocity;

    protected float AttackCooldown = 2.5f;
    protected readonly RandomNumberGenerator Rng = new();

    public override void _Ready()
    {
        base._Ready();
        Rng.Randomize();
        KnockbackResistance = 1f;
    }

    protected override void Think(float delta)
    {
        int phase = 1;
        foreach (float threshold in PhaseThresholds)
        {
            if (LifeFraction <= threshold)
            {
                phase++;
            }
        }

        if (phase > Phase && !Busy)
        {
            Phase = phase;
            RunSequence(TransitionTo(phase));
            return;
        }

        if (Busy)
        {
            DesiredVelocity = BusyVelocity;
            return;
        }

        Move(delta);
        AttackCooldown -= delta;
        if (AttackCooldown <= 0f)
        {
            RunSequence(ChooseAttack());
        }
    }

    /// <summary>Idle movement between attacks.</summary>
    protected virtual void Move(float delta)
    {
        DesiredVelocity = DistanceToTarget > 220f ? DirectionToTarget * Speed : Vector2.Zero;
    }

    /// <summary>Picks and returns the next attack sequence for the current phase.</summary>
    protected abstract Task ChooseAttack();

    protected abstract string PhaseMessage(int phase);

    private async Task TransitionTo(int phase)
    {
        CurrentAttack = "";
        Health.GrantInvulnerability(PhaseTransitionTime);
        PhaseChanged?.Invoke(phase, PhaseMessage(phase));
        Core.Sfx.Play(this, "boss_warn");
        Core.CameraShake.Request(this, 8f);
        Sprite?.Play("windup");
        await Wait(PhaseTransitionTime);
        Sprite?.Play("idle");
        AttackCooldown = 0.6f;
    }

    private async void RunSequence(Task sequence)
    {
        Busy = true;
        BusyVelocity = Vector2.Zero;
        try
        {
            await sequence;
        }
        finally
        {
            if (IsInstanceValid(this))
            {
                Busy = false;
                BusyVelocity = Vector2.Zero;
                CurrentAttack = "";
            }
        }
    }

    /// <summary>Waits in physics time (pauses with the game). Returns false if
    /// the boss died or was freed meanwhile — every sequence must stop then.</summary>
    protected async Task<bool> Wait(float seconds)
    {
        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return false;
        }

        await ToSignal(GetTree().CreateTimer(seconds, processAlways: false, processInPhysics: true), SceneTreeTimer.SignalName.Timeout);
        return Alive;
    }

    protected bool Alive => IsInstanceValid(this) && IsInsideTree() && !Health.IsDead;

    protected void CallOut(string text, Color color) => MechanicCalled?.Invoke(text, color);

    protected void Announce(string attack)
    {
        CurrentAttack = attack;
    }

    protected AreaBurst Slam(Vector2 at, float radius, float delay, float damage, float innerRadius = 0f, DamageType type = DamageType.Fire)
    {
        AreaBurst burst = AreaBurst.Spawn(GetTree().CurrentScene, at, radius, delay, new Color(1f, 0.3f, 0.2f), MakeHit(damage, type, SkillTags.Area), innerRadius);
        burst.Impacted += _ => Core.Sfx.Play(this, "slam", -4f);
        return burst;
    }

    protected LineStrike Lane(Vector2 origin, Vector2 direction, float length, float width, float delay, float damage)
    {
        LineStrike strike = LineStrike.Spawn(GetTree().CurrentScene, origin, direction, length, width, delay, MakeHit(damage, DamageType.Fire));
        strike.Impacted += () => Core.Sfx.Play(this, "explode", -6f);
        return strike;
    }

    protected BossBullet Bullet(Vector2 direction, float speed, float damage, SpriteFrames? frames, float scale = 2f, DamageType type = DamageType.Fire)
    {
        return BossBullet.Spawn(GetTree().CurrentScene, GlobalPosition, direction.Normalized() * speed, MakeHit(damage, type, SkillTags.Projectile), frames, scale);
    }

    /// <summary>Where the player is, or a point in front of the boss if not.</summary>
    protected Vector2 TargetPosition => Target?.GlobalPosition ?? GlobalPosition + Vector2.Down * 100f;

    protected Run.Arena? Arena => GetTree().CurrentScene.GetNodeOrNull<Run.Arena>("Arena");

    protected Vector2 ClampToArena(Vector2 p, float margin = 60f) => Arena?.Clamp(p, margin) ?? p;
}
