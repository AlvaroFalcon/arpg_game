#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Stats;

namespace ArpgGame.Skills;

/// <summary>
/// What the auras in a caster's slots do — docs/design/skills.md -> "Auras". Owned by the
/// <see cref="SkillUser"/>: <see cref="Refresh"/> (on every loadout change) rebuilds the
/// passive modifiers of every equipped aura from its resolved cast context (gem level and
/// tree included); <see cref="Activate"/> starts an active (or switches Immolation);
/// <see cref="Tick"/> ends actives and burns for Immolation. Every modifier carries its own
/// source object, so taking a gem out of the bar removes exactly what it granted.
/// </summary>
public sealed class AuraController
{
    private sealed class ActiveState
    {
        public readonly object Source = new();
        public float Left;
        public float Duration;
        public bool Toggle;
        public float Tick;
        public AuraRing? Ring;
    }

    private readonly SkillUser _user;
    private readonly Node2D _caster;
    private readonly ActorStats _stats;
    private readonly object _passiveSource = new();
    private readonly Dictionary<SkillInstance, ActiveState> _active = new();

    public AuraController(SkillUser user, Node2D caster, ActorStats stats)
    {
        _user = user;
        _caster = caster;
        _stats = stats;
    }

    /// <summary>Everything the passives grant now (tests, tooltips).</summary>
    public int PassiveCount { get; private set; }

    private static float LevelMultiplier(SkillInstance i) => 1f + AuraCatalog.EffectPerLevel * (i.Level - 1);

    /// <summary>Rebuilds the passive modifiers of every equipped aura, and drops the actives
    /// of auras no longer equipped.</summary>
    public void Refresh()
    {
        _stats.RemoveModifiersFromSource(_passiveSource);
        PassiveCount = 0;
        var equipped = new HashSet<SkillInstance>();
        foreach ((SkillSlotId _, SkillInstance instance) in _user.EquippedSkills())
        {
            if (instance.Definition is not AuraSkillDefinition)
            {
                continue;
            }

            equipped.Add(instance);
            CastContext context = _user.ResolveContext(instance, Vector2.Right);
            if (context.Aura is not { } aura)
            {
                continue;
            }

            float m = aura.PassiveMultiplier * LevelMultiplier(instance);
            foreach (AuraStat stat in aura.Passive)
            {
                _stats.Get(stat.Stat).AddModifier(AuraCatalog.Build(stat, m, aura.Focus, _passiveSource));
                PassiveCount++;
            }

            if (aura.PassiveUnstoppable)
            {
                _stats.Get(StatId.KnockbackImmunity).AddModifier(new StatModifier(ModifierType.Flat, 1f, _passiveSource));
                PassiveCount++;
            }
        }

        foreach (SkillInstance gone in new List<SkillInstance>(_active.Keys))
        {
            if (!equipped.Contains(gone))
            {
                End(gone);
            }
        }

        _stats.Life?.ClampToMax();
    }

    /// <summary>Whether an aura's active is running (Immolation: switched on).</summary>
    public bool IsActive(SkillInstance instance) => _active.ContainsKey(instance);

    /// <summary>Fraction (0..1) of the active left, 1 for a toggle that is on.</summary>
    public float ActiveFraction(SkillInstance instance) =>
        _active.TryGetValue(instance, out ActiveState? s) ? s.Toggle ? 1f : Mathf.Clamp(s.Left / Mathf.Max(0.01f, s.Duration), 0f, 1f) : 0f;

    public void Activate(SkillInstance instance, AuraSkillDefinition definition, CastContext context)
    {
        if (context.Aura is not { } aura)
        {
            return;
        }

        if (definition.IsToggle && _active.ContainsKey(instance))
        {
            End(instance);
            return;
        }

        if (_active.ContainsKey(instance))
        {
            End(instance);
        }

        var state = new ActiveState
        {
            Toggle = definition.IsToggle,
            Duration = definition.ActiveDuration * context.DurationMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.SkillDuration).Value),
        };
        state.Left = state.Duration;
        _active[instance] = state;

        float m = aura.ActiveMultiplier * LevelMultiplier(instance);
        foreach (AuraStat stat in aura.Active)
        {
            _stats.Get(stat.Stat).AddModifier(AuraCatalog.Build(stat, m, aura.Focus, state.Source));
        }

        if (aura.HealFraction > 0f && _caster.GetNodeOrNull<Health>("Health") is { } health && _stats.Life is { } life)
        {
            health.Heal(life.Max * aura.HealFraction * m);
        }

        float radius = definition.IsToggle ? definition.Radius * context.AreaMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.AreaOfEffect).Value) : 46f;
        if (_caster.IsInsideTree())
        {
            state.Ring = new AuraRing { Radius = radius, Tint = definition.Tint, Pulse = !definition.IsToggle };
            _caster.AddChild(state.Ring);
        }

        Core.Sfx.Play(_user, "levelup", -8f);
    }

    private void End(SkillInstance instance)
    {
        if (!_active.Remove(instance, out ActiveState? state))
        {
            return;
        }

        _stats.RemoveModifiersFromSource(state.Source);
        if (state.Ring is not null && GodotObject.IsInstanceValid(state.Ring))
        {
            state.Ring.QueueFree();
        }

        _stats.Life?.ClampToMax();
    }

    /// <summary>Ends every active and switches Immolation off (death, leaving a scene).</summary>
    public void EndAll()
    {
        foreach (SkillInstance i in new List<SkillInstance>(_active.Keys))
        {
            End(i);
        }
    }

    public void Tick(float dt)
    {
        if (_active.Count == 0)
        {
            return;
        }

        Health? health = _caster.GetNodeOrNull<Health>("Health");
        if (health is { IsDead: true })
        {
            EndAll();
            return;
        }

        foreach (SkillInstance instance in new List<SkillInstance>(_active.Keys))
        {
            ActiveState state = _active[instance];
            if (!state.Toggle)
            {
                state.Left -= dt;
                if (state.Left <= 0f)
                {
                    End(instance);
                }

                continue;
            }

            state.Tick -= dt;
            if (state.Tick > 0f)
            {
                continue;
            }

            state.Tick += 0.5f;
            Burn(instance, (AuraSkillDefinition)instance.Definition, health, 0.5f);
        }
    }

    /// <summary>Immolation, one half-second: fire damage to every hostile in the circle,
    /// and the life it costs (never below 1).</summary>
    private void Burn(SkillInstance instance, AuraSkillDefinition definition, Health? health, float step)
    {
        if (!_caster.IsInsideTree())
        {
            return;
        }

        CastContext context = _user.ResolveContext(instance, Vector2.Right);
        HitProfile hit = _user.BuildHitProfile(instance, context);
        hit.Damage *= step;
        float radius = definition.Radius * context.AreaMultiplier * Mathf.Max(0.1f, _stats.Get(StatId.AreaOfEffect).Value);
        if (_active.TryGetValue(instance, out ActiveState? state) && state.Ring is not null && GodotObject.IsInstanceValid(state.Ring))
        {
            state.Ring.Radius = radius;
        }

        foreach (Health target in CombatQuery.HostilesInRadius(_caster.GetTree(), _caster.GlobalPosition, radius, _stats.Faction))
        {
            Vector2 to = target.GetParent<Node2D>().GlobalPosition - _caster.GlobalPosition;
            target.TakeDamage(hit.BuildPayload(to.Normalized()));
            _user.ReportHit(target.GetParent<Node2D>(), hit.Damage);
        }

        if (_stats.Life is { } life && health is not null && !Net.NetMode.ClientSide)
        {
            float drain = life.Max * definition.LifeDrainPercent / 100f * step * Mathf.Max(0f, context.Aura?.LifeDrainMultiplier ?? 1f);
            life.Current = Mathf.Max(1f, life.Current - drain);
        }
    }
}

/// <summary>The circle an aura draws around its caster: a steady ring while Immolation
/// burns, a fading pulse when another aura's active fires.</summary>
public partial class AuraRing : Node2D
{
    public float Radius = 100f;
    public Color Tint = new(1f, 0.6f, 0.3f);
    public bool Pulse;
    private float _t;

    public override void _Ready() => ZIndex = -1;

    public override void _Process(double delta)
    {
        _t += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Pulse)
        {
            // A ring that rises off the caster for the first second, then a faint halo.
            float k = Mathf.Clamp(_t / 0.6f, 0f, 1f);
            if (k < 1f)
            {
                DrawArc(Vector2.Zero, Radius * (0.6f + k), 0f, Mathf.Tau, 40, Tint with { A = 0.7f * (1f - k) }, 3f);
            }

            DrawArc(Vector2.Zero, 18f, 0f, Mathf.Tau, 24, Tint with { A = 0.35f + 0.15f * Mathf.Sin(_t * 5f) }, 1.5f);
            return;
        }

        float flicker = 0.5f + 0.5f * Mathf.Sin(_t * 7f);
        DrawCircle(Vector2.Zero, Radius, Tint with { A = 0.06f + 0.03f * flicker });
        DrawArc(Vector2.Zero, Radius, 0f, Mathf.Tau, 48, Tint with { A = 0.45f + 0.2f * flicker }, 2f);
        for (int i = 0; i < 6; i++)
        {
            float a = _t * 1.4f + i * Mathf.Tau / 6f;
            DrawCircle(Vector2.Right.Rotated(a) * Radius * 0.85f, 3f + 1.5f * flicker, Tint with { A = 0.5f });
        }
    }
}
