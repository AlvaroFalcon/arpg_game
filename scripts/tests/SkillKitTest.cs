#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Progression;
using ArpgGame.Skills;
using ArpgGame.Stats;

namespace ArpgGame.Tests;

/// <summary>
/// The skill kit of 2026-11-01 (docs/design/skills.md -> "Auras, totems and stacks"), cast
/// for real through <see cref="SkillUser"/> at target dummies: R and T are skill buttons and
/// Space takes a movement skill; bleed and ignite stack (99, each with its own timer); Douse
/// oils, Combust and Exsanguinate cash the stacks in; the seven auras grant their passive
/// while equipped and their active on use (Immolation toggles and drains); Titan stops
/// knockback; a totem casts the skill socketed into it; Blink and Leap Slam move the
/// player; and the owner's sheet changes (Fireball, Frost Nova, Curse of the World). Run:
///   ./scripts/run_headless.sh res://scenes/tests/SkillKitTest.tscn --quit-after 6000
/// </summary>
public partial class SkillKitTest : Node2D
{
    private bool _failed;
    private Player _player = null!;
    private SkillUser _skills = null!;
    private readonly List<string> _lines = new();

    public override void _Ready()
    {
        _player = GetNode<Player>("Player");
        _skills = _player.GetNode<SkillUser>("SkillUser");
        _ = Run();
    }

    private void Check(string name, bool ok, string detail)
    {
        _failed |= !ok;
        string line = $"[SKILLKIT] {name}: {(ok ? "PASS" : "FAIL")} — {detail}";
        _lines.Add(line);
        GD.Print(line);
    }

    private async Task Seconds(float s) => await ToSignal(GetTree().CreateTimer(s, processInPhysics: true), SceneTreeTimer.SignalName.Timeout);

    private readonly Dictionary<ulong, float> _taken = new();

    /// <summary>A dummy that counts the damage it takes, with ailments (dummies have none by default).</summary>
    private Node2D Dummy(Vector2 at, bool immortal = true)
    {
        var dummy = GD.Load<PackedScene>("res://scenes/combat/TargetDummy.tscn").Instantiate<TargetDummy>();
        dummy.Immortal = immortal;
        AddChild(dummy);
        dummy.GlobalPosition = at;
        dummy.AddChild(new Ailments { Name = "Ailments" });
        ulong id = dummy.GetInstanceId();
        _taken[id] = 0f;
        dummy.GetNode<Health>("Health").Damaged += amount => _taken[id] += amount;
        return dummy;
    }

    private float Missing(Node2D n) => _taken.TryGetValue(n.GetInstanceId(), out float t) ? t : 0f;

    private static Ailments Ail(Node2D n) => n.GetNode<Ailments>("Ailments");

    private SkillInstance Equip(string id, SkillSlotId slot = SkillSlotId.Slot1)
    {
        var instance = new SkillInstance(UpgradeCatalog.FindSkill(id)!.Definition);
        _skills.EquipSkill(slot, instance);
        return instance;
    }

    private void Ready(SkillInstance i)
    {
        i.UseTimeRemaining = 0f;
        i.ChargesAvailable = i.Definition.MaxCharges;
        i.RechargeTimer = 0f;
        if (_player.Stats.Mana is { } mana)
        {
            mana.Current = mana.Max;
        }
    }

    private async Task Run()
    {
        await Seconds(0.2f);
        _player.Stats.Get(StatId.MaxMana).AddModifier(new StatModifier(ModifierType.Flat, 2000f, "test"));
        _player.Stats.Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.Flat, 900f, "test"));

        SheetChecks();
        SlotChecks();
        await StackChecks();
        await AfflictionChecks();
        await AuraChecks();
        await ImmolationChecks();
        await TitanChecks();
        await TotemChecks();
        await MovementChecks();

        string verdict = _failed ? "[SKILLKIT] === RESULT: FAIL ===" : "[SKILLKIT] === RESULT: PASS ===";
        _lines.Add(verdict);
        GD.Print(verdict);
        DirAccess.MakeDirRecursiveAbsolute("user://test_results");
        using (var f = FileAccess.Open("user://test_results/SkillKitTest.txt", FileAccess.ModeFlags.Write))
        {
            f?.StoreString(string.Join("\n", _lines) + "\n");
        }

        GetTree().Quit();
    }

    // --- the owner's sheet ------------------------------------------------------------------

    private void SheetChecks()
    {
        var fireball = (ProjectileSkillDefinition)UpgradeCatalog.FindSkill("fireball")!.Definition;
        var nova = (AreaSkillDefinition)UpgradeCatalog.FindSkill("frost_nova")!.Definition;
        SkillDefinition spark = UpgradeCatalog.FindSkill("spark")!.Definition;
        SkillDefinition world = UpgradeCatalog.FindSkill("curse_time")!.Definition;
        SkillDefinition slam = UpgradeCatalog.FindSkill("ground_slam")!.Definition;
        Check("SHEET-CHANGES", fireball.ExplodeRadius == 0f && nova.Knockback == 0f && Mathf.IsEqualApprox(spark.Costs[0].Amount, 7f)
                && world.DisplayName == "Curse of the World" && slam is MovementSkillDefinition { Kind: MovementKind.Leap },
            $"fireball burst {fireball.ExplodeRadius} px, nova knockback {nova.Knockback}, spark mana {spark.Costs[0].Amount}, curse_time is \"{world.DisplayName}\", ground_slam is {slam.GetType().Name}");

        // The burst and the push are tree nodes now.
        var ctx = new CastContext(null!, null!, Faction.Player, Vector2.Zero, Vector2.Right) { Projectile = ProjectileParams.FromDefinition(fireball) };
        SkillTrees.For("fireball")!.Node("blast")!.Apply(ctx, 1);
        var novaCtx = new CastContext(null!, null!, Faction.Player, Vector2.Zero, Vector2.Right);
        SkillTrees.For("frost_nova")!.Node("knock")!.Apply(novaCtx, 3);
        Check("SHEET-NODES", ctx.Projectile.ExplodeRadius > 0f && ctx.Projectile.ExplodeDamageFraction > 0f && novaCtx.ExtraKnockback > 0f,
            $"Impact Burst rank 1: {ctx.Projectile.ExplodeRadius:0} px at {ctx.Projectile.ExplodeDamageFraction:P0}; Gale rank 3: +{novaCtx.ExtraKnockback:0} knockback");

        string[] fresh = { "blink", "immolation", "elemental_ward", "elemental_storm", "precision", "bloodthirst", "titan", "vitality", "totem", "douse", "combust", "exsanguinate" };
        var missing = fresh.Where(id => UpgradeCatalog.FindSkill(id)?.Definition is not { Icon: not null } || SkillTrees.For(id) is null || Characters.GemShop.Find(id, false) is null).ToList();
        var inRift = fresh.Where(id => id is "blink" or "totem" or "immolation" or "precision" && SkillRules.InRift(UpgradeCatalog.FindSkill(id)!.Definition)).ToList();
        Check("NEW-SKILLS-LISTED", missing.Count == 0 && inRift.Count == 0,
            $"{fresh.Length} new skills; missing a definition/icon/tree/shop entry: [{string.Join(",", missing)}]; wrongly offered in the Rift: [{string.Join(",", inRift)}]");
    }

    private void SlotChecks()
    {
        bool r = InputMap.HasAction("skill_slot_3") && InputMap.ActionGetEvents("skill_slot_3").OfType<InputEventKey>().Any(k => k.PhysicalKeycode == Key.R);
        bool t = InputMap.HasAction("skill_slot_4") && InputMap.ActionGetEvents("skill_slot_4").OfType<InputEventKey>().Any(k => k.PhysicalKeycode == Key.T);
        var bound = PlayerSkillInput.BoundSlots.ToList();
        Check("SLOTS-R-T-SPACE", r && t && bound.Contains(SkillSlotId.Slot3) && bound.Contains(SkillSlotId.Slot4) && bound.Count == 6 && SkillUser.SlotCount == 7
                && SkillRules.FitsSpace(UpgradeCatalog.FindSkill("blink")!.Definition) && !SkillRules.FitsSpace(UpgradeCatalog.FindSkill("fireball")!.Definition),
            $"R bound={r}, T bound={t}, {bound.Count} bound buttons, {SkillUser.SlotCount} slots; Space takes Blink, refuses Fireball");
    }

    // --- stacking bleed and ignite ----------------------------------------------------------

    private async Task StackChecks()
    {
        Vector2 at = new(2000f, 0f);
        _player.GlobalPosition = at + new Vector2(0f, -600f);
        Node2D dummy = Dummy(at);
        await Seconds(0.1f);
        Ailments ail = Ail(dummy);
        Health health = dummy.GetNode<Health>("Health");
        for (int i = 0; i < 30; i++)
        {
            health.TakeDamage(new DamagePayload(20f, DamageType.Physical, _player, Faction.Player, SkillTags.Attack, DamageDeliveryKind.Hit) { BleedChance = 1f });
        }

        int thirty = ail.BleedStacks;
        await Seconds(0.6f);
        // A second wave later: each stack keeps its own timer, so the first wave runs out first.
        for (int i = 0; i < 10; i++)
        {
            health.TakeDamage(new DamagePayload(20f, DamageType.Physical, _player, Faction.Player, SkillTags.Attack, DamageDeliveryKind.Hit) { BleedChance = 1f, BleedStacks = 2 });
        }

        int fifty = ail.BleedStacks;
        float ticked = Missing(dummy);
        await Seconds(Ailments.BleedDuration - 0.4f);
        int afterFirstWave = ail.BleedStacks;
        Check("BLEED-STACKS", thirty == 30 && fifty == 50 && afterFirstWave == 20 && ticked > 0f,
            $"30 hits → {thirty} stacks; 10 more hits at 2 stacks each → {fifty}; when the first wave ran out {afterFirstWave} were left (20 expected); damage so far {ticked:0}");

        for (int i = 0; i < 150; i++)
        {
            health.TakeDamage(new DamagePayload(5f, DamageType.Physical, _player, Faction.Player, SkillTags.Attack, DamageDeliveryKind.Hit) { BleedChance = 1f });
        }

        Check("BLEED-CAP-99", ail.BleedStacks == Ailments.MaxStacks, $"150 more bleeding hits → {ail.BleedStacks} stacks (cap {Ailments.MaxStacks})");

        for (int i = 0; i < 6; i++)
        {
            ail.ApplyIgnite(60f, _player);
        }

        Check("IGNITE-STACKS", ail.IgniteStacks == 6 && ail.IsIgnited, $"six ignites → {ail.IgniteStacks} stacks (they used to replace each other)");
        dummy.QueueFree();
    }

    // --- Douse, Combust, Exsanguinate -------------------------------------------------------

    private async Task AfflictionChecks()
    {
        Vector2 at = new(4000f, 0f);
        _player.GlobalPosition = at;
        Node2D dummy = Dummy(at + new Vector2(200f, 0f));
        await Seconds(0.1f);
        Ailments ail = Ail(dummy);
        Health health = dummy.GetNode<Health>("Health");

        // Without oil, a fire hit with no ignite chance does not ignite.
        _taken[dummy.GetInstanceId()] = 0f;
        health.TakeDamage(new DamagePayload(100f, DamageType.Fire, _player, Faction.Player, SkillTags.Spell | SkillTags.Fire, DamageDeliveryKind.Hit));
        float dry = Missing(dummy);
        bool dryIgnite = ail.IsIgnited;

        SkillInstance douse = Equip("douse");
        Ready(douse);
        CastFailure? fail = _skills.TryCast(SkillSlotId.Slot1, Vector2.Right, dummy.GlobalPosition);
        await Seconds(0.05f);
        _taken[dummy.GetInstanceId()] = 0f;
        health.TakeDamage(new DamagePayload(100f, DamageType.Fire, _player, Faction.Player, SkillTags.Spell | SkillTags.Fire, DamageDeliveryKind.Hit));
        float wet = Missing(dummy);
        Check("DOUSE-OILS", fail is null && ail.IsOiled && !dryIgnite && ail.IsIgnited && wet > dry * 1.2f,
            $"cast={fail?.ToString() ?? "ok"}; oiled={ail.IsOiled}; a fire hit took {dry:0} dry (ignited={dryIgnite}), {wet:0} oiled (ignited={ail.IsIgnited})");

        for (int i = 0; i < 9; i++)
        {
            ail.ApplyIgnite(90f, _player);
        }

        int before = ail.IgniteStacks;
        SkillInstance combust = Equip("combust", SkillSlotId.Slot2);
        Ready(combust);
        _taken[dummy.GetInstanceId()] = 0f;
        _skills.TryCast(SkillSlotId.Slot2, Vector2.Right, dummy.GlobalPosition);
        await Seconds(0.05f);
        float burst = Missing(dummy);
        Check("COMBUST-DETONATES", before >= 10 && ail.IgniteStacks == 0 && burst > 9f * 90f * 0.9f,
            $"{before} ignite stacks → {ail.IgniteStacks} after Combust; the burst dealt {burst:0} at once (9 stacks of 90 left ≈ 810, +30%)");

        for (int i = 0; i < 20; i++)
        {
            ail.AddBleed(40f, Ailments.BleedDuration, _player);
        }

        SkillInstance ex = Equip("exsanguinate", SkillSlotId.Slot3);
        Ready(ex);
        _taken[dummy.GetInstanceId()] = 0f;
        _skills.TryCast(SkillSlotId.Slot3, Vector2.Right, dummy.GlobalPosition);
        await Seconds(0.05f);
        float blood = Missing(dummy);
        Check("EXSANGUINATE-DETONATES", ail.BleedStacks == 0 && blood > 20f * 40f,
            $"20 bleed stacks of 40 → {ail.BleedStacks} left; the burst dealt {blood:0} (800 stored, +30%)");
        _skills.ClearSlot(SkillSlotId.Slot1);
        _skills.ClearSlot(SkillSlotId.Slot2);
        _skills.ClearSlot(SkillSlotId.Slot3);
        dummy.QueueFree();
    }

    // --- auras -----------------------------------------------------------------------------

    private async Task AuraChecks()
    {
        ActorStats stats = _player.Stats;
        float fire = stats.FireResistance.Value;
        SkillInstance ward = Equip("elemental_ward", SkillSlotId.Slot4);
        float passive = stats.FireResistance.Value;
        Ready(ward);
        _skills.TryCast(SkillSlotId.Slot4, Vector2.Right);
        float active = stats.FireResistance.Value;
        bool lit = _skills.Auras.IsActive(ward);
        await Seconds(6.3f);
        float after = stats.FireResistance.Value;
        _skills.ClearSlot(SkillSlotId.Slot4);
        float removed = stats.FireResistance.Value;
        Check("AURA-WARD", Mathf.IsEqualApprox(passive, fire + 10f) && Mathf.IsEqualApprox(active, fire + 50f) && lit && Mathf.IsEqualApprox(after, fire + 10f) && Mathf.IsEqualApprox(removed, fire),
            $"fire resistance {fire:0} → equipped {passive:0} → active {active:0} → after 6 s {after:0} → unequipped {removed:0}");

        // The tree: Warding rank 3 is +25 more, the owner's 5 / 10 / 25.
        var gemWard = new SkillInstance(UpgradeCatalog.FindSkill("elemental_ward")!.Definition);
        gemWard.TreeModifiers.AddRange(SkillTrees.ModifiersFor("elemental_ward", new Dictionary<string, int> { ["warding"] = 3 }));
        _skills.EquipSkill(SkillSlotId.Slot4, gemWard);
        float treed = stats.FireResistance.Value;
        _skills.ClearSlot(SkillSlotId.Slot4);
        Check("AURA-TREE", Mathf.IsEqualApprox(treed, fire + 35f), $"Warding 3 makes the passive +{treed - fire:0} (10 + 25)");

        float crit = stats.CritChance.Value;
        Equip("precision", SkillSlotId.Slot4);
        float critOn = stats.CritChance.Value;
        Equip("bloodthirst", SkillSlotId.Slot3);
        float bleed = stats.Get(StatId.BleedChance).Value;
        Equip("vitality", SkillSlotId.Slot2);
        float life = stats.Life!.Max;
        _skills.ClearSlot(SkillSlotId.Slot2);
        float lifeOff = stats.Life.Max;
        Check("AURA-PASSIVES", critOn > crit + 0.02f && bleed >= 0.15f && life > lifeOff * 1.1f,
            $"Precision: crit {crit:P0} → {critOn:P0}; Bloodthirst: physical attacks bleed {bleed:P0}; Vitality: max life {lifeOff:0} → {life:0}");

        SkillInstance vit = Equip("vitality", SkillSlotId.Slot2);
        stats.Life.Current = stats.Life.Max * 0.4f;
        float low = stats.Life.Current;
        float taken = stats.Get(StatId.DamageTaken).Value;
        Ready(vit);
        _skills.TryCast(SkillSlotId.Slot2, Vector2.Right);
        Check("AURA-VITALITY-ACTIVE", stats.Life.Current > low + stats.Life.Max * 0.2f && stats.Get(StatId.DamageTaken).Value < taken * 0.75f,
            $"life {low:0} → {stats.Life.Current:0} of {stats.Life.Max:0}; damage taken ×{taken:0.00} → ×{stats.Get(StatId.DamageTaken).Value:0.00}");

        SkillInstance storm = Equip("elemental_storm", SkillSlotId.Slot1);
        storm.TreeModifiers.AddRange(SkillTrees.ModifiersFor("elemental_storm", new Dictionary<string, int> { ["embers"] = 2, ["wildfire"] = 2, ["fire_focus"] = 1 }));
        _skills.EquipSkill(SkillSlotId.Slot1, storm);
        float fireDmg = Stat.ComputeFormula(100f, stats.GetModifiers(new ModifierQuery(StatId.Damage, SkillTags.Fire | SkillTags.Spell)), 1f);
        float coldDmg = Stat.ComputeFormula(100f, stats.GetModifiers(new ModifierQuery(StatId.Damage, SkillTags.Cold | SkillTags.Spell)), 1f);
        Check("AURA-STORM-FOCUS", fireDmg > coldDmg + 30f, $"focused on fire: a 100 fire hit becomes {fireDmg:0}, a 100 cold hit {coldDmg:0}");

        foreach (SkillSlotId slot in new[] { SkillSlotId.Slot1, SkillSlotId.Slot2, SkillSlotId.Slot3, SkillSlotId.Slot4 })
        {
            _skills.ClearSlot(slot);
        }

        Check("AURA-CLEARED", _skills.Auras.PassiveCount == 0 && Mathf.IsEqualApprox(stats.CritChance.Value, crit) && Mathf.IsEqualApprox(stats.Get(StatId.DamageTaken).Value, 1f),
            $"passives left {_skills.Auras.PassiveCount}; crit back to {stats.CritChance.Value:P0}; damage taken ×{stats.Get(StatId.DamageTaken).Value:0.00}");
    }

    private async Task ImmolationChecks()
    {
        Vector2 at = new(6000f, 0f);
        _player.GlobalPosition = at;
        Node2D near = Dummy(at + new Vector2(60f, 0f));
        Node2D far = Dummy(at + new Vector2(400f, 0f));
        await Seconds(0.1f);
        SkillInstance immo = Equip("immolation");
        Ready(immo);
        ResourcePool life = _player.Stats.Life!;
        life.Current = life.Max;
        float start = life.Current;
        _skills.TryCast(SkillSlotId.Slot1, Vector2.Right);
        bool on = _skills.Auras.IsActive(immo);
        await Seconds(2.1f);
        float drained = start - life.Current;
        Ready(immo);
        _skills.TryCast(SkillSlotId.Slot1, Vector2.Right);
        bool off = !_skills.Auras.IsActive(immo);
        float burnt = Missing(near);
        await Seconds(1.1f);
        bool stopped = Mathf.IsEqualApprox(burnt, Missing(near));
        Check("IMMOLATION-TOGGLE", on && off && burnt > 0f && Missing(far) == 0f && drained > life.Max * 0.02f && stopped,
            $"on={on}, off again={off}; the near dummy took {burnt:0}, the far one {Missing(far):0}; drained {drained:0} life in 2 s ({life.Max * 0.04f:0} expected); burning stopped={stopped}");
        _skills.ClearSlot(SkillSlotId.Slot1);
        near.QueueFree();
        far.QueueFree();
    }

    private async Task TitanChecks()
    {
        Vector2 at = new(8000f, 0f);
        _player.GlobalPosition = at;
        await Seconds(0.1f);
        _player.Health!.TakeDamage(new DamagePayload(1f, DamageType.Physical, null, Faction.Enemy, SkillTags.Melee, DamageDeliveryKind.Hit) { Knockback = new Vector2(400f, 0f) });
        await Seconds(0.4f);
        float pushed = _player.GlobalPosition.X - at.X;

        _player.GlobalPosition = at;
        SkillInstance titan = Equip("titan", SkillSlotId.Slot2);
        Ready(titan);
        _skills.TryCast(SkillSlotId.Slot2, Vector2.Right);
        await Seconds(0.05f);
        _player.Health.TakeDamage(new DamagePayload(1f, DamageType.Physical, null, Faction.Enemy, SkillTags.Melee, DamageDeliveryKind.Hit) { Knockback = new Vector2(400f, 0f) });
        await Seconds(0.4f);
        float held = _player.GlobalPosition.X - at.X;
        Check("TITAN-UNSTOPPABLE", pushed > 30f && Mathf.Abs(held) < 2f && _player.Stats.Armour.Value > 40f,
            $"a heavy hit pushed the player {pushed:0} px; with Titan's active {held:0} px; armour {_player.Stats.Armour.Value:0}");
        _skills.ClearSlot(SkillSlotId.Slot2);
    }

    private async Task TotemChecks()
    {
        Vector2 at = new(10000f, 0f);
        _player.GlobalPosition = at;
        Node2D dummy = Dummy(at + new Vector2(300f, 0f));
        await Seconds(0.1f);
        SkillInstance totem = Equip("totem");
        Ready(totem);
        CastFailure? empty = _skills.TryCast(SkillSlotId.Slot1, Vector2.Right, at + new Vector2(120f, 0f));
        totem.Socketed = new SkillInstance(UpgradeCatalog.FindSkill("frost_shards")!.Definition);
        totem.TreeModifiers.AddRange(SkillTrees.ModifiersFor("totem", new Dictionary<string, int> { ["quickened"] = 2, ["lingering"] = 2, ["ancestral_ring"] = 1 }));
        Ready(totem);
        CastFailure? placed = _skills.TryCast(SkillSlotId.Slot1, Vector2.Right, at + new Vector2(120f, 0f));
        Ready(totem);
        _skills.TryCast(SkillSlotId.Slot1, Vector2.Right, at + new Vector2(120f, 60f));
        await Seconds(2.5f);
        var totems = GetTree().GetNodesInGroup(Totem.Group).OfType<Totem>().ToList();
        int casts = totems.Sum(t => t.Casts);
        Check("TOTEM-CASTS", empty is not null && placed is null && totems.Count == 2 && casts >= 3 && Missing(dummy) > 0f,
            $"an empty totem is refused={empty is not null}; with Frost Shards socketed: {totems.Count} totems up (Ancestral Ring: 2), {casts} casts in 2.5 s, the dummy took {Missing(dummy):0}");
        await Seconds(10f);
        Check("TOTEM-EXPIRES", !GetTree().GetNodesInGroup(Totem.Group).OfType<Totem>().Any(t => !t.IsQueuedForDeletion()), "every totem is gone after its duration");
        _skills.ClearSlot(SkillSlotId.Slot1);
        dummy.QueueFree();
    }

    private async Task MovementChecks()
    {
        Vector2 at = new(12000f, 0f);
        _player.GlobalPosition = at;
        await Seconds(0.1f);
        Dodge dodge = _player.Dodge!;
        int charges = dodge.Charges;
        SkillInstance blink = Equip("blink", SkillSlotId.Space);
        Ready(blink);
        dodge.SpacePressed(Vector2.Right, at + new Vector2(600f, 0f));
        await Seconds(0.3f);
        float moved = _player.GlobalPosition.X - at.X;
        Check("BLINK-ON-SPACE", moved > 200f && moved < 250f && dodge.Charges == charges,
            $"Space with Blink moved the player {moved:0} px (range 230) and spent {charges - dodge.Charges} dodge charges");

        at = _player.GlobalPosition;
        Node2D dummy = Dummy(at + new Vector2(200f, 0f));
        await Seconds(0.1f);
        SkillInstance leap = Equip("ground_slam", SkillSlotId.Space);
        Ready(leap);
        dodge.SpacePressed(Vector2.Right, at + new Vector2(200f, 0f));
        bool untouchable = _player.Health!.IsInvulnerable;
        await Seconds(0.7f);
        float jumped = _player.GlobalPosition.X - at.X;
        Check("LEAP-SLAM", jumped > 120f && Missing(dummy) > 0f && untouchable,
            $"leapt {jumped:0} px, untouchable in the air={untouchable}, the dummy at the landing took {Missing(dummy):0}");

        _skills.ClearSlot(SkillSlotId.Space);
        _player.GlobalPosition = at;
        dodge.SpacePressed(Vector2.Left, at + new Vector2(-300f, 0f));
        Check("SPACE-EMPTY-ROLLS", dodge.IsDashing && dodge.Charges == charges - 1, $"with Space empty, Space rolls (charges {charges} → {dodge.Charges})");
        dummy.QueueFree();
        await Seconds(0.3f);
    }
}
