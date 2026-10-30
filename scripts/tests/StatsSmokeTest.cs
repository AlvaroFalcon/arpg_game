#nullable enable
using Godot;
using Godot.Collections;
using ArpgGame.Combat;
using ArpgGame.Stats;

namespace ArpgGame.Tests;

/// <summary>
/// Headless functional smoke test for the M2 stat-system pass
/// (docs/design/stats.md's second pass). Run with:
///   dotnet build ArpgGame.csproj
///   ./scripts/run_headless.sh res://scenes/tests/StatsSmokeTest.tscn --quit-after 60
///
/// Covers, in order: the keyed registry (get/set + named-accessor equivalence),
/// StatBlock base loading (including fallback-to-default for unlisted stats),
/// absolute-preserve-with-floor-at-1 resource-pool clamping in both directions,
/// the armour formula's small-hit-vs-large-hit asymmetry, resistance cap +
/// overcap, negative resistance floored at -100%, penetration eating overcap
/// first, and crit applying before mitigation through the real
/// <see cref="Health.TakeDamage"/> path.
///
/// Most checks call <see cref="DamageMitigation"/>'s static functions or mutate
/// a <see cref="Stat"/>'s BaseValue directly rather than going through a full
/// cast — this is pure math with no scene-tree dependency, so a direct call is
/// the real production path, not a bypass (same rationale SkillsSmokeTest gives
/// for calling TryCast directly). Only CRIT-BEFORE-MITIGATION exercises the full
/// Health.TakeDamage pipeline, since that's the one behaviour that only exists
/// as the composition of "roll crit" + "look up the attacker's stats" + "apply
/// mitigation," not as a single pure function.
/// </summary>
public partial class StatsSmokeTest : Node2D
{
    private ActorStats _sourceStats = null!;
    private ActorStats _targetStats = null!;
    private Health _targetHealth = null!;

    private int _frame;
    private bool _tickChecksDone;
    private bool _failed;
    private TargetDummy? _dummy;

    public override void _Ready()
    {
        _sourceStats = GetNode<ActorStats>("Source/Stats");
        _targetStats = GetNode<ActorStats>("Target/Stats");
        _targetHealth = GetNode<Health>("Target/Health");

        GD.Print("[STATSTEST] Ready.");

        CheckRegistry();
        CheckDefaultBaseTable();
        CheckResourcePoolClamping();
        CheckArmourMitigation();
        CheckResistanceCapAndOvercap();
        CheckNegativeResistanceFloor();
        CheckPenetrationEatsOvercapFirst();
        CheckFormulaNoAlloc();
        _ = EnemyTickChecks();

        // Deferred to _PhysicsProcess: exercises AddChild -> _Ready timing for a
        // node built entirely at runtime, so it needs at least one frame to
        // settle before its registry can be trusted.
    }

    private void Check(string name, bool passed, string detail)
    {
        GD.Print($"[STATSTEST] {name}: {(passed ? "PASS" : "FAIL")} — {detail}");
        if (!passed)
        {
            _failed = true;
        }
    }

    private static bool Approx(float a, float b, float epsilon = 0.01f) => Mathf.Abs(a - b) <= epsilon;

    private async System.Threading.Tasks.Task PhysicsFrames(int n)
    {
        for (int i = 0; i < n; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private Enemies.Enemy SpawnSlime(Vector2 at, bool replica = false)
    {
        var slime = GD.Load<PackedScene>("res://scenes/enemies/Slime.tscn").Instantiate<Enemies.Enemy>();
        slime.IsReplica = replica;
        slime.Position = at;
        AddChild(slime);
        slime.Stats.Get(StatId.MaxLife).BaseValue = 10000f;
        slime.Stats.Life!.Current = slime.Stats.Life.Max;
        return slime;
    }

    /// <summary>ENEMY-TICKS-DRIVEN-BY-OWNER (docs/design/performance.md -> "Frame budget pass",
    /// I5): a monster ticks its Stats, Health and Ailments itself; their own physics
    /// callbacks are off, and the timers, damage over time and regeneration run as before.
    /// A co-op replica keeps its components' own callbacks.</summary>
    private async System.Threading.Tasks.Task EnemyTickChecks()
    {
        try
        {
            await PhysicsFrames(2);
            Enemies.Enemy chilled = SpawnSlime(new Vector2(-3000f, -3000f));
            Enemies.Enemy ignited = SpawnSlime(new Vector2(-3000f, 3000f));
            Enemies.Enemy regen = SpawnSlime(new Vector2(3000f, -3000f));
            Enemies.Enemy replica = SpawnSlime(new Vector2(3000f, 3000f), replica: true);
            regen.ApplyRarity(Enemies.MonsterRarity.Rare, new[] { Enemies.MonsterRarities.FindMod("regenerating")! });
            await PhysicsFrames(2);
            bool off = !chilled.Stats.IsPhysicsProcessing() && !chilled.Health.IsPhysicsProcessing() && !chilled.Ailments!.IsPhysicsProcessing();
            bool replicaOn = replica.Stats.IsPhysicsProcessing() && replica.Health.IsPhysicsProcessing() && replica.Ailments!.IsPhysicsProcessing();

            float speed = chilled.Stats.MovementSpeed.Value;
            chilled.Ailments!.ApplyChill();
            float slowed = chilled.Stats.MovementSpeed.Value;
            ignited.Ailments!.ApplyIgnite(300f, null);
            float igniteStart = ignited.Stats.Life!.Current;
            regen.Stats.Life!.Current = regen.Stats.Life.Max * 0.5f;
            float regenStart = regen.Stats.Life.Current;
            int chillFrames = 0;
            float igniteAt1s = 0f;
            for (int i = 1; i <= 200; i++)
            {
                await PhysicsFrames(1);
                if (chillFrames == 0 && !chilled.Ailments.IsChilled)
                {
                    chillFrames = i;
                }

                if (i == 75)
                {
                    igniteAt1s = igniteStart - ignited.Stats.Life.Current;
                }
            }

            float igniteTotal = igniteStart - ignited.Stats.Life.Current;
            float healed = regen.Stats.Life.Current - regenStart;
            int expected = Mathf.RoundToInt(Ailments.ChillDuration * Engine.PhysicsTicksPerSecond);
            Check("ENEMY-TICKS-DRIVEN-BY-OWNER",
                off && replicaOn && Mathf.Abs(chillFrames - expected) <= 1 && slowed < speed && Mathf.IsEqualApprox(chilled.Stats.MovementSpeed.Value, speed)
                && Mathf.IsEqualApprox(igniteAt1s, 100f) && igniteTotal >= 249.9f && igniteTotal <= 300.1f && healed > 0f,
                $"components' own callbacks off={off} (replica on={replicaOn}); chill expired after {chillFrames} frames (expected {expected}), speed {speed:0} -> {slowed:0} -> {chilled.Stats.MovementSpeed.Value:0}; "
                + $"ignite 300 over 3 s: {igniteAt1s:0} at 1.25 s, {igniteTotal:0} in all; regenerating rare healed {healed:0}");
            foreach (Enemies.Enemy e in new[] { chilled, ignited, regen, replica })
            {
                e.QueueFree();
            }
        }
        catch (System.Exception e)
        {
            Check("ENEMY-TICKS-DRIVEN-BY-OWNER", false, e.ToString());
        }

        _tickChecksDone = true;
    }

    /// <summary>The formula as it was written before the allocation-free loop
    /// (docs/design/performance.md -> "Frame budget pass", I1): the reference.</summary>
    private static float OldFormula(float baseValue, System.Collections.Generic.IEnumerable<StatModifier> modifiers, float flatEffectiveness = 1f)
    {
        float flat = 0f, increased = 0f, more = 1f;
        foreach (StatModifier m in modifiers)
        {
            switch (m.Type)
            {
                case ModifierType.Flat: flat += m.Value; break;
                case ModifierType.Increased: increased += m.Value; break;
                case ModifierType.More: more *= 1f + m.Value / 100f; break;
            }
        }

        return (baseValue + flat * flatEffectiveness) * (1f + increased / 100f) * more;
    }

    /// <summary>PERF-STAT-FORMULA-NO-ALLOC: Stat.ComputeFormula is read for every stat on
    /// every frame; it must not allocate (a foreach over the interface boxed the
    /// enumerator), and it must give the old results for any Flat/Increased/More mix.</summary>
    private void CheckFormulaNoAlloc()
    {
        var mods = new System.Collections.Generic.List<StatModifier>
        {
            new(ModifierType.Flat, 12f), new(ModifierType.Increased, 35f), new(ModifierType.More, 20f),
            new(ModifierType.Flat, -4f), new(ModifierType.Increased, -10f), new(ModifierType.More, -15f),
        };
        var mixes = new[]
        {
            mods,
            new System.Collections.Generic.List<StatModifier> { new(ModifierType.Flat, 10f), new(ModifierType.Increased, 100f) },
            new System.Collections.Generic.List<StatModifier> { new(ModifierType.More, 50f), new(ModifierType.More, 50f) },
            new System.Collections.Generic.List<StatModifier>(),
        };
        bool same = true;
        string detail = "";
        foreach (var mix in mixes)
        {
            foreach (float eff in new[] { 1f, 0.5f, 2.5f })
            {
                float now = Stat.ComputeFormula(10f, mix, eff);
                float viaArray = Stat.ComputeFormula(10f, mix.ToArray(), eff);
                float old = OldFormula(10f, mix, eff);
                if (now != old || viaArray != old)
                {
                    same = false;
                    detail += $" [{mix.Count} mods x{eff}: {now} / {viaArray} vs {old}]";
                }
            }
        }

        Stat.ComputeFormula(10f, mods, 0.5f);
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        float sink = 0f;
        for (int i = 0; i < 10000; i++)
        {
            sink += Stat.ComputeFormula(10f, mods, 0.5f);
        }

        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("PERF-STAT-FORMULA-NO-ALLOC", allocated == 0 && same,
            $"10000 calls over 6 modifiers allocated {allocated} B (sum {sink:0}); same results as the old formula for {mixes.Length} mixes x 3 effectivenesses{detail}");
    }

    /// <summary>Registry get/set, and that the named accessors
    /// (docs/design/stats.md -> "The registry") are thin wrappers over the exact
    /// same <see cref="Stat"/> instance <see cref="ActorStats.Get"/> returns,
    /// not a parallel copy.</summary>
    private void CheckRegistry()
    {
        Check("REGISTRY-DEFAULT", _targetStats.Get(StatId.Armour).Value == 0f,
            $"Armour starts at default 0, got {_targetStats.Get(StatId.Armour).Value}");

        _targetStats.Get(StatId.Armour).BaseValue = 50f;
        Check("REGISTRY-SET-VIA-GET-VISIBLE-VIA-ACCESSOR",
            _targetStats.Armour.Value == 50f,
            $"named accessor Armour.Value={_targetStats.Armour.Value} after Get(Armour).BaseValue=50 (expected 50 — same Stat instance)");

        Check("REGISTRY-STAT-COUNT",
            System.Enum.GetValues<StatId>().Length == 46,
            $"StatId has {System.Enum.GetValues<StatId>().Length} entries, expected 46 (23 from docs/design/stats.md's third pass + 14 run stats from docs/design/progression.md + 2 stamina stats from docs/design/movement.md + AutoCastSpeed from docs/design/run.md + 5 passive-tree stats: LifeLeech, ManaOnKill, FreezeChance, ElementalDamageTaken, PhysicalDamageTaken + EnergyShield from docs/design/progression.md)");

        // Reset for the mitigation checks below, which assume a clean Armour.
        _targetStats.Get(StatId.Armour).BaseValue = 0f;
    }

    /// <summary>Spot-checks a few entries of docs/design/stats.md's base-value
    /// table directly, since every other check in this file depends on
    /// <see cref="ActorStats.DefaultBase"/> being right.</summary>
    private void CheckDefaultBaseTable()
    {
        bool ok = ActorStats.DefaultBase(StatId.MovementSpeed) == 130f
            && ActorStats.DefaultBase(StatId.MaxLife) == 50f
            && ActorStats.DefaultBase(StatId.ManaRegen) == 1.5f
            && ActorStats.DefaultBase(StatId.CritChance) == 0.05f
            && ActorStats.DefaultBase(StatId.CritMultiplier) == 1.5f
            && ActorStats.DefaultBase(StatId.Armour) == 0f;

        Check("DEFAULT-BASE-TABLE", ok, "spot-checked MovementSpeed/MaxLife/ManaRegen/CritChance/CritMultiplier/Armour against the design doc's table");
    }

    /// <summary>docs/design/stats.md -> "What happens to Current when Max
    /// changes": absolute preserve (not a free heal on increase), clamp-down on
    /// decrease, and the clamp itself floors at 1, never 0.</summary>
    private void CheckResourcePoolClamping()
    {
        ResourcePool life = _targetStats.Life!;
        float startingMax = life.Max;
        Check("LIFE-STARTS-FULL", life.Current == startingMax, $"Current={life.Current}, Max={startingMax}");

        // Simulate having taken damage, so there's headroom to observe "absolute
        // preserve" distinctly from "always clamped to the new max."
        life.Current = 20f;

        // Increase: Current must NOT change (no free heal from a bigger Max).
        _targetStats.Get(StatId.MaxLife).BaseValue = startingMax + 40f;
        life.ClampToMax();
        Check("MAXLIFE-INCREASE-NO-FREE-HEAL",
            life.Current == 20f && life.Max == startingMax + 40f,
            $"Current={life.Current} (expected unchanged 20), Max={life.Max} (expected {startingMax + 40f})");

        // Decrease, but still above 1: Current clamps down to the new Max exactly.
        _targetStats.Get(StatId.MaxLife).BaseValue = 10f;
        life.ClampToMax();
        Check("MAXLIFE-DECREASE-CLAMPS-DOWN",
            life.Current == 10f, $"Current={life.Current}, expected clamped to new Max=10");

        // Decrease below 1: the clamp must floor at 1, not 0 — "dying from taking
        // off a hat is a bug report, not a mechanic."
        _targetStats.Get(StatId.MaxLife).BaseValue = 0f;
        life.ClampToMax();
        Check("MAXLIFE-CLAMP-FLOORS-AT-ONE-NOT-ZERO",
            life.Current == 1f && !_targetHealth.IsDead,
            $"Current={life.Current} (expected 1, never 0), IsDead={_targetHealth.IsDead} (expected false)");

        // Restore a sane baseline for the crit/mitigation checks that follow.
        _targetStats.Get(StatId.MaxLife).BaseValue = startingMax;
        life.Current = startingMax;
        life.ClampToMax();
    }

    /// <summary>Armour is strong against many small hits, weak against one large
    /// hit — docs/design/stats.md -> "Physical: Armour". Checked as a *relative*
    /// claim (percentage mitigated), which is the actual design property, not an
    /// absolute number.</summary>
    private void CheckArmourMitigation()
    {
        const float armour = 100f;

        float smallHit = 10f;
        float smallTaken = DamageMitigation.MitigatePhysical(smallHit, armour);
        float smallMitigatedPct = 1f - smallTaken / smallHit;

        float largeHit = 1000f;
        float largeTaken = DamageMitigation.MitigatePhysical(largeHit, armour);
        float largeMitigatedPct = 1f - largeTaken / largeHit;

        Check("ARMOUR-SMALL-HIT-FORMULA", Approx(smallTaken, 5f),
            $"100 armour vs a 10-damage hit: taken={smallTaken} (expected 5, i.e. 50% reduced)");
        Check("ARMOUR-LARGE-HIT-FORMULA", Approx(largeTaken, 990.1f, 0.1f),
            $"100 armour vs a 1000-damage hit: taken={largeTaken} (expected ~990.1, i.e. ~1% reduced)");
        Check("ARMOUR-STRONGER-AGAINST-SMALL-HITS", smallMitigatedPct > largeMitigatedPct,
            $"mitigated% small={smallMitigatedPct:P1} vs large={largeMitigatedPct:P1} (small must mitigate more)");
    }

    /// <summary>docs/design/stats.md -> "Elemental: resistances": capped at 75%,
    /// and the overcap above the cap is tracked (readable via
    /// <see cref="DamageMitigation.Overcap"/>), not discarded.</summary>
    private void CheckResistanceCapAndOvercap()
    {
        const float resistance = 120f; // 75% effective + 45% overcap, per the doc's own example.

        float overcap = DamageMitigation.Overcap(resistance);
        Check("RESISTANCE-OVERCAP-TRACKED", Approx(overcap, 45f),
            $"120% resistance -> overcap={overcap} (expected 45, matching the design doc's own example)");

        float taken = DamageMitigation.MitigateElemental(100f, resistance, penetration: 0f);
        Check("RESISTANCE-CAPPED-AT-75", Approx(taken, 25f),
            $"120% resistance, no penetration, 100 damage -> taken={taken} (expected 25, i.e. 75% effective, not 120%)");
    }

    /// <summary>Resistance can go negative (extra damage taken) but never past
    /// -100%, even under heavy penetration — floored so penetration can never
    /// invert damage into healing.</summary>
    private void CheckNegativeResistanceFloor()
    {
        float taken = DamageMitigation.MitigateElemental(100f, resistance: -50f, penetration: 200f);
        Check("NEGATIVE-RESISTANCE-FLOORED-AT-MINUS-100", Approx(taken, 200f),
            $"-50% resistance under 200 penetration -> taken={taken} (expected exactly 200 = double damage, " +
            "the -100% floor, not more)");
    }

    /// <summary>Penetration eats overcap before it ever touches the capped,
    /// damage-reducing portion of resistance.</summary>
    private void CheckPenetrationEatsOvercapFirst()
    {
        const float resistance = 100f; // cap 75 + overcap 25.

        float takenWithSmallPen = DamageMitigation.MitigateElemental(100f, resistance, penetration: 20f);
        Check("PENETRATION-BELOW-OVERCAP-DOES-NOTHING", Approx(takenWithSmallPen, 25f),
            $"100% resistance (25 overcap), 20 penetration -> taken={takenWithSmallPen} (expected 25 — " +
            "penetration entirely absorbed by overcap, effective resistance still capped at 75%)");

        float takenWithLargePen = DamageMitigation.MitigateElemental(100f, resistance, penetration: 40f);
        Check("PENETRATION-ABOVE-OVERCAP-REDUCES-EFFECTIVE", Approx(takenWithLargePen, 40f),
            $"100% resistance (25 overcap), 40 penetration -> taken={takenWithLargePen} (expected 40 — " +
            "25 absorbed by overcap, remaining 15 penetration drops effective resistance to 60%)");
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;

        // Frame 1: build a StatBlock and a fresh ActorStats entirely at runtime,
        // rather than via an authored scene, and confirm both the overridden
        // entry and the fallback-to-default behaviour for an unlisted stat.
        if (_frame == 1)
        {
            var block = new StatBlock
            {
                Entries = new Array<StatBlockEntry>
                {
                    new() { Id = StatId.MaxLife, BaseValue = 99f },

                    // Attributes zeroed so this measures the StatBlock override
                    // itself. ActorStats._Ready derives attribute bonuses now, so
                    // base Strength 10 would silently add +20 life and +10 armour
                    // to both expectations below.
                    new() { Id = StatId.Strength, BaseValue = 0f },
                    new() { Id = StatId.Dexterity, BaseValue = 0f },
                    new() { Id = StatId.Intelligence, BaseValue = 0f },
                },
            };

            var dynamicStats = new ActorStats { Bases = block };
            dynamicStats.Name = "DynamicStats";
            AddChild(dynamicStats);

            // Damage numbers (docs/design/menus.md -> "Damage numbers"): an immortal dummy to hit.
            _dummy = GD.Load<PackedScene>("res://scenes/combat/TargetDummy.tscn").Instantiate<TargetDummy>();
            _dummy.Immortal = true;
            _dummy.Position = new Vector2(400f, 300f);
            AddChild(_dummy);
        }

        if (_frame == 3)
        {
            var dynamicStats = GetNode<ActorStats>("DynamicStats");
            Check("STATBLOCK-OVERRIDES-LISTED-ENTRY",
                dynamicStats.Get(StatId.MaxLife).Value == 99f,
                $"MaxLife={dynamicStats.Get(StatId.MaxLife).Value}, expected 99 from the assigned StatBlock");
            Check("STATBLOCK-FALLS-BACK-FOR-UNLISTED-STAT",
                dynamicStats.Get(StatId.Armour).Value == ActorStats.DefaultBase(StatId.Armour),
                $"Armour={dynamicStats.Get(StatId.Armour).Value}, expected the default {ActorStats.DefaultBase(StatId.Armour)} (not in the StatBlock)");

            CheckCritBeforeMitigation();
            CheckDamageNumbers();
        }

        if (_frame >= 10 && _tickChecksDone)
        {
            GD.Print(_failed ? "[STATSTEST] === RESULT: FAIL ===" : "[STATSTEST] === RESULT: PASS ===");
            GetTree().Quit(_failed ? 1 : 0);
        }
    }

    /// <summary>
    /// The one check that needs the full <see cref="Health.TakeDamage"/>
    /// pipeline rather than a pure function: crit is rolled from the
    /// *attacker's* stats, but mitigation is resolved against the *target's* —
    /// and the design requires crit to apply before mitigation, which is only
    /// observable through their composition. CritChance is forced to 1.0 so the
    /// roll is deterministic rather than relying on a lucky seed.
    /// </summary>
    private void CheckCritBeforeMitigation()
    {
        _sourceStats.Get(StatId.CritChance).BaseValue = 1f; // guaranteed crit
        _sourceStats.Get(StatId.CritMultiplier).BaseValue = 2f;
        _targetStats.Get(StatId.Armour).BaseValue = 100f;

        float lifeBefore = _targetStats.Life!.Current;

        var payload = new DamagePayload(10f, DamageType.Physical, GetNode<Node2D>("Source"), Faction.Player, SkillTags.None, DamageDeliveryKind.Hit);
        _targetHealth.TakeDamage(payload);

        float taken = lifeBefore - _targetStats.Life.Current;

        // Crit-then-mitigate: (10 * 2) mitigated by 100 armour = 20 * (1 - 100/300) = 13.33.
        // Mitigate-then-crit (the WRONG order) would give 10 * (1 - 100/200) * 2 = 10 — a
        // different number, which is exactly why this has to be checked end to end rather
        // than by testing crit and mitigation in isolation.
        Check("CRIT-STAMPED-ON-PAYLOAD", payload.IsCrit, "payload.IsCrit after a guaranteed-crit hit");
        Check("CRIT-APPLIED-BEFORE-MITIGATION", Approx(taken, 13.33f, 0.05f),
            $"life lost={taken} (expected ~13.33 = mitigate(10 * 2 crit mult, 100 armour); " +
            "10 would mean mitigation ran before crit instead)");

        // Reset Armour so it doesn't bleed into any check that runs after this one.
        _targetStats.Get(StatId.Armour).BaseValue = 0f;
    }

    /// <summary>
    /// Damage numbers as columns (docs/design/menus.md -> "Damage numbers"), through the
    /// real <see cref="Health.TakeDamage"/> → <see cref="Health.HitPresented"/> path on a
    /// training dummy: hits on one target stack in one column anchored to it, rapid
    /// same-kind hits merge, a crit gets the CRITICAL tag and the big style, the column
    /// caps its length, Compact drops tags and shortens columns, ticks sum into one
    /// line, damage taken never joins a column, and finished lines are reused.
    /// </summary>
    private void CheckDamageNumbers()
    {
        if (_dummy is null)
        {
            Check("DMGNUM-SETUP", false, "no dummy");
            return;
        }

        Node2D source = GetNode<Node2D>("Source");
        Health health = _dummy.GetNode<Health>("Health");
        Core.GameSettings? settings = Core.GameSettings.Get(this);
        Core.DamageNumberMode modeBefore = settings?.DamageNumberMode ?? Core.DamageNumberMode.Full;
        settings?.SetDamageNumberMode(Core.DamageNumberMode.Full);
        _sourceStats.Get(StatId.CritChance).BaseValue = 0f;

        DamagePayload Hit(DamageType type, float amount = 10f)
        {
            var p = new DamagePayload(amount, type, source, Faction.Player, SkillTags.None, DamageDeliveryKind.Hit);
            health.TakeDamage(p);
            return p;
        }

        // Three hits of different types on one target: three lines, one column.
        Hit(DamageType.Physical);
        Hit(DamageType.Fire);
        Hit(DamageType.Cold);
        UI.DamageNumberLayer? layer = UI.DamageNumberLayer.Get(this, create: false);
        System.Collections.Generic.IReadOnlyList<UI.DamageNumber> rows = layer?.RowsOf(_dummy) ?? System.Array.Empty<UI.DamageNumber>();
        bool sameX = rows.Count == 3 && Mathf.IsEqualApprox(rows[0].GlobalPosition.X, rows[2].GlobalPosition.X);
        bool anchored = rows.Count == 3 && rows[0].GlobalPosition.X > _dummy.GlobalPosition.X && rows[0].GlobalPosition.DistanceTo(_dummy.GlobalPosition) < 80f;
        bool newestLowest = rows.Count == 3 && rows[0].Type == DamageType.Cold && rows[0].TargetY > rows[1].TargetY && rows[1].TargetY > rows[2].TargetY;
        Check("DMGNUM-ONE-COLUMN-PER-TARGET", layer is not null && layer.ColumnCount == 1 && rows.Count == 3 && sameX && anchored && newestLowest,
            $"columns={layer?.ColumnCount}, lines={rows.Count}, same x={sameX}, anchored right of the dummy={anchored}, newest at the bottom={newestLowest}");

        // Another player's hit shows no number at all (owner decision): the column is untouched.
        var ally = new Player { IsLocal = false };
        int linesBeforeAlly = rows.Count;
        health.TakeDamage(new DamagePayload(30f, DamageType.Physical, ally, Faction.Player, SkillTags.None, DamageDeliveryKind.Hit));
        UI.DamageNumber? allyLine = UI.DamageNumberLayer.ShowHit(_dummy, new DamagePayload(30f, DamageType.Physical, ally, Faction.Player, SkillTags.None, DamageDeliveryKind.Hit), 30f, null, 0f, 0f, Vector2.Zero);
        Check("DMGNUM-ALLY-HIT-NO-LINE", allyLine is null && rows.Count == linesBeforeAlly && health.LastHitSource == ally,
            $"ally line={(allyLine is null ? "none" : "drawn")}, lines {linesBeforeAlly} -> {rows.Count}, the hit still landed={health.LastHitSource == ally}");
        ally.Free();

        // A second cold hit right away merges into the newest line (its sum), no new line.
        float first = rows.Count > 0 ? rows[0].Amount : 0f;
        Hit(DamageType.Cold);
        Check("DMGNUM-RAPID-HITS-MERGE", rows.Count == 3 && rows[0].Merged == 2 && Approx(rows[0].Amount, first * 2f) && rows[0].Text == UI.DamageNumber.Format(first * 2f),
            $"lines={rows.Count}, newest merged={rows[0].Merged}, text={rows[0].Text} (expected 2 hits of {first} summed)");

        // A crit: CRITICAL on the tag line and the big style.
        _sourceStats.Get(StatId.CritChance).BaseValue = 1f;
        Hit(DamageType.Physical);
        UI.DamageNumber crit = rows[0];
        Check("DMGNUM-CRIT-TAG-AND-BIG", crit.Big && crit.TagText.StartsWith("CRITICAL") && (crit.Tags & UI.HitTag.Critical) != 0,
            $"big={crit.Big}, tags='{crit.TagText}'");

        // Many crits (never merged): the column stops at its cap, the overflow leaves.
        for (int i = 0; i < 12; i++)
        {
            Hit(DamageType.Physical);
        }

        float height = 0f;
        foreach (UI.DamageNumber row in rows)
        {
            height += row.Height;
        }

        Check("DMGNUM-COLUMN-CAPS", rows.Count >= 3 && rows.Count <= UI.DamageNumberLayer.FullCap && height <= UI.DamageNumberLayer.MaxColumnHeight && layer!.ColumnCount == 1,
            $"lines={rows.Count} after 17 hits (cap {UI.DamageNumberLayer.FullCap} lines / {UI.DamageNumberLayer.MaxColumnHeight} px), height={height}, columns={layer?.ColumnCount}");

        // Damage that was already resolved (an ignite tick): one small line that sums.
        int before = rows.Count;
        health.ApplyResolvedDamage(5f, source);
        health.ApplyResolvedDamage(7f, source);
        UI.DamageNumber tick = rows[0];
        Check("DMGNUM-TICKS-SUM", tick.Kind == UI.DamageNumberKind.Tick && tick.Text == "12" && rows.Count <= UI.DamageNumberLayer.FullCap,
            $"newest kind={tick.Kind}, text={tick.Text} (expected 12), lines {before} -> {rows.Count}");

        // Damage taken floats on its own, never in a column.
        UI.DamageNumber? taken = UI.DamageNumberLayer.ShowIncoming(source, 9f, 100f);
        Check("DMGNUM-INCOMING-NOT-IN-COLUMN", taken is { Kind: UI.DamageNumberKind.Incoming } && layer!.ColumnCount == 1 && !System.Linq.Enumerable.Contains(rows, taken),
            $"kind={taken?.Kind}, columns={layer?.ColumnCount}");

        // Pooling: let every line finish, then hit again: no new node is made.
        layer!._Process(5.0);
        int created = UI.DamageNumberLayer.Created;
        int reused = UI.DamageNumberLayer.Reused;
        int idle = layer.IdleCount;
        Hit(DamageType.Fire);
        Hit(DamageType.Lightning);
        Check("DMGNUM-POOLED-NODES-REUSED", layer.LiveCount == 2 && UI.DamageNumberLayer.Created == created && UI.DamageNumberLayer.Reused == reused + 2 && idle >= 2,
            $"live={layer.LiveCount}, created {created} -> {UI.DamageNumberLayer.Created}, reused {reused} -> {UI.DamageNumberLayer.Reused}, idle before={idle}");

        // Compact: shorter column, no tag line, no icon, crits a medium line.
        if (settings is not null)
        {
            layer._Process(5.0);
            settings.SetDamageNumberMode(Core.DamageNumberMode.Compact);
            for (int i = 0; i < 6; i++)
            {
                Hit(DamageType.Physical);
            }

            rows = layer.RowsOf(_dummy);
            Check("DMGNUM-COMPACT", rows.Count == UI.DamageNumberLayer.CompactCap && rows[0].TagText == "" && !rows[0].Big && rows[0].Medium,
                $"lines={rows.Count} (cap {UI.DamageNumberLayer.CompactCap}), tags='{rows[0].TagText}', big={rows[0].Big}, medium={rows[0].Medium}");

            layer._Process(5.0);
            settings.SetDamageNumberMode(Core.DamageNumberMode.Off);
            Hit(DamageType.Physical);
            Check("DMGNUM-OFF", layer.LiveCount == 0, $"live lines with numbers off={layer.LiveCount}");
            settings.SetDamageNumberMode(modeBefore);
        }

        _sourceStats.Get(StatId.CritChance).BaseValue = 0f;
    }
}
