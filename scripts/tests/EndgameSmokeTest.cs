#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Combat;
using ArpgGame.Core;
using ArpgGame.Enemies;
using ArpgGame.Items;
using ArpgGame.Run;
using ArpgGame.Skills;
using ArpgGame.Stats;
using ArpgGame.World;

namespace ArpgGame.Tests;

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
        FeedbackChecks();
        await SprintChecks();
        await RarityChecks();
        await BuildChecks();
        await RealityChecks();
        await TutorialChecks();
        await CraftingChecks();
        await IssueFeedbackChecks();
        await ShardVendorChecks();
        await JewelChecks();
        DropTableChecks();
    }

    /// <summary>items.md -> "Drop tables (cascade)": the rolls match the tables, a
    /// rarity multiplier scales every tier, chances cap, Majestic stays at 0 % until
    /// authored, and orbs cascade from the rarest down.</summary>
    private void DropTableChecks()
    {
        var rng = new RandomNumberGenerator { Seed = 4242 };
        const int n = 40000;
        Dictionary<Rarity, float> Rates(DropSource source, float multiplier)
        {
            var counts = new Dictionary<Rarity, float>();
            for (int i = 0; i < n; i++)
            {
                Rarity r = DropTables.RollRarity(source, multiplier, rng);
                counts[r] = counts.GetValueOrDefault(r) + 1f / n;
            }

            return counts;
        }

        bool Near(float got, float want, float tolerance) => Mathf.Abs(got - want) <= tolerance;
        Dictionary<Rarity, float> normal = Rates(DropSource.NormalMonster, 1f);
        Check("DROP-CASCADE-TABLE", Near(normal.GetValueOrDefault(Rarity.Rare), 0.015f, 0.003f) && Near(normal.GetValueOrDefault(Rarity.Magic), 0.985f * 0.5f, 0.01f),
            $"normal monster: rare {normal.GetValueOrDefault(Rarity.Rare):P1} (table 1.5 %), magic {normal.GetValueOrDefault(Rarity.Magic):P1} (50 % of what falls through)");
        Dictionary<Rarity, float> doubled = Rates(DropSource.NormalMonster, 2f);
        Check("DROP-RARITY-MULTIPLIES", Near(doubled.GetValueOrDefault(Rarity.Rare), 0.03f, 0.004f) && doubled.GetValueOrDefault(Rarity.Common) < normal.GetValueOrDefault(Rarity.Common),
            $"×2 rarity: rare {doubled.GetValueOrDefault(Rarity.Rare):P1}, common {normal.GetValueOrDefault(Rarity.Common):P0} → {doubled.GetValueOrDefault(Rarity.Common):P0}");
        Dictionary<Rarity, float> boss = Rates(DropSource.Boss, 50f);
        Check("DROP-CAP-AND-MAJESTIC", boss.GetValueOrDefault(Rarity.Majestic) == 0f && Near(boss.GetValueOrDefault(Rarity.Rare), DropTables.MaxChance, 0.01f),
            $"boss ×50: majestic {boss.GetValueOrDefault(Rarity.Majestic):P1} (held at 0 %), rare capped at {boss.GetValueOrDefault(Rarity.Rare):P1}");

        var orbs = new Dictionary<string, float>();
        for (int i = 0; i < n; i++)
        {
            string id = DropTables.RollOrb(rng).Id;
            orbs[id] = orbs.GetValueOrDefault(id) + 1f / n;
        }

        float miss = DropTables.Orbs.Aggregate(1f, (left, o) => left * (1f - o.Chance));
        Check("DROP-ORB-CASCADE", Near(orbs.GetValueOrDefault("ascendant_orb"), 0.01f, 0.002f) && Near(orbs.GetValueOrDefault(DropTables.FallbackOrb), miss, 0.01f)
            && orbs.Count == Crafting.Orbs.Count,
            $"ascendant {orbs.GetValueOrDefault("ascendant_orb"):P1} (table 1 %), kindling (fallback) {orbs.GetValueOrDefault(DropTables.FallbackOrb):P0} (expected {miss:P0}), {orbs.Count} kinds seen");
        Check("DROP-MULTIPLIERS", Mathf.IsEqualApprox(DropTables.RealityMultiplier(0.5f, 20), 1.5f * 1.76f) && Mathf.IsEqualApprox(DropTables.RiftMultiplier(600f), 1.5f),
            $"reality +50 % rarity at tier 20 ×{DropTables.RealityMultiplier(0.5f, 20):0.00}, Rift at 10:00 ×{DropTables.RiftMultiplier(600f):0.00}");
    }

    /// <summary>endgame.md -> "Mirror jewels": 70 connected sockets, socket next to
    /// the filled ones, take out only leaves of the filled set, bonuses add up, drops
    /// gated by tier past 40, saved with the account, and the Mirror's tab works.</summary>
    private async Task JewelChecks()
    {
        int notables = JewelTree.Nodes.Values.Count(n => n.Kind == JewelNodeKind.Notable);
        var seen = new HashSet<string> { JewelTree.HeartId };
        var queue = new Queue<string>(seen);
        while (queue.Count > 0)
        {
            foreach (string l in JewelTree.Nodes[queue.Dequeue()].Links)
            {
                if (seen.Add(l))
                {
                    queue.Enqueue(l);
                }
            }
        }

        Check("JEWEL-TREE-SHAPE", JewelTree.Nodes.Count == 70 && notables == 12 && seen.Count == 70 && JewelTree.Nodes.Values.All(n => n.Links.All(l => JewelTree.Nodes[l].Links.Contains(n.Id))),
            $"{JewelTree.Nodes.Count} nodes, {notables} notables, {seen.Count} reachable from the heart");

        var data = new JewelData { Found = 3 };
        bool farFirst = JewelTree.Socket(data, "b0s2");
        bool near = JewelTree.Socket(data, "b0s1") && JewelTree.Socket(data, "b0s2") && JewelTree.Socket(data, "b3s1");
        bool noneLeft = !JewelTree.Socket(data, "b0s3");
        Check("JEWEL-SOCKET-RULES", !farFirst && near && noneLeft && data.Free == 0, $"far first={farFirst}, adjacent={near}, with none left={!noneLeft}");
        bool cutOff = JewelTree.Unsocket(data, "b0s1");
        bool leaf = JewelTree.Unsocket(data, "b0s2");
        Check("JEWEL-UNSOCKET-KEEPS-CONNECTED", !cutOff && leaf && data.Free == 1, $"middle removed={cutOff}, leaf removed={leaf}");

        data.Found = 10;
        foreach (string id in new[] { "b0s2", "b0s3", "b0s4", "b0s5", "b0s6", "b0n" })
        {
            JewelTree.Socket(data, id);
        }

        JewelBonuses b = JewelTree.Bonuses(data);
        Check("JEWEL-BONUSES-ADD-UP", Mathf.IsEqualApprox(b[JewelStat.Quantity], 0.03f * 6 + 0.10f) && Mathf.IsEqualApprox(b[JewelStat.Density], 0.03f),
            $"quantity +{b[JewelStat.Quantity] * 100f:0}%, density +{b[JewelStat.Density] * 100f:0}%");
        Check("JEWEL-DROPS-GATED", JewelTree.DropChance(10, 1) > 0f && JewelTree.DropChance(40, 11) == 0f && JewelTree.DropChance(40, 12) > 0f
            && JewelTree.DropChance(60, 16) == 0f && JewelTree.DropChance(60, 17) > 0f && JewelTree.DropChance(JewelTree.Sockets, 20) == 0f,
            $"tier needed: {JewelTree.RequiredTier(0)} → {JewelTree.RequiredTier(40)} → {JewelTree.RequiredTier(60)} → none at {JewelTree.Sockets}");

        var account = new Profile { Persist = false };
        account.Jewels.Found = data.Found;
        account.Jewels.Socketed = new HashSet<string>(data.Socketed);
        var reread = new Profile { Persist = false };
        reread.LoadJson(account.ToJson());
        Check("JEWEL-SAVES", reread.Jewels.Found == 10 && reread.Jewels.Socketed.SetEquals(data.Socketed), $"found {reread.Jewels.Found}, socketed {reread.Jewels.Socketed.Count}");
        account.Free();
        reread.Free();

        var player = GD.Load<PackedScene>("res://scenes/Player.tscn").Instantiate<Player>();
        AddChild(player);
        player.GlobalPosition = new Vector2(-7000f, 0f);
        // Never touch the real account's jewels or write it to disk.
        Profile? real = Profile.Get(this);
        bool persisted = real?.Persist ?? false;
        JewelData? realJewels = real?.Jewels;
        if (real is not null)
        {
            real.Persist = false;
            real.Jewels = new JewelData();
        }

        var mirror = new UI.MirrorPanel { Player = player, Name = "JewelMirror" };
        AddChild(mirror);
        await Frames(1);
        mirror.Open();
        mirror.ShowTab(true);
        mirror.JewelView.Data.Found += 1;
        bool clicked = mirror.ClickJewel(JewelTree.Get("b5s1")!, false);
        mirror.Close();
        Check("JEWEL-MIRROR-TAB", mirror.ShowingJewels && clicked && mirror.JewelView.Data.Socketed.Contains("b5s1"), $"tab shown={mirror.ShowingJewels}, socketed through the panel={clicked}");
        mirror.QueueFree();
        player.QueueFree();
        if (real is not null)
        {
            real.Jewels = realJewels!;
            real.Persist = persisted;
        }
    }

    /// <summary>endgame.md -> "Arehucaz": only cleared tiers, five at a time, restock
    /// after ten clears or for 10,000 gold, and the stock survives a save.</summary>
    private async Task ShardVendorChecks()
    {
        CharacterData c = CharacterData.Create("Dealer", "rogue");
        c.HighestTierCleared = 6;
        var wallet = new Wallet();
        AddChild(wallet);
        wallet.Add(Profile.GoldId, 50_000);
        bool gated = ShardVendor.CannotBuy(c, wallet, 7) is not null && ShardVendor.CannotBuy(c, wallet, 1) is not null
            && ShardVendor.CannotBuy(c, wallet, 6) is null;
        int bought = Enumerable.Range(0, 7).Count(i => ShardVendor.TryBuy(c, wallet, 6, out string _));
        long spent = 50_000 - wallet.GetBalance(Profile.GoldId);
        Check("VENDOR-CLEARED-TIERS-ONLY", gated, "tier 7 (not cleared) and tier 1 (free) refused, tier 6 sold");
        Check("VENDOR-STOCK-OF-5", bought == 5 && c.ShardCount(6) == 5 && c.ShardVendorStock == 0 && spent == 5 * ShardVendor.Price(6),
            $"bought {bought} of 7 tries, own {c.ShardCount(6)}, paid {spent} ({ShardVendor.Price(6)} each)");

        c.RealitiesCleared += 9;
        bool early = ShardVendor.Update(c);
        c.RealitiesCleared += 1;
        bool restocked = ShardVendor.Update(c);
        Check("VENDOR-RESTOCK-AFTER-10", !early && restocked && c.ShardVendorStock == 5 && ShardVendor.ClearsUntilRestock(c) == 10,
            $"after 9 clears restocked={early}, after 10 restocked={restocked}, stock {c.ShardVendorStock}");

        ShardVendor.TryBuy(c, wallet, 3, out _);
        long before = wallet.GetBalance(Profile.GoldId);
        bool paid = ShardVendor.TryRestock(c, wallet, out _);
        Check("VENDOR-RESTOCK-FOR-GOLD", paid && c.ShardVendorStock == 5 && before - wallet.GetBalance(Profile.GoldId) == ShardVendor.RestockGold,
            $"restock paid={paid}, cost {before - wallet.GetBalance(Profile.GoldId)}");

        ShardVendor.TryBuy(c, wallet, 4, out _);
        CharacterData? back = Profile.DeserializeCharacter(Profile.SerializeCharacter(c));
        Check("VENDOR-SAVES", back is { ShardVendorStock: 4 } && back.ShardVendorSince == c.ShardVendorSince && back.ShardCount(4) == 1,
            $"stock {back?.ShardVendorStock}, since {back?.ShardVendorSince}");
        Check("BOSS-ALWAYS-DROPS-SHARD", Enumerable.Range(0, 500).All(_ => RealityTiers.RollBossShard(4, new RandomNumberGenerator { Seed = (ulong)GD.Randi() }) >= 4),
            "500 boss rolls, every one a shard");

        // The panel, live.
        var player = GD.Load<PackedScene>("res://scenes/Player.tscn").Instantiate<Player>();
        AddChild(player);
        player.GlobalPosition = new Vector2(-6000f, 0f);
        await Frames(1);
        player.Wallet.Add(Profile.GoldId, 5_000);
        Profile? real = Profile.Get(this);
        bool persisted = real?.Persist ?? false;
        if (real is not null)
        {
            real.Persist = false;
        }

        var panel = new UI.ShardVendorPanel { Player = player, Character = c, Name = "VendorPanel" };
        AddChild(panel);
        await Frames(1);
        panel.Open();
        int owned = c.ShardCount(5);
        bool ok = panel.Buy(5);
        panel.Close();
        Check("VENDOR-PANEL-BUY", ok && c.ShardCount(5) == owned + 1, $"bought through the panel={ok}");
        panel.QueueFree();
        player.QueueFree();
        wallet.QueueFree();
        if (real is not null)
        {
            real.Persist = persisted;
        }
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
        CaveChecks();
        foreach (LayoutDef def in LayoutCatalog.RoomLayouts)
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

    private static string Around(string dump, Vector2I c)
    {
        string[] rows = dump.Split('\n');
        var lines = new List<string>();
        for (int y = Mathf.Max(0, c.Y - 4); y <= Mathf.Min(rows.Length - 1, c.Y + 4); y++)
        {
            int from = Mathf.Max(0, c.X - 8);
            if (from < rows[y].Length)
            {
                lines.Add(rows[y].Substring(from, Mathf.Min(17, rows[y].Length - from)));
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>endgame.md -> "Caverns": every style over many seeds is connected, has
    /// its boss behind a small gate, enough well-spaced reachable packs, no one-cell
    /// passages; the same seed gives the same map.</summary>
    private void CaveChecks()
    {
        foreach (LayoutDef def in LayoutCatalog.Layouts)
        {
            int bad = 0;
            string detail = "";
            int minFloor = int.MaxValue;
            int maxFloor = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                RasterLayout r = LayoutBuilder.Rasterize(def, new RandomNumberGenerator { Seed = (ulong)(seed * 104729 + 17) });
                HashSet<Vector2I> reach = Reachable(r, r.Entrance);
                var gate = new HashSet<Vector2I>(r.Gate);
                int floor = 0;
                int pinches = 0;
                Vector2I pinchAt = default;
                for (int x = 1; x < r.Width - 1; x++)
                {
                    for (int y = 1; y < r.Height - 1; y++)
                    {
                        if (!r.Walkable(new Vector2I(x, y)))
                        {
                            continue;
                        }

                        floor++;
                        bool solidL = r.Cells[x - 1, y] is CellKind.Void or CellKind.Pillar;
                        bool solidR = r.Cells[x + 1, y] is CellKind.Void or CellKind.Pillar;
                        bool solidU = r.Cells[x, y - 1] is CellKind.Void or CellKind.Pillar;
                        bool solidD = r.Cells[x, y + 1] is CellKind.Void or CellKind.Pillar;
                        if ((solidL && solidR && r.Cells[x - 1, y] == CellKind.Void && r.Cells[x + 1, y] == CellKind.Void)
                            || (solidU && solidD && r.Cells[x, y - 1] == CellKind.Void && r.Cells[x, y + 1] == CellKind.Void))
                        {
                            pinches++;
                            pinchAt = new Vector2I(x, y);
                        }
                    }
                }

                minFloor = Mathf.Min(minFloor, floor);
                maxFloor = Mathf.Max(maxFloor, floor);
                bool packsOk = r.Packs.Count >= 12 && r.Packs.All(p => reach.Contains(p.Cell) && !r.BossRegion.Contains(p.Cell) && !gate.Contains(p.Cell))
                    && r.Packs.All(p => r.Packs.All(q => q == p || (q.Cell - p.Cell).LengthSquared() >= 25));
                bool ok = r.Walkable(r.Entrance) && reach.Contains(r.BossCell) && r.BossRegion.Contains(r.BossCell) && !r.BossRegion.Contains(r.Entrance)
                    && r.Gate.Count is > 0 and <= 6 && packsOk && pinches == 0;
                if (!ok && bad++ == 0)
                {
                    detail = $"seed {seed}: boss reachable={reach.Contains(r.BossCell)}, gate={r.Gate.Count}, packs={r.Packs.Count} ok={packsOk}, one-cell gaps={pinches} (last at {pinchAt}, entrance {r.Entrance}, boss {r.BossCell})\n{Around(r.Dump(), pinchAt)}";
                }
            }

            Check($"CAVE-CONNECTED-{def.Id}", bad == 0, bad == 0 ? $"60 seeds: reachable boss behind a 1–6 cell gate, 12+ packs at least 5 cells apart, no one-cell gaps; floor {minFloor}–{maxFloor} cells" : detail);
        }

        // Spawning (endgame.md -> "Spawning"): packs sit in the hot parts of the density
        // field, every walking-distance zone gets some, and territories vary.
        float hotter = 0f;
        int zonesCovered = 0;
        int territorySets = 0;
        const int spawnSeeds = 30;
        for (int seed = 0; seed < spawnSeeds; seed++)
        {
            RasterLayout r = LayoutBuilder.Rasterize(LayoutCatalog.Layouts[seed % LayoutCatalog.Layouts.Count], new RandomNumberGenerator { Seed = (ulong)(seed * 7 + 3) });
            hotter += r.Packs.Average(p => p.Heat);
            zonesCovered += r.Packs.Select(p => p.Zone).Distinct().Count() >= 4 ? 1 : 0;
            territorySets += r.Packs.Select(p => p.Territory).Distinct().Count() >= 3 ? 1 : 0;
        }

        hotter /= spawnSeeds;
        Check("SPAWN-FIELDS", hotter > 0.5f && zonesCovered >= spawnSeeds - 2 && territorySets >= spawnSeeds - 2,
            $"mean pack heat {hotter:0.00} (field average ≈ 0.5), {zonesCovered}/{spawnSeeds} maps with packs in 4+ distance zones, {territorySets}/{spawnSeeds} with 3+ territories");

        var cells = new HashSet<Vector2I>();
        for (int x = 0; x < 40; x++)
        {
            for (int y = 0; y < 40; y++)
            {
                cells.Add(new Vector2I(x, y));
            }
        }

        List<Vector2I> disk = CaveGenerator.PoissonDisk(cells, 5f, _ => 1f, new RandomNumberGenerator { Seed = 5 });
        int minGap = int.MaxValue;
        foreach (Vector2I p in disk)
        {
            foreach (Vector2I q in disk)
            {
                if (p != q)
                {
                    minGap = Mathf.Min(minGap, (p - q).LengthSquared());
                }
            }
        }

        Check("POISSON-DISK", minGap >= 25 && disk.Count >= 35, $"{disk.Count} points in a 40×40 field, closest pair {Mathf.Sqrt(minGap):0.0} cells apart (min 5)");

        string a = LayoutBuilder.Rasterize(LayoutCatalog.Layouts[0], new RandomNumberGenerator { Seed = 99 }).Dump();
        string b = LayoutBuilder.Rasterize(LayoutCatalog.Layouts[0], new RandomNumberGenerator { Seed = 99 }).Dump();
        string other = LayoutBuilder.Rasterize(LayoutCatalog.Layouts[0], new RandomNumberGenerator { Seed = 100 }).Dump();
        Check("CAVE-SEED-DETERMINISTIC", a == b && a != other, "seed 99 twice gives the same map, seed 100 another");
    }

    // --- tiers and shards ---------------------------------------------------------

    /// <summary>docs/design/endgame.md -> "Monster scaling".</summary>
    private void MonsterScalingChecks()
    {
        (int Level, float Dps)[] table = MonsterScaling.ExpectedDpsTable;
        bool anchors = table.All(a => Mathf.Abs(MonsterScaling.ExpectedDps(a.Level) - a.Dps) < 0.5f);
        bool monotone = Enumerable.Range(2, 99).All(l => MonsterScaling.ExpectedDps(l) >= MonsterScaling.ExpectedDps(l - 1));
        float mid = MonsterScaling.ExpectedDps((table[0].Level + table[1].Level) / 2);
        Check("MONSTER-SCALING-TABLE", anchors && monotone && mid > table[0].Dps && mid < table[1].Dps,
            $"anchors hit: {anchors}, monotone: {monotone}, level {(table[0].Level + table[1].Level) / 2} -> {mid:0} dps");

        // Life = TTK × expected damage: a standard monster dies in TtkNormal, a rare in TtkRare.
        float lifeAt50 = MonsterScaling.StandardBaseLife * MonsterScaling.LifeMultiplier(50);
        float ttk = lifeAt50 / MonsterScaling.ExpectedDps(50);
        float rareTtk = ttk * MonsterScaling.RarityLife(MonsterRarity.Rare);
        float bossLife = 3000f * MonsterScaling.BossLifeMultiplier(50, 3000f);
        Check("MONSTER-SCALING-TTK",
            Mathf.Abs(ttk - MonsterScaling.TtkNormal) < 0.01f && Mathf.Abs(rareTtk - MonsterScaling.TtkRare) < 0.05f
            && Mathf.Abs(bossLife / MonsterScaling.ExpectedDps(50) - MonsterScaling.TtkBoss) < 0.1f,
            $"level 50: normal {ttk:0.00}s, rare {rareTtk:0.00}s, boss {bossLife / MonsterScaling.ExpectedDps(50):0.0}s");

        // Boss hits are aimed at the reference character's life (owner: standing still never killed you).
        float share1 = MonsterScaling.AuthoredBossHit * MonsterScaling.BossDamageMultiplier(1) / MonsterScaling.ReferenceLife(1);
        float share85 = MonsterScaling.AuthoredBossHit * MonsterScaling.BossDamageMultiplier(85) / MonsterScaling.ReferenceLife(85);
        Check("BOSS-DAMAGE-SHARE",
            Mathf.Abs(share1 - MonsterScaling.BossHitShare) < 0.01f && Mathf.Abs(share85 - MonsterScaling.BossHitShare) < 0.01f
            && MonsterScaling.BossDamageMultiplier(85) > MonsterScaling.DamageMultiplier(85),
            $"a typical boss hit takes {share1:P0} of the reference life at level 1, {share85:P0} at 85 (x{MonsterScaling.BossDamageMultiplier(1):0.0} -> x{MonsterScaling.BossDamageMultiplier(85):0.0})");

        // Defences: nothing at level 1; at 85 the base resistance, the theme's element more, and
        // armour that stops MaxArmourReduction of the reference hit.
        var low = GD.Load<PackedScene>("res://scenes/enemies/Slime.tscn").Instantiate<Enemy>();
        var high = GD.Load<PackedScene>("res://scenes/enemies/Slime.tscn").Instantiate<Enemy>();
        AddChild(low);
        AddChild(high);
        MonsterScaling.ApplyDefences(low, 1, DamageType.Fire);
        MonsterScaling.ApplyDefences(high, 85, DamageType.Fire);
        float hit = MonsterScaling.ExpectedDps(85) * MonsterScaling.ReferenceUseTime;
        float physTaken = DamageMitigation.MitigatePhysical(hit, high.Stats.Armour.Value) / hit;
        bool lowOk = low.Stats.FireResistance.Value < 25.01f && low.Stats.ColdResistance.Value < 0.01f && low.Stats.Armour.Value < 1f;
        bool highOk = Mathf.Abs(high.Stats.FireResistance.Value - (MonsterScaling.MaxResistance + MonsterScaling.AffinityResistance)) < 0.01f
            && Mathf.Abs(high.Stats.ColdResistance.Value - MonsterScaling.MaxResistance) < 0.01f
            && Mathf.Abs(physTaken - (1f - MonsterScaling.MaxArmourReduction)) < 0.01f;
        Check("MONSTER-SCALING-DEFENCES", lowOk && highOk,
            $"level 1 fire {low.Stats.FireResistance.Value:0}/cold {low.Stats.ColdResistance.Value:0}/armour {low.Stats.Armour.Value:0}; level 85 fire {high.Stats.FireResistance.Value:0}/cold {high.Stats.ColdResistance.Value:0}, reference hit takes {physTaken:P0}");
        low.QueueFree();
        high.QueueFree();

        // Added damage effectiveness: flat gear counts at the skill's rate.
        var flat = new List<StatModifier> { new(ModifierType.Flat, 10f, this), new(ModifierType.Increased, 100f, this) };
        var spark = GD.Load<SkillDefinition>("res://resources/skills/run/spark.tres");
        var meteor = GD.Load<SkillDefinition>("res://resources/skills/run/meteor.tres");
        Check("ADDED-DAMAGE-EFFECTIVENESS",
            Mathf.IsEqualApprox(Stat.ComputeFormula(10f, flat, 0.5f), 30f) && Mathf.IsEqualApprox(Stat.ComputeFormula(10f, flat), 40f)
            && spark.AddedDamageEffectiveness < 1f && meteor.AddedDamageEffectiveness > 1f,
            $"base 10 + flat 10 at 50% = {Stat.ComputeFormula(10f, flat, 0.5f):0} (x2 increased); Spark {spark.AddedDamageEffectiveness:P0}, Meteor {meteor.AddedDamageEffectiveness:P0}");
    }

    private void TierChecks()
    {
        bool rising = Enumerable.Range(2, RealityTiers.MaxTier - 1).All(t => RealityTiers.LifeMultiplier(t) > RealityTiers.LifeMultiplier(t - 1));
        Check("TIER-SCALING",
            rising && Mathf.IsEqualApprox(RealityTiers.LifeMultiplier(1), MonsterScaling.LifeMultiplier(1))
            && Mathf.Abs(RealityTiers.DamageMultiplier(20) - 5f) < 0.01f && RealityTiers.MonsterLevel(1) == 1 && RealityTiers.MonsterLevel(20) == 85
            && RealityTiers.Portals(14) == 3 && RealityTiers.Portals(15) == 1,
            $"life x{RealityTiers.LifeMultiplier(1):0.0} -> x{RealityTiers.LifeMultiplier(20):0}, dmg x{RealityTiers.DamageMultiplier(20):0.0}, mlvl {RealityTiers.MonsterLevel(20)} at tier 20");

        MonsterScalingChecks();

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
        Check("SHARD-TABLE", none == 0 && near(same, 0.65f) && near(plus1, 0.28f) && near(plus2, 0.07f) && none + same + plus1 + plus2 == n,
            $"none {none * 100f / n:0.0}% same {same * 100f / n:0.0}% +1 {plus1 * 100f / n:0.0}% +2 {plus2 * 100f / n:0.0}%");

        int over = 0;
        for (int i = 0; i < 2000; i++)
        {
            if (RealityTiers.RollBossShard(20, rng) > 20) over++;
        }

        Check("SHARD-CAP-20", over == 0, $"{over} shards above tier 20");

        float b1 = RealityTiers.PackBudget(1);
        float b20 = RealityTiers.PackBudget(20);
        Check("DENSITY-BY-TIER", b20 >= b1 * 2.3f && RealityTiers.BudgetScale(1f, 4) > RealityTiers.BudgetScale(0f, 0) * 1.5f
            && RealityTiers.ExtraPackChance(20) > RealityTiers.ExtraPackChance(1) && RealityTiers.RaresPerPack(20) > RealityTiers.RaresPerPack(1),
            $"pack budget {b1:0.0} → {b20:0.0} points, hot deep spot ×{RealityTiers.BudgetScale(1f, 4):0.00} vs cold near ×{RealityTiers.BudgetScale(0f, 0):0.00}, extra pack {RealityTiers.ExtraPackChance(1):P0} → {RealityTiers.ExtraPackChance(20):P0}");

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

        // The redesign (docs/design/classes.md -> "Passive tree"): ~340 nodes, so 69 points
        // take about a fifth; the nearest notable costs a real road; paths allocate in order.
        int notables = PassiveTree.Nodes.Values.Count(n => n.Kind == PassiveKind.Notable);
        CharacterData fresh = CharacterData.Create("Road", "arcanist");
        int nearest = PassiveTree.Nodes.Values.Where(n => n.Kind == PassiveKind.Notable).Min(n => PassiveTree.PathTo(fresh, n.Id).Count);
        List<string> road = PassiveTree.PathTo(fresh, "mana_notable");
        bool ordered = road.Count > 0 && road[^1] == "mana_notable";
        var walk = new HashSet<string>(fresh.Passives);
        foreach (string id in road)
        {
            ordered &= PassiveTree.Get(id)!.Links.Any(walk.Contains);
            walk.Add(id);
        }

        Check("PASSIVE-TREE-SIZE", PassiveTree.Nodes.Count is >= 320 and <= 380 && keystones == 12 && notables == 24 && nearest >= 7,
            $"{PassiveTree.Nodes.Count} nodes, {notables} notables, {keystones} keystones; nearest notable from a start costs {nearest} points (69 points at level 70 = {69f / PassiveTree.Nodes.Count:P0} of the tree)");
        Check("PASSIVE-PATH", ordered && PassiveTree.PathTo(fresh, "start_rogue").Count == 0,
            $"path to Wellspring: {road.Count} nodes, each adjacent to what came before: {ordered}");

        foreach (string id in road)
        {
            fresh.Passives.Add(id);
        }

        string saved = Profile.SerializeCharacter(fresh);
        CharacterData? same = Profile.DeserializeCharacter(saved);
        CharacterData? old = Profile.DeserializeCharacter(saved.Replace($"\"TreeVersion\":{PassiveTree.Version}", "\"TreeVersion\":1"));
        PassiveTree.ResetAll(fresh);
        Check("PASSIVE-RESET", fresh.Passives.Count == 1 && same is not null && same.Passives.Count == road.Count + 1 && old is not null && old.Passives.Count == 1 && PassiveTree.RefundCost(c) == 0,
            $"reset leaves the start; a save from this tree keeps {same?.Passives.Count} nodes, one from the old tree comes back with {old?.Passives.Count}; refunds cost {PassiveTree.RefundCost(c)}");
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

    // --- feedback batch (2026-10-12) -------------------------------------------------

    private void FeedbackChecks()
    {
        var rng = new RandomNumberGenerator { Seed = 99 };
        bool integers = true;
        bool fractional = false;
        for (int i = 0; i < 200; i++)
        {
            float v = Items.AffixRoller.RollValue(10.2f, 20.7f, rng);
            integers &= Mathf.IsEqualApprox(v, Mathf.Round(v)) && v >= 11f && v <= 20f;
            float f = Items.AffixRoller.RollValue(0.02f, 0.08f, rng);
            fractional |= !Mathf.IsEqualApprox(f, Mathf.Round(f * 100f) / 100f) || f is > 0.02f and < 0.08f;
        }

        Check("ROLLS-WHOLE-NUMBERS", integers && fractional, "ranges >= 1 roll integers (10.2-20.7 -> 11..20); 0.02-0.08 keeps decimals");

        var profile = new Profile();
        long first = profile.NextStashTabCost;
        profile.StashTabs.Add(new List<(Items.EquipmentItem, Vector2I)>());
        long second = profile.NextStashTabCost;
        while (profile.StashTabs.Count < Profile.MaxStashTabs)
        {
            profile.StashTabs.Add(new List<(Items.EquipmentItem, Vector2I)>());
        }

        Check("STASH-TAB-COSTS", first == 150 && second == 300 && profile.NextStashTabCost == -1, $"tab 2: {first}, tab 3: {second}, after {Profile.MaxStashTabs}: {profile.NextStashTabCost}");
        profile.Free();

        Check("LOADOUT-SIX-SLOTS", CharacterData.LoadoutSlots == 6 && CharacterBuild.Slots[0] == SkillSlotId.Lmb && CharacterBuild.Slots[1] == SkillSlotId.Rmb && CharacterBuild.Slots[5] == SkillSlotId.Slot4,
            $"{string.Join(", ", CharacterData.SlotLabels)}");

        var tab = new Items.Inventory { Columns = Profile.StashColumns, Rows = Profile.StashRows };
        Check("STASH-GRID-SIZE", tab.BuildOccupancyMask().GetLength(0) == 12 && tab.BuildOccupancyMask().GetLength(1) == 10, "stash tabs are 12x10 grids");
        tab.Free();
    }

    private async Task SprintChecks()
    {
        var player = GD.Load<PackedScene>("res://scenes/Player.tscn").Instantiate<Player>();
        AddChild(player);
        player.GlobalPosition = new Vector2(-6000f, 0f);
        await Frames(2);
        Vector2 start = player.GlobalPosition;
        Input.ActionPress("move_right");
        await Frames(20);
        float walked = player.GlobalPosition.X - start.X;
        Input.ActionPress("sprint");
        Vector2 sprintStart = player.GlobalPosition;
        await Frames(20);
        float sprinted = player.GlobalPosition.X - sprintStart.X;
        float drained = player.Stamina;
        Input.ActionRelease("sprint");
        Input.ActionRelease("move_right");
        await Frames(90);
        float recovered = player.Stamina;
        Check("SPRINT-STAMINA", sprinted > walked * 1.4f && drained < player.MaxStamina && recovered > drained,
            $"walk {walked:0} px vs sprint {sprinted:0} px in 20 frames; stamina {drained:0} -> {recovered:0} / {player.MaxStamina:0}");
        player.QueueFree();
        await Frames(2);
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
        SkillInstance? slot1 = player.SkillUser.GetSkill(SkillSlotId.Lmb);
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

        // Caverns (endgame.md -> "Caverns"): a pinned seed (tools) rebuilds the same
        // map; without one — as the Mirror opens them — every entry is a new map.
        var unseen = new HashSet<string>();
        for (int i = 0; i < 2; i++)
        {
            RealityLaunch.Tier = 6;
            var fresh = GD.Load<PackedScene>(Scenes.Reality).Instantiate<RealityController>();
            AddChild(fresh);
            await Frames(2);
            unseen.Add(fresh.Raster.Dump());
            bool ok = fresh.Seed is null && fresh.Layout.Cave is not null;
            fresh.QueueFree();
            await Frames(2);
            if (!ok)
            {
                unseen.Clear();
                break;
            }
        }

        Check("CAVE-NEW-EVERY-ENTRY", unseen.Count == 2, "two entries without a seed built two different caverns");

        foreach (ulong seed in new ulong[] { 11, 12, 13 })
        {
            RealityLaunch.Tier = 6;
            RealityLaunch.Seed = seed;
            var cave = GD.Load<PackedScene>(Scenes.Reality).Instantiate<RealityController>();
            AddChild(cave);
            await Frames(3);
            LayoutDef expected = RealityLaunch.LayoutFor(seed);
            string preview = LayoutBuilder.Rasterize(expected, new RandomNumberGenerator { Seed = seed }).Dump();
            Vector2I at = cave.Field.CellOf(cave.Player.GlobalPosition);
            Check($"CAVE-REALITY-{seed}", cave.Layout == expected && cave.Layout.Cave is not null && cave.Raster.Dump() == preview
                && cave.MonstersTotal >= 30 && cave.Boss is not null && (at - cave.Raster.Entrance).LengthSquared() <= 2 && cave.Seed == seed,
                $"{cave.Layout.Name}: {cave.MonstersTotal} monsters, same map as its seed={cave.Raster.Dump() == preview}, boss {cave.Boss?.DisplayName}, player cell {at} vs entrance {cave.Raster.Entrance}, seed {cave.Seed}");
            cave.QueueFree();
            await Frames(2);
        }

        // HUD layout editor (docs/design/menus.md -> "HUD layout editor"): panels
        // register, a move is saved and comes back in the next reality.
        UI.HudLayout.SavePath = "user://test_hud_layout.cfg";
        UI.HudLayout.Reload();
        UI.HudLayout.Reset();
        await Frames(2);
        var movables = GetTree().GetNodesInGroup(UI.HudLayout.Group).OfType<UI.HudMovable>().ToList();
        UI.HudMovable? vitals = movables.FirstOrDefault(m => m.Id == "vitals");
        bool moved = false;
        if (vitals is not null)
        {
            Vector2 before = vitals.Target.Position;
            vitals.MoveBy(new Vector2(-120f, -60f), GetViewport().GetVisibleRect().Size);
            moved = vitals.Target.Position.DistanceTo(before + new Vector2(-120f, -60f)) < 0.5f;
            vitals.SetScale(0.8f);
            UI.HudLayout.Save();
            UI.HudLayout.Reload();
        }

        Check("HUD-LAYOUT-SAVE", movables.Count >= 6 && moved && UI.HudLayout.OffsetOf("vitals").DistanceTo(new Vector2(-120f, -60f)) < 0.5f && Mathf.IsEqualApprox(UI.HudLayout.ScaleOf("vitals"), 0.8f),
            $"{movables.Count} movable panels ({string.Join(", ", movables.Select(m => m.Id))}), moved={moved}, saved offset {UI.HudLayout.OffsetOf("vitals")}, saved size {UI.HudLayout.ScaleOf("vitals"):P0}");

        // The small bars over the character.
        var overhead = reality.Player.GetNodeOrNull<Node2D>("OverheadBars");
        Check("OVERHEAD-BARS", overhead is not null && overhead.Visible, $"overhead bars present={overhead is not null}, visible={overhead?.Visible}");

        // The Tab overlay map. Headless has no window to deliver key events, so the
        // test toggles it directly and lets it draw a few frames.
        var map = reality.GetNode("Hud").FindChild("MapOverlay", true, false) as UI.MapOverlay;
        map?.Toggle();
        await Frames(4);
        bool opened = map?.Visible == true;
        map?.Toggle();
        Check("MAP-OVERLAY", map is not null && opened && !map.Visible && InputMap.HasAction(UI.MapOverlay.Action),
            $"overlay found={map is not null}, opened and drew={opened}, closed again={map?.Visible == false}, Tab bound={InputMap.HasAction(UI.MapOverlay.Action)}");

        // Flasks (docs/design/items.md -> "Flasks and refill orbs"): Q drinks, the
        // recovery lands over time, a full pool refuses, orbs refill charges.
        Flasks? flasks = Flasks.Of(reality.Player);
        Stats.ResourcePool? pool = reality.Player.Stats.Life;
        if (flasks is not null && pool is not null)
        {
            pool.Current = pool.Max * 0.3f;
            Input.ActionPress(Flasks.LifeAction);
            await Frames(2);
            Input.ActionRelease(Flasks.LifeAction);
            await ToSignal(GetTree().CreateTimer(1.8), SceneTreeTimer.SignalName.Timeout);
            float healed = pool.Current / pool.Max;
            int afterDrink = flasks.Life.Usable;
            pool.Current = pool.Max;
            bool refused = !flasks.TryDrink(flasks.Life);
            bool refilled = flasks.Refill() && flasks.Life.Usable == flasks.Life.MaxCharges;
            Pickup orb = Pickup.Spawn(reality, PickupKind.Life, 0.2f, reality.Player.GlobalPosition + new Vector2(30f, 0f));
            flasks.Mana.Charges = flasks.Mana.MaxCharges;
            await Frames(20);
            bool orbStays = IsInstanceValid(orb) && !orb.IsQueuedForDeletion();
            Check("FLASKS", healed > 0.65f && afterDrink == flasks.Life.MaxCharges - 1 && refused && refilled && orbStays,
                $"life 30% → {healed:P0} after Q, {afterDrink}/{flasks.Life.MaxCharges} charges left, refused at full life={refused}, orb refill={refilled}, orb left on the ground while full={orbStays}");
            if (IsInstanceValid(orb))
            {
                orb.QueueFree();
            }
        }

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

        // The other two realities: built, populated with their own roster, and
        // their boss fights (Bone Herald, Nereth) produce telegraphs once woken
        // (telegraphs spawn under the current scene, which is this test).
        foreach (string id in new[] { "bone_crypt", "drowned_mirror" })
        {
            RealityLaunch.Tier = 8;
            RealityLaunch.LayoutId = id;
            RealityLaunch.Modifiers = new List<string>();
            var r = GD.Load<PackedScene>(Scenes.Reality).Instantiate<RealityController>();
            AddChild(r);
            await Frames(5);
            var roster = r.GetChildren().OfType<Enemy>().Where(e => !e.IsBoss).Select(e => e.DisplayName).Distinct().ToList();
            if (id == "bone_crypt")
            {
                await Frames(2);
                UI.HudMovable? v = GetTree().GetNodesInGroup(UI.HudLayout.Group).OfType<UI.HudMovable>().FirstOrDefault(m => m.Id == "vitals" && r.IsAncestorOf(m));
                Check("HUD-LAYOUT-APPLIED", v is not null && v.Target.Position.DistanceTo(v.Default + new Vector2(-120f, -60f)) < 0.5f && Mathf.IsEqualApprox(v.Target.Scale.X, 0.8f),
                    $"the next reality's life cluster sits at its default {v?.Default} + the saved offset: {v?.Target.Position}, size {v?.Target.Scale.X:P0}");
                UI.HudLayout.Reset();
                UI.HudLayout.Save();
                UI.HudLayout.SavePath = "user://hud_layout.cfg";
                UI.HudLayout.Reload();
            }

            r.Player.Health!.IgnoreDamage = true;
            r.Player.GlobalPosition = RasterLayout.World(r.Raster.BossCell) + new Vector2(0f, 150f);
            int telegraphs = 0;
            for (int i = 0; i < 360 && telegraphs == 0; i++)
            {
                await Frames(1);
                telegraphs = GetChildren().Count(n => n is AreaBurst or LineStrike or BossBullet);
            }

            // The target frame follows the boss, and its cast bar runs while a telegraphed
            // attack winds up (bullets don't count: they aren't charged).
            var frame = r.GetNode("Hud").FindChild("TargetFrame", true, false) as UI.TargetFrame;
            float cast = -1f;
            for (int i = 0; i < 600 && (cast < 0f || frame?.Target != r.Boss); i++)
            {
                await Frames(1);
                cast = Mathf.Max(cast, r.Boss?.CastProgress ?? -1f);
            }

            Check($"TARGET-FRAME-{id}", frame?.Target == r.Boss && cast >= 0f,
                $"target frame shows {frame?.Target?.DisplayName ?? "nothing"} (boss {r.Boss?.DisplayName}), cast bar at {cast:P0} while the first telegraph is pending");
            Check($"REALITY-{id}", r.MonstersTotal >= 20 && r.Boss is not null && r.BossFightActive && telegraphs > 0,
                $"{r.MonstersTotal} monsters ({string.Join(", ", roster)}), boss {r.Boss?.DisplayName}, first attack '{r.Boss?.CurrentAttack}' with {telegraphs} telegraphs");
            r.QueueFree();
            await Frames(2);
        }
    }

    // --- the introduction (docs/design/tutorial.md) ---------------------------------

    private async Task TutorialChecks()
    {
        // New characters owe the introduction; old saves (no field) don't.
        var profile = new Profile { Persist = false };
        CharacterData fresh = profile.CreateCharacter("Newcomer", "warrior");
        CharacterData? back = Profile.DeserializeCharacter(Profile.SerializeCharacter(fresh));
        CharacterData? legacy = Profile.DeserializeCharacter(Profile.SerializeCharacter(fresh).Replace("\"TutorialDone\":false,", ""));
        Check("TUTORIAL-FLAG", !fresh.TutorialDone && back is { TutorialDone: false } && legacy is { TutorialDone: true },
            $"new={fresh.TutorialDone}, round-trip={back?.TutorialDone}, legacy save={legacy?.TutorialDone}");
        profile.Free();

        RealityLaunch.Tutorial = true;
        var reality = GD.Load<PackedScene>(Scenes.Reality).Instantiate<RealityController>();
        AddChild(reality);
        await Frames(5);
        var guide = reality.GetNodeOrNull<TutorialGuide>("TutorialGuide");
        int packMonsters = reality.GetChildren().OfType<Enemy>().Count(e => !e.IsBoss);
        Check("TUTORIAL-BUILT", reality.IsTutorial && reality.Layout == LayoutCatalog.Tutorial && guide is not null && reality.PortalsLeft == RealityController.TutorialPortals
            && packMonsters == 5 && reality.Boss is not null && !LayoutCatalog.Layouts.Contains(LayoutCatalog.Tutorial),
            $"tutorial={reality.IsTutorial}, layout={reality.Layout.Id}, guide={guide is not null}, {packMonsters} pack monsters (3 yard + 2 magic), portals={reality.PortalsLeft}");
        if (guide is null)
        {
            return;
        }

        Player p = reality.Player;
        p.Health!.IgnoreDamage = true;
        var steps = new List<string> { guide.StepTitle };
        async Task Next()
        {
            for (int i = 0; i < 30 && steps[^1] == guide.StepTitle; i++)
            {
                await Frames(1);
            }

            steps.Add(guide.StepTitle);
        }

        p.GlobalPosition += new Vector2(500f, 0f);
        await Next();
        p.Dodge!.EmitSignal(Dodge.SignalName.Dodged);
        await Next();
        foreach (Enemy e in RoomMonsters(reality, "yard"))
        {
            e.Health.ApplyResolvedDamage(1e9f, p);
        }

        await Next();
        EquipmentItem? gift = reality.GetChildren().OfType<EquipmentItem>().LastOrDefault();
        var bag = p.GetNode<Inventory>("Inventory");
        if (gift is not null)
        {
            gift.GetParent().RemoveChild(gift);
            bag.TryAdd(gift);
        }

        await Next();
        if (gift is not null)
        {
            bag.TryRightClickEquip(gift, p.GetNode<Equipment>("Equipment"));
        }

        await Next();
        foreach (Enemy e in RoomMonsters(reality, "cache"))
        {
            e.Health.ApplyResolvedDamage(1e9f, p);
        }

        await Next();
        p.GlobalPosition = RasterLayout.World(reality.Raster.Entrance);
        reality.Boss!.Health.ApplyResolvedDamage(1e9f, p);
        await Next();

        Check("TUTORIAL-STEPS", guide.StepIndex == guide.StepCount - 1 && steps.Distinct().Count() == guide.StepCount,
            $"reached step {guide.StepIndex + 1}/{guide.StepCount}: {string.Join(" → ", steps.Distinct())}");
        reality.QueueFree();
        await Frames(2);
    }

    // --- crafting, merchant, disenchanting, forging (docs/design/crafting.md) --------

    private async Task CraftingChecks()
    {
        var rng = new RandomNumberGenerator { Seed = 1234 };
        IReadOnlyList<AffixDefinition> pool = ItemFactory.Affixes.Entries;
        // A base with plenty of modifiers to choose from, at a high item level.
        BaseItemDefinition ring = ItemFactory.Bases.OrderByDescending(b => Crafting.ForgeableAffixes(b, 80).Count).First();
        CraftingCurrency Orb(CraftAction a) => Crafting.Orbs.First(o => o.Action == a);

        EquipmentItem item = ItemFactory.Roll(ring, Rarity.Common, 80, rng);
        bool refused = !Crafting.Apply(Orb(CraftAction.Tempest), item, rng, out string why) && item.Rarity == Rarity.Common;
        Check("CRAFT-WRONG-RARITY-REFUSED", refused && why.Contains("rare"), $"Tempest on a common item: '{why}', item still {item.Rarity}");

        bool kindled = Crafting.Apply(Orb(CraftAction.Kindle), item, rng, out _) && item.Rarity == Rarity.Magic && item.RolledAffixes.Count is >= 1 and <= 2;
        bool fluxed = Crafting.Apply(Orb(CraftAction.Flux), item, rng, out _) && item.Rarity == Rarity.Magic;
        if (item.RolledAffixes.Count == 2)
        {
            Crafting.Apply(Orb(CraftAction.Sunder), item, rng, out _);
        }

        bool grafted = Crafting.Apply(Orb(CraftAction.Graft), item, rng, out _) && item.RolledAffixes.Count == 2;
        Check("CRAFT-MAGIC-ORBS", kindled && fluxed && grafted, $"kindle → magic, flux keeps magic, graft fills to 2 — now {item.Descriptor.DisplayName} ({item.RolledAffixes.Count} mods)");

        int before = item.RolledAffixes.Count;
        bool crowned = Crafting.Apply(Orb(CraftAction.Crown), item, rng, out _) && item.Rarity == Rarity.Rare && item.RolledAffixes.Count == before + 1;
        bool ascended = Crafting.Apply(Orb(CraftAction.Ascend), item, rng, out _) && item.RolledAffixes.Count == 4;
        bool full = Crafting.CannotApply(Orb(CraftAction.Ascend), item) is not null;
        bool tempest = Crafting.Apply(Orb(CraftAction.Tempest), item, rng, out _) && item.Rarity == Rarity.Rare && item.RolledAffixes.Count is >= 3 and <= 4;
        var pools = item.RolledAffixes.GroupBy(a => a.Definition.Pool).Select(g => g.Count()).ToList();
        Check("CRAFT-RARE-ORBS", crowned && ascended && full && tempest && pools.All(n => n <= AffixRoller.MaxPerPool),
            $"crown +1 → rare, ascend fills to 4 then refuses, tempest rerolls 3–4 (pools {string.Join("/", pools)})");

        var tiers = item.RolledAffixes.Select(a => (a.Definition.Id, a.Tier)).ToList();
        bool tuned = Crafting.Apply(Orb(CraftAction.Tune), item, rng, out _)
            && item.RolledAffixes.Select(a => (a.Definition.Id, a.Tier)).SequenceEqual(tiers)
            && item.RolledAffixes.All(a => a.Value >= a.Definition.ValueRangeForTier(a.Tier).Min - 0.01f && a.Value <= a.Definition.ValueRangeForTier(a.Tier).Max + 0.01f);
        int count = item.RolledAffixes.Count;
        bool sundered = Crafting.Apply(Orb(CraftAction.Sunder), item, rng, out _) && item.RolledAffixes.Count == count - 1 && item.Rarity == Rarity.Rare;
        bool ashed = Crafting.Apply(Orb(CraftAction.Ash), item, rng, out _) && item.Rarity == Rarity.Common && item.RolledAffixes.Count == 0 && item.Descriptor.DisplayName == ring.DisplayName;
        Check("CRAFT-TUNE-SUNDER-ASH", tuned && sundered && ashed, $"tune keeps mods and tiers, sunder removes one, ash → common '{item.Descriptor.DisplayName}'");

        EquipmentItem common = ItemFactory.Roll(ring, Rarity.Common, 20, rng);
        EquipmentItem magic = ItemFactory.Roll(ring, Rarity.Magic, 20, rng);
        EquipmentItem rare = ItemFactory.Roll(ring, Rarity.Rare, 20, rng);
        Check("MERCHANT-PRICES", Crafting.SellPrice(common) > 0 && Crafting.SellPrice(common) < Crafting.SellPrice(magic) && Crafting.SellPrice(magic) < Crafting.SellPrice(rare),
            $"common {Crafting.SellPrice(common)} < magic {Crafting.SellPrice(magic)} < rare {Crafting.SellPrice(rare)} gold");
        Check("DISENCHANT-YIELD", Crafting.DisenchantYield(common).Dust.Id == "grey_dust" && Crafting.DisenchantYield(magic).Dust.Id == "azure_dust"
            && Crafting.DisenchantYield(rare).Dust.Id == "gilded_dust" && Crafting.DisenchantYield(rare).Amount >= 1,
            $"common → {Crafting.DisenchantYield(common).Dust.Name}, magic → {Crafting.DisenchantYield(magic).Dust.Name}, rare → {Crafting.DisenchantYield(rare).Amount} {Crafting.DisenchantYield(rare).Dust.Name}");

        // Every base item and every currency has inventory art (docs/design/items.md -> "Item art").
        var missing = ItemFactory.Bases.Where(b => ItemArt.ForBase(b.Id) is null).Select(b => b.Id)
            .Concat(Crafting.All.Where(c => ItemArt.ForCurrency(c.Id) is null).Select(c => c.Id)).ToList();
        bool sized = ItemFactory.Bases.All(b => ItemArt.ForBase(b.Id) is not { } t || (t.GetWidth() == b.Width * 16 && t.GetHeight() == b.Height * 16));
        Check("ITEM-ART", missing.Count == 0 && sized && ItemArt.ForCurrency("artificer_seal") is not null,
            missing.Count == 0 ? $"{ItemFactory.Bases.Count} item icons sized to their grid footprint, {Crafting.All.Count()} currency icons" : $"missing: {string.Join(", ", missing)}");

        AffixDefinition chosen = Crafting.ForgeableAffixes(ring, 80).First();
        Crafting.ForgeGrade gilded = Crafting.Grades.First(g => g.Name.StartsWith("Gilded"));
        Crafting.ForgeGrade grey = Crafting.Grades.First(g => g.Name.StartsWith("Grey"));
        EquipmentItem forged = Crafting.Forge(ring, chosen, gilded, 80, rng);
        EquipmentItem rough = Crafting.Forge(ring, chosen, grey, 80, rng);
        AffixRoller.RolledAffix? mine = forged.RolledAffixes.FirstOrDefault(a => a.Definition == chosen);
        Check("FORGE-GUARANTEED-MOD", forged.Rarity == Rarity.Rare && mine is { Crafted: true } && mine.Tier == chosen.MaxReachableTier(80)
            && forged.RolledAffixes.Count >= 3 && rough.Rarity == Rarity.Magic && rough.RolledAffixes[0].Tier == Mathf.Max(1, chosen.MaxReachableTier(80) - 2),
            $"gilded: {forged.Descriptor.DisplayName} with {chosen.Id} T{mine?.Tier} crafted={mine?.Crafted} (+{forged.RolledAffixes.Count - 1} random); grey: magic T{rough.RolledAffixes[0].Tier}");

        // Crafted flags and currency balances survive the save.
        CharacterData c = CharacterData.Create("Smith", "warrior");
        c.Inventory.Add((forged, Vector2I.Zero));
        CharacterData? back = Profile.DeserializeCharacter(Profile.SerializeCharacter(c));
        EquipmentItem? saved = back?.Inventory.FirstOrDefault().Item;

        // Orbs and dust belong to the account; old per-character balances are pooled.
        var account = new Profile { Persist = false };
        account.Currency["tempest_orb"] = 3;
        account.Characters.Add(c);
        // The character's own (empty) currency table stands in for an old save's.
        string json = account.ToJson().Replace("\"Currency\": {}", "\"Currency\": { \"tempest_orb\": 2 }");
        var reloaded = new Profile { Persist = false };
        reloaded.LoadJson(json);
        Check("CRAFT-SAVE-ROUND-TRIP", saved is not null && saved.RolledAffixes.Any(a => a.Crafted && a.Definition == chosen)
            && reloaded.Currency.GetValueOrDefault("tempest_orb") == 5 && reloaded.Characters[0].Currency.Count == 0,
            $"crafted mod kept={saved?.RolledAffixes.Any(a => a.Crafted)}, account tempest orbs={reloaded.Currency.GetValueOrDefault("tempest_orb")} (3 account + 2 from an old per-character save)");
        account.Free();
        reloaded.Free();

        // The stations, live: sell + buy back + buy at the merchant; orbs, disenchant
        // and forge at the bench.
        var player = GD.Load<PackedScene>("res://scenes/Player.tscn").Instantiate<Player>();
        AddChild(player);
        player.GlobalPosition = new Vector2(-4000f, 0f);
        var merchant = new UI.MerchantPanel { Player = player };
        AddChild(merchant);
        await Frames(1);
        EquipmentItem loot = ItemFactory.Roll(ring, Rarity.Rare, 30, rng);
        player.Inventory.TryAdd(loot);
        merchant.Open();
        long gold0 = player.Wallet.GetBalance(Profile.GoldId);
        bool sold = merchant.Sell(loot) && !player.Inventory.Placements.ContainsKey(loot) && merchant.Buyback.Placements.ContainsKey(loot);
        long gold1 = player.Wallet.GetBalance(Profile.GoldId);
        bool boughtBack = merchant.BuyBack(loot) && player.Inventory.Placements.ContainsKey(loot) && player.Wallet.GetBalance(Profile.GoldId) == gold0;
        merchant.Sell(loot);
        player.Wallet.Add(Profile.GoldId, 100);
        bool bought = merchant.Buy(Orb(CraftAction.Kindle)) && player.Wallet.GetBalance("kindling_shard") == 1;
        merchant.Close();
        Check("MERCHANT-SELL-BUYBACK", sold && gold1 == gold0 + Crafting.SellPrice(loot) && boughtBack && bought && merchant.Buyback.Placements.Count == 0,
            $"sold for {gold1 - gold0}, bought back={boughtBack}, bought a shard={bought}, buyback cleared on close");

        var bench = new UI.ArtificerPanel { Player = player };
        AddChild(bench);
        await Frames(1);
        EquipmentItem plain = ItemFactory.Roll(ring, Rarity.Common, 30, rng);
        player.Inventory.TryAdd(plain);
        // Orbs are used from the inventory's Crafting tab.
        var inventoryUi = player.GetNode<UI.InventoryUI>("InventoryUI");
        inventoryUi.SetOpen(true);
        inventoryUi.ShowCrafting(true);
        UI.CraftingBoard board = inventoryUi.Board;
        board.Arm(Orb(CraftAction.Kindle));
        bool applied = board.ApplyArmed(plain) && plain.Rarity == Rarity.Magic && player.Wallet.GetBalance("kindling_shard") == 0 && board.Armed is null;
        Check("INVENTORY-CRAFTING-TAB", applied && inventoryUi.CraftingShown && board.Visible && !player.GetNode<Control>("InventoryUI/Panel/StripControl").Visible,
            $"kindling used from the I panel's Crafting tab: {plain.Descriptor.DisplayName}");

        // Seals: protect one modifier from the next orb; every seal on the same item costs more.
        EquipmentItem target = ItemFactory.Roll(ring, Rarity.Rare, 60, rng);
        player.Inventory.TryAdd(target);
        player.Wallet.Add(Profile.GoldId, 1000);
        player.Wallet.Add("gilded_dust", 10);
        player.Wallet.Add("tempest_orb", 2);
        long firstCost = Crafting.SealCost(target).Gold;
        AffixDefinition keptDef = target.RolledAffixes[0].Definition;
        float keptValue = target.RolledAffixes[0].Value;
        bool sealedOk = board.SealForTest(target, 0);
        long secondCost = Crafting.SealCost(target).Gold;
        board.Arm(Orb(CraftAction.Tempest));
        board.ApplyArmed(target);
        bool kept = target.RolledAffixes.Any(a => a.Definition == keptDef && Mathf.IsEqualApprox(a.Value, keptValue));
        bool broke = target.RolledAffixes.All(a => !a.Sealed);
        bool persisted = Profile.DeserializeCharacter(Profile.SerializeCharacter(new CharacterData { Inventory = { (target, Vector2I.Zero) } }))
            ?.Inventory.FirstOrDefault().Item?.SealCount == 1;
        Check("CRAFT-SEAL", sealedOk && kept && broke && secondCost > firstCost && target.SealCount == 1 && persisted,
            $"sealed mod survived Tempest={kept}, seal broke after it={broke}, cost {firstCost} → {secondCost}, seal count saved={persisted}");
        inventoryUi.ShowCrafting(false);
        inventoryUi.SetOpen(false);

        bench.Open();
        bench.Show(UI.ArtificerPanel.Tab.Disenchant);
        bool disenchanted = bench.Disenchant(plain) && player.Wallet.GetBalance("azure_dust") >= 1 && !player.Inventory.Placements.ContainsKey(plain);
        bench.Show(UI.ArtificerPanel.Tab.Forge);
        player.Wallet.Add("grey_dust", 6);
        player.Wallet.Add(Profile.GoldId, grey.Gold);
        bench.SelectForge(ring, chosen);
        long greyBefore = player.Wallet.GetBalance("grey_dust");
        EquipmentItem? made = bench.Forge(grey);
        Check("ARTIFICER-BENCH", applied && disenchanted && made is not null && player.Inventory.Placements.ContainsKey(made)
            && made.RolledAffixes.Any(a => a.Crafted && a.Definition == chosen) && player.Wallet.GetBalance("grey_dust") == greyBefore - 6,
            $"orb applied={applied}, disenchant={disenchanted}, forged={made?.Descriptor.DisplayName}");
        bench.Close();

        // The hideout has both stations.
        player.QueueFree();
        merchant.QueueFree();
        bench.QueueFree();
        await Frames(2);
        await HideoutChecks();
    }

    // --- the hideout town, its editor and shop (docs/design/hideout.md) ----------------

    private async Task HideoutChecks()
    {
        // Never touch the real account: an empty town, nothing written to disk.
        Profile? profile = Profile.Get(this);
        bool persisted = profile?.Persist ?? false;
        HideoutData? realTown = profile?.Hideout;
        if (profile is not null)
        {
            profile.Persist = false;
            profile.Hideout = new HideoutData { GuideSeen = true };
        }

        var hideout = GD.Load<PackedScene>("res://scenes/Hideout.tscn").Instantiate<Hideout>();
        AddChild(hideout);
        await Frames(4);

        bool stations = new[] { "Mirror", "Merchant", "Artificer", "Stash", "Altar", "Decorator" }.All(n => hideout.GetNodeOrNull(n) is not null)
            && hideout.GetNodeOrNull<UI.MerchantPanel>("MerchantPanel") is not null && hideout.GetNodeOrNull<UI.ArtificerPanel>("ArtificerPanel") is not null;
        Vector2 mirror = hideout.Data.Find("mirror")!.Position;
        Vector2I forgeCell = HideoutTown.CellOf(hideout.Data.Find("forge")!.Position);
        bool inPlaza = mirror.DistanceTo(HideoutTown.World(new Vector2I(25, 15))) < 40f && HideoutTown.BaseChar(HideoutTown.CellOf(hideout.Data.Find("merchant")!.Position)) is '.' or 'o';
        bool inSmithy = HideoutTown.BaseChar(forgeCell) == 'p';
        Check("HIDEOUT-STATIONS", stations && inPlaza && inSmithy,
            $"Mirror and Odran in the plaza, the forge in the smithy ({forgeCell}), stash, altar and Ysolde's shop in town");

        // The HUD explains the town: a directory of every station, an E prompt naming
        // the one you stand next to, and a click in the directory walks you there.
        UI.HideoutHud hud = hideout.Hud;
        hideout.Player.GlobalPosition = mirror + new Vector2(0f, 40f);
        for (int i = 0; i < 3; i++)
        {
            // The HUD updates on process frames, which headless runs less often than physics frames.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        string prompt = hud.PromptText;
        hideout.WalkTo("forge");
        Vector2 target = hideout.Player.GetNode<NavigationAgent2D>("NavigationAgent2D").TargetPosition;
        hideout.OpenGuide();
        await Frames(1);
        var guide = hideout.GetNode<UI.TownGuidePanel>("TownGuide");
        bool guideOpen = guide.IsOpen;
        guide.Close();
        Check("HIDEOUT-HUD", hud.DirectoryEntries.Count == HideoutCatalog.Props.Count(p => p.Station) && prompt.StartsWith("Mirror of Realities")
            && target.DistanceTo(hideout.Data.Find("forge")!.Position) < 60f && guideOpen && !guide.IsOpen && hideout.Data.GuideSeen,
            $"directory: {string.Join(", ", hud.DirectoryEntries)}; prompt '{prompt}' (paused={GetTree().Paused}, panels open={UI.PanelStack.HasOpen}, near={hideout.NearestStation()?.Def.Id}, player at {hideout.Player.GlobalPosition}, mirror {mirror}); walk-to target {target.DistanceTo(hideout.Data.Find("forge")!.Position):0} px from the forge; guide opened={guideOpen}");
        hideout.Player.GetNode<NavigationAgent2D>("NavigationAgent2D").TargetPosition = hideout.Player.GlobalPosition;

        // Opening the inventory never paints HUD over it: it draws above the HUD, the
        // town directory fades and the menu bar slides out from under it.
        var inv = hideout.Player.GetNode<UI.InventoryUI>("InventoryUI");
        var movables = GetTree().GetNodesInGroup(UI.HudLayout.Group).OfType<UI.HudMovable>().Where(m => m.IsAncestorOf(hud) || hud.IsAncestorOf(m)).ToList();
        UI.HudMovable? town = movables.FirstOrDefault(m => m.Id == "town");
        UI.HudMovable? menuBar = movables.FirstOrDefault(m => m.Id == "hideout_menu");
        inv.SetOpen(true);
        for (int i = 0; i < 40; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        Rect2 block = UI.HudLayout.OpenBlockers().FirstOrDefault();
        bool townHidden = town is not null && !town.Target.Visible;
        bool menuClear = menuBar is not null && !menuBar.Target.GetGlobalRect().Intersects(block);
        inv.SetOpen(false);
        for (int i = 0; i < 40; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        bool restored = town is not null && town.Target.Visible && menuBar is not null && menuBar.AvoidShift == Vector2.Zero;
        Check("HUD-AVOIDS-INVENTORY", inv.Layer > 10 && townHidden && menuClear && restored,
            $"inventory layer {inv.Layer}, town directory hidden={townHidden}, menu bar clear of the panel={menuClear}, back in place after closing={restored} (town visible={town?.Target.Visible}, menu shift={menuBar?.AvoidShift}, blockers open={UI.HudLayout.OpenBlockers().Count})");

        // Walls block, and click-to-move can path from the plaza into the smithy.
        Vector2 from = HideoutTown.World(HideoutTown.SpawnCell);
        Vector2 to = HideoutTown.World(forgeCell + new Vector2I(1, 2));
        Vector2[] path = NavigationServer2D.MapGetPath(hideout.GetWorld2D().NavigationMap, from, to, true);
        bool reaches = path.Length > 1 && path[^1].DistanceTo(to) < 20f;
        bool walled = !HideoutTown.Walkable(new Vector2I(3, 12)) && !HideoutTown.Walkable(new Vector2I(5, 24)) && HideoutTown.Walkable(HideoutTown.SpawnCell);
        Check("HIDEOUT-TOWN", reaches && walled && hideout.Town.Maps.Count() >= 4,
            $"path plaza → smithy: {path.Length} points, ends {(path.Length > 0 ? path[^1].DistanceTo(to) : -1):0} px from the forge; walls and canal block");

        // Ysolde's shop: floor tiles come in packs, decorations one at a time.
        var shop = hideout.GetNode<UI.DecorShopPanel>("DecorShopPanel");
        hideout.Player.Wallet.Add(Profile.GoldId, 2000);
        FloorStyle marble = HideoutCatalog.Floor("marble");
        PropDef statueDef = HideoutCatalog.Prop("statue")!;
        long gold0 = hideout.Player.Wallet.GetBalance(Profile.GoldId);
        bool boughtFloor = shop.BuyFloor(marble) && hideout.Data.StockOf("marble") == HideoutCatalog.FloorPack;
        bool boughtDecor = shop.BuyDecor(statueDef) && hideout.Data.StockOf("statue") == 1;
        bool noStations = !shop.BuyDecor(HideoutCatalog.Prop("mirror")!);
        Check("HIDEOUT-SHOP", boughtFloor && boughtDecor && noStations && hideout.Player.Wallet.GetBalance(Profile.GoldId) == gold0 - marble.PackPrice - statueDef.Price,
            $"bought {HideoutCatalog.FloorPack} marble tiles ({marble.PackPrice}g) and a statue ({statueDef.Price}g); stations aren't for sale");

        // Edit mode: drag the Mirror, store a brazier, place the statue, pave a cell.
        hideout.OpenEditor();
        await Frames(1);
        UI.HideoutEditor editor = hideout.Editor!;
        editor.Use(UI.HideoutEditor.Tool.Move);
        float pick = HideoutCatalog.Prop("mirror")!.PickRadius;
        Vector2 grabAt = mirror + new Vector2(0f, -pick * 0.6f);
        Vector2 newSpot = HideoutTown.World(new Vector2I(25, 12));
        editor.PressAt(grabAt);
        editor.DragTo(grabAt + (newSpot - mirror));
        editor.Release();
        bool moved = hideout.Data.Find("mirror")!.Position.DistanceTo(newSpot) < 6f && hideout.Props["mirror"].GlobalPosition.DistanceTo(newSpot) < 6f;
        editor.PressAt(HideoutTown.World(new Vector2I(4, 12)) + new Vector2(0f, -pick * 0.6f));
        editor.DragTo(HideoutTown.World(new Vector2I(3, 12)));
        editor.Release();
        bool notIntoWalls = hideout.Data.Props.All(p => HideoutTown.Walkable(HideoutTown.CellOf(p.Position)));

        string brazier = hideout.Data.Props.First(p => p.Kind == "brazier").Uid;
        Vector2 brazierAt = hideout.Data.Find(brazier)!.Position;
        int propsBefore = hideout.Data.Props.Count;
        editor.RightAt(brazierAt + new Vector2(0f, -15f));
        bool stored = hideout.Data.Find(brazier) is null && hideout.Data.StockOf("brazier") == 1 && hideout.Data.Props.Count == propsBefore - 1;
        editor.RightAt(hideout.Data.Find("stash")!.Position + new Vector2(0f, -15f));
        bool stationsStay = hideout.Data.Find("stash") is not null;
        editor.Use(UI.HideoutEditor.Tool.Decorations);
        editor.SelectDecor("statue");
        Vector2 statueSpot = HideoutTown.World(new Vector2I(22, 23));
        editor.PressAt(statueSpot);
        bool placed = hideout.Data.Props.Any(p => p.Kind == "statue" && p.Position.DistanceTo(statueSpot) < 8f) && hideout.Data.StockOf("statue") == 0;

        editor.Use(UI.HideoutEditor.Tool.Floors);
        editor.SelectFloor("marble");
        Vector2I paveCell = new(24, 22);
        editor.PressAt(HideoutTown.World(paveCell));
        editor.Release();
        bool paved = hideout.Town.StyleAt(paveCell) == "marble" && hideout.Data.StockOf("marble") == HideoutCatalog.FloorPack - 1;
        editor.PressAt(HideoutTown.World(new Vector2I(3, 12)));
        editor.Release();
        bool wallsUnpaved = hideout.Town.StyleAt(new Vector2I(3, 12)) == "wall";
        editor.RightAt(HideoutTown.World(paveCell));
        bool refunded = hideout.Town.StyleAt(paveCell) == "cobble" && hideout.Data.StockOf("marble") == HideoutCatalog.FloorPack;
        editor.Use(UI.HideoutEditor.Tool.Floors);
        editor.SelectFloor("marble");
        editor.PressAt(HideoutTown.World(paveCell));
        editor.Release();
        editor.Close();
        await Frames(1);
        Check("HIDEOUT-EDIT", moved && notIntoWalls && stored && stationsStay && placed && paved && wallsUnpaved && refunded && !hideout.Editing,
            $"moved the Mirror={moved}, props stay off walls={notIntoWalls}, brazier stored={stored}, stash can't be stored={stationsStay}, statue placed={placed}, paved={paved}, walls unpaintable={wallsUnpaved}, right-click refunds={refunded}");

        // The arrangement is the account's: it survives the save file.
        var account = new Profile { Persist = false };
        account.Hideout = hideout.Data;
        var reread = new Profile { Persist = false };
        reread.LoadJson(account.ToJson());
        HideoutData back = reread.Hideout;
        bool same = back.Initialized && back.Props.Count == hideout.Data.Props.Count
            && back.Find("mirror")!.Position.DistanceTo(newSpot) < 6f && back.Floors.GetValueOrDefault(HideoutData.Key(paveCell)) == "marble"
            && back.StockOf("brazier") == 1;
        Check("HIDEOUT-SAVE", same, $"{back.Props.Count} props, mirror at {back.Find("mirror")?.Position}, {back.Floors.Count} paved cell(s), stock {string.Join(", ", back.Stock.Select(kv => $"{kv.Key}×{kv.Value}"))}");
        account.Free();
        reread.Free();

        hideout.QueueFree();
        await Frames(2);
        if (profile is not null)
        {
            profile.Hideout = realTown ?? new HideoutData();
            profile.Persist = persisted;
        }
    }

    // --- in-game feedback → GitHub issues (docs/design/menus.md -> "Feedback") ------

    private async Task IssueFeedbackChecks()
    {
        bool urls = true;
        string detail = "";
        foreach (FeedbackKind kind in System.Enum.GetValues<FeedbackKind>())
        {
            string url = Feedback.BuildUrl(kind, "Boss stuck in wall", "It happened & I lost a portal?", "1. go", "Version: dev build");
            bool ok = url.StartsWith(Feedback.Repository + "/issues/new?template=" + Feedback.Template(kind))
                && url.Contains("title=" + System.Uri.EscapeDataString(Feedback.TitlePrefix(kind) + "Boss stuck in wall"))
                && url.Contains("description=It%20happened%20%26%20I%20lost%20a%20portal%3F") && url.Contains("game-info=");
            // The issue form for this kind exists and applies the matching label.
            string form = ProjectSettings.GlobalizePath($"res://.github/ISSUE_TEMPLATE/{Feedback.Template(kind)}");
            bool labelled = System.IO.File.Exists(form) && System.IO.File.ReadAllText(form).Contains($"labels: [\"{Feedback.Label(kind)}\"]");
            urls &= ok && labelled;
            detail += $"{kind}: url={ok} form+label={labelled}; ";
        }

        string longUrl = Feedback.BuildUrl(FeedbackKind.Idea, "Long", new string('x', 20000), "", "info");
        Check("FEEDBACK-URL", urls && longUrl.Length <= Feedback.MaxUrlLength, detail + $"20k-char idea trimmed to a {longUrl.Length}-char URL");

        // The Esc menu's form: fill it in and "open" (captured, no real browser).
        var menu = new UI.PauseMenu();
        AddChild(menu);
        await Frames(1);
        menu.Open();
        UI.FeedbackPanel panel = menu.OpenFeedback();
        await Frames(1);
        string? opened = null;
        panel.OpenUrl = url => opened = url;
        panel.SetKind(FeedbackKind.FeatureRequest);
        panel.Fill("Controller support", "Play with a gamepad", "Couch play");
        panel.Send();
        bool sent = opened is { } o && o.Contains("template=feature_request.yml") && o.Contains("why=Couch%20play") && o.Contains("Sent%20from%20the%20game");
        panel.Close();
        menu.Close();
        Check("FEEDBACK-FORM", sent, $"Esc → Send feedback opened {(opened is null ? "nothing" : opened[..System.Math.Min(110, opened.Length)] + "…")}");
        menu.QueueFree();
        await Frames(1);
    }

    private static IEnumerable<Enemy> RoomMonsters(RealityController reality, string room) =>
        reality.GetChildren().OfType<Enemy>().Where(e => e.HasMeta(RealityController.RoomMeta) && (string)e.GetMeta(RealityController.RoomMeta) == room).ToList();
}
