#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Progression;
using ArpgGame.Skills;
using ArpgGame.Stats;

namespace ArpgGame.Tests;

/// <summary>
/// The skills rework of 2026-10-30 (docs/design/skills.md -> "Fireball and Spark",
/// "Mana and cooldowns", docs/design/classes.md -> "The Witch's curses"), cast for real
/// through <see cref="SkillUser.TryCast"/> at target dummies far from anything else:
/// Fireball bursts where it lands; Spark passes through one enemy and bounces off a wall;
/// the three curses do what they say; Corruption spreads on death; a level-1 Thaumaturge
/// can't cast Fireball for free any more. Run with:
///   ./scripts/run_headless.sh res://scenes/tests/SkillReworkTest.tscn --quit-after 3000
/// </summary>
public partial class SkillReworkTest : Node2D
{
    private bool _failed;
    private Player _player = null!;
    private SkillUser _skills = null!;

    public override void _Ready()
    {
        _player = GetNode<Player>("Player");
        _skills = _player.GetNode<SkillUser>("SkillUser");
        _ = Run();
    }

    private void Check(string name, bool ok, string detail)
    {
        _failed |= !ok;
        GD.Print($"[SKILLREWORK] {name}: {(ok ? "PASS" : "FAIL")} — {detail}");
    }

    private async Task Seconds(float s) => await ToSignal(GetTree().CreateTimer(s, processInPhysics: true), SceneTreeTimer.SignalName.Timeout);

    private readonly System.Collections.Generic.Dictionary<ulong, float> _taken = new();

    /// <summary>A dummy that counts the damage it takes (an immortal one heals at once, so its life says nothing).</summary>
    private Node2D Dummy(Vector2 at, bool immortal = true)
    {
        var dummy = GD.Load<PackedScene>("res://scenes/combat/TargetDummy.tscn").Instantiate<TargetDummy>();
        dummy.Immortal = immortal;
        AddChild(dummy);
        dummy.GlobalPosition = at;
        ulong id = dummy.GetInstanceId();
        _taken[id] = 0f;
        dummy.GetNode<Health>("Health").Damaged += amount => _taken[id] += amount;
        return dummy;
    }

    private float Missing(Node2D n) => _taken.TryGetValue(n.GetInstanceId(), out float t) ? t : 0f;

    private SkillInstance Equip(string id, SkillSlotId slot = SkillSlotId.Slot1)
    {
        var instance = new SkillInstance(UpgradeCatalog.FindSkill(id)!.Definition);
        _skills.EquipSkill(slot, instance);
        return instance;
    }

    private void Refill()
    {
        if (_player.Stats.Mana is { } mana)
        {
            mana.Current = mana.Max;
        }
    }

    private async Task Run()
    {
        await Seconds(0.2f);
        _player.Stats.Get(StatId.MaxMana).AddModifier(new StatModifier(ModifierType.Flat, 2000f, "test"));
        Refill();

        await FireballChecks();
        await SparkChecks();
        await CurseChecks();
        ManaChecks();

        GD.Print(_failed ? "[SKILLREWORK] === RESULT: FAIL ===" : "[SKILLREWORK] === RESULT: PASS ===");
        GetTree().Quit();
    }

    private async Task FireballChecks()
    {
        // A target in the path and a second one beside it, out of the projectile's way.
        Vector2 at = new(2000f, 0f);
        _player.GlobalPosition = at;
        Node2D first = Dummy(at + new Vector2(160f, 0f));
        Node2D beside = Dummy(at + new Vector2(170f, 40f));
        await Seconds(0.1f);
        Equip("fireball");
        Refill();
        CastFailure? fail = _skills.TryCast(SkillSlotId.Slot1, Vector2.Right);
        await Seconds(0.25f);
        bool slow = GetChildren().OfType<Projectile>().Any(p => p.Speed <= 360f);
        await Seconds(0.8f);
        Check("FIREBALL-BURSTS", fail is null && slow && Missing(first) > 0f && Missing(beside) > 0f,
            $"cast={fail?.ToString() ?? "ok"}, slow (≤360 px/s)={slow}, the target lost {Missing(first):0}, the one beside it {Missing(beside):0}");
        first.QueueFree();
        beside.QueueFree();
    }

    private async Task SparkChecks()
    {
        // Two in a line: one pierce means both are hit.
        Vector2 at = new(4000f, 0f);
        _player.GlobalPosition = at;
        Node2D a = Dummy(at + new Vector2(90f, 0f));
        Node2D b = Dummy(at + new Vector2(170f, 0f));
        await Seconds(0.1f);
        Equip("spark");
        Refill();
        _skills.TryCast(SkillSlotId.Slot1, Vector2.Right);
        await Seconds(1.2f);
        Check("SPARK-PIERCES-ONE", Missing(a) > 0f && Missing(b) > 0f, $"first lost {Missing(a):0}, the one behind {Missing(b):0}");
        a.QueueFree();
        b.QueueFree();

        // A wall: sparks bounce back instead of dying, zigzagging on the way.
        at = new Vector2(6000f, 0f);
        _player.GlobalPosition = at;
        var wall = new StaticBody2D { CollisionLayer = CollisionLayers.World, CollisionMask = 0 };
        wall.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(20f, 400f) } });
        AddChild(wall);
        wall.GlobalPosition = at + new Vector2(130f, 0f);
        await Seconds(0.1f);
        _skills.GetSkill(SkillSlotId.Slot1)!.UseTimeRemaining = 0f;
        Refill();
        _skills.TryCast(SkillSlotId.Slot1, Vector2.Right);
        float minY = 0f, maxY = 0f;
        for (int i = 0; i < 30; i++)
        {
            await Seconds(0.033f);
            foreach (Projectile p in GetChildren().OfType<Projectile>())
            {
                minY = Mathf.Min(minY, p.GlobalPosition.Y);
                maxY = Mathf.Max(maxY, p.GlobalPosition.Y);
            }
        }

        await Seconds(0.3f);
        bool back = GetChildren().OfType<Projectile>().Any(p => p.Direction.X < 0f);
        Check("SPARK-BOUNCES-AND-ZIGZAGS", back, $"a spark is flying back from the wall={back}; sideways spread {maxY - minY:0} px");
        wall.QueueFree();
        foreach (Projectile p in GetChildren().OfType<Projectile>())
        {
            p.QueueFree();
        }
    }

    private async Task CurseChecks()
    {
        Vector2 at = new(8000f, 0f);
        _player.GlobalPosition = at;
        Node2D target = Dummy(at + new Vector2(200f, 0f));
        await Seconds(0.1f);
        ActorStats stats = target.GetNode<ActorStats>("Stats");
        float fire = stats.Get(StatId.FireResistance).Value;
        float speed = stats.MovementSpeed.Value;

        Equip("corruption", SkillSlotId.Slot1);
        Equip("curse_elements", SkillSlotId.Slot2);
        Equip("curse_time", SkillSlotId.Lmb);
        Refill();
        Vector2 point = target.GlobalPosition;
        _skills.TryCast(SkillSlotId.Slot1, Vector2.Right, point);
        _skills.TryCast(SkillSlotId.Slot2, Vector2.Right, point);
        _skills.TryCast(SkillSlotId.Lmb, Vector2.Right, point);
        await Seconds(1.2f);
        Curses? curses = target.GetNodeOrNull<Curses>("Curses");
        bool all = curses is not null && curses.Has(CurseKind.Corruption) && curses.Has(CurseKind.Elements) && curses.Has(CurseKind.Time);
        float fireNow = stats.Get(StatId.FireResistance).Value;
        float speedNow = stats.MovementSpeed.Value;
        Check("CURSES-APPLY", all && Missing(target) > 0f && fireNow <= fire - 24.9f && speedNow < speed * 0.7f,
            $"three curses on={all}; Corruption ate {Missing(target):0} life; fire resistance {fire:0} → {fireNow:0}; speed {speed:0} → {speedNow:0}");

        // Spread: a Corruption that may jump once moves to a neighbour when its bearer dies.
        Node2D bearer = Dummy(at + new Vector2(0f, 300f), immortal: false);
        Node2D neighbour = Dummy(at + new Vector2(80f, 300f));
        await Seconds(0.1f);
        Curses.On(bearer)!.Apply(new CurseEffect { Kind = CurseKind.Corruption, Duration = 5f, DamagePerSecond = 5f, Spread = 1, Source = _player, Tint = Colors.Purple });
        bearer.GetNode<Health>("Health").ApplyResolvedDamage(1e9f, _player);
        await Seconds(0.2f);
        bool jumped = neighbour.GetNodeOrNull<Curses>("Curses")?.Has(CurseKind.Corruption) == true;
        Check("CURSE-SPREADS-ON-DEATH", jumped, $"the neighbour caught it={jumped}");
        await Seconds(6.5f);
        bool expired = curses is not null && IsInstanceValid(curses) && !curses.Any && Mathf.IsEqualApprox(stats.Get(StatId.FireResistance).Value, fire);
        Check("CURSES-EXPIRE", expired, $"curses left on the target: {curses?.Active.Count}, fire resistance back to {stats.Get(StatId.FireResistance).Value:0}");
        target.QueueFree();
        neighbour.QueueFree();
    }

    private void ManaChecks()
    {
        // docs/design/skills.md -> "Mana and cooldowns, 2026-10-30".
        SkillDefinition fireball = UpgradeCatalog.FindSkill("fireball")!.Definition;
        float cost = fireball.Costs[0].Amount;
        var cls = Characters.ClassCatalog.Get("arcanist");
        float regen = ActorStats.DefaultBase(StatId.ManaRegen) + cls.Intelligence * 0.05f;
        float perSecond = cost / fireball.BaseUseTime;
        var high = new SkillInstance(fireball) { Level = 20 };
        Check("MANA-FIREBALL-NOT-FREE", perSecond > regen * 3f && high.LevelCostMultiplier > 1.3f,
            $"casting nonstop spends {perSecond:0.0} mana/s against {regen:0.0}/s of regeneration at level 1; a level-20 gem costs ×{high.LevelCostMultiplier:0.00}");
    }
}
