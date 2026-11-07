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
/// The Survival Rift's auto-cast with every skill (docs/design/run.md -> "Toggles and the
/// timers"). Its own suite: it plays each skill for real seconds, which would push
/// SkillKitTest past the runner's frame limit. Run:
///   ./scripts/run_headless.sh res://scenes/tests/RiftAutoCastTest.tscn --quit-after 6000
/// </summary>
public partial class RiftAutoCastTest : Node2D
{
    private bool _failed;
    private Player _player = null!;
    private SkillUser _skills = null!;
    private readonly List<string> _lines = new();
    private readonly Dictionary<ulong, float> _taken = new();

    public override void _Ready()
    {
        _player = GetNode<Player>("Player");
        _skills = _player.GetNode<SkillUser>("SkillUser");
        _ = Run();
    }

    private void Check(string name, bool ok, string detail)
    {
        _failed |= !ok;
        string line = $"[RIFTCAST] {name}: {(ok ? "PASS" : "FAIL")} — {detail}";
        _lines.Add(line);
        GD.Print(line);
    }

    private async Task Seconds(float s) => await ToSignal(GetTree().CreateTimer(s, processInPhysics: true), SceneTreeTimer.SignalName.Timeout);

    private Node2D Dummy(Vector2 at)
    {
        var dummy = GD.Load<PackedScene>("res://scenes/combat/TargetDummy.tscn").Instantiate<TargetDummy>();
        dummy.Immortal = true;
        AddChild(dummy);
        dummy.GlobalPosition = at;
        dummy.AddChild(new Ailments { Name = "Ailments" });
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

    private async Task Run()
    {
        await Seconds(0.2f);
        _player.Stats.Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.Flat, 900f, "test"));
        await RiftAutoCastChecks();

        string verdict = _failed ? "[RIFTCAST] === RESULT: FAIL ===" : "[RIFTCAST] === RESULT: PASS ===";
        _lines.Add(verdict);
        GD.Print(verdict);
        DirAccess.MakeDirRecursiveAbsolute("user://test_results");
        using (var f = FileAccess.Open("user://test_results/RiftAutoCastTest.txt", FileAccess.ModeFlags.Write))
        {
            f?.StoreString(string.Join("\n", _lines) + "\n");
        }

        GetTree().Quit();
    }

    /// <summary>
    /// Every skill a character can bring into the Survival Rift (its loadout carries over,
    /// auras and totems included) fires on its own there. Each one, alone in a slot under
    /// survival casting for a few seconds next to dummies: it must cast, a skill that hits
    /// must hurt them, and a toggle (Immolation) must switch on once and stay on — the
    /// auto-cast used to switch it off again on every timer.
    /// </summary>
    private async Task RiftAutoCastChecks()
    {
        var input = _player.GetNode<PlayerSkillInput>("PlayerSkillInput");
        ulong started = Time.GetTicksMsec();
        bool ignored = _player.Stats.IgnoresResourceCosts;
        _player.Stats.IgnoresResourceCosts = true;
        // Four times the auto-cast speed: Vitality's first cast would otherwise wait 6 s.
        var fast = new StatModifier(ModifierType.More, 300f, "rift-autocast");
        _player.Stats.Get(StatId.AutoCastSpeed).AddModifier(fast);
        Vector2 at = new(14000f, 0f);
        var broken = new List<string>();
        var report = new List<string>();
        // Alone in the bar: the test player's default skills would auto-cast too.
        foreach (SkillSlotId other in new[] { SkillSlotId.Lmb, SkillSlotId.Rmb, SkillSlotId.Slot1, SkillSlotId.Slot2, SkillSlotId.Slot3, SkillSlotId.Slot4 })
        {
            _skills.ClearSlot(other);
        }

        int slotFired = 0;
        void OnFired(int slot) => slotFired += slot == (int)SkillSlotId.Slot1 ? 1 : 0;
        _skills.CastFired += OnFired;
        foreach (SkillEntry entry in UpgradeCatalog.Skills)
        {
            SkillDefinition d = entry.Definition;
            if (SkillRules.FitsSpace(d))
            {
                continue; // the Space slot is never auto-cast
            }

            _player.GlobalPosition = at;
            Node2D a = Dummy(at + new Vector2(70f, 0f));
            Node2D b = Dummy(at + new Vector2(0f, 90f));
            await Seconds(0.1f);
            SkillInstance skill = Equip(entry.Id);
            if (d is TotemSkillDefinition)
            {
                skill.Socketed = new SkillInstance(UpgradeCatalog.FindSkill("fireball")!.Definition);
            }

            slotFired = 0;
            input.Mode = PlayerSkillInput.CastMode.Survival;
            int flips = 0;
            bool was = false;
            bool toggle = d is AuraSkillDefinition { IsToggle: true };
            bool hits = toggle || d is ProjectileSkillDefinition or AreaSkillDefinition or OrbitSkillDefinition or MeleeSkillDefinition or TotemSkillDefinition;
            // The first cast comes after a quarter of the interval (the staggered start).
            float window = toggle ? 5f : 0.3f * input.AutoCastInterval(skill) + 1.5f;
            float sinceFirst = 0f;
            for (float t = 0f; t < window; t += 0.1f)
            {
                await Seconds(0.1f);
                bool now = _skills.Auras.IsActive(skill);
                flips += now != was ? 1 : 0;
                was = now;
                sinceFirst += slotFired > 0 ? 0.1f : 0f;
                if (!toggle && slotFired > 0 && (Missing(a) + Missing(b) > 0f || (!hits && sinceFirst >= 0.3f)))
                {
                    break;
                }
            }

            input.Mode = PlayerSkillInput.CastMode.Hold;
            float dealt = Missing(a) + Missing(b);
            bool ok = slotFired > 0 && (!hits || dealt > 0f);
            string note = $"{entry.Id}: {slotFired} casts, {dealt:0} dmg";
            if (toggle)
            {
                ok &= flips == 1 && was;
                note += $", switched {flips}× (on at the end={was})";
            }

            report.Add(note);
            if (!ok)
            {
                broken.Add(note);
            }

            _skills.ClearSlot(SkillSlotId.Slot1);
            _skills.Auras.EndAll();
            foreach (Totem t in _skills.Totems.ToList())
            {
                if (IsInstanceValid(t))
                {
                    t.Expire();
                }
            }

            a.QueueFree();
            b.QueueFree();
            at += new Vector2(0f, 1500f);
        }

        _skills.CastFired -= OnFired;
        _player.Stats.IgnoresResourceCosts = ignored;
        _player.Stats.RemoveModifiersFromSource("rift-autocast");
        GD.Print($"[RIFTCAST] rift auto-cast ({(Time.GetTicksMsec() - started) / 1000f:0.0} s, frames {Engine.GetProcessFrames()}): " + string.Join("; ", report));
        Check("RIFT-AUTOCAST", broken.Count == 0,
            broken.Count == 0 ? $"{report.Count} skills fire on their own and Immolation stays on" : "broken: " + string.Join("; ", broken));
    }
}
