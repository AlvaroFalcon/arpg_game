#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Enemies;
using ArpgGame.Progression;
using ArpgGame.World;

namespace ArpgGame.Tests;

/// <summary>
/// The hidden playground (docs/design/playground.md), end to end: from the hideout,
/// Ctrl+Shift+P's entry builds a sandbox copy of the character with every skill at
/// level 20 and loads the playground (no packs, no boss); the spawner makes hordes and
/// bosses at the chosen tier; Escaldón and the Artificer are there; nothing drops; and
/// leaving restores the real character untouched. The test survives the scene changes by
/// moving itself under the root. Run:
///   ./scripts/run_headless.sh res://scenes/tests/PlaygroundTest.tscn --quit-after 6000
/// </summary>
public partial class PlaygroundTest : Node
{
    private bool _failed;
    private readonly List<string> _lines = new();

    public override void _Ready() => CallDeferred(nameof(Start));

    private void Start()
    {
        Window root = GetTree().Root;
        GetParent().RemoveChild(this);
        root.AddChild(this);
        _ = Run();
    }

    private void Check(string name, bool ok, string detail)
    {
        _failed |= !ok;
        string line = $"[PLAYGROUND] {name}: {(ok ? "PASS" : "FAIL")} — {detail}";
        _lines.Add(line);
        GD.Print(line);
    }

    private async Task Seconds(float s) => await ToSignal(GetTree().CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);

    private async Task<T?> WaitScene<T>(float timeout) where T : Node
    {
        for (float t = 0f; t < timeout; t += 0.1f)
        {
            if (GetTree().CurrentScene is T found && found.IsNodeReady())
            {
                return found;
            }

            await Seconds(0.1f);
        }

        return null;
    }

    private async Task Run()
    {
        Profile? profile = Profile.Get(this);
        if (profile is null)
        {
            Check("PROFILE", false, "no Profile autoload");
            Finish();
            return;
        }

        profile.Persist = false; // never touch the real save from a test
        GetTree().ChangeSceneToFile(Scenes.Hideout);
        Hideout? hideout = await WaitScene<Hideout>(10f);
        await Seconds(0.5f);
        if (hideout is null)
        {
            Check("HIDEOUT", false, "the hideout did not load");
            Finish();
            return;
        }

        CharacterData real = profile.Active;
        string name = real.Name;
        int gems = real.Gems.Count;
        long gold = hideout.Player.Wallet.GetBalance(Profile.GoldId);
        int characters = profile.Characters.Count;

        // The owner's dead playground (2026-11-01): Ctrl+Shift+P is also P, so the passive tree
        // opened — and paused the game — in the same frame the playground was launched, and the
        // playground loaded paused with a freed panel on the stack. Two defences, both checked:
        // the panel keys match exactly (no P with Ctrl+Shift), and a reality always starts running.
        var combo = new InputEventKey { Keycode = Key.P, PhysicalKeycode = Key.P, CtrlPressed = true, ShiftPressed = true, Pressed = true };
        var plain = new InputEventKey { Keycode = Key.P, PhysicalKeycode = Key.P, Pressed = true };
        bool exact = !InputMap.EventIsAction(combo, "toggle_passives", true) && InputMap.EventIsAction(plain, "toggle_passives", true);
        for (int i = 0; i < 5 && UI.PanelStack.Topmost is { } open; i++)
        {
            open.Close();
            await Seconds(0.1f);
        }

        hideout.EnterPlayground();
        hideout.GetNode<UI.CharacterPanels>("CharacterPanels").Tree.Open(); // the old same-frame toggle
        RealityController? ground = await WaitScene<RealityController>(20f);
        await Seconds(1f);
        Check("NOT-PAUSED", exact && ground is not null && !GetTree().Paused && !UI.PanelStack.HasOpen,
            $"Ctrl+Shift+P is not the passive tree's P={exact}; launched with a pausing panel open on the way out: playground loaded={ground is not null}, paused={GetTree().Paused}, a panel left open={UI.PanelStack.HasOpen}");
        if (ground is null)
        {
            Check("ENTERS", false, "the playground did not load");
            Finish();
            return;
        }

        Playground? pg = ground.GetNodeOrNull<Playground>("Playground");
        CharacterData sandbox = profile.Active;
        bool allMax = UpgradeCatalog.Skills.All(s => sandbox.Gems.Any(g => g.Id == s.Id && !g.IsSupport && g.Level == GemData.MaxLevel));
        int monsters = GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup).Count;
        Check("ENTERS", ground.IsPlayground && pg is not null && sandbox.Name.EndsWith("(playground)") && allMax && monsters == 0 && !profile.Persist
                && ground.Player.Wallet.GetBalance(Profile.GoldId) >= Playground.SandboxGold,
            $"playground={ground.IsPlayground}; sandbox \"{sandbox.Name}\" with every skill at {GemData.MaxLevel}={allMax}; {monsters} monsters at the start; writes off={!profile.Persist}; gold {ground.Player.Wallet.GetBalance(Profile.GoldId):N0}");
        if (pg is null)
        {
            Finish();
            return;
        }

        bool npcs = ground.GetNodeOrNull("Escaldón") is not null && ground.GetNodeOrNull("theArtificer") is not null
            && ground.GetNodeOrNull<UI.SkillTreePanel>("SkillTreePanel") is not null && ground.GetNodeOrNull<UI.ArtificerPanel>("ArtificerPanel") is not null;
        Check("NPCS", npcs && pg.ConsoleOpen, $"Escaldón and the Artificer stand in the camp with their panels={npcs}; spawner open={pg.ConsoleOpen}");

        // The DPS meter: the player's hits and damage over time count, by source; anyone else's don't.
        var tracker = new Combat.DpsTracker();
        tracker.Add(10f, 100f, "A", hit: true, crit: true);
        tracker.Add(12f, 300f, "B", hit: true);
        float fight = tracker.FightDps;
        float recent = tracker.Recent(12.5f);
        tracker.Add(20f, 50f, "A");
        Check("DPS-TRACKER", Mathf.IsEqualApprox(fight, 200f) && recent > 150f && tracker.Total == 50f && tracker.Hits == 0,
            $"400 over 2 s = {fight:0} DPS; last 5 s {recent:0}; 8 s of silence starts a new fight (total {tracker.Total:0}, hits {tracker.Hits})");

        Combat.TargetDummy dummy = pg.SpawnDummy();
        await Seconds(0.2f);
        pg.Meter.Tracker.Reset();
        Combat.Health dummyLife = dummy.GetNode<Combat.Health>("Health");
        Skills.SkillDefinition fireball = UpgradeCatalog.FindSkill("fireball")!.Definition;
        dummyLife.TakeDamage(new Combat.DamagePayload(100f, Combat.DamageType.Fire, ground.Player, Combat.Faction.Player, Combat.SkillTags.Spell | Combat.SkillTags.Fire, Combat.DamageDeliveryKind.Hit) { Skill = fireball });
        dummyLife.ApplyResolvedDamage(40f, ground.Player, "Bleed");
        dummyLife.ApplyResolvedDamage(500f, null, "Other");
        var rows = pg.Meter.Tracker.Breakdown.ToDictionary(r => r.Label, r => r.Total);
        bool counted = rows.ContainsKey("Fireball") && rows.TryGetValue("Bleed", out float bleed) && Mathf.IsEqualApprox(bleed, 40f) && !rows.ContainsKey("Other") && pg.Meter.Tracker.Hits == 1;
        // A real cast at the dummy shows up under the skill's name (a melee strike: it can't miss).
        Skills.SkillInstance spark = new(UpgradeCatalog.FindSkill("double_strike")!.Definition);
        ground.Player.SkillUser.EquipSkill(Skills.SkillSlotId.Slot1, spark);
        if (ground.Player.Stats.Mana is { } mana)
        {
            mana.Current = mana.Max;
        }

        ground.Player.GlobalPosition = dummy.GlobalPosition + new Vector2(-40f, 0f);
        await Seconds(0.1f);
        ground.Player.SkillUser.TryCast(Skills.SkillSlotId.Slot1, Vector2.Right, dummy.GlobalPosition);
        await Seconds(0.6f);
        bool cast = pg.Meter.Tracker.Breakdown.Any(r => r.Label == "Double Strike");
        Check("DPS-METER", counted && cast && pg.Meter.Visible && dummy.IsInsideTree(),
            $"a Fireball hit, 40 of bleed and someone else's 500 → [{string.Join(", ", pg.Meter.Tracker.Breakdown.Select(r => $"{r.Label} {r.Total:0}"))}]; a real Double Strike counted={cast}; the dummy is still standing={dummy.IsInsideTree()}");
        pg.ClearMonsters();
        await Seconds(0.2f);

        ground.SetPlaygroundTier(10);
        int horde = ground.SpawnPlaygroundHorde(12, null, null, ground.Player.GlobalPosition + new Vector2(260f, 0f)).Count;
        await Seconds(0.3f);
        var spawned = GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup).OfType<Enemy>().ToList();
        bool levelled = spawned.Count == 12 && spawned.All(e => e.MonsterLevel == RealityTiers.MonsterLevel(10) && !e.Dormant);
        Check("HORDE-AT-TIER", horde == 12 && levelled, $"{spawned.Count} monsters awake at monster level {spawned.FirstOrDefault()?.MonsterLevel} (tier 10 is {RealityTiers.MonsterLevel(10)})");

        // Monsters that stand still / don't attack (owner's request, 2026-11-02).
        pg.ClearMonsters();
        await Seconds(0.2f);
        ground.SetPlaygroundTier(1);
        pg.SetMonsters(standStill: true, pacified: false);
        List<Enemy> still = ground.SpawnPlaygroundHorde(6, "Slime", MonsterRarity.Normal, ground.Player.GlobalPosition + new Vector2(200f, 0f));
        await Seconds(0.4f);
        Vector2[] before = still.Select(e => e.GlobalPosition).ToArray();
        await Seconds(1.5f);
        float walked = still.Select((e, i) => e.GlobalPosition.DistanceTo(before[i])).Max();

        pg.SetMonsters(standStill: false, pacified: true);
        Stats.ResourcePool life = ground.Player.Stats.Life!;
        life.Current = life.Max;
        foreach (Enemy e in still)
        {
            e.GlobalPosition = ground.Player.GlobalPosition + new Vector2(10f, 0f);
        }

        await Seconds(2f);
        float harmed = life.Max - life.Current;
        Vector2 parked = still[0].GlobalPosition;
        pg.SetMonsters(standStill: false, pacified: false);
        ground.Player.GlobalPosition += new Vector2(-220f, 0f);
        await Seconds(1.5f);
        bool chases = still[0].GlobalPosition.DistanceTo(parked) > 20f;
        Check("MONSTERS-HOLD", walked < 4f && harmed == 0f && chases && Enemy.HoldStill == false && Enemy.Pacified == false,
            $"standing still they moved {walked:0.0} px in 1.5 s; not attacking, touching the player for 2 s, they took {harmed:0} life; switched off, they chase again={chases}");

        pg.ClearMonsters();
        await Seconds(0.2f);
        ground.SetPlaygroundTier(3);
        BossEnemy low = ground.SpawnPlaygroundBoss(RealityController.AllBosses[0], ground.Player.GlobalPosition + new Vector2(300f, 0f));
        float lowLife = low.Stats.Life!.Max;
        low.QueueFree();
        ground.SetPlaygroundTier(18);
        BossEnemy high = ground.SpawnPlaygroundBoss(RealityController.AllBosses[0], ground.Player.GlobalPosition + new Vector2(300f, 0f));
        float highLife = high.Stats.Life!.Max;
        await Seconds(0.2f);
        high.Health.ApplyResolvedDamage(1e12f, ground.Player);
        await Seconds(1.5f);
        int pickups = GetTree().GetNodesInGroup(ArpgGame.Run.RunState.PickupGroup).Count;
        Check("BOSS-ANY-TIER", highLife > lowLife * 2f && high.Health.IsDead && pickups == 0 && !ground.Completed,
            $"{high.DisplayName}: {lowLife:N0} life at tier 3, {highLife:N0} at tier 18; killed with no loot ({pickups} pickups) and no exit portal");

        // Leaving throws the sandbox away.
        pg.SetMonsters(standStill: true, pacified: true); // must not outlive the playground
        ground.Pause.OnLeave?.Invoke();
        Hideout? back = await WaitScene<Hideout>(20f);
        await Seconds(0.5f);
        CharacterData after = profile.Active;
        bool reset = !Enemy.HoldStill && !Enemy.Pacified;
        Check("LEAVES-CLEAN", reset && back is not null && after.Name == name && after.Gems.Count == gems && profile.Characters.Count == characters
                && (back?.Player.Wallet.GetBalance(Profile.GoldId) ?? -1) == gold,
            $"monster switches reset={reset}; back home as \"{after.Name}\" with {after.Gems.Count} gems (was {gems}), {profile.Characters.Count} characters (was {characters}), gold {back?.Player.Wallet.GetBalance(Profile.GoldId):N0} (was {gold:N0})");
        Finish();
    }

    private void Finish()
    {
        string verdict = _failed ? "[PLAYGROUND] === RESULT: FAIL ===" : "[PLAYGROUND] === RESULT: PASS ===";
        _lines.Add(verdict);
        GD.Print(verdict);
        DirAccess.MakeDirRecursiveAbsolute("user://test_results");
        using (var f = FileAccess.Open("user://test_results/PlaygroundTest.txt", FileAccess.ModeFlags.Write))
        {
            f?.StoreString(string.Join("\n", _lines) + "\n");
        }

        GetTree().Quit();
    }
}
