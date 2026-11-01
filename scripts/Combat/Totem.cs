#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Skills;

namespace ArpgGame.Combat;

/// <summary>
/// A placed totem (docs/design/skills.md -> "Totems"): every <see cref="Interval"/> seconds
/// it casts <see cref="Skill"/> — the gem socketed into the totem gem — at the nearest
/// hostile it can see within <see cref="Reach"/>, through its owner's <see cref="SkillUser"/>
/// (so the owner's stats and the socketed gem's tree apply), with the totem gem's own tree
/// (<see cref="Modifiers"/>) on top. It cannot be hit and fades after <see cref="Lifetime"/>.
/// A plain drawn pole for now: no sprite or voxel model yet.
/// </summary>
public partial class Totem : Node2D
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

    /// <summary>Casts so far (tests).</summary>
    public int Casts { get; private set; }

    private float _age;
    private float _timer = 0.25f;
    private float _flash;

    public override void _Ready()
    {
        AddToGroup(Group);
        ZIndex = 2;
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
        if (_age >= Lifetime || !IsInstanceValid(Owner) || Owner.GetParent() is not Node2D caster || (caster.GetNodeOrNull<Health>("Health")?.IsDead ?? false))
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

        _timer = Interval;
        Vector2 aim = GlobalPosition.DirectionTo(target.GlobalPosition);
        Owner.CastFrom(Skill, this, aim == Vector2.Zero ? Vector2.Right : aim, target.GlobalPosition, Modifiers, DamageEffectiveness);
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
        DrawRect(new Rect2(-4.5f, -32.5f, 9f, 35f), wood);
        DrawRect(new Rect2(-9f, -40f, 18f, 9f), dark);
        DrawRect(new Rect2(-7.5f, -38.5f, 15f, 6f), Tint with { A = fade });
        DrawCircle(new Vector2(0f, -35f), 4f + 3f * _flash, Tint with { A = (0.5f + 0.5f * _flash) * fade });
        // Time left: a thin arc under the pole.
        float left = Mathf.Clamp(1f - _age / Mathf.Max(0.1f, Lifetime), 0f, 1f);
        DrawArc(new Vector2(0f, 4f), 15f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * left, 24, Tint with { A = 0.7f * fade }, 2f);
    }
}
