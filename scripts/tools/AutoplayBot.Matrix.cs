#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Combat;
using ArpgGame.Core;
using ArpgGame.Enemies;
using ArpgGame.Progression;
using ArpgGame.Skills;
using ArpgGame.World;

namespace ArpgGame.Tools;

/// <summary>
/// The damage matrix of docs/design/balance-testing.md, layer A:
/// <c>./scripts/autoplay.sh --mode=matrix --class=arcanist --tier=20 [--gear=none,typical]
/// [--trees=none,best] [--skills=fireball,spark] [--seconds=12] [--gem=20]</c>.
///
/// An in-memory character at the reference level of the tier (passives spent, ascendancy
/// and gear as the bot's reference character), every skill gem at <c>--gem</c>, goes into the
/// playground at that tier. For each skill × tree × gear it casts on a loop at tier monsters
/// that stand still and never attack (one at a time, respawned when it dies — the owner's
/// question: "how long does a normal monster take?"), then at a pack of 8. The damage counted
/// is what the monsters actually took (after resistances and armour, damage over time
/// included). Rows go to <c>user://balance/matrix.csv</c>; the ratio column is DPS ÷
/// <see cref="MonsterScaling.ExpectedDps"/>, the curve monster life is built on (1.0 = a normal
/// monster in 0.4 s). Nothing is written to the profile.
/// </summary>
public partial class AutoplayBot
{
    private float _matrixDamage;
    private Player? _matrixPlayer;

    private static readonly string[] MatrixTargets = { "Skeleton", "Slime" };

    private async Task MatrixLoop()
    {
        if (Profile.Get(this) is not { } profile)
        {
            Log("[MATRIX] no Profile autoload");
            GetTree().Quit();
            return;
        }

        string[] gears = Arg("gear", "none,typical").Split(',');
        string[] trees = Arg("trees", "none,best").Split(',');
        float seconds = Arg("seconds", "12").ToFloat();
        int gemLevel = Mathf.Clamp(Arg("gem", "20").ToInt(), 1, GemData.MaxLevel);
        int tier = Mathf.Clamp(_startTier, 1, RealityTiers.MaxTier);
        int monsterLevel = RealityTiers.MonsterLevel(tier);
        float expected = MonsterScaling.ExpectedDps(monsterLevel);
        List<SkillEntry> skills = UpgradeCatalog.Skills.Where(s => Damaging(s.Definition)).ToList();
        if (Arg("skills", "") is { Length: > 0 } only)
        {
            HashSet<string> wanted = only.Split(',').ToHashSet();
            skills = skills.Where(s => wanted.Contains(s.Id)).ToList();
        }

        profile.Persist = false;
        _character = profile.CreateCharacter($"Matrix-{_classId}", _classId);
        _character.TutorialDone = true;
        _character.Level = _startLevel > 1 ? _startLevel : MonsterScaling.ReferenceCharacterLevel(monsterLevel);
        SpendPassives(_character);
        foreach (SkillEntry s in UpgradeCatalog.Skills)
        {
            if (_character.Gems.FirstOrDefault(g => g.Id == s.Id && !g.IsSupport) is { } owned)
            {
                owned.Level = gemLevel;
            }
            else
            {
                _character.AddGem(s.Id, false, gemLevel);
            }
        }

        RealityLaunch.Reset();
        RealityLaunch.Playground = true;
        RealityLaunch.LayoutId = LayoutCatalog.Playground.Id;
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(Scenes.Reality);
        RealityController? reality = null;
        for (int i = 0; i < 200 && reality is null; i++)
        {
            await ToSignal(GetTree().CreateTimer(0.1, processAlways: true), SceneTreeTimer.SignalName.Timeout);
            reality = GetTree().CurrentScene as RealityController is { } r && r.IsNodeReady() ? r : null;
        }

        if (reality is null)
        {
            Log("[MATRIX] the playground did not load");
            GetTree().Quit();
            return;
        }

        await ToSignal(GetTree().CreateTimer(1.0, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        reality.SetPlaygroundTier(tier);
        Enemy.Pacified = true;
        Enemy.HoldStill = true;
        _matrixPlayer = reality.Player;
        Health.AnyHitResolved += MatrixHit;
        Health.AnyResolvedDamage += MatrixResolved;
        if (_matrixPlayer.Health is { } ph)
        {
            ph.IgnoreDamage = true;
        }

        var csv = new StringBuilder("class,level,tier,skill,gear,tree,path,gem,target,dps_single,ttk_normal,kills,dps_pack,pack_clear_s,mana_per_s,expected,ratio_single\n");
        Log($"[MATRIX] class={_classId} lv {_character.Level} tier {tier} (monster level {monsterLevel}) expected DPS {expected:0}; {skills.Count} skills × trees [{string.Join(",", trees)}] × gear [{string.Join(",", gears)}], {seconds:0} s each");
        foreach (string gear in gears)
        {
            ApplyMatrixGear(_character, gear, tier);
            // CharacterBuild.Rebuild leaves equipment alone: put this profile's items on the player.
            foreach (Items.EquipmentSlot slot in System.Enum.GetValues<Items.EquipmentSlot>())
            {
                _matrixPlayer.Equipment.Unequip(slot);
            }

            foreach ((Items.EquipmentSlot slot, Items.EquipmentItem item) in _character.Equipped)
            {
                _matrixPlayer.Equipment.TryEquip(slot, item, out _);
            }

            int worn = System.Enum.GetValues<Items.EquipmentSlot>().Count(sl => _matrixPlayer.Equipment.GetEquipped(sl) is not null);
            Log($"[MATRIX] gear={gear}: {worn} items worn");
            foreach (SkillEntry skill in skills)
            {
                GemData gem = _character.Gems.First(g => g.Id == skill.Id && !g.IsSupport);
                foreach (string tree in trees)
                {
                    gem.Tree.Clear();
                    if (tree == "best")
                    {
                        BestPath(gem);
                    }

                    _character.Loadout[0].SkillUid = gem.Uid;
                    for (int i = 1; i < _character.Loadout.Length; i++)
                    {
                        _character.Loadout[i].SkillUid = null;
                    }

                    CharacterBuild.Of(_matrixPlayer)?.Rebuild();
                    await Frames(2);
                    string path = string.Join(" ", gem.Tree.Select(kv => $"{kv.Key}{kv.Value}"));
                    foreach (string target in MatrixTargets)
                    {
                        (float dps, float ttk, int kills, float mana) = await MeasureSingle(reality, skill, target, seconds);
                        (float packDps, float clear) = await MeasurePack(reality, skill, target, seconds);
                        float ratio = dps / expected;
                        csv.Append($"{_classId},{_character.Level},{tier},{skill.Id},{gear},{tree},{path},{gemLevel},{target},{dps:0},{ttk:0.00},{kills},{packDps:0},{clear:0.0},{mana:0.0},{expected:0},{ratio:0.000}\n");
                        Log($"[MATRIX] {skill.Id,-16} gear={gear,-8} tree={tree,-5} vs {target,-8} single {dps,7:0} DPS ({ratio,5:0.00}× expected, normal in {ttk,5:0.00} s, {kills} kills)  pack {packDps,7:0} DPS (8 cleared in {clear:0.0} s)  mana {mana:0.0}/s  [{path}]");
                    }
                }
            }
        }

        Enemy.Pacified = false;
        Enemy.HoldStill = false;
        Health.AnyHitResolved -= MatrixHit;
        Health.AnyResolvedDamage -= MatrixResolved;
        DirAccess.MakeDirRecursiveAbsolute("user://balance");
        string file = $"user://balance/matrix_{_classId}_t{tier}.csv";
        using (FileAccess? f = FileAccess.Open(file, FileAccess.ModeFlags.Write))
        {
            f?.StoreString(csv.ToString());
        }

        Log($"[MATRIX] wrote {file}");
        Log("[MATRIX] done");
        GetTree().Quit();
    }

    private static string Arg(string key, string fallback)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string[] kv = arg.TrimStart('-').Split('=', 2);
            if (kv.Length == 2 && kv[0] == key)
            {
                return kv[1];
            }
        }

        return fallback;
    }

    /// <summary>Skills measured on their own: those that hit. Auras, curses, movement,
    /// totems and detonations need a partner (layer B of the plan).</summary>
    private static bool Damaging(SkillDefinition d) =>
        d is ProjectileSkillDefinition or AreaSkillDefinition or MeleeSkillDefinition or OrbitSkillDefinition && d.BaseDamage > 0f;

    private void ApplyMatrixGear(CharacterData c, string gear, int tier)
    {
        c.Equipped.Clear();
        c.Ascendancy = null;
        c.AscendancyNodes.Clear();
        if (gear == "typical")
        {
            _gearedTier = 0;
            ApplyReferenceCharacter(c, tier);
        }
        else if (gear == "ascendancy")
        {
            // Ascendancy and passives but no gear: how much the character alone brings.
            _gearedTier = tier;
            ApplyReferenceCharacter(c, tier);
            c.Equipped.Clear();
        }
    }

    // --- the best tree path: beam search on the cast's numbers --------------------------

    private void BestPath(GemData gem)
    {
        if (SkillTrees.For(gem.Id) is not { } tree || _matrixPlayer is null)
        {
            return;
        }

        var beam = new List<Dictionary<string, int>> { new() };
        while (true)
        {
            var next = new List<(Dictionary<string, int> Ranks, float Score)>();
            foreach (Dictionary<string, int> ranks in beam)
            {
                foreach (SkillTreeNode node in tree.Nodes)
                {
                    var probe = new GemData { Id = gem.Id, Level = gem.Level, Tree = new Dictionary<string, int>(ranks) };
                    if (SkillTreeRules.Raise(probe, tree, node))
                    {
                        next.Add((probe.Tree, Score(gem.Id, probe.Tree)));
                    }
                }
            }

            if (next.Count == 0)
            {
                break;
            }

            beam = next.GroupBy(n => string.Join(",", n.Ranks.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}{kv.Value}")))
                .Select(g => g.First())
                .OrderByDescending(n => n.Score)
                .Take(16)
                .Select(n => n.Ranks)
                .ToList();
        }

        Dictionary<string, int> best = beam.OrderByDescending(r => Score(gem.Id, r)).First();
        gem.Tree.Clear();
        foreach ((string id, int r) in best)
        {
            gem.Tree[id] = r;
        }
    }

    /// <summary>A single-target damage-per-second estimate of a tree, from the real cast
    /// context the game resolves (its multipliers, counts, bursts, repeats, chances).</summary>
    private float Score(string skillId, Dictionary<string, int> ranks)
    {
        if (UpgradeCatalog.FindSkill(skillId) is not { } entry || _matrixPlayer?.SkillUser is not { } user)
        {
            return 0f;
        }

        var instance = new SkillInstance(entry.Definition) { Level = GemData.MaxLevel };
        instance.TreeModifiers.AddRange(SkillTrees.ModifiersFor(skillId, ranks));
        CastContext c = user.ResolveContext(instance, Vector2.Right);
        SkillDefinition d = entry.Definition;
        float hits = d switch
        {
            ProjectileSkillDefinition => c.Projectile?.Count ?? 1,
            MeleeSkillDefinition m => m.Strikes + c.ExtraCount,
            OrbitSkillDefinition o => o.Count + c.ExtraCount,
            _ => 1 + c.ExtraCount,
        };
        float burst = c.Projectile is { ExplodeRadius: > 0f } p ? p.ExplodeDamageFraction : 0f;
        float dot = 1f + 0.5f * (c.IgniteChanceBonus * c.IgniteDamageMultiplier + c.BleedChance);
        float period = Mathf.Max(0.1f, Mathf.Max(d.BaseUseTime * c.UseTimeMultiplier, d.Cooldown * c.CooldownMultiplier));
        // A hair of value for anything else, so the search still walks through utility nodes
        // on the way to a notable.
        float other = 1f + 0.001f * ranks.Values.Sum();
        return c.DamageMultiplier * hits * (1f + c.Repeats) * (1f + burst) * dot * other / period;
    }

    // --- measuring ---------------------------------------------------------------------

    private void MatrixHit(Health target, DamagePayload payload, float taken)
    {
        if (_matrixPlayer is { } p && IsInstanceValid(p) && payload.SourceActor == p && target.GetParent() is Enemy)
        {
            _matrixDamage += taken;
        }
    }

    private void MatrixResolved(Health target, float amount, Node2D? source, string label)
    {
        if (_matrixPlayer is { } p && IsInstanceValid(p) && source == p && target.GetParent() is Enemy)
        {
            _matrixDamage += amount;
        }
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private static float StandOff(SkillDefinition d) => d switch
    {
        MeleeSkillDefinition => 60f,
        OrbitSkillDefinition => 70f,
        AreaSkillDefinition { AtTarget: false } => 70f,
        _ => 240f,
    };

    private void ClearMonsters()
    {
        foreach (Node n in GetTree().GetNodesInGroup(CombatQuery.EnemyGroup))
        {
            if (n is Enemy e)
            {
                e.QueueFree();
            }
        }
    }

    private Vector2 Arena(RealityController reality) => reality.Player.GlobalPosition;

    /// <summary>One normal monster at a time, a new one as soon as it dies.</summary>
    private async Task<(float Dps, float Ttk, int Kills, float Mana)> MeasureSingle(RealityController reality, SkillEntry skill, string type, float seconds)
    {
        ClearMonsters();
        await Frames(2);
        Player player = reality.Player;
        Vector2 home = Arena(reality);
        Vector2 at = home + Vector2.Right * StandOff(skill.Definition);
        Enemy? target = null;
        int kills = 0;
        float elapsed = 0f, mana = 0f;
        _matrixDamage = 0f;
        float dt = 1f / Engine.PhysicsTicksPerSecond;
        while (elapsed < seconds)
        {
            if (target is null || !IsInstanceValid(target) || target.IsQueuedForDeletion() || target.Health.IsDead)
            {
                if (target is not null)
                {
                    kills++;
                }

                target = reality.SpawnPlaygroundHorde(1, type, MonsterRarity.Normal, at).FirstOrDefault();
                if (target is not null)
                {
                    target.GlobalPosition = at;
                }
            }

            mana += Cast(player, skill, target, home);
            await Frames(1);
            elapsed += dt;
        }

        float dps = _matrixDamage / seconds;
        float ttk = kills > 0 ? seconds / kills : float.PositiveInfinity;
        return (dps, ttk, kills, mana / seconds);
    }

    /// <summary>A pack of 8 around one point: damage per second over the pack, and how long
    /// until the 8 are dead (∞ when they outlive the measure).</summary>
    private async Task<(float Dps, float Clear)> MeasurePack(RealityController reality, SkillEntry skill, string type, float seconds)
    {
        ClearMonsters();
        await Frames(2);
        Player player = reality.Player;
        Vector2 home = Arena(reality);
        Vector2 centre = home + Vector2.Right * (StandOff(skill.Definition) + 30f);
        List<Enemy> pack = reality.SpawnPlaygroundHorde(8, type, MonsterRarity.Normal, centre);
        for (int i = 0; i < pack.Count; i++)
        {
            pack[i].GlobalPosition = centre + Vector2.Right.Rotated(Mathf.Tau * i / pack.Count) * 50f;
        }

        _matrixDamage = 0f;
        float elapsed = 0f, clear = float.PositiveInfinity;
        float dt = 1f / Engine.PhysicsTicksPerSecond;
        while (elapsed < seconds)
        {
            Enemy? nearest = pack.Where(e => IsInstanceValid(e) && !e.IsQueuedForDeletion() && !e.Health.IsDead)
                .OrderBy(e => e.GlobalPosition.DistanceSquaredTo(home)).FirstOrDefault();
            if (nearest is null)
            {
                clear = elapsed;
                break;
            }

            Cast(player, skill, nearest, home);
            await Frames(1);
            elapsed += dt;
        }

        ClearMonsters();
        return (_matrixDamage / Mathf.Max(0.1f, Mathf.Min(seconds, clear)), clear);
    }

    /// <summary>Keeps the player at <paramref name="home"/>, full of mana, and fires the skill
    /// at the target. Returns the mana it spent this frame.</summary>
    private float Cast(Player player, SkillEntry skill, Enemy? target, Vector2 home)
    {
        player.GlobalPosition = home;
        player.Velocity = Vector2.Zero;
        float spent = 0f;
        if (player.Stats.Mana is { } m)
        {
            spent = Mathf.Max(0f, m.Max - m.Current);
            m.Current = m.Max;
        }

        if (target is not null)
        {
            Vector2 point = target.GlobalPosition;
            player.SkillUser.TryCast(SkillSlotId.Lmb, home.DirectionTo(point), point);
        }

        return spent;
    }
}
