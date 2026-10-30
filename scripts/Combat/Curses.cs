#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Skills;
using ArpgGame.Stats;

namespace ArpgGame.Combat;

/// <summary>One curse on an enemy, as cast (already scaled by the caster's stats, the
/// skill's level and its upgrade tree).</summary>
public sealed class CurseEffect
{
    public CurseKind Kind;
    public float Duration;
    public float Left;

    /// <summary>Corruption: shadow damage per second.</summary>
    public float DamagePerSecond;

    /// <summary>Elements: resistance lowered; Time: % slow.</summary>
    public float Potency;

    /// <summary>How many enemies it jumps to when its bearer dies (skill upgrades, the Witch's Contagion).</summary>
    public int Spread;

    /// <summary>Elements: the cursed also take this much more damage of every kind (upgrade).</summary>
    public float Vulnerability;

    /// <summary>The bearer bursts on death for this fraction of its maximum life (upgrade).</summary>
    public float DeathBurst;

    public Node2D? Source;
    public Color Tint;

    public CurseEffect Copy() => (CurseEffect)MemberwiseClone();
}

/// <summary>
/// The curses on one enemy — docs/design/classes.md -> "The Witch's curses". Added to an
/// enemy the first time it is cursed. One curse of each kind at a time: a new one
/// refreshes the duration and keeps the stronger numbers. Corruption ticks shadow damage
/// every half second; Elements lowers resistances; Time slows. When the bearer dies, a
/// curse with <see cref="CurseEffect.Spread"/> jumps to that many enemies nearby (one
/// fewer jump each time, so it cannot loop forever). A small sigil over the enemy's head
/// shows each curse in its colour.
/// </summary>
public partial class Curses : Node2D
{
    public const float SpreadRange = 220f;
    private const float Tick = 0.5f;

    private static readonly StringName ElementsSource = "curse_elements";
    private static readonly StringName TimeSource = "curse_time";
    private static readonly StringName VulnerableSource = "curse_vulnerable";

    private readonly Dictionary<CurseKind, CurseEffect> _active = new();
    private ActorStats _stats = null!;
    private Health _health = null!;
    private float _tick = Tick;
    private float _spin;

    public IReadOnlyDictionary<CurseKind, CurseEffect> Active => _active;

    public bool Has(CurseKind kind) => _active.ContainsKey(kind);

    public bool Any => _active.Count > 0;

    /// <summary>The curses node of an enemy, created on first use (null for something that can't be cursed).</summary>
    public static Curses? On(Node target)
    {
        if (target.GetNodeOrNull<Curses>("Curses") is { } existing)
        {
            return existing;
        }

        if (target.GetNodeOrNull<ActorStats>("Stats") is null || target.GetNodeOrNull<Health>("Health") is not { IsDead: false })
        {
            return null;
        }

        var curses = new Curses { Name = "Curses", Position = new Vector2(0f, -40f), ZIndex = 60 };
        target.AddChild(curses);
        return curses;
    }

    public override void _Ready()
    {
        _stats = GetParent().GetNode<ActorStats>("Stats");
        _health = GetParent().GetNode<Health>("Health");
        _health.Died += OnBearerDied;
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_health))
        {
            _health.Died -= OnBearerDied;
        }
    }

    public void Apply(CurseEffect curse)
    {
        if (_health.IsDead)
        {
            return;
        }

        if (_active.TryGetValue(curse.Kind, out CurseEffect? old))
        {
            curse.DamagePerSecond = Mathf.Max(curse.DamagePerSecond, old.DamagePerSecond);
            curse.Potency = Mathf.Max(curse.Potency, old.Potency);
            curse.Spread = Mathf.Max(curse.Spread, old.Spread);
            curse.Vulnerability = Mathf.Max(curse.Vulnerability, old.Vulnerability);
            curse.DeathBurst = Mathf.Max(curse.DeathBurst, old.DeathBurst);
            Remove(old);
        }

        curse.Left = curse.Duration;
        _active[curse.Kind] = curse;
        switch (curse.Kind)
        {
            case CurseKind.Elements:
                foreach (StatId res in new[] { StatId.FireResistance, StatId.ColdResistance, StatId.LightningResistance })
                {
                    _stats.Get(res).AddModifier(new StatModifier(ModifierType.Flat, -curse.Potency, ElementsSource));
                }

                if (curse.Vulnerability > 0f)
                {
                    _stats.Get(StatId.DamageTaken).AddModifier(new StatModifier(ModifierType.More, curse.Vulnerability, VulnerableSource));
                }

                break;
            case CurseKind.Time:
                _stats.MovementSpeed.AddModifier(new StatModifier(ModifierType.More, -Mathf.Clamp(curse.Potency, 0f, 90f), TimeSource));
                break;
        }

        QueueRedraw();
    }

    private void Remove(CurseEffect curse)
    {
        _active.Remove(curse.Kind);
        switch (curse.Kind)
        {
            case CurseKind.Elements:
                foreach (StatId res in new[] { StatId.FireResistance, StatId.ColdResistance, StatId.LightningResistance })
                {
                    _stats.Get(res).RemoveModifiersFromSource(ElementsSource);
                }

                _stats.Get(StatId.DamageTaken).RemoveModifiersFromSource(VulnerableSource);
                break;
            case CurseKind.Time:
                _stats.MovementSpeed.RemoveModifiersFromSource(TimeSource);
                break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_active.Count == 0)
        {
            return;
        }

        float dt = (float)delta;
        _spin += dt;
        var expired = new List<CurseEffect>();
        foreach (CurseEffect c in _active.Values)
        {
            c.Left -= dt;
            if (c.Left <= 0f)
            {
                expired.Add(c);
            }
        }

        _tick -= dt;
        if (_tick <= 0f)
        {
            _tick += Tick;
            if (_active.TryGetValue(CurseKind.Corruption, out CurseEffect? corruption) && corruption.Left > 0f && !_health.IsDead)
            {
                Node2D? source = corruption.Source is not null && IsInstanceValid(corruption.Source) ? corruption.Source : null;
                _health.ApplyResolvedDamage(corruption.DamagePerSecond * Tick * Mathf.Max(0f, _stats.Get(StatId.DamageTaken).Value), source);
            }
        }

        foreach (CurseEffect c in expired)
        {
            Remove(c);
        }

        QueueRedraw();
    }

    /// <summary>Curses with a spread jump to enemies around before their bearer goes.</summary>
    private void OnBearerDied()
    {
        Node2D bearer = GetParent<Node2D>();
        foreach (CurseEffect c in new List<CurseEffect>(_active.Values))
        {
            int spread = c.Spread;
            if (c.Source is Player player && IsInstanceValid(player) && Characters.CharacterBuild.Of(player)?.Ascendancy.Has("witch_contagion") == true)
            {
                spread = Mathf.Max(spread, 2);
            }

            if (spread <= 0 || c.Left <= 0f)
            {
                continue;
            }

            int jumped = 0;
            Faction attacker = c.Source?.GetNodeOrNull<ActorStats>("Stats")?.Faction ?? Faction.Player;
            foreach (Health h in CombatQuery.HostilesInRadius(GetTree(), bearer.GlobalPosition, SpreadRange, attacker))
            {
                if (jumped >= spread || h.IsDead || h.GetParent() == bearer)
                {
                    continue;
                }

                if (On(h.GetParent()) is { } next && !next.Has(c.Kind))
                {
                    CurseEffect copy = c.Copy();
                    copy.Spread = c.Spread - 1;
                    next.Apply(copy);
                    jumped++;
                }
            }
        }

        float burst = 0f;
        Node2D? burstSource = null;
        foreach (CurseEffect c in _active.Values)
        {
            if (c.DeathBurst > burst)
            {
                burst = c.DeathBurst;
                burstSource = c.Source is not null && IsInstanceValid(c.Source) ? c.Source : null;
            }
        }

        if (burst > 0f && _stats.Life is { } life)
        {
            var hit = new HitProfile
            {
                Damage = life.Max * burst,
                Type = DamageType.Shadow,
                Source = burstSource,
                Faction = burstSource?.GetNodeOrNull<ActorStats>("Stats")?.Faction ?? Faction.Player,
                Tags = SkillTags.Shadow | SkillTags.Area | SkillTags.Curse,
            };
            AreaBurst.SpawnOnKill(GetTree().CurrentScene, bearer.GlobalPosition, 80f, UI.UiTheme.DamageShadow, hit);
        }

        foreach (CurseEffect c in new List<CurseEffect>(_active.Values))
        {
            Remove(c);
        }

        QueueRedraw();
    }

    /// <summary>A small turning sigil per curse over the bearer's head.</summary>
    public override void _Draw()
    {
        int i = 0;
        foreach (CurseEffect c in _active.Values)
        {
            var at = new Vector2((i - (_active.Count - 1) / 2f) * 14f, 0f);
            float fade = Mathf.Clamp(c.Left / 0.6f, 0f, 1f);
            Color col = c.Tint with { A = 0.9f * fade };
            DrawArc(at, 5.5f, _spin * 3f, _spin * 3f + Mathf.Tau * 0.75f, 12, col, 2f, true);
            DrawCircle(at, 2f, col);
            i++;
        }
    }
}
