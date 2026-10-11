#nullable enable
using System.Collections.Generic;
using Godot;
using PathToExile.Combat;
using PathToExile.Enemies;
using PathToExile.Progression;
using PathToExile.Skills;
using PathToExile.Stats;

namespace PathToExile.Characters;

/// <summary>
/// Runtime behaviour of ascendancy nodes — docs/design/classes.md ->
/// "Ascendancies". Stat-only nodes are applied as modifiers; the rest are
/// hooks: an <see cref="IDamageHook"/> for conditional damage (executions,
/// marks, block, evasion, iaido), listeners on hits, kills, casts and dodges,
/// and a few per-frame effects (consecration, renewal, time field, familiar).
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
    private double _shadowstepUntil;
    private bool _parryReady;
    private float _warcryTimer;
    private bool _warcryReady;
    private int _overloadStacks;
    private float _overloadTimer;
    private DamageType? _lastElement;
    private float _radianceCooldown;
    private float _stillTime;
    private Vector2 _consecratedAt;
    private float _consecrationLeft;
    private float _tick;
    private float _familiarTimer;
    private Node2D? _familiar;

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
            res.RageGrantsAttackSpeed = Has("barb_wrath");
            res.Invalidate();
        }

        if (Has("sage_echo"))
        {
            build.SkillUser.GlobalModifiers.Add(new GlobalCastModifier(21, c =>
            {
                if ((c.Tags & SkillTags.Spell) != 0)
                {
                    c.Repeats += 1;
                }
            }));
        }

        if (Has("war_burn"))
        {
            build.SkillUser.GlobalModifiers.Add(new GlobalCastModifier(30, c => c.IgniteDamageMultiplier *= 1.5f));
        }

        if (_nodes.Count > 0 && !_hooked)
        {
            _hooked = true;
            DamageHooks.Register(Player, this);
            Health.AnyHitResolved += OnAnyHit;
            Health.AnyHitNegated += OnHitNegated;
            Enemy.AnyKilled += OnKill;
            build.SkillUser.CastFired += OnCast;
            if (Player.Dodge is { } dodge)
            {
                dodge.Dodged += OnDodged;
            }
        }

        if (Has("war_familiar") && _familiar is null)
        {
            _familiar = new Familiar();
            Player.AddChild(_familiar);
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
            build.SkillUser.CastFired -= OnCast;
            if (build.Player.Dodge is { } dodge)
            {
                dodge.Dodged -= OnDodged;
            }
        }

        if (_familiar is not null && IsInstanceValid(_familiar))
        {
            _familiar.QueueFree();
        }

        _familiar = null;
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

        if (Has("assassin_executioner") && lifeFrac < 0.35f) m *= 1.4f;
        if (Has("assassin_mark") && enemy is { } e && (e.Rarity >= MonsterRarity.Rare || e.IsBoss)) m *= 1.3f;
        if (Has("sam_bushido") && Player.Stats.Life is { } pl && pl.Current >= pl.Max * 0.9f) m *= 1.2f;
        if (Has("pal_aura") && target.GetParent<Node2D>().GlobalPosition.DistanceTo(Player.GlobalPosition) < 140f) m *= 1.12f;
        if (Has("pal_consecrate") && _consecrationLeft > 0f && target.GetParent<Node2D>().GlobalPosition.DistanceTo(_consecratedAt) < 110f) m *= 1.15f;

        if (Has("sam_iaido") && (now - _lastOutgoingHit >= 2.0 || Engine.GetPhysicsFrames() == _iaidoFrame))
        {
            m *= 1.8f;
            _iaidoFrame = Engine.GetPhysicsFrames();
        }

        if (Has("ninja_shadowstep") && now < _shadowstepUntil) m *= 1.6f;
        if (Has("sam_parry") && _parryReady) m *= 2f;
        if (Has("barb_warcry") && _warcryReady && (payload.Tags & SkillTags.Melee) != 0) m *= 1.8f;

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

        if (Has("ninja_evasive") && _rng.Randf() < 0.15f)
        {
            FloatingText(self.GetParent<Node2D>().GlobalPosition, "Evade", new Color(0.6f, 1f, 0.7f));
            return 0f;
        }

        return 1f;
    }

    private static bool IsElemental(DamagePayload p)
    {
        foreach ((DamageType t, float _) in p.Components)
        {
            if (t != DamageType.Physical)
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
        if (Engine.GetPhysicsFrames() != _iaidoFrame)
        {
            _shadowstepUntil = 0;
        }

        if ((payload.Tags & SkillTags.Melee) != 0)
        {
            _warcryReady = false;
        }

        var ailments = target.GetParent().GetNodeOrNull<Ailments>("Ailments");
        if (Has("assassin_venom") && payload.IsCrit && ailments is not null)
        {
            bool toxic = Has("assassin_toxic");
            ailments.AddPoison(taken * 0.6f * (toxic ? 1.5f : 1f), toxic ? 3f : 2f, Player);
        }

        if (Has("barb_leech") && _build.Resource is { Value: > 15 })
        {
            Player.Health?.Heal(taken * 0.03f);
        }

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
            var hit = new HitProfile { Damage = taken * 0.3f, Type = DamageType.Fire, Source = Player, Faction = Faction.Player, Tags = SkillTags.Fire | SkillTags.Area };
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
        if (Has("assassin_predator") && enemy.Rarity >= MonsterRarity.Rare)
        {
            Player.Health?.Heal((stats.Life?.Max ?? 0f) * 0.25f);
        }

        if (Has("war_siphon"))
        {
            Player.Health?.Heal((stats.Life?.Max ?? 0f) * 0.03f);
            if (stats.Mana is { } mana)
            {
                mana.Current = Mathf.Min(mana.Max, mana.Current + mana.Max * 0.03f);
            }
        }

        if (Has("war_decay") && _rng.Randf() < 0.25f)
        {
            var hit = new HitProfile { Damage = (enemy.Stats.Life?.Max ?? 0f) * 0.1f, Type = DamageType.Physical, Source = Player, Faction = Faction.Player, Tags = SkillTags.Area };
            AreaBurst.Spawn(GetTree().CurrentScene, enemy.GlobalPosition, 70f, 0f, new Color(0.7f, 0.4f, 1f), hit);
        }

        if (Has("ninja_fleet"))
        {
            Player.Dodge?.AdvanceRecharge(0.1f);
        }
    }

    private void OnCast(int slot)
    {
        if (_build is null || !Has("barb_fissure") || _build.SkillUser.GetSkill((SkillSlotId)slot) is not { } skill)
        {
            return;
        }

        SkillTags tags = skill.Definition.Tags;
        if ((tags & SkillTags.Melee) == 0 || (tags & SkillTags.Area) == 0)
        {
            return;
        }

        float damage = Stat.ComputeFormula(skill.Definition.BaseDamage, Player.Stats.GetModifiers(new ModifierQuery(StatId.Damage, tags))) * skill.LevelDamageMultiplier * 0.6f;
        Vector2 aim = Player.GlobalPosition.DirectionTo(Player.GetGlobalMousePosition());
        if (Core.GameSettings.Get(this)?.AutoAim == true && CombatQuery.NearestHostile(GetTree(), Player.GlobalPosition, Faction.Player, 500f) is { } t)
        {
            aim = Player.GlobalPosition.DirectionTo(t.GlobalPosition);
        }

        var hit = new HitProfile { Damage = damage, Type = DamageType.Physical, Source = Player, Faction = Faction.Player, Tags = tags, KnockbackForce = 80f };
        LineStrike.Spawn(GetTree().CurrentScene, Player.GlobalPosition, aim, 240f, 50f, 0.25f, hit);
    }

    private void OnDodged()
    {
        if (_build is null)
        {
            return;
        }

        if (Has("ninja_shadowstep"))
        {
            _shadowstepUntil = Now + 2.0;
        }

        if (Has("ninja_clone"))
        {
            float dmg = 25f + 8f * _build.Data.Level;
            ShadowClone.Spawn(GetTree().CurrentScene, Player, dmg);
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

        if (Has("barb_warcry") && !_warcryReady)
        {
            _warcryTimer += dt;
            if (_warcryTimer >= 8f)
            {
                _warcryTimer = 0f;
                _warcryReady = true;
            }
        }

        ActorStats stats = Player.Stats;
        if (Has("pal_renewal"))
        {
            Player.Health?.Heal((stats.Life?.Max ?? 0f) * 0.015f * dt);
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

        _tick -= dt;
        if (_tick <= 0f)
        {
            _tick = 0.25f;
            if (Has("sage_time"))
            {
                foreach (Health h in CombatQuery.HostilesInRadius(GetTree(), Player.GlobalPosition, 180f, Faction.Player))
                {
                    h.GetParent().GetNodeOrNull<Ailments>("Ailments")?.ApplyChill();
                }
            }
        }

        if (Has("war_familiar") && _familiar is not null)
        {
            _familiarTimer -= dt;
            if (_familiarTimer <= 0f && CombatQuery.NearestHostile(GetTree(), Player.GlobalPosition, Faction.Player, 500f) is { } target)
            {
                _familiarTimer = 1f;
                var tags = SkillTags.Spell | SkillTags.Projectile;
                float damage = Stat.ComputeFormula(8f + 2.2f * _build.Data.Level, stats.GetModifiers(new ModifierQuery(StatId.Damage, tags)));
                var hit = new HitProfile { Damage = damage, Type = DamageType.Physical, Source = Player, Faction = Faction.Player, Tags = tags };
                Vector2 from = _familiar.GlobalPosition;
                var frames = GD.Load<SpriteFrames>("res://assets/sprites/projectiles/enemy_bolt.tres");
                BossBullet.Spawn(GetTree().CurrentScene, from, from.DirectionTo(target.GlobalPosition) * 420f, hit, frames, 1.6f);
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

    /// <summary>The Warlock's familiar: a wisp circling the player.</summary>
    private sealed partial class Familiar : Node2D
    {
        private float _t;

        public override void _Process(double delta)
        {
            _t += (float)delta;
            Position = new Vector2(Mathf.Cos(_t * 2.2f), Mathf.Sin(_t * 2.2f) * 0.6f) * 36f + new Vector2(0f, -10f);
            QueueRedraw();
        }

        public override void _Draw()
        {
            DrawCircle(Vector2.Zero, 7f, new Color(0.6f, 0.25f, 1f, 0.5f));
            DrawCircle(Vector2.Zero, 4f, new Color(0.9f, 0.7f, 1f));
        }
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

/// <summary>Ninja shadow clone: taunts nearby enemies (decoy group), then explodes.</summary>
public partial class ShadowClone : Node2D
{
    private float _left = 2.5f;
    private float _damage;
    private Node2D _owner = null!;
    private Sprite2D? _sprite;

    public static void Spawn(Node parent, Player owner, float damage)
    {
        var clone = new ShadowClone { _damage = damage, _owner = owner, ZIndex = 0 };
        if (owner.GetNodeOrNull<AnimatedSprite2D>("Sprite") is { SpriteFrames: { } frames } src)
        {
            clone._sprite = new Sprite2D
            {
                Texture = frames.GetFrameTexture(src.Animation, src.Frame),
                Scale = src.Scale,
                FlipH = src.FlipH,
                Position = src.Position,
                Modulate = new Color(0.4f, 0.3f, 0.6f, 0.75f),
                TextureFilter = TextureFilterEnum.Nearest,
            };
            clone.AddChild(clone._sprite);
        }

        parent.AddChild(clone);
        clone.GlobalPosition = owner.GlobalPosition;
        clone.AddToGroup(Enemy.DecoyGroup);
    }

    public override void _Process(double delta)
    {
        _left -= (float)delta;
        if (_sprite is not null)
        {
            _sprite.Modulate = new Color(0.4f, 0.3f, 0.6f, 0.4f + 0.35f * Mathf.Abs(Mathf.Sin(_left * 6f)));
        }

        if (_left > 0f)
        {
            return;
        }

        var hit = new HitProfile { Damage = _damage, Type = DamageType.Physical, Source = IsInstanceValid(_owner) ? _owner : null, Faction = Faction.Player, Tags = SkillTags.Area, KnockbackForce = 160f };
        AreaBurst.Spawn(GetTree().CurrentScene, GlobalPosition, 100f, 0f, new Color(0.5f, 0.3f, 0.8f), hit);
        QueueFree();
    }
}
