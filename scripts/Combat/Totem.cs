#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Skills;
using ArpgGame.Stats;

namespace ArpgGame.Combat;

/// <summary>
/// A placed totem (docs/design/skills.md -> "Totems"): it casts <see cref="Skill"/> — the gem
/// socketed into the totem gem — at the nearest hostile it can see within <see cref="Reach"/>,
/// through its owner's <see cref="SkillUser"/> (so the owner's stats and the socketed gem's
/// tree apply), with the totem gem's own tree (<see cref="Modifiers"/>) on top, as often as
/// that skill's use time and cooldown allow (never faster than <see cref="Interval"/>).
/// Since 2026-11-04 it has life (<see cref="MaxLife"/>): it joins the players' group, so
/// monsters may pick it as a target and their hits, projectiles and bursts reach it; it
/// breaks at 0 life or fades after <see cref="Lifetime"/>.
/// A plain drawn pole for now: no sprite or voxel model yet.
/// </summary>
public partial class Totem : Area2D
{
    public const string Group = "totems";

    public SkillUser Owner = null!;
    public SkillInstance Skill = null!;
    public IReadOnlyList<CastModifier> Modifiers = System.Array.Empty<CastModifier>();
    public float Lifetime = 8f;
    public float Interval = 0.9f;
    public float Reach = 420f;
    public float DamageEffectiveness = 0.7f;
    public Color Tint = new(0.95f, 0.75f, 0.45f);
    public Faction Faction = Faction.Player;
    public float MaxLife = 60f;

    /// <summary>The totem's life (a child "Health" with its own "Stats"), so every hit that
    /// reaches a player's Health reaches it too.</summary>
    public Health Health { get; private set; } = null!;

    /// <summary>Casts so far (tests).</summary>
    public int Casts { get; private set; }

    /// <summary>Seconds it waited after its last cast (tests).</summary>
    public float LastWait { get; private set; }

    private float _age;
    private float _timer = 0.25f;
    private float _flash;
    private float _hurt;

    public override void _Ready()
    {
        AddToGroup(Group);
        // Targetable like a player (monster AI, bursts, auto-aim); code that wants players
        // filters on `is Player`.
        AddToGroup(CombatQuery.PlayerGroup);
        ZIndex = 2;

        // Enemy projectiles find their targets by physics: the totem sits on the players' layer.
        CollisionLayer = CollisionLayers.PlayerFaction;
        CollisionMask = 0;
        Monitoring = false;
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 14f }, Position = new Vector2(0f, -14f) });

        var bases = new StatBlock();
        bases.Entries.Add(new StatBlockEntry { Id = StatId.MaxLife, BaseValue = MaxLife });
        bases.Entries.Add(new StatBlockEntry { Id = StatId.LifeRegen, BaseValue = 0f });
        AddChild(new ActorStats { Name = "Stats", Faction = Faction, IgnoresResourceCosts = true, Bases = bases });
        Health = new Health { Name = "Health" };
        AddChild(Health);
        Health.Died += () => QueueFree();
        Health.Damaged += _ => _hurt = 1f;
    }

    public void Expire()
    {
        _age = Mathf.Max(_age, Lifetime);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        _flash = Mathf.Max(0f, _flash - dt * 4f);
        _hurt = Mathf.Max(0f, _hurt - dt * 5f);
        if (_age >= Lifetime || Health.IsDead || !IsInstanceValid(Owner) || Owner.GetParent() is not Node2D caster || (caster.GetNodeOrNull<Health>("Health")?.IsDead ?? false))
        {
            QueueFree();
            return;
        }

        _timer -= dt;
        if (_timer > 0f)
        {
            QueueRedraw();
            return;
        }

        if (CombatQuery.NearestHostile(GetTree(), GlobalPosition, Faction, Reach, requireSight: true) is not { } target)
        {
            _timer = 0.2f;
            QueueRedraw();
            return;
        }

        Vector2 aim = GlobalPosition.DirectionTo(target.GlobalPosition);
        float wait = Owner.CastFrom(Skill, this, aim == Vector2.Zero ? Vector2.Right : aim, target.GlobalPosition, Modifiers, DamageEffectiveness);
        _timer = Mathf.Max(Interval, wait);
        LastWait = _timer;
        Casts++;
        _flash = 1f;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float fade = Mathf.Clamp((Lifetime - _age) / 0.5f, 0f, 1f);
        var dark = new Color(0.16f, 0.11f, 0.08f, fade);
        var wood = new Color(0.45f, 0.3f, 0.18f, fade);
        DrawCircle(new Vector2(0f, 4f), 13f, new Color(0f, 0f, 0f, 0.3f * fade));
        DrawRect(new Rect2(-6f, -34f, 12f, 38f), dark);
        DrawRect(new Rect2(-4.5f, -32.5f, 9f, 35f), wood.Lerp(new Color(1f, 1f, 1f, fade), _hurt * 0.7f));
        DrawRect(new Rect2(-9f, -40f, 18f, 9f), dark);
        DrawRect(new Rect2(-7.5f, -38.5f, 15f, 6f), Tint with { A = fade });
        DrawCircle(new Vector2(0f, -35f), 4f + 3f * _flash, Tint with { A = (0.5f + 0.5f * _flash) * fade });
        // Time left: a thin arc under the pole.
        float left = Mathf.Clamp(1f - _age / Mathf.Max(0.1f, Lifetime), 0f, 1f);
        DrawArc(new Vector2(0f, 4f), 15f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * left, 24, Tint with { A = 0.7f * fade }, 2f);
        // Life: a small bar over the pole, shown once it has been hit.
        if (Health?.GetParent()?.GetNodeOrNull<ActorStats>("Stats")?.Life is { } life && life.Current < life.Max)
        {
            float f = Mathf.Clamp(life.Current / Mathf.Max(1f, life.Max), 0f, 1f);
            DrawRect(new Rect2(-14f, -50f, 28f, 4f), new Color(0f, 0f, 0f, 0.6f * fade));
            DrawRect(new Rect2(-14f, -50f, 28f * f, 4f), new Color(0.85f, 0.2f, 0.2f, fade));
        }
    }
}
