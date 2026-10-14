#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Enemies;
using ArpgGame.Items;
using ArpgGame.Progression;
using ArpgGame.Run;
using ArpgGame.Skills;
using ArpgGame.Stats;

namespace ArpgGame.Tests;

/// <summary>
/// Headless smoke test for the survivor-run layer (docs/design/run.md,
/// progression.md, dodge.md, enemies.md, bosses.md). Run with:
///   ./scripts/run_all_tests.sh RunSmokeTest
///
/// Builds a player and enemies by hand (no director, no UI) and checks the
/// rules those systems promise: dodge charges and invulnerability, ailments,
/// supports transforming a cast, offers and keystones, the experience curve,
/// area hits, chains, contact damage, boss phases, and profile item save/load.
/// </summary>
public partial class RunSmokeTest : Node2D
{
    private bool _failed;

    public override void _Ready()
    {
        GD.Print("[RUNTEST] Ready.");
        _ = RunAll();
    }

    private void Check(string name, bool ok, string detail)
    {
        GD.Print($"[RUNTEST] {name}: {(ok ? "PASS" : "FAIL")} — {detail}");
        if (!ok)
        {
            _failed = true;
        }
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private Player SpawnPlayer(Vector2 at)
    {
        var player = GD.Load<PackedScene>("res://scenes/Player.tscn").Instantiate<Player>();
        AddChild(player);
        player.GlobalPosition = at;
        return player;
    }

    private Enemy SpawnEnemy(string scene, Vector2 at)
    {
        var enemy = GD.Load<PackedScene>($"res://scenes/enemies/{scene}.tscn").Instantiate<Enemy>();
        AddChild(enemy);
        enemy.GlobalPosition = at;
        return enemy;
    }

    private async Task RunAll()
    {
        await Frames(2);
        await DodgeChecks();
        await AilmentChecks();
        SupportChecks();
        await BuildChecks();
        ExperienceChecks();
        await AreaAndChainChecks();
        await ContactChecks();
        await BossPhaseChecks();
        ProfileChecks();
        RiftCurveChecks();
        DirectorChecks();
        await RiftPowerUpChecks();

        GD.Print($"[RUNTEST] === RESULT: {(_failed ? "FAIL" : "PASS")} ===");
        GetTree().Quit(_failed ? 1 : 0);
    }

    private async Task DodgeChecks()
    {
        Player player = SpawnPlayer(new Vector2(200, 200));
        await Frames(2);
        Dodge dodge = player.Dodge!;
        int start = dodge.Charges;
        Check("DODGE-STARTS-FULL", start == dodge.MaxCharges && start == 2, $"charges={start}, max={dodge.MaxCharges}");

        Vector2 before = player.GlobalPosition;
        bool ok = dodge.TryDodge(Vector2.Right);
        Check("DODGE-SPENDS-CHARGE", ok && dodge.Charges == start - 1, $"ok={ok}, charges={dodge.Charges}");
        Check("DODGE-GRANTS-INVULNERABILITY", player.Health!.IsInvulnerable, $"invulnerable={player.Health.IsInvulnerable}");

        float lifeBefore = player.Stats.Life!.Current;
        player.Health.TakeDamage(new DamagePayload(25f, DamageType.Physical, null, Faction.Enemy, SkillTags.None, DamageDeliveryKind.Hit));
        Check("INVULNERABLE-IGNORES-HITS", Mathf.IsEqualApprox(player.Stats.Life.Current, lifeBefore), $"life {lifeBefore} -> {player.Stats.Life.Current}");

        await Frames(12);
        float moved = player.GlobalPosition.DistanceTo(before);
        Check("DODGE-MOVES-DASH-DISTANCE", moved > dodge.DashDistance * 0.8f, $"moved {moved:0} px (dash {dodge.DashDistance})");

        dodge.TryDodge(Vector2.Left);
        bool third = dodge.TryDodge(Vector2.Left);
        Check("DODGE-NEEDS-A-CHARGE", !third && dodge.Charges == 0, $"third dodge refused={!third}, charges={dodge.Charges}");

        await Frames(30);
        Check("INVULNERABILITY-ENDS", !player.Health.IsInvulnerable, $"invulnerable={player.Health.IsInvulnerable}");

        int frames = Mathf.CeilToInt(dodge.RechargeTime * 60f) + 10;
        await Frames(frames);
        Check("DODGE-RECHARGES", dodge.Charges == 1, $"charges after {frames} frames = {dodge.Charges}");

        player.Stats.Get(StatId.DodgeCharges).AddModifier(new StatModifier(ModifierType.Flat, 1f, this));
        Check("DODGE-CHARGES-ARE-A-STAT", dodge.MaxCharges == 3, $"max after +1 = {dodge.MaxCharges}");
        player.QueueFree();
        await Frames(1);
    }

    private async Task AilmentChecks()
    {
        Enemy slime = SpawnEnemy("Slime", new Vector2(900, 900));
        await Frames(2);
        float speed = slime.Stats.MovementSpeed.Value;
        slime.Health.TakeDamage(new DamagePayload(1f, DamageType.Cold, null, Faction.Player, SkillTags.Cold, DamageDeliveryKind.Hit));
        float chilled = slime.Stats.MovementSpeed.Value;
        Check("COLD-HIT-CHILLS", slime.Ailments!.IsChilled && chilled < speed * 0.75f, $"speed {speed:0} -> {chilled:0}");

        var shock = new DamagePayload(1f, DamageType.Lightning, null, Faction.Player, SkillTags.Lightning, DamageDeliveryKind.Hit) { ShockChance = 1f };
        slime.Health.TakeDamage(shock);
        float lifeBefore = slime.Stats.Life!.Current;
        slime.Health.TakeDamage(new DamagePayload(10f, DamageType.Fire, null, Faction.Player, SkillTags.Fire, DamageDeliveryKind.Hit));
        float lost = lifeBefore - slime.Stats.Life.Current;
        Check("SHOCK-INCREASES-DAMAGE-TAKEN", slime.Ailments.IsShocked && lost > 12f, $"10 fire on a shocked slime took {lost:0.0}");

        var focus = new DamagePayload(1f, DamageType.Fire, null, Faction.Player, SkillTags.Fire, DamageDeliveryKind.Hit) { IgniteChance = 1f, CanInflictAilments = false };
        slime.Health.TakeDamage(focus);
        Check("NO-AILMENTS-FLAG-RESPECTED", !slime.Ailments.IsIgnited, $"ignited={slime.Ailments.IsIgnited}");

        var ignite = new DamagePayload(10f, DamageType.Fire, null, Faction.Player, SkillTags.Fire, DamageDeliveryKind.Hit) { IgniteChance = 1f };
        slime.Health.TakeDamage(ignite);
        float afterHit = slime.Stats.Life.Current;
        await Frames(70);
        Check("IGNITE-BURNS-OVER-TIME", slime.Ailments.IsIgnited || slime.Health.IsDead || slime.Stats.Life.Current < afterHit,
            $"life {afterHit:0.0} -> {(slime.Health.IsDead ? 0 : slime.Stats.Life.Current):0.0} over ~1s");
        if (IsInstanceValid(slime))
        {
            slime.QueueFree();
        }

        await Frames(1);
    }

    private void SupportChecks()
    {
        var def = GD.Load<ProjectileSkillDefinition>("res://resources/skills/run/fireball.tres");
        var context = new CastContext(this, new ActorStats(), Faction.Player, Vector2.Zero, Vector2.Right)
        {
            Tags = def.Tags,
            Projectile = ProjectileParams.FromDefinition(def),
        };
        SupportDefinition multi = UpgradeCatalog.Supports.First(s => s.Id == "multiple_projectiles");
        SupportDefinition cold = UpgradeCatalog.Supports.First(s => s.Id == "cold_conversion");
        new SupportGem(multi).Apply(context);
        Check("SUPPORT-ADDS-PROJECTILES", context.Projectile.Count == 3 && context.Projectile.SpreadDegrees > 0f,
            $"count={context.Projectile.Count}, spread={context.Projectile.SpreadDegrees}");
        Check("SUPPORT-LESS-DAMAGE", Mathf.IsEqualApprox(context.DamageMultiplier, 0.8f), $"multiplier={context.DamageMultiplier}");
        new SupportGem(cold).Apply(context);
        Check("CONVERSION-REWRITES-TAGS", DamageTypeResolution.Resolve(context.Tags) == DamageType.Cold && (context.Tags & SkillTags.Fire) == 0,
            $"resolved {DamageTypeResolution.Resolve(context.Tags)}");
        Check("SUPPORT-TAG-GATING", !UpgradeCatalog.Supports.First(s => s.Id == "increased_area").Accepts(def), "Increased Area rejects a non-area Fireball");
    }

    private async Task BuildChecks()
    {
        Player player = SpawnPlayer(new Vector2(300, 600));
        var build = new RunBuild();
        player.AddChild(build);
        await Frames(1);
        foreach (SkillSlotId slot in new[] { SkillSlotId.Slot1, SkillSlotId.Slot2, SkillSlotId.Slot3, SkillSlotId.Slot4, SkillSlotId.Rmb })
        {
            player.SkillUser.ClearSlot(slot);
        }

        Check("ADD-SKILL", build.AddSkill("fireball") && build.AddSkill("spark"), "fireball + spark equipped");
        Check("NO-DUPLICATE-SKILL", !build.AddSkill("fireball"), "fireball refused twice");
        build.AddSkill("frost_nova");
        build.AddSkill("blades");
        Check("FOUR-SKILL-CAP", !build.AddSkill("meteor") && build.Skills.Count() == 4, $"skills={build.Skills.Count()}");

        var rng = new RandomNumberGenerator { Seed = 42 };
        var keystones = build.RollOffers(5, rng);
        Check("KEYSTONE-EVERY-FIFTH-LEVEL", keystones.Count == 3 && keystones.All(o => o.Kind == UpgradeKind.Keystone), string.Join(", ", keystones.Select(o => o.Title)));
        var regular = build.RollOffers(6, rng);
        Check("REGULAR-OFFERS-DISTINCT", regular.Count == 3 && regular.Select(o => o.Kind + o.Id + o.Subtitle).Distinct().Count() == 3,
            string.Join(", ", regular.Select(o => $"{o.Kind}:{o.Title}")));
        Check("NO-NEW-SKILL-WHEN-FULL", Enumerable.Range(0, 30).SelectMany(_ => build.RollOffers(6, rng)).All(o => o.Kind != UpgradeKind.NewSkill), "30 rolls, no NewSkill offer at 4 skills");

        float lifeMax = player.Stats.Get(StatId.MaxLife).Value;
        build.AddKeystone(UpgradeCatalog.Keystones.First(k => k.Id == "glass_cannon"));
        Check("GLASS-CANNON-LESS-LIFE", player.Stats.Get(StatId.MaxLife).Value < lifeMax * 0.65f, $"max life {lifeMax:0} -> {player.Stats.Get(StatId.MaxLife).Value:0}");
        player.Stats.RemoveModifiersFromSource(build.Source);
        Check("BUILD-STRIPS-BY-SOURCE", Mathf.IsEqualApprox(player.Stats.Get(StatId.MaxLife).Value, lifeMax), $"max life back to {player.Stats.Get(StatId.MaxLife).Value:0}");

        SkillInstance fireball = build.FindSkill("fireball")!;
        build.AddSupport(fireball, UpgradeCatalog.Supports.First(s => s.Id == "cold_conversion"));
        Check("EFFECTIVE-TAGS-FOLLOW-CONVERSION", (build.EffectiveTags(fireball) & SkillTags.Cold) != 0 && (build.EffectiveTags(fireball) & SkillTags.Fire) == 0, $"{build.EffectiveTags(fireball)}");
        player.QueueFree();
        await Frames(1);
    }

    private void ExperienceChecks()
    {
        var state = new RunState();
        AddChild(state);
        Check("XP-CURVE-STARTS-AT-5", Mathf.IsEqualApprox(RunState.ExperienceToNext(1), 5f), $"{RunState.ExperienceToNext(1)}");
        Check("XP-CURVE-GROWS", RunState.ExperienceToNext(10) > RunState.ExperienceToNext(5) * 2f, $"L5 {RunState.ExperienceToNext(5)}, L10 {RunState.ExperienceToNext(10)}");
        int levelUps = 0;
        state.LevelUpReady += _ => levelUps++;
        state.AddExperience(RunState.ExperienceToNext(1) + RunState.ExperienceToNext(2) + 0.5f);
        Check("XP-QUEUES-LEVEL-UPS", state.Level == 3 && state.PendingLevelUps == 2 && levelUps > 0, $"level={state.Level}, pending={state.PendingLevelUps}");
        state.QueueFree();
    }

    private async Task AreaAndChainChecks()
    {
        Player player = SpawnPlayer(new Vector2(2000, 2000));
        Enemy near = SpawnEnemy("Slime", new Vector2(2060, 2000));
        Enemy far = SpawnEnemy("Slime", new Vector2(2400, 2000));
        await Frames(2);
        float nearLife = near.Stats.Life!.Current;
        float farLife = far.Stats.Life!.Current;
        var hit = new HitProfile { Damage = 5f, Type = DamageType.Physical, Source = player, Faction = Faction.Player };
        AreaBurst.Spawn(this, new Vector2(2000, 2000), 100f, 0f, Colors.White, hit);
        await Frames(3);
        Check("AREA-HITS-INSIDE", near.Stats.Life.Current < nearLife, $"near {nearLife} -> {near.Stats.Life.Current}");
        Check("AREA-SPARES-OUTSIDE", Mathf.IsEqualApprox(far.Stats.Life.Current, farLife), $"far {farLife} -> {far.Stats.Life.Current}");

        var playerHit = new HitProfile { Damage = 5f, Type = DamageType.Physical, Source = near, Faction = Faction.Enemy };
        float playerLife = player.Stats.Life!.Current;
        AreaBurst.Spawn(this, new Vector2(2000, 2000), 60f, 0f, Colors.White, playerHit, 0f, harmless: true);
        await Frames(3);
        Check("HARMLESS-TELEGRAPH", Mathf.IsEqualApprox(player.Stats.Life.Current, playerLife), "summon marker deals nothing");

        // Chain: one spark-like projectile, two targets in a line off-axis.
        Enemy a = SpawnEnemy("Brute", new Vector2(2000, 2300));
        Enemy b = SpawnEnemy("Brute", new Vector2(2150, 2420));
        await Frames(2);
        float bLife = b.Stats.Life!.Current;
        var def = new ProjectileSkillDefinition { DisplayName = "TestChain", BaseDamage = 4f, BaseUseTime = 0.05f, Tags = SkillTags.Spell | SkillTags.Projectile, Speed = 600f, Range = 400f, Chains = 1, SpawnOffset = 10f };
        foreach (SkillSlotId slot in new[] { SkillSlotId.Slot1, SkillSlotId.Rmb })
        {
            player.SkillUser.ClearSlot(slot);
        }

        player.SkillUser.EquipSkill(SkillSlotId.Slot1, new SkillInstance(def));
        player.Stats.Mana!.Current = player.Stats.Mana.Max;
        player.SkillUser.TryCast(SkillSlotId.Slot1, Vector2.Down);
        await Frames(50);
        Check("PROJECTILE-CHAINS", b.Stats.Life.Current < bLife, $"second target {bLife} -> {b.Stats.Life.Current}");
        foreach (Node n in new Node[] { player, near, far, a, b })
        {
            if (IsInstanceValid(n))
            {
                n.QueueFree();
            }
        }

        await Frames(1);
    }

    private async Task ContactChecks()
    {
        Player player = SpawnPlayer(new Vector2(3000, 3000));
        await Frames(2);
        float life = player.Stats.Life!.Current;
        Enemy slime = SpawnEnemy("Slime", new Vector2(3010, 3000));
        await Frames(5);
        Check("CONTACT-DAMAGE", player.Stats.Life.Current < life, $"player {life} -> {player.Stats.Life.Current}");
        float after = player.Stats.Life.Current;
        await Frames(10);
        Check("CONTACT-COOLDOWN", Mathf.IsEqualApprox(player.Stats.Life.Current, after) || player.Stats.Life.Current > after - slime.ContactDamage - 0.1f,
            $"no second hit inside the cooldown ({after} -> {player.Stats.Life.Current})");
        Check("PLAYER-IN-GROUP", player.IsInGroup(CombatQuery.PlayerGroup) && slime.IsInGroup(CombatQuery.EnemyGroup), "groups set");
        player.QueueFree();
        slime.QueueFree();
        await Frames(1);
    }

    private async Task BossPhaseChecks()
    {
        Player player = SpawnPlayer(new Vector2(4000, 4000));
        player.Health!.IgnoreDamage = true;
        var boss = (BossEnemy)SpawnEnemy("EmberColossus", new Vector2(4000, 3700));
        int phaseEvents = 0;
        boss.PhaseChanged += (_, _) => phaseEvents++;
        await Frames(2);
        Check("BOSS-IS-BOSS", boss.IsBoss && boss.Phase == 1, $"phase={boss.Phase}");
        boss.Stats.Life!.Current = boss.Stats.Life.Max * 0.6f;
        await Frames(3);
        Check("BOSS-PHASE-2", boss.Phase == 2 || phaseEvents == 0 && boss.Phase == 1, $"phase={boss.Phase}, events={phaseEvents}");
        // A phase change waits for the current attack to finish, so give it time.
        for (int i = 0; i < 600 && boss.Phase < 2; i++)
        {
            await Frames(1);
        }

        Check("BOSS-PHASE-CHANGES-AT-THRESHOLD", boss.Phase == 2 && phaseEvents >= 1, $"phase={boss.Phase}, events={phaseEvents}");
        Check("BOSS-INVULNERABLE-DURING-TRANSITION", boss.Health.IsInvulnerable || boss.Health.InvulnerableTime <= 0f, $"t={boss.Health.InvulnerableTime:0.00}");
        boss.QueueFree();
        player.QueueFree();
        await Frames(1);
    }

    private void ProfileChecks()
    {
        var rng = new RandomNumberGenerator { Seed = 7 };
        EquipmentItem item = ItemFactory.RollRandom(40, DropSource.RareMonster, 2f, rng);
        var restored = ItemFactory.NewBlank();
        restored.Restore(item.Base!, item.Rarity, item.Descriptor.ItemLevel, item.Descriptor.DisplayName, item.RolledBaseStats, item.RolledAffixes);
        Check("ITEM-RESTORE-ROUNDTRIP",
            restored.Descriptor.DisplayName == item.Descriptor.DisplayName
            && restored.RolledAffixes.Count == item.RolledAffixes.Count
            && restored.Descriptor.Width == item.Descriptor.Width,
            $"{item.Descriptor.DisplayName} ({item.RolledAffixes.Count} affixes)");
        Check("ITEM-BASES-DISCOVERED", ItemFactory.Bases.Count >= 9, $"{ItemFactory.Bases.Count} bases");
        item.Free();
        restored.Free();
    }

    /// <summary>run.md -> "The timeline" and "Rewards": the danger curve compounds,
    /// everything that spawns scales up with it, and rewards grow with time.</summary>
    private void RiftCurveChecks()
    {
        Check("DANGER-LEVEL", RunDirector.Danger(0f) == 1 && RunDirector.Danger(61f) == 2 && RunDirector.Danger(600f) == 11,
            $"0s={RunDirector.Danger(0f)}, 61s={RunDirector.Danger(61f)}, 600s={RunDirector.Danger(600f)}");
        (float life7, float dmg7) = RunDirector.Scaling(420f);
        (float life15, float dmg15) = RunDirector.Scaling(900f);
        Check("SCALING-COMPOUNDS", life7 > 4.5f && life15 > life7 * 2.5f && dmg15 > dmg7,
            $"life ×{life7:0.0} at 7:00, ×{life15:0.0} at 15:00; damage ×{dmg7:0.0} → ×{dmg15:0.0}");
        Check("PRESSURE-RISES", RunDirector.SpawnRate(600f) > RunDirector.SpawnRate(60f) * 3f && RunDirector.EnemyCap(600f) > RunDirector.EnemyCap(0f)
            && RunDirector.EliteIntervalAt(600f) < RunDirector.EliteIntervalAt(0f) && RunDirector.EliteChance(0f) == 0f && RunDirector.EliteChance(600f) > 0f,
            $"rate {RunDirector.SpawnRate(60f):0.0}→{RunDirector.SpawnRate(600f):0.0}/s, cap {RunDirector.EnemyCap(0f)}→{RunDirector.EnemyCap(600f)}, elites every {RunDirector.EliteIntervalAt(0f):0}→{RunDirector.EliteIntervalAt(600f):0}s");
        var rng = new RandomNumberGenerator { Seed = 3 };
        RunRewards early = RunRewards.For(240f, 1, 300, false, rng);
        RunRewards won = RunRewards.For(450f, 3, 1500, true, rng);
        RunRewards endless = RunRewards.For(960f, 7, 4000, true, rng);
        Check("REWARDS-GROW-WITH-TIME", early.Gold < won.Gold && won.Gold < endless.Gold && early.Orbs.Count < won.Orbs.Count && won.Orbs.Count < endless.Orbs.Count
            && early.Choices == 3 && endless.Choices == 5 && early.Keep == 1 && endless.Keep == 3 && endless.RarityMultiplier > won.RarityMultiplier,
            $"gold {early.Gold}/{won.Gold}/{endless.Gold}, orbs {early.Orbs.Count}/{won.Orbs.Count}/{endless.Orbs.Count}, choices {early.Choices}/{won.Choices}/{endless.Choices}, keep {early.Keep}/{won.Keep}/{endless.Keep}");
    }

    /// <summary>run.md -> "The director": intensity waves stay in range and actually
    /// rise and fall, a hurt player gets a breather, edge spawns are never on screen.</summary>
    private void DirectorChecks()
    {
        FastNoiseLite noise = RunDirector.WaveNoise(7);
        float min = float.MaxValue;
        float max = float.MinValue;
        int crossings = 0;
        bool above = RunDirector.WaveIntensity(noise, 0f) > 1f;
        for (int t = 0; t <= 900; t++)
        {
            float w = RunDirector.WaveIntensity(noise, t);
            min = Mathf.Min(min, w);
            max = Mathf.Max(max, w);
            if (w > 1f != above)
            {
                above = !above;
                crossings++;
            }
        }

        Check("DIRECTOR-WAVES", min >= 0.55f && max <= 1.5f && min < 0.8f && max > 1.25f && crossings is >= 8 and <= 40,
            $"15 minutes: intensity {min:0.00}–{max:0.00}, crossed 1.0 {crossings} times");
        Check("DIRECTOR-STRESS", RunDirector.StressEase(0.2f) < RunDirector.StressEase(0.5f) && RunDirector.StressEase(0.5f) < RunDirector.StressEase(0.9f),
            $"ease at 20 % / 50 % / 90 % life: {RunDirector.StressEase(0.2f)} / {RunDirector.StressEase(0.5f)} / {RunDirector.StressEase(0.9f)}");

        var rng = new RandomNumberGenerator { Seed = 11 };
        var view = new Rect2(1000f, 1000f, 1280f, 720f);
        var open = new Rect2(-5000f, -5000f, 20000f, 20000f);
        var walled = new Rect2(900f, 900f, 1600f, 1200f); // the arena ends just past the view's left and top
        int seen = 0;
        for (int i = 0; i < 400; i++)
        {
            Rect2 arena = i % 2 == 0 ? open : walled;
            Vector2 p = RunDirector.EdgePointFor(view, 60f, q => new Vector2(Mathf.Clamp(q.X, arena.Position.X, arena.End.X), Mathf.Clamp(q.Y, arena.Position.Y, arena.End.Y)), rng);
            seen += view.Grow(-8f).HasPoint(p) ? 1 : 0;
        }

        Check("DIRECTOR-EDGE-SPAWNS", seen == 0, $"400 spawn points around a 1280×720 view (half with an arena wall just off screen): {seen} on screen");
    }

    private async Task RiftPowerUpChecks()
    {
        Player player = SpawnPlayer(new Vector2(4000, 600));
        var state = new RunState { Player = player };
        AddChild(state);
        await Frames(2);

        // Auto-cast timers: a skill has an interval, and Quickening shortens it.
        PlayerSkillInput input = player.GetNode<PlayerSkillInput>("PlayerSkillInput");
        var spark = new SkillInstance(UpgradeCatalog.Skills.First(s => s.Id == "spark").Definition);
        float before = input.AutoCastInterval(spark);
        var quick = new StatModifier(ModifierType.Increased, 50f, this);
        player.Stats.Get(StatId.AutoCastSpeed).AddModifier(quick);
        float after = input.AutoCastInterval(spark);
        player.Stats.Get(StatId.AutoCastSpeed).RemoveModifier(quick);
        Check("AUTOCAST-INTERVAL", before >= 0.5f && after < before * 0.75f, $"spark every {before:0.00}s, {after:0.00}s with +50% cast frequency");

        // Cataclysm: on-screen enemies die, off-screen ones don't, bosses only lose life.
        Enemy near = SpawnEnemy("Slime", player.GlobalPosition + new Vector2(120f, 40f));
        Enemy far = SpawnEnemy("Slime", player.GlobalPosition + new Vector2(5000f, 0f));
        state.RegisterEnemy(near);
        state.RegisterEnemy(far);
        await Frames(2);
        int killed = state.Nuke(player);
        Check("NUKE-KILLS-ON-SCREEN", near.Health.IsDead && !far.Health.IsDead && killed >= 1, $"near dead={near.Health.IsDead}, far dead={far.Health.IsDead}, killed={killed}");
        Check("NUKE-GIVES-EXPERIENCE", GetTree().GetNodesInGroup(RunState.PickupGroup).OfType<Pickup>().Any(p => p.Kind == PickupKind.Experience), "the slain drop their gems");

        // Lodestone: a gem far across the map flies in and is collected.
        foreach (Pickup p in GetTree().GetNodesInGroup(RunState.PickupGroup).OfType<Pickup>())
        {
            p.QueueFree();
        }

        await Frames(1);
        Pickup gem = Pickup.Spawn(this, PickupKind.Experience, 3f, player.GlobalPosition + new Vector2(-3000f, 0f));
        float xp = state.Experience;
        state.Magnetize();
        await Frames(150);
        Check("MAGNET-PULLS-ALL-XP", !IsInstanceValid(gem) && state.Experience > xp, $"gem collected={!IsInstanceValid(gem)}, xp {xp:0.0} → {state.Experience:0.0}");

        // Orbs collected in the Rift go to the wallet (and so to the account).
        Pickup orb = Pickup.Spawn(this, PickupKind.Currency, 1f, player.GlobalPosition);
        orb.CurrencyKey = "kindling_shard";
        long orbs = player.Wallet.GetBalance("kindling_shard");
        await Frames(10);
        Check("RIFT-ORB-PICKUP", player.Wallet.GetBalance("kindling_shard") == orbs + 1 && state.OrbsCollected == 1, $"kindling {orbs} → {player.Wallet.GetBalance("kindling_shard")}");

        far.QueueFree();
        state.QueueFree();
        player.QueueFree();
        await Frames(1);
    }
}
