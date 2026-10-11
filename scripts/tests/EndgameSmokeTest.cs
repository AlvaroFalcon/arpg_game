#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PathToExile.Characters;
using PathToExile.Core;
using PathToExile.Enemies;
using PathToExile.Skills;
using PathToExile.Stats;
using PathToExile.World;

namespace PathToExile.Tests;

/// <summary>
/// Headless smoke test for the endgame layer (docs/design/endgame.md, classes.md).
/// Run with: ./scripts/run_all_tests.sh EndgameSmokeTest
///
/// Pure rules first (layouts connected for every corridor variant, the boss
/// shard table, tier scaling, shard spending, passive allocate/refund, gem and
/// character experience, gem pickups, the character save format), then the
/// live pieces: a monster promoted to each rarity, a class build attached to a
/// player, and a whole reality scene (packs spawned, boss behind the gate, the
/// gate closing on entry, the exit portal after the kill).
/// </summary>
public partial class EndgameSmokeTest : Node2D
{
    private bool _failed;

    public override void _Ready()
    {
        GD.Print("[ENDTEST] Ready.");
        _ = RunAll();
    }

    // Stdout is block-buffered when not a terminal and this scene can hang at
    // shutdown (CLAUDE.md), so every line is mirrored to a file the test runner reads.
    private const string ResultPath = "user://test_results/EndgameSmokeTest.txt";
    private readonly System.Text.StringBuilder _log = new();

    private void Log(string line)
    {
        GD.Print(line);
        _log.AppendLine(line);
        DirAccess.MakeDirRecursiveAbsolute("user://test_results");
        using FileAccess? f = FileAccess.Open(ResultPath, FileAccess.ModeFlags.Write);
        f?.StoreString(_log.ToString());
    }

    private void Check(string name, bool ok, string detail)
    {
        Log($"[ENDTEST] {name}: {(ok ? "PASS" : "FAIL")} — {detail}");
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

    private async Task RunAll()
    {
        try
        {
            await RunChecks();
        }
        catch (System.Exception e)
        {
            Check("EXCEPTION", false, e.ToString());
        }

        Log($"[ENDTEST] RESULT: {(_failed ? "FAIL" : "PASS")}");
        GetTree().Quit(_failed ? 1 : 0);
    }

    private async Task RunChecks()
    {
        await Frames(2);
        LayoutChecks();
        TierChecks();
        CharacterChecks();
        PassiveChecks();
        GemChecks();
        ProfileChecks();
        await RarityChecks();
        await BuildChecks();
        await RealityChecks();
    }

    // --- layouts ---------------------------------------------------------------------

    private static HashSet<Vector2I> Reachable(RasterLayout r, Vector2I from)
    {
        var seen = new HashSet<Vector2I> { from };
        var queue = new Queue<Vector2I>(seen);
        Vector2I[] dirs = { Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right };
        while (queue.Count > 0)
        {
            Vector2I c = queue.Dequeue();
            foreach (Vector2I d in dirs)
            {
                Vector2I n = c + d;
                if (r.Walkable(n) && seen.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        return seen;
    }

    private void LayoutChecks()
    {
        foreach (LayoutDef def in LayoutCatalog.Layouts)
        {
            int bad = 0;
            string detail = "";
            var corridorSets = new HashSet<string>();
            for (int seed = 0; seed < 40; seed++)
            {
                var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7919 + 1) };
                RasterLayout r = LayoutBuilder.Rasterize(def, rng);
                corridorSets.Add(string.Join(",", r.BuiltCorridors));
                HashSet<Vector2I> reach = Reachable(r, r.Entrance);
                bool ok = r.Walkable(r.Entrance) && reach.Contains(r.BossCell) && r.Gate.Count > 0
                    && r.Packs.All(p => reach.Contains(p.Cell)) && r.Packs.Count >= 6;
                if (!ok && bad++ == 0)
                {
                    detail = $"seed {seed}: boss reachable={reach.Contains(r.BossCell)}, gate={r.Gate.Count}, packs={r.Packs.Count}, unreachable packs={r.Packs.Count(p => !reach.Contains(p.Cell))}";
                }
            }

            Check($"LAYOUT-CONNECTED-{def.Id}", bad == 0, bad == 0 ? $"40 seeds connected, {corridorSets.Count} corridor variants seen" : detail);
        }
    }

    // --- tiers and shards ---------------------------------------------------------

    private void TierChecks()
    {
        Check("TIER-SCALING",
            Mathf.IsEqualApprox(RealityTiers.LifeMultiplier(1), 1f) && Mathf.Abs(RealityTiers.LifeMultiplier(20) - 25f) < 0.01f
            && Mathf.Abs(RealityTiers.DamageMultiplier(20) - 5f) < 0.01f && RealityTiers.MonsterLevel(1) == 1 && RealityTiers.MonsterLevel(20) == 85
            && RealityTiers.Portals(14) == 3 && RealityTiers.Portals(15) == 1,
            $"life x{RealityTiers.LifeMultiplier(20):0.0}, dmg x{RealityTiers.DamageMultiplier(20):0.0}, mlvl {RealityTiers.MonsterLevel(20)} at tier 20");

        var rng = new RandomNumberGenerator { Seed = 1234 };
        int none = 0, same = 0, plus1 = 0, plus2 = 0;
        const int n = 20000;
        for (int i = 0; i < n; i++)
        {
            int s = RealityTiers.RollBossShard(5, rng);
            if (s == 0) none++;
            else if (s == 5) same++;
            else if (s == 6) plus1++;
            else if (s == 7) plus2++;
        }

        bool near(int count, float p) => Mathf.Abs(count / (float)n - p) < 0.02f;
        Check("SHARD-TABLE", near(none, 0.25f) && near(same, 0.45f) && near(plus1, 0.25f) && near(plus2, 0.05f) && none + same + plus1 + plus2 == n,
            $"none {none * 100f / n:0.0}% same {same * 100f / n:0.0}% +1 {plus1 * 100f / n:0.0}% +2 {plus2 * 100f / n:0.0}%");

        int over = 0;
        for (int i = 0; i < 2000; i++)
        {
            if (RealityTiers.RollBossShard(20, rng) > 20) over++;
        }

        Check("SHARD-CAP-20", over == 0, $"{over} shards above tier 20");

        var mods = RealityLaunch.RollModifiers(18, rng);
        (float q, float r) = RealityLaunch.Bonuses(mods);
        Check("SHARD-MODIFIERS", mods.Count is >= 1 and <= 3 && mods.Distinct().Count() == mods.Count && q > 0f && r > 0f,
            $"{string.Join(", ", mods)} → +{q * 100f:0}% quantity, +{r * 100f:0}% rarity");
    }

    // --- characters -----------------------------------------------------------------

    private void CharacterChecks()
    {
        CharacterData c = CharacterData.Create("Test", "warrior");
        Check("CHAR-CREATE", c.Passives.Contains("start_warrior") && c.Gems.Count == 2 && c.Loadout[0].SkillUid is not null && c.Loadout[0].SupportUids.Count == 1,
            $"{c.Gems.Count} gems, loadout0 skill={c.FindGem(c.Loadout[0].SkillUid)?.Id}");

        Check("SHARD-SPEND", c.TrySpendShard(1) && !c.TrySpendShard(3), "tier 1 free, tier 3 needs a shard");
        c.AddShards(3, 2);
        bool spent = c.TrySpendShard(3);
        Check("SHARD-SPEND-OWNED", spent && c.ShardCount(3) == 1, $"after spending: {c.ShardCount(3)} left");

        int levels = c.AddExperience(CharacterData.ExperienceToNext(1) + CharacterData.ExperienceToNext(2) + 1f);
        Check("CHAR-XP", levels == 2 && c.Level == 3 && c.PassivePointsFree == 2, $"level {c.Level}, free points {c.PassivePointsFree}");
        Check("SUPPORT-SOCKETS", c.SupportSockets == 2 && new CharacterData { Level = 12 }.SupportSockets == 3 && new CharacterData { Level = 30 }.SupportSockets == 4,
            "2 / 3 at 12 / 4 at 28");
    }

    private void PassiveChecks()
    {
        CharacterData c = CharacterData.Create("Tree", "rogue");
        c.Level = 10;
        PassiveNode start = PassiveTree.Get("start_rogue")!;
        string first = start.Links.First(id => PassiveTree.Get(id)!.Kind != PassiveKind.Start);
        bool canFirst = PassiveTree.CanAllocate(c, first);
        c.Passives.Add(first);
        string second = PassiveTree.Get(first)!.Links.First(id => !c.Passives.Contains(id) && PassiveTree.Get(id)!.Kind != PassiveKind.Start);
        bool canSecond = PassiveTree.CanAllocate(c, second);
        c.Passives.Add(second);
        string far = PassiveTree.Nodes.Keys.First(id => !c.Passives.Contains(id) && !PassiveTree.CanAllocate(c, id) && !id.StartsWith("start_"));

        Check("PASSIVE-ALLOCATE", canFirst && canSecond && !PassiveTree.CanAllocate(c, far) && !PassiveTree.CanAllocate(c, first),
            $"{first} → {second}; {far} not adjacent");
        Check("PASSIVE-REFUND", !PassiveTree.CanRefund(c, first) && PassiveTree.CanRefund(c, second) && !PassiveTree.CanRefund(c, "start_rogue"),
            "refunding the middle node would orphan the tip");

        int unreachable = 0;
        var seen = new HashSet<string>();
        var queue = new Queue<string>(PassiveTree.Nodes.Values.Where(n => n.Kind == PassiveKind.Start).Select(n => n.Id));
        foreach (string s in queue) seen.Add(s);
        while (queue.Count > 0)
        {
            foreach (string next in PassiveTree.Nodes[queue.Dequeue()].Links)
            {
                if (seen.Add(next)) queue.Enqueue(next);
            }
        }

        unreachable = PassiveTree.Nodes.Count - seen.Count;
        int keystones = PassiveTree.Nodes.Values.Count(n => n.Kind == PassiveKind.Keystone);
        Check("PASSIVE-TREE-CONNECTED", unreachable == 0 && keystones >= 6, $"{PassiveTree.Nodes.Count} nodes, {keystones} keystones, {unreachable} unreachable");
    }

    private void GemChecks()
    {
        var gem = new GemData { Id = "fireball" };
        bool levelled = gem.AddExperience(GemData.ExperienceToNext(1) + 1f);
        var support = new GemData { Id = "pierce", IsSupport = true };
        Check("GEM-XP", levelled && gem.Level == 2 && !support.AddExperience(1e6f) && support.Level == 1, $"skill level {gem.Level}, support stays 1");

        CharacterData c = CharacterData.Create("Loot", "arcanist");
        int before = c.Gems.Count;
        (string newText, _) = GemLoot.Collect(c, "frost_nova", false);
        bool added = c.Gems.Count == before + 1;
        (string dupText, _) = GemLoot.Collect(c, "frost_nova", false);
        GemData nova = c.Gems.First(g => g.Id == "frost_nova");
        bool absorbed = c.Gems.Count == before + 1 && (nova.Level > 1 || nova.Experience > 0f);
        Check("GEMLOOT-SKILL", added && absorbed, $"'{newText}' then '{dupText}'");

        long gold = 0;
        for (int i = 0; i < GemLoot.MaxSupportCopies + 1; i++)
        {
            gold += GemLoot.Collect(c, "pierce", true).Gold;
        }

        Check("GEMLOOT-SUPPORT-CAP", c.Gems.Count(g => g.Id == "pierce") == GemLoot.MaxSupportCopies && gold == GemLoot.SupportSurplusGold,
            $"{c.Gems.Count(g => g.Id == "pierce")} copies kept, {gold} gold from the surplus");
    }

    private void ProfileChecks()
    {
        CharacterData c = CharacterData.Create("Saver", "rogue");
        c.Level = 14;
        c.Experience = 33f;
        c.Passives.Add(PassiveTree.Get("start_rogue")!.Links[0]);
        c.Ascendancy = "assassin";
        c.AscendancyPoints = 4;
        c.AscendancyNodes.Add("assassin_venom");
        c.AddShards(7, 3);
        c.Gold = 999;
        c.HighestTierCleared = 6;
        c.Gems[0].Level = 5;

        string json = Profile.SerializeCharacter(c);
        CharacterData? back = Profile.DeserializeCharacter(json);
        bool ok = back is not null && back.Name == "Saver" && back.ClassId == "rogue" && back.Level == 14 && back.Passives.SetEquals(c.Passives)
            && back.Ascendancy == "assassin" && back.AscendancyNodes.SetEquals(c.AscendancyNodes) && back.ShardCount(7) == 3 && back.Gold == 999
            && back.Gems.Count == c.Gems.Count && back.Gems[0].Level == 5 && back.Gems[0].Uid == c.Gems[0].Uid
            && back.Loadout[0].SkillUid == c.Loadout[0].SkillUid && back.Loadout[0].SupportUids.SequenceEqual(c.Loadout[0].SupportUids)
            && back.HighestTierCleared == 6;
        Check("PROFILE-CHARACTER-ROUNDTRIP", ok, $"{json.Length} bytes of JSON");
    }

    // --- live checks --------------------------------------------------------------

    private async Task RarityChecks()
    {
        var results = new List<string>();
        bool ok = true;
        foreach (MonsterRarity rarity in new[] { MonsterRarity.Normal, MonsterRarity.Magic, MonsterRarity.Rare, MonsterRarity.Unique })
        {
            var enemy = GD.Load<PackedScene>("res://scenes/enemies/Slime.tscn").Instantiate<Enemy>();
            enemy.Dormant = true;
            AddChild(enemy);
            enemy.GlobalPosition = new Vector2(-2000f - 200f * (int)rarity, 0f);
            float baseLife = enemy.Stats.Life!.Max;
            enemy.ApplyRarity(rarity, new List<MonsterMod>());
            float ratio = enemy.Stats.Life.Max / baseLife;
            ok &= Mathf.Abs(ratio - MonsterRarities.LifeMultiplier(rarity)) < 0.01f && enemy.Rarity == rarity
                && (rarity < MonsterRarity.Rare || enemy.ShownName != enemy.DisplayName);
            results.Add($"{rarity} x{ratio:0.0} '{enemy.ShownName}'");
            enemy.QueueFree();
        }

        var tiered = GD.Load<PackedScene>("res://scenes/enemies/Brute.tscn").Instantiate<Enemy>();
        tiered.Dormant = true;
        AddChild(tiered);
        float before = tiered.Stats.Life!.Max;
        tiered.ApplyTierScaling(RealityTiers.LifeMultiplier(10), 1f);
        float tierRatio = tiered.Stats.Life.Max / before;
        tiered.QueueFree();
        await Frames(1);

        Check("MONSTER-RARITY", ok, string.Join(", ", results));
        Check("MONSTER-TIER-SCALING", Mathf.Abs(tierRatio - RealityTiers.LifeMultiplier(10)) < 0.01f, $"tier 10 life x{tierRatio:0.00}");
    }

    private async Task BuildChecks()
    {
        var player = GD.Load<PackedScene>("res://scenes/Player.tscn").Instantiate<Player>();
        AddChild(player);
        player.GlobalPosition = new Vector2(-4000f, 0f);
        await Frames(1);
        float baseLife = player.Stats.Life!.Max;

        CharacterData c = CharacterData.Create("Brick", "warrior");
        c.Level = 10;
        CharacterBuild build = CharacterBuild.Attach(player, c);
        await Frames(2);
        SkillInstance? slot1 = player.SkillUser.GetSkill(SkillSlotId.Slot1);
        bool skillOk = slot1 is not null && slot1.Definition.DisplayName.Length > 0 && slot1.Attached.Count == 1;
        Check("CLASS-BUILD", skillOk && player.Stats.Life.Max > baseLife && build.Resource?.Kind == ClassResourceKind.Rage,
            $"slot1={slot1?.Definition.DisplayName} supports={slot1?.Attached.Count}, life {baseLife:0} → {player.Stats.Life.Max:0}, resource {build.Resource?.Kind}");

        int levelsBefore = c.Level;
        build.GrantExperience(CharacterData.ExperienceToNext(c.Level) * 1.01f / Mathf.Max(0.01f, player.Stats.Get(StatId.ExperienceGain).Value));
        Check("CLASS-LEVEL-UP", c.Level == levelsBefore + 1, $"level {levelsBefore} → {c.Level}");

        player.QueueFree();
        await Frames(2);
    }

    private async Task RealityChecks()
    {
        RealityLaunch.Tier = 3;
        RealityLaunch.LayoutId = "ashen_halls";
        RealityLaunch.Modifiers = new List<string> { "hardened" };
        var reality = GD.Load<PackedScene>(Scenes.Reality).Instantiate<RealityController>();
        AddChild(reality);
        await Frames(5);

        Check("REALITY-BUILT", reality.MonstersTotal >= 20 && reality.Boss is not null && reality.Boss.Dormant && reality.PortalsLeft == 3 && reality.Tier == 3,
            $"{reality.Layout.Name}: {reality.MonstersTotal} monsters, boss {reality.Boss?.DisplayName}, {reality.PortalsLeft} portals, {reality.Raster.Width}x{reality.Raster.Height} cells");

        Vector2I playerCell = reality.Field.CellOf(reality.Player.GlobalPosition);
        Check("REALITY-PLAYER-AT-ENTRANCE", playerCell == reality.Raster.Entrance, $"player cell {playerCell}, entrance {reality.Raster.Entrance}");

        // Walk into the boss room: the gate must close and the boss wake.
        reality.Player.Health!.IgnoreDamage = true;
        reality.Player.GlobalPosition = RasterLayout.World(reality.Raster.BossCell) + new Vector2(0f, 120f);
        await Frames(6);
        bool gateBlocked = reality.Raster.Gate.All(g => !reality.Field.IsWalkable(g));
        Check("REALITY-BOSS-GATE", reality.BossFightActive && !reality.Boss!.Dormant && gateBlocked,
            $"fight active={reality.BossFightActive}, boss dormant={reality.Boss?.Dormant}, gate blocked in flow field={gateBlocked}");

        // Step back first: the exit portal spawns under the boss and touching it
        // leaves the reality (a scene change would free this test).
        reality.Player.GlobalPosition = RasterLayout.World(reality.Raster.Entrance);
        reality.Boss!.Health.ApplyResolvedDamage(1e9f, reality.Player);
        await Frames(10);
        int drops = GetTree().GetNodesInGroup(Run.RunState.PickupGroup).Count;
        Check("REALITY-BOSS-KILLED", reality.Completed && !reality.BossFightActive && reality.ExitPortal is not null && drops > 0 && reality.Raster.Gate.All(g => reality.Field.IsWalkable(g)),
            $"completed={reality.Completed}, exit portal={reality.ExitPortal is not null}, {drops} drops on the floor, gate reopened");

        reality.QueueFree();
        await Frames(2);
    }
}
