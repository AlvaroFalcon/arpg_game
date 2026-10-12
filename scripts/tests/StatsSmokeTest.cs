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
    private bool _failed;

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
            System.Enum.GetValues<StatId>().Length == 39,
            $"StatId has {System.Enum.GetValues<StatId>().Length} entries, expected 39 (23 from docs/design/stats.md's third pass + 14 run stats from docs/design/progression.md + 2 stamina stats from docs/design/movement.md)");

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
            && ActorStats.DefaultBase(StatId.ManaRegen) == 5f
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
        }

        if (_frame >= 10)
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
}
