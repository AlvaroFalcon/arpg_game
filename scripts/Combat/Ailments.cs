#nullable enable
using Godot;
using PathToExile.Stats;

namespace PathToExile.Combat;

/// <summary>
/// The three elemental ailments — docs/design/progression.md -> "Ailments".
/// They are what makes "which element am I?" a build decision rather than a
/// colour: cold slows, fire burns over time, lightning makes everything hit
/// harder.
///
/// - <b>Chill</b>: every Cold hit chills for <see cref="ChillDuration"/>
///   seconds — <see cref="ChillSlowPercent"/>% less movement speed. No roll.
/// - <b>Ignite</b>: a Fire hit ignites with <see cref="DamagePayload.IgniteChance"/>,
///   dealing <see cref="IgniteDamageRatio"/> of the hit over
///   <see cref="IgniteDuration"/> seconds. A stronger ignite replaces a weaker one.
/// - <b>Shock</b>: a Lightning hit shocks with <see cref="DamagePayload.ShockChance"/>,
///   making the target take <see cref="ShockPercent"/>% more damage.
///
/// Expects sibling "Stats" (<see cref="ActorStats"/>) and "Health" nodes. The
/// effects are expressed as ordinary <see cref="StatModifier"/>s tagged with
/// this component's own source objects, so they compose with everything else
/// and are removed with the existing remove-by-source mechanism.
/// </summary>
[GlobalClass]
public partial class Ailments : Node
{
    public const float ChillDuration = 2f;
    public const float ChillSlowPercent = 30f;
    public const float IgniteDuration = 3f;
    public const float IgniteDamageRatio = 0.9f;
    public const float IgniteTick = 0.5f;
    public const float ShockDuration = 3f;
    public const float ShockPercent = 25f;

    private static readonly object ChillSource = new();
    private static readonly object CurseSource = new();

    /// <summary>Poison stacks: each is (damage per second, seconds left).
    /// Independent and stacking, unlike ignite (docs/design/classes.md -> Assassin).</summary>
    private readonly System.Collections.Generic.List<(float Dps, float Left)> _poisons = new();
    private float _poisonTick;
    private Node2D? _poisonSource;

    public float CurseRemaining { get; private set; }
    public bool IsCursed => CurseRemaining > 0f;
    public int PoisonStacks => _poisons.Count;
    private static readonly object ShockSource = new();

    private ActorStats _stats = null!;
    private Health _health = null!;
    private readonly RandomNumberGenerator _rng = new();

    public float ChillRemaining { get; private set; }
    public float IgniteRemaining { get; private set; }
    public float ShockRemaining { get; private set; }

    private float _ignitePerSecond;
    private float _igniteTickTimer;
    private Node2D? _igniteSource;

    public bool IsChilled => ChillRemaining > 0f;
    public bool IsIgnited => IgniteRemaining > 0f;
    public bool IsShocked => ShockRemaining > 0f;

    public override void _Ready()
    {
        _rng.Randomize();
        _stats = GetParent().GetNode<ActorStats>("Stats");
        _health = GetParent().GetNode<Health>("Health");
        _health.HitResolved += OnHitResolved;
    }

    /// <summary>The tint the owner's visual should take, or white for none.
    /// Ignite outranks shock outranks chill: the one doing damage reads first.</summary>
    public Color Tint =>
        IsIgnited ? new Color(1f, 0.6f, 0.35f)
        : PoisonStacks > 0 ? new Color(0.55f, 1f, 0.5f)
        : IsCursed ? new Color(0.8f, 0.5f, 1f)
        : IsShocked ? new Color(1f, 1f, 0.45f)
        : IsChilled ? new Color(0.55f, 0.8f, 1f)
        : Colors.White;

    private void OnHitResolved(DamagePayload payload, float taken)
    {
        if (!payload.CanInflictAilments || _health.IsDead)
        {
            return;
        }

        foreach ((DamageType type, float amount) in payload.Components)
        {
            if (amount <= 0f)
            {
                continue;
            }

            switch (type)
            {
                case DamageType.Cold:
                    ApplyChill();
                    break;
                case DamageType.Fire when _rng.Randf() < payload.IgniteChance:
                    ApplyIgnite(taken * IgniteDamageRatio * payload.IgniteDamageMultiplier, payload.SourceActor);
                    break;
                case DamageType.Lightning when _rng.Randf() < payload.ShockChance:
                    ApplyShock();
                    break;
            }
        }
    }

    public void ApplyChill()
    {
        if (ChillRemaining <= 0f)
        {
            _stats.MovementSpeed.AddModifier(new StatModifier(ModifierType.More, -ChillSlowPercent, ChillSource));
        }

        ChillRemaining = ChillDuration;
    }

    public void ApplyIgnite(float totalDamage, Node2D? source)
    {
        float perSecond = totalDamage / IgniteDuration;
        if (IsIgnited && perSecond < _ignitePerSecond)
        {
            return;
        }

        _ignitePerSecond = perSecond;
        IgniteRemaining = IgniteDuration;
        _igniteTickTimer = IgniteTick;
        _igniteSource = source;
    }

    /// <summary>Adds a poison stack (max 12) dealing <paramref name="total"/> over <paramref name="duration"/> s.</summary>
    public void AddPoison(float total, float duration, Node2D? source)
    {
        if (_poisons.Count >= 12)
        {
            _poisons.RemoveAt(0);
        }

        _poisons.Add((total / duration, duration));
        _poisonSource = source;
    }

    /// <summary>Vulnerability curse: <paramref name="percent"/>% more damage taken (Warlock).</summary>
    public void ApplyCurse(float percent, float duration)
    {
        if (CurseRemaining <= 0f)
        {
            _stats.Get(StatId.DamageTaken).AddModifier(new StatModifier(ModifierType.More, percent, CurseSource));
        }

        CurseRemaining = Mathf.Max(CurseRemaining, duration);
    }

    public void ApplyShock()
    {
        if (ShockRemaining <= 0f)
        {
            _stats.Get(StatId.DamageTaken).AddModifier(new StatModifier(ModifierType.More, ShockPercent, ShockSource));
        }

        ShockRemaining = ShockDuration;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (ChillRemaining > 0f)
        {
            ChillRemaining -= dt;
            if (ChillRemaining <= 0f)
            {
                _stats.MovementSpeed.RemoveModifiersFromSource(ChillSource);
            }
        }

        if (ShockRemaining > 0f)
        {
            ShockRemaining -= dt;
            if (ShockRemaining <= 0f)
            {
                _stats.Get(StatId.DamageTaken).RemoveModifiersFromSource(ShockSource);
            }
        }

        if (CurseRemaining > 0f)
        {
            CurseRemaining -= dt;
            if (CurseRemaining <= 0f)
            {
                _stats.Get(StatId.DamageTaken).RemoveModifiersFromSource(CurseSource);
            }
        }

        if (_poisons.Count > 0)
        {
            _poisonTick -= dt;
            if (_poisonTick <= 0f)
            {
                _poisonTick = 0.5f;
                float dps = 0f;
                for (int i = _poisons.Count - 1; i >= 0; i--)
                {
                    (float d, float left) = _poisons[i];
                    dps += d;
                    left -= 0.5f;
                    if (left <= 0f)
                    {
                        _poisons.RemoveAt(i);
                    }
                    else
                    {
                        _poisons[i] = (d, left);
                    }
                }

                Node2D? source = _poisonSource is not null && IsInstanceValid(_poisonSource) ? _poisonSource : null;
                _health.ApplyResolvedDamage(dps * 0.5f, source);
            }
        }

        if (IgniteRemaining > 0f)
        {
            IgniteRemaining -= dt;
            _igniteTickTimer -= dt;
            if (_igniteTickTimer <= 0f)
            {
                _igniteTickTimer += IgniteTick;
                Node2D? source = _igniteSource is not null && IsInstanceValid(_igniteSource) ? _igniteSource : null;
                _health.ApplyResolvedDamage(_ignitePerSecond * IgniteTick, source);
            }
        }
    }
}
