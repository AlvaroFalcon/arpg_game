#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Enemies;
using ArpgGame.Progression;
using ArpgGame.Skills;
using ArpgGame.Stats;

namespace ArpgGame.Characters;

/// <summary>
/// Runtime behaviour of ascendancy nodes — docs/design/classes.md ->
/// "Ascendancies". Stat-only nodes are applied as modifiers; the rest are
/// hooks: an <see cref="IDamageHook"/> for conditional damage (executions,
/// marks, block, iaido), listeners on hits and kills, and a few per-frame effects
/// (consecration, renewal, undying). The six evolutions of 2026-10-30 reuse the old
/// behaviour ids where a node moved (war_hex now belongs to the Witch, asc_* to the
/// Elementalist); the Witch's Contagion lives with the curses (Combat/Curses.cs).
/// Everything is torn down by <see cref="Strip"/> so a respec or a rebuild
/// never leaves a hook behind.
/// </summary>
public partial class AscendancyEffects : Node, IDamageHook
{
    private CharacterBuild? _build;
    private readonly HashSet<string> _nodes = new();
    private readonly RandomNumberGenerator _rng = new();
    private bool _hooked;

    // Timers / state.
    private double _lastOutgoingHit = -100;
    private ulong _iaidoFrame;
    private bool _parryReady;
    private int _overloadStacks;
    private float _overloadTimer;
    private DamageType? _lastElement;
    private float _radianceCooldown;
    private float _stillTime;
    private Vector2 _consecratedAt;
    private float _consecrationLeft;

    public bool Has(string id) => _nodes.Contains(id);

    private Player Player => _build!.Player;

    public override void _Ready()
    {
        _rng.Randomize();
    }

    public void Apply(CharacterBuild build)
    {
        _build = build;
        _nodes.Clear();
        CharacterData data = build.Data;
        if (data.Ascendancy is null)
        {
            return;
        }

        foreach (AscendancyNode node in AscendancyCatalog.For(data.Ascendancy))
        {
            if (!data.AscendancyNodes.Contains(node.Id))
            {
                continue;
            }

            _nodes.Add(node.Id);
            foreach (PassiveMod m in node.Mods)
            {
                build.Stats.Get(m.Stat).AddModifier(new StatModifier(m.Type, m.Value, build.Source, m.Condition, m.ConditionAny));
            }
        }

        if (build.Resource is { } res)
        {
            res.BonusMax = Has("barb_rage_cap") ? 20 : 0;
            res.RageGrantsAttackSpeed = false;
            res.Invalidate();
        }

        if (_nodes.Count > 0 && !_hooked)
        {
            _hooked = true;
            DamageHooks.Register(Player, this);
            Health.AnyHitResolved += OnAnyHit;
            Health.AnyHitNegated += OnHitNegated;
            Enemy.AnyKilled += OnKill;
        }
    }

    public void Strip(CharacterBuild build)
    {
        _nodes.Clear();
        if (_hooked)
        {
            _hooked = false;
            DamageHooks.Unregister(build.Player);
            Health.AnyHitResolved -= OnAnyHit;
            Health.AnyHitNegated -= OnHitNegated;
            Enemy.AnyKilled -= OnKill;
        }
    }

    public override void _ExitTree()
    {
        if (_build is not null && IsInstanceValid(_build.Player))
        {
            Strip(_build);
        }
    }

    private static double Now => Time.GetTicksMsec() / 1000.0;

    // --- IDamageHook ------------------------------------------------------------

    public float Outgoing(Health target, DamagePayload payload)
    {
        float m = 1f;
        var enemy = target.GetParent() as Enemy;
        float lifeFrac = target.GetParent().GetNodeOrNull<ActorStats>("Stats") is { Life: { } l } && l.Max > 0 ? l.Current / l.Max : 1f;
        double now = Now;

        if (Has("assassin_mark") && enemy is { } e && (e.Rarity >= MonsterRarity.Rare || e.IsBoss)) m *= 1.3f;
        if (Has("sam_bushido") && Player.Stats.Life is { } pl && pl.Current >= pl.Max * 0.9f) m *= 1.2f;
        if (Has("pal_aura") && target.GetParent<Node2D>().GlobalPosition.DistanceTo(Player.GlobalPosition) < 140f) m *= 1.12f;
        if (Has("pal_consecrate") && _consecrationLeft > 0f && target.GetParent<Node2D>().GlobalPosition.DistanceTo(_consecratedAt) < 110f) m *= 1.15f;

        if (Has("sam_iaido") && (now - _lastOutgoingHit >= 2.0 || Engine.GetPhysicsFrames() == _iaidoFrame))
        {
            m *= 1.8f;
            _iaidoFrame = Engine.GetPhysicsFrames();
        }

        if (Has("sam_parry") && _parryReady) m *= 2f;

        if (Has("asc_overload") && _overloadStacks > 0 && IsElemental(payload)) m *= 1f + 0.08f * _overloadStacks;
        return m;
    }

    public float Incoming(Health self, DamagePayload payload)
    {
        if (Has("pal_block") && _rng.Randf() < 0.25f)
        {
            FloatingText(self.GetParent<Node2D>().GlobalPosition, "Block", new Color(0.9f, 0.9f, 1f));
            return 0f;
        }

        return 1f;
    }

    private static bool IsElemental(DamagePayload p)
    {
        foreach ((DamageType t, float _) in p.Components)
        {
            if (t is DamageType.Fire or DamageType.Cold or DamageType.Lightning)
            {
                return true;
            }
        }

        return false;
    }

    // --- event listeners ----------------------------------------------------------

    private void OnAnyHit(Health target, DamagePayload payload, float taken)
    {
        if (_build is null || payload.SourceActor != Player || target.GetParent() == Player)
        {
            return;
        }

        _lastOutgoingHit = Now;
        _parryReady = false;
        var ailments = target.GetParent().GetNodeOrNull<Ailments>("Ailments");

        if (Has("war_hex") && ailments is not null)
        {
            ailments.ApplyCurse(20f, 4f);
            if (Has("war_wither"))
            {
                ailments.ApplyChill();
            }
        }

        if (Has("asc_prismatic") && ailments is not null && IsElemental(payload) && _rng.Randf() < 0.2f)
        {
            ailments.ApplyChill();
            ailments.ApplyShock();
            ailments.ApplyIgnite(taken * 0.5f, Player);
        }

        if (Has("asc_overload") && IsElemental(payload))
        {
            DamageType type = payload.Components[0].Type;
            if (_lastElement is { } last && last != type)
            {
                _overloadStacks = Mathf.Min(5, _overloadStacks + 1);
            }

            _lastElement = type;
            _overloadTimer = 4f;
        }

        if (Has("pal_radiance") && (payload.Tags & SkillTags.Melee) != 0 && _radianceCooldown <= 0f)
        {
            _radianceCooldown = 0.25f;
            var hit = new HitProfile { Damage = taken * 0.3f, Type = DamageType.Holy, Source = Player, Faction = Faction.Player, Tags = SkillTags.Holy | SkillTags.Area };
            AreaBurst.Spawn(GetTree().CurrentScene, target.GetParent<Node2D>().GlobalPosition, 60f, 0f, new Color(1f, 0.9f, 0.5f), hit);
        }
    }

    private void OnHitNegated(Health self, DamagePayload payload, bool byInvulnerability)
    {
        if (!Has("sam_parry") || self.GetParent() != Player || !byInvulnerability || Player.Dodge is not { } dodge)
        {
            return;
        }

        if (Now - dodge.LastDodgeTime <= 0.2 && !_parryReady)
        {
            _parryReady = true;
            dodge.AddCharge();
            FloatingText(Player.GlobalPosition, "Parry!", new Color(1f, 0.85f, 0.4f));
            Core.Sfx.Play(this, "ui");
        }
    }

    private void OnKill(Enemy enemy)
    {
        if (_build is null || enemy.Health.LastHitSource != Player)
        {
            return;
        }

        ActorStats stats = Player.Stats;
        if (Has("war_siphon"))
        {
            Player.Health?.Heal((stats.Life?.Max ?? 0f) * 0.03f);
            if (stats.Mana is { } mana)
            {
                mana.Current = Mathf.Min(mana.Max, mana.Current + mana.Max * 0.03f);
            }
        }
    }

    // --- per-frame ------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (_build is null || _nodes.Count == 0 || !IsInstanceValid(Player))
        {
            return;
        }

        float dt = (float)delta;
        _radianceCooldown -= dt;
        if (_overloadTimer > 0f)
        {
            _overloadTimer -= dt;
            if (_overloadTimer <= 0f)
            {
                _overloadStacks = 0;
            }
        }

        ActorStats stats = Player.Stats;
        if (Has("pal_renewal"))
        {
            Player.Health?.Heal((stats.Life?.Max ?? 0f) * 0.015f * dt);
        }

        if (Has("jugg_undying") && stats.Life is { Max: > 0f } life)
        {
            float rate = life.Current < life.Max * 0.35f ? 0.04f : 0.02f;
            Player.Health?.Heal(life.Max * rate * dt);
        }

        if (Has("pal_consecrate"))
        {
            bool still = Player.Velocity.LengthSquared() < 25f;
            _stillTime = still ? _stillTime + dt : 0f;
            if (_stillTime >= 1f && (_consecrationLeft <= 0f || Player.GlobalPosition.DistanceTo(_consecratedAt) > 110f))
            {
                _consecratedAt = Player.GlobalPosition;
                _consecrationLeft = 4f;
                ConsecratedGround.Spawn(GetTree().CurrentScene, _consecratedAt, 110f, 4f);
            }

            if (_consecrationLeft > 0f)
            {
                _consecrationLeft -= dt;
                if (Player.GlobalPosition.DistanceTo(_consecratedAt) < 110f)
                {
                    Player.Health?.Heal((stats.Life?.Max ?? 0f) * 0.04f * dt);
                    _consecrationLeft = Mathf.Max(_consecrationLeft, 0.5f);
                }
            }
        }
    }

    private void FloatingText(Vector2 at, string text, Color color)
    {
        var label = new Label { Text = text, ZIndex = 50 };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", 16);
        GetTree().CurrentScene.AddChild(label);
        label.GlobalPosition = at + new Vector2(-20f, -50f);
        Tween tween = label.CreateTween();
        tween.TweenProperty(label, "position:y", label.Position.Y - 24f, 0.6f);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, 0.6f);
        tween.TweenCallback(Callable.From(label.QueueFree));
    }
}

/// <summary>Paladin consecrated ground: a soft golden circle.</summary>
public partial class ConsecratedGround : Node2D
{
    private float _radius;
    private float _left;
    private float _life;

    public static void Spawn(Node parent, Vector2 at, float radius, float duration)
    {
        var g = new ConsecratedGround { _radius = radius, _left = duration, _life = duration, ZIndex = -2 };
        parent.AddChild(g);
        g.GlobalPosition = at;
    }

    public override void _Process(double delta)
    {
        _left -= (float)delta;
        if (_left <= 0f)
        {
            QueueFree();
            return;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        float a = Mathf.Min(1f, _left / 0.5f) * 0.25f;
        DrawCircle(Vector2.Zero, _radius, new Color(1f, 0.9f, 0.5f, a));
        DrawArc(Vector2.Zero, _radius, 0f, Mathf.Tau, 40, new Color(1f, 0.95f, 0.6f, a * 3f), 2f);
    }
}
