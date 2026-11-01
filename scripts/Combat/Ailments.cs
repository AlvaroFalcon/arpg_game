#nullable enable
using Godot;
using ArpgGame.Stats;

namespace ArpgGame.Combat;

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
/// <summary>Ailments one hit applied (<see cref="Ailments.LastHitApplied"/>).</summary>
[System.Flags]
public enum AilmentHit : byte
{
    None = 0,
    Ignite = 1,
    Chill = 2,
    Shock = 4,
    Freeze = 8,
}

[GlobalClass]
public partial class Ailments : Node
{
    public const float ChillDuration = 2f;
    public const float ChillSlowPercent = 30f;
    /// <summary>How long one ignite stack burns (docs/design/skills.md -> "Stacking bleed
    /// and ignite"): every successful ignite adds its own stack, up to <see cref="MaxStacks"/>.</summary>
    public const float IgniteDuration = 3f;

    /// <summary>One ignite stack deals this fraction of its hit over its duration (was 0.9
    /// of the strongest hit when ignites replaced each other).</summary>
    public const float IgniteDamageRatio = 0.5f;
    public const float IgniteTick = 0.5f;

    /// <summary>Most bleed (and ignite) stacks one enemy can carry.</summary>
    public const int MaxStacks = 99;

    /// <summary>Oil (the Douse skill): fire damage taken is multiplied by 1 + potency/100, and
    /// every fire hit ignites an oiled enemy.</summary>
    public const float OilDuration = 6f;
    public const float ShockDuration = 3f;
    public const float ShockPercent = 25f;
    public const float FreezeDuration = 0.6f;
    /// <summary>How long one bleed stack lasts: short on purpose, so a high stack count needs
    /// fast, steady hits (owner's brief, 2026-11-01).</summary>
    public const float BleedDuration = 4f;

    /// <summary>One bleed stack deals this fraction of its hit (as taken) over its duration.</summary>
    public const float BleedDamageRatio = 0.5f;
    public const float BossFreezeDuration = 0.25f;
    public const float BossFreezeLockout = 4f;

    private static readonly object ChillSource = new();
    private static readonly object CurseSource = new();

    /// <summary>One stack of a stacking ailment: damage per second, seconds left, and the
    /// duration it started with (the target frame draws how much of it is left).</summary>
    public struct Stack
    {
        public float Dps;
        public float Left;
        public float Duration;
    }

    /// <summary>Bleed stacks (physical damage over time): each its own damage and timer,
    /// up to <see cref="MaxStacks"/> (docs/design/skills.md -> "Stacking bleed and ignite").</summary>
    private readonly System.Collections.Generic.List<Stack> _bleeds = new();
    private float _bleedTick;
    private Node2D? _bleedSource;

    /// <summary>Ignite stacks (fire damage over time), same rules as bleeding.</summary>
    private readonly System.Collections.Generic.List<Stack> _ignites = new();

    public float CurseRemaining { get; private set; }
    public bool IsCursed => CurseRemaining > 0f;

    public int BleedStacks => _bleeds.Count;
    public int IgniteStacks => _ignites.Count;

    /// <summary>Older name of <see cref="BleedStacks"/> (the stacking damage over time was
    /// called poison before bleeding got its own rules).</summary>
    public int PoisonStacks => _bleeds.Count;

    public System.Collections.Generic.IReadOnlyList<Stack> Bleeds => _bleeds;
    public System.Collections.Generic.IReadOnlyList<Stack> Ignites => _ignites;

    /// <summary>Seconds of oil left (Douse); while oiled, fire hits always ignite and fire
    /// damage taken is raised by <see cref="OilPotency"/>%.</summary>
    public float OilRemaining { get; private set; }
    public float OilPotency { get; private set; }
    public bool IsOiled => OilRemaining > 0f;

    /// <summary>Douse's Accelerant: ignites on this oiled enemy last this much longer.</summary>
    public float OilIgniteBonus { get; private set; }

    /// <summary>Fraction (0..1) left on the stack that runs out next, for the target frame.</summary>
    public static float NextToExpire(System.Collections.Generic.IReadOnlyList<Stack> stacks)
    {
        float best = 1f;
        foreach (Stack st in stacks)
        {
            best = Mathf.Min(best, st.Duration > 0f ? st.Left / st.Duration : 0f);
        }

        return best;
    }
    private static readonly object ShockSource = new();

    private ActorStats _stats = null!;
    private Health _health = null!;
    private readonly RandomNumberGenerator _rng = new();

    public float ChillRemaining { get; private set; }
    public float ShockRemaining { get; private set; }

    /// <summary>Seconds left on the longest-lasting ignite stack (0 = not burning).</summary>
    public float IgniteRemaining
    {
        get
        {
            float best = 0f;
            foreach (Stack st in _ignites)
            {
                best = Mathf.Max(best, st.Left);
            }

            return best;
        }
    }

    private float _igniteTickTimer;
    private Node2D? _igniteSource;

    public bool IsChilled => ChillRemaining > 0f;
    public bool IsIgnited => _ignites.Count > 0;
    public bool IsShocked => ShockRemaining > 0f;

    /// <summary>Seconds of freeze left: a frozen enemy neither moves nor acts.</summary>
    public float FreezeRemaining { get; private set; }
    public bool IsFrozen => FreezeRemaining > 0f;
    private float _freezeLockout;

    public override void _Ready()
    {
        _rng.Randomize();
        _stats = GetParent().GetNode<ActorStats>("Stats");
        _health = GetParent().GetNode<Health>("Health");
        _health.HitResolved += OnHitResolved;
    }

    /// <summary>The tint the owner's visual should take, or white for none.
    /// Ignite outranks shock outranks chill: the one doing damage reads first.</summary>
    public Color Tint => TintForCode(TintCode);

    /// <summary>The tint as one byte (co-op snapshots): 0 none, 1 frozen, 2 ignited,
    /// 3 poisoned, 4 cursed, 5 shocked, 6 chilled — same priority as <see cref="Tint"/>.</summary>
    public byte TintCode =>
        IsFrozen ? (byte)1
        : IsIgnited ? (byte)2
        : BleedStacks > 0 ? (byte)3
        : IsCursed ? (byte)4
        : IsShocked ? (byte)5
        : IsChilled ? (byte)6
        : (byte)0;

    public static Color TintForCode(byte code) => code switch
    {
        1 => new Color(0.8f, 0.97f, 1.4f),
        2 => new Color(1f, 0.6f, 0.35f),
        3 => new Color(0.55f, 1f, 0.5f),
        4 => new Color(0.8f, 0.5f, 1f),
        5 => new Color(1f, 1f, 0.45f),
        6 => new Color(0.55f, 0.8f, 1f),
        _ => Colors.White,
    };

    /// <summary>Which ailments the last resolved hit actually applied (a fresh chill, a
    /// successful ignite/shock/freeze roll). Presentation only: damage numbers read it
    /// from <see cref="Health.HitPresented"/> to tag the hit (docs/design/menus.md ->
    /// "Damage numbers").</summary>
    public AilmentHit LastHitApplied { get; private set; }

    private void OnHitResolved(DamagePayload payload, float taken)
    {
        LastHitApplied = AilmentHit.None;
        if (_health.IsDead)
        {
            KillBlast(payload, taken);
            return;
        }

        if (!payload.CanInflictAilments)
        {
            return;
        }

        // Bleeding (skill upgrades, Bloodthirst): physical damage over time, any damage type
        // can cause it. Every successful roll adds its own stack(s) with its own timer.
        if (payload.BleedChance > 0f && taken > 0f && _rng.Randf() < payload.BleedChance)
        {
            for (int i = 0; i < Mathf.Max(1, payload.BleedStacks); i++)
            {
                AddBleed(taken * BleedDamageRatio, BleedDuration * Mathf.Max(0.1f, payload.BleedDurationMultiplier), payload.SourceActor);
            }
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
                    if (!IsChilled)
                    {
                        LastHitApplied |= AilmentHit.Chill;
                    }

                    ApplyChill();
                    if (payload.FreezeChance > 0f && _rng.Randf() < payload.FreezeChance)
                    {
                        float before = FreezeRemaining;
                        ApplyFreeze();
                        if (FreezeRemaining > before)
                        {
                            LastHitApplied |= AilmentHit.Freeze;
                        }
                    }

                    break;
                case DamageType.Fire when IsOiled || _rng.Randf() < payload.IgniteChance:
                {
                    int before = _ignites.Count;
                    ApplyIgnite(taken * IgniteDamageRatio * payload.IgniteDamageMultiplier, payload.SourceActor,
                        IgniteDuration * Mathf.Max(0.1f, payload.IgniteDurationMultiplier) * (IsOiled ? 1f + OilIgniteBonus : 1f));
                    if (_ignites.Count > before || before == MaxStacks)
                    {
                        LastHitApplied |= AilmentHit.Ignite;
                    }

                    break;
                }

                case DamageType.Lightning when _rng.Randf() < payload.ShockChance:
                    LastHitApplied |= AilmentHit.Shock;
                    ApplyShock();
                    break;
            }
        }
    }

    /// <summary>A kill by a hit that carries a kill blast: the body bursts around it
    /// (skill upgrades). The blast's own hits carry none, so it never cascades.</summary>
    private void KillBlast(DamagePayload payload, float taken)
    {
        if (payload.KillBlastFraction <= 0f || payload.Components.Count == 0 || GetParent() is not Node2D body)
        {
            return;
        }

        var hit = new HitProfile
        {
            Damage = Mathf.Max(taken, payload.Total) * payload.KillBlastFraction,
            Type = payload.Components[0].Type,
            Source = payload.SourceActor,
            Faction = payload.SourceFaction,
            Tags = payload.Tags | SkillTags.Area,
            CanInflictAilments = payload.CanInflictAilments,
            Skill = payload.Skill,
        };
        AreaBurst.SpawnOnKill(GetTree().CurrentScene, body.GlobalPosition, payload.KillBlastRadius, UI.UiTheme.DamageColor(hit.Type), hit);
    }

    public void ApplyChill()
    {
        if (ChillRemaining <= 0f)
        {
            _stats.MovementSpeed.AddModifier(new StatModifier(ModifierType.More, -ChillSlowPercent, ChillSource));
        }

        ChillRemaining = ChillDuration;
    }

    /// <summary>Freezes the owner: 0.6 s, or 0.25 s at most once per 4 s for bosses.</summary>
    public void ApplyFreeze()
    {
        bool boss = GetParent() is Enemies.Enemy { IsBoss: true };
        if (boss)
        {
            if (_freezeLockout > 0f)
            {
                return;
            }

            _freezeLockout = BossFreezeLockout;
        }

        FreezeRemaining = Mathf.Max(FreezeRemaining, boss ? BossFreezeDuration : FreezeDuration);
    }

    /// <summary>Adds an ignite stack dealing <paramref name="totalDamage"/> over its duration.
    /// At <see cref="MaxStacks"/> the weakest stack gives way to a stronger one.</summary>
    public void ApplyIgnite(float totalDamage, Node2D? source, float duration = IgniteDuration)
    {
        if (totalDamage <= 0f)
        {
            return;
        }

        if (_ignites.Count == 0)
        {
            _igniteTickTimer = IgniteTick;
        }

        AddStack(_ignites, totalDamage, duration);
        _igniteSource = source;
    }

    /// <summary>Adds a bleed stack dealing <paramref name="total"/> over <paramref name="duration"/> s.</summary>
    public void AddBleed(float total, float duration, Node2D? source)
    {
        if (total <= 0f)
        {
            return;
        }

        AddStack(_bleeds, total, duration);
        _bleedSource = source;
    }

    /// <summary>Older name of <see cref="AddBleed"/>.</summary>
    public void AddPoison(float total, float duration, Node2D? source) => AddBleed(total, duration, source);

    private static void AddStack(System.Collections.Generic.List<Stack> stacks, float total, float duration)
    {
        duration = Mathf.Max(0.5f, duration);
        var stack = new Stack { Dps = total / duration, Left = duration, Duration = duration };
        if (stacks.Count < MaxStacks)
        {
            stacks.Add(stack);
            return;
        }

        int weakest = 0;
        for (int i = 1; i < stacks.Count; i++)
        {
            if (stacks[i].Dps * stacks[i].Left < stacks[weakest].Dps * stacks[weakest].Left)
            {
                weakest = i;
            }
        }

        if (stacks[weakest].Dps * stacks[weakest].Left < total)
        {
            stacks[weakest] = stack;
        }
    }

    /// <summary>All the damage the ignite stacks have left, removing them (Combust).</summary>
    public float ConsumeIgnites() => Consume(_ignites);

    /// <summary>All the damage the bleed stacks have left, removing them (Exsanguinate).</summary>
    public float ConsumeBleeds() => Consume(_bleeds);

    private static float Consume(System.Collections.Generic.List<Stack> stacks)
    {
        float total = 0f;
        foreach (Stack st in stacks)
        {
            total += st.Dps * Mathf.Max(0f, st.Left);
        }

        stacks.Clear();
        return total;
    }

    /// <summary>Douses the owner in oil for <paramref name="duration"/> s: fire damage taken
    /// +<paramref name="potency"/>%, and fire hits always ignite. A stronger oil wins.</summary>
    public void ApplyOil(float potency, float duration, float igniteDurationBonus = 0f)
    {
        OilIgniteBonus = OilRemaining > 0f ? Mathf.Max(OilIgniteBonus, igniteDurationBonus) : igniteDurationBonus;
        OilPotency = OilRemaining > 0f ? Mathf.Max(OilPotency, potency) : potency;
        OilRemaining = Mathf.Max(OilRemaining, duration);
        _health.FireDamageTakenMultiplier = 1f + OilPotency / 100f;
    }

    /// <summary>Sums the damage per second of a stack list and ages it by one tick.</summary>
    private static float TickStacks(System.Collections.Generic.List<Stack> stacks, float step)
    {
        float dps = 0f;
        for (int i = stacks.Count - 1; i >= 0; i--)
        {
            Stack st = stacks[i];
            float active = Mathf.Min(step, st.Left);
            dps += st.Dps * active / step;
            st.Left -= step;
            if (st.Left <= 0.0001f)
            {
                stacks.RemoveAt(i);
            }
            else
            {
                stacks[i] = st;
            }
        }

        return dps;
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

    public override void _PhysicsProcess(double delta) => Tick(delta);

    /// <summary>One physics step of this component. A monster drives it from its own
    /// <c>_PhysicsProcess</c> (its Ailments has physics processing turned off): one engine callback
    /// per monster instead of four (docs/design/performance.md -> "Frame budget pass", I5).</summary>
    public void Tick(double delta)
    {
        float dt = (float)delta;

        if (FreezeRemaining > 0f)
        {
            FreezeRemaining -= dt;
        }

        if (_freezeLockout > 0f)
        {
            _freezeLockout -= dt;
        }

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

        if (OilRemaining > 0f)
        {
            OilRemaining -= dt;
            if (OilRemaining <= 0f)
            {
                OilPotency = 0f;
                _health.FireDamageTakenMultiplier = 1f;
            }
        }

        if (_bleeds.Count > 0)
        {
            _bleedTick -= dt;
            if (_bleedTick <= 0f)
            {
                _bleedTick = 0.5f;
                float dps = TickStacks(_bleeds, 0.5f);
                Node2D? source = _bleedSource is not null && IsInstanceValid(_bleedSource) ? _bleedSource : null;
                _health.ApplyResolvedDamage(dps * 0.5f, source);
            }
        }

        if (_ignites.Count > 0)
        {
            _igniteTickTimer -= dt;
            if (_igniteTickTimer <= 0f)
            {
                _igniteTickTimer += IgniteTick;
                float dps = TickStacks(_ignites, IgniteTick);
                Node2D? source = _igniteSource is not null && IsInstanceValid(_igniteSource) ? _igniteSource : null;
                _health.ApplyResolvedDamage(dps * IgniteTick, source);
            }
        }
    }
}
