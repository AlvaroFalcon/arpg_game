#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Combat;
using ArpgGame.Core;
using ArpgGame.Enemies;
using ArpgGame.Progression;
using ArpgGame.Run;
using ArpgGame.World;

namespace ArpgGame.Tools;

/// <summary>
/// Reality mode for the autoplay bot — docs/design/endgame.md -> "Tuning".
/// <c>-- --mode=reality --class=warrior --tier=1 --runs=6</c> creates a fresh
/// in-memory character (the real profile is never touched), then plays
/// realities back to back: it paths to the nearest monster or pickup with a
/// BFS over the layout grid, fights with the same threat avoidance as the
/// survival bot, spends passive points as it levels, moves up a tier after
/// each clear and stays put after a collapse. It never equips drops: with
/// <c>--gear=none</c> it measures the "level and gems only" floor of
/// progression; the default <c>--gear=typical</c> gives it, before every
/// reality, the reference character of docs/design/endgame.md -> "Monster
/// scaling": gems at the level its character level gives, supports in every
/// socket, its ascendancy once earned and a fresh set of rare gear at the
/// tier's item level. <c>--level=auto</c> sets the character level the
/// reference expects at each tier. Every RESULT line carries the time to kill
/// per monster rarity (first hit to death), the number the life table aims at.
/// </summary>
public partial class AutoplayBot
{
    private string _mode = "";
    private string _classId = "warrior";
    private int _startTier = 1;

    /// <summary>`--layouts=a,b` plays these layout ids in turn (e.g. a room layout
    /// against a cavern); default: the Mirror's caverns.</summary>
    private string[]? _layouts;
    private int _startLevel = 1;
    private readonly List<Vector2I> _path = new();
    private float _repath;
    private Vector2 _goal;
    private bool _goalIsEnemy;
    private CharacterData? _character;
    private string _gear = "typical";
    private RealityController? _ttkReality;
    private readonly Dictionary<ulong, float> _firstHit = new();
    private readonly Dictionary<string, List<float>> _ttk = new();
    private int _gearedTier;

    /// <summary>Supports the reference character socketed, best first (the bot
    /// takes the first ones its main skill accepts).</summary>
    private static readonly string[] SupportPreference =
    {
        "elemental_focus", "brutality", "concentrated_effect", "faster_projectiles", "spell_echo", "multistrike", "faster_casting", "melee_splash", "pierce",
    };

    private int _gen2Seen;

    private async Task RealityLoop()
    {
        if (Profile.Get(this) is not { } profile)
        {
            Log("[BOT] no Profile autoload — cannot run realities");
            GetTree().Quit();
            return;
        }

        profile.Persist = false;
        _character = profile.CreateCharacter($"Bot-{_classId}", _classId);
        int tier = Mathf.Clamp(_startTier, 1, RealityTiers.MaxTier);
        _character.Level = _startLevel <= 0 ? MonsterScaling.ReferenceCharacterLevel(RealityTiers.MonsterLevel(tier)) : Mathf.Max(1, _startLevel);
        Health.AnyHitResolved += OnAnyHit;
        Enemy.AnyKilled += OnAnyKilled;
        int clears = 0;
        for (_runIndex = 0; _runIndex < _runs; _runIndex++)
        {
            if (_startLevel <= 0)
            {
                _character.Level = Mathf.Max(_character.Level, MonsterScaling.ReferenceCharacterLevel(RealityTiers.MonsterLevel(tier)));
            }

            SpendPassives(_character);
            if (_gear == "typical")
            {
                ApplyReferenceCharacter(_character, tier);
            }

            string layout = _layouts is { Length: > 0 } ids ? ids[_runIndex % ids.Length] : LayoutCatalog.Layouts[_runIndex % LayoutCatalog.Layouts.Count].Id;
            RealityLaunch.Tier = tier;
            RealityLaunch.LayoutId = layout;
            RealityLaunch.Modifiers = new List<string>();
            GetTree().Paused = false;
            // The real path (the Mirror, the hideout portal): through the loading screen
            // (docs/design/performance.md -> "Frame budget pass", I4).
            UI.LoadingScreen.Go(GetTree(), Scenes.Reality, "Autoplay", "");
            await ToSignal(GetTree().CreateTimer(1.0, processAlways: true), SceneTreeTimer.SignalName.Timeout);
            if (GetTree().CurrentScene is not RealityController reality)
            {
                Log("[BOT] reality scene failed to load");
                break;
            }

            _ttkReality = reality;
            _firstHit.Clear();
            _ttk.Clear();
            _damageTaken = 0f;
            _lastLife = -1f;
            _dodges = 0;
            _damageBySource.Clear();
            _hooked = null;
            _nextReport = ReportEvery;
            int levelBefore = _character.Level;
            Log($"[BOT] reality {_runIndex + 1}/{_runs}: {layout} tier {tier} (char lv {_character.Level})");

            const float timeLimit = 480f;
            // Cost of a simulated frame (the bot runs unthrottled, so wall time per
            // physics frame is what the game would spend): watch for dense caverns.
            var frameMs = new List<float>();
            ulong last = Time.GetTicksUsec();
            while (IsInstanceValid(reality) && !reality.Completed && !reality.Collapsed && reality.Elapsed < timeLimit)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                ulong now = Time.GetTicksUsec();
                float ms = (now - last) / 1000f;
                frameMs.Add(ms);
                last = now;
                int gen2 = System.GC.CollectionCount(2);
                if (ms > 100f)
                {
                    // Attribute the spike: a gen2 collection since the last frame, or not.
                    System.GCMemoryInfo g = System.GC.GetGCMemoryInfo(System.GCKind.Any);
                    Log($"[BOT] spike {ms:0} ms at {reality.Elapsed:0.0} s: gen2 {(gen2 > _gen2Seen ? "yes" : "no")}, last GC #{g.Index} gen{g.Generation} pause {g.PauseDurations[0].TotalMilliseconds:0}+{g.PauseDurations[1].TotalMilliseconds:0} ms, heap {g.HeapSizeBytes / 1048576.0:0} MB, monsters {GetTree().GetNodesInGroup(CombatQuery.EnemyGroup).Count}");
                }

                _gen2Seen = gen2;
            }

            frameMs.Sort();
            string perf = frameMs.Count == 0 ? "" : $" frameMs avg={frameMs.Average():0.00} p95={frameMs[(int)(frameMs.Count * 0.95f)]:0.00} max={frameMs[^1]:0.0}";

            if (!IsInstanceValid(reality))
            {
                Log("[BOT] reality ended unexpectedly");
                continue;
            }

            string outcome = reality.Completed ? "CLEARED" : reality.Collapsed ? "COLLAPSED" : "TIMEOUT";
            string boss = reality.Boss is { } b && IsInstanceValid(b) ? $" boss@{b.LifeFraction:P0}" : "";
            Log($"[BOT] RESULT reality={_runIndex + 1} tier={tier} layout={layout} outcome={outcome} time={UI.UiTheme.FormatTime(reality.Elapsed)} kills={reality.Kills}/{reality.MonstersTotal} portalsLeft={reality.PortalsLeft} level={levelBefore}->{_character.Level} dodges={_dodges} dmgTaken={_damageTaken:0}{boss}{perf}");
            Log($"[BOT] ttk tier={tier} gear={_gear} {TtkSummary()}");
            string sources = string.Join(", ", _damageBySource.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key} {kv.Value:0}"));
            Log($"[BOT] damage by source: {sources}");

            if (reality.Completed)
            {
                clears++;
                tier = Mathf.Min(RealityTiers.MaxTier, tier + 1);
            }

            ReleaseAll();
            await ToSignal(GetTree().CreateTimer(0.3, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        }

        Health.AnyHitResolved -= OnAnyHit;
        Enemy.AnyKilled -= OnAnyKilled;
        Log($"[BOT] done: {clears}/{_runs} cleared, reached tier {tier}, character level {_character.Level}");
        GetTree().Quit();
    }

    private void OnAnyHit(Health health, DamagePayload payload, float taken)
    {
        if (_ttkReality is { } r && IsInstanceValid(r) && health.GetParent() is Enemy e && payload.SourceActor is Player)
        {
            _firstHit.TryAdd(e.GetInstanceId(), r.Elapsed);
        }
    }

    private void OnAnyKilled(Enemy e)
    {
        if (_ttkReality is not { } r || !IsInstanceValid(r) || !_firstHit.Remove(e.GetInstanceId(), out float first))
        {
            return;
        }

        string key = e.IsBoss ? "boss" : e.Rarity.ToString().ToLowerInvariant();
        if (!_ttk.TryGetValue(key, out List<float>? list))
        {
            _ttk[key] = list = new List<float>();
        }

        list.Add(r.Elapsed - first);
    }

    /// <summary>"normal med=0.31s avg=0.42s n=180 · magic ...": the median resists AoE one-shots and stragglers alike.</summary>
    private string TtkSummary() => string.Join(" · ", new[] { "normal", "magic", "rare", "unique", "boss" }
        .Where(_ttk.ContainsKey)
        .Select(k =>
        {
            List<float> l = _ttk[k].OrderBy(v => v).ToList();
            return $"{k} med={l[l.Count / 2]:0.00}s avg={l.Average():0.00}s n={l.Count}";
        }));

    /// <summary>
    /// The reference character of docs/design/endgame.md -> "Monster scaling" at
    /// this tier: gems at <see cref="MonsterScaling.ReferenceGemLevel"/>, a
    /// damage support in every socket of the main skill, the class's first
    /// ascendancy once earned (notables first), and rare gear rolled at the
    /// tier's item level (best of two rolls per slot by damage affixes —
    /// a player picks up more than one item per slot).
    /// </summary>
    private void ApplyReferenceCharacter(CharacterData c, int tier)
    {
        int gemLevel = MonsterScaling.ReferenceGemLevel(c.Level);
        foreach (GemData gem in c.Gems)
        {
            gem.Level = Mathf.Max(gem.Level, gemLevel);
        }

        LoadoutSlot main = c.Loadout[0];
        if (c.FindGem(main.SkillUid) is { } skillGem && UpgradeCatalog.Skills.FirstOrDefault(s => s.Id == skillGem.Id) is { } entry)
        {
            foreach (string id in SupportPreference)
            {
                if (main.SupportUids.Count >= c.SupportSockets)
                {
                    break;
                }

                bool have = main.SupportUids.Any(uid => c.FindGem(uid)?.Id == id);
                if (!have && UpgradeCatalog.Supports.FirstOrDefault(s => s.Id == id) is { } sup && sup.Accepts(entry.Definition))
                {
                    main.SupportUids.Add(c.AddGem(id, true, gemLevel).Uid);
                }
            }
        }

        if (tier > AscendancyCatalog.Tier1Unlock && c.Class.Ascendancies.Count > 0)
        {
            c.Ascendancy ??= c.Class.Ascendancies[0];
            c.AscendancyPoints = AscendancyCatalog.FirstPoints + (tier > AscendancyCatalog.Tier2Unlock ? AscendancyCatalog.SecondPoints : 0);
            foreach (AscendancyNode node in AscendancyCatalog.For(c.Ascendancy).OrderByDescending(n => n.Notable))
            {
                if (c.AscendancyPointsFree <= 0)
                {
                    break;
                }

                c.AscendancyNodes.Add(node.Id);
            }
        }

        if (_gearedTier == tier)
        {
            return;
        }

        _gearedTier = tier;
        int itemLevel = MonsterScaling.ReferenceItemLevel(RealityTiers.MonsterLevel(tier));
        string weapon = c.ClassId switch { "warrior" => "iron_axe", "rogue" => "bone_dagger", _ => "iron_wand" };
        foreach ((string id, Items.EquipmentSlot slot) in new[]
        {
            (weapon, Items.EquipmentSlot.Weapon), ("oak_buckler", Items.EquipmentSlot.Offhand), ("leather_cap", Items.EquipmentSlot.Helmet),
            ("ringmail_vest", Items.EquipmentSlot.BodyArmour), ("rawhide_gloves", Items.EquipmentSlot.Gloves), ("rawhide_boots", Items.EquipmentSlot.Boots),
            ("rope_belt", Items.EquipmentSlot.Belt), ("copper_amulet", Items.EquipmentSlot.Amulet), ("copper_ring", Items.EquipmentSlot.Ring1),
            ("copper_ring", Items.EquipmentSlot.Ring2), ("linen_trousers", Items.EquipmentSlot.Pants),
        })
        {
            if (Items.ItemFactory.FindBase(id) is not { } b)
            {
                continue;
            }

            Items.EquipmentItem best = Items.ItemFactory.Roll(b, Items.Rarity.Rare, itemLevel, _rng);
            Items.EquipmentItem other = Items.ItemFactory.Roll(b, Items.Rarity.Rare, itemLevel, _rng);
            c.Equipped[slot] = DamageAffixes(other) > DamageAffixes(best) ? other : best;
        }

        Log($"[BOT] reference character: lv {c.Level}, gems {gemLevel}, {c.Loadout[0].SupportUids.Count} supports, ascendancy {c.Ascendancy ?? "-"} ({c.AscendancyNodes.Count}), rare gear ilvl {itemLevel}");
    }

    private static int DamageAffixes(Items.EquipmentItem item) => item.RolledAffixes.Count(a => a.Definition.Stat == Stats.StatId.Damage);

    /// <summary>Spends free points walking outward from the start, notables and keystones welcome.</summary>
    private void SpendPassives(CharacterData c)
    {
        while (c.PassivePointsFree > 0)
        {
            List<string> options = PassiveTree.Nodes.Keys.Where(id => PassiveTree.CanAllocate(c, id)).ToList();
            if (options.Count == 0)
            {
                return;
            }

            // Prefer notables, then random — an average, not an optimal, player.
            string pick = options.FirstOrDefault(id => PassiveTree.Get(id)!.Kind == PassiveKind.Notable) ?? options[_rng.RandiRange(0, options.Count - 1)];
            c.Passives.Add(pick);
        }
    }

    private void RealityTick(RealityController reality, float dt)
    {
        if (reality.Completed || reality.Collapsed || GetTree().Paused)
        {
            ReleaseAll();
            return;
        }

        Player player = reality.Player;
        player.GetNode<PlayerSkillInput>("PlayerSkillInput").ForceAutoCast = true;
        if (_hooked != player.Health && player.Health is { } h)
        {
            _hooked = h;
            h.HitResolved += (payload, taken) =>
            {
                string key = payload.SourceActor is Enemy e && IsInstanceValid(e)
                    ? (e.IsBoss ? "BOSS " : e.Rarity >= MonsterRarity.Rare ? "Rare " : "") + e.DisplayName
                    : "unknown";
                _damageBySource[key] = _damageBySource.GetValueOrDefault(key) + taken;
            };
        }

        if (player.Stats.Life is { } life)
        {
            if (_lastLife >= 0f && life.Current < _lastLife)
            {
                _damageTaken += _lastLife - life.Current;
            }

            _lastLife = life.Current;
        }

        if (player.Health?.IsDead ?? true)
        {
            ReleaseAll();
            return;
        }

        if (_character is { PassivePointsFree: > 0 } c)
        {
            SpendPassives(c);
            CharacterBuild.Of(player)?.Rebuild();
        }

        _shortRange = !player.SkillUser.EquippedSkills().Any(s => (s.Skill.Definition.Tags & SkillTags.Projectile) != 0);
        Vector2 pos = player.GlobalPosition;
        bool inDanger = false;
        Vector2 escape = Vector2.Zero;
        int crowd = 0;
        Vector2 crowdCentre = Vector2.Zero;
        Vector2 push = Threats(pos, ref inDanger, ref escape, ref crowd, ref crowdCentre);

        _repath -= dt;
        if (_repath <= 0f)
        {
            _repath = 0.3f;
            Repath(reality, pos);
        }

        Vector2 goalDir = Vector2.Zero;
        if (_path.Count > 0)
        {
            Vector2 next = reality.Field.CenterOf(_path[Mathf.Min(1, _path.Count - 1)]);
            if (pos.DistanceTo(next) < 20f && _path.Count > 1)
            {
                _path.RemoveAt(0);
            }

            // Ranged builds stop at range only with line of sight: a monster
            // behind a wall is close as the crow flies but not hittable.
            float stopAt = _goalIsEnemy ? (_shortRange || !reality.Field.HasLineOfSight(pos, _goal) ? 45f : 230f) : 0f;
            if (pos.DistanceTo(_goal) > stopAt)
            {
                goalDir = pos.DirectionTo(_path.Count > 1 ? next : _goal);
            }
        }

        float goalWeight = push.Length() < 1.5f ? 1f : 0.35f;
        Vector2 move = (push + goalDir * goalWeight).LimitLength(1f);
        Press("move_right", Mathf.Max(0f, move.X));
        Press("move_left", Mathf.Max(0f, -move.X));
        Press("move_down", Mathf.Max(0f, move.Y));
        Press("move_up", Mathf.Max(0f, -move.Y));

        if (crowd >= 5 && player.Dodge is { Charges: > 0 })
        {
            inDanger = true;
            escape = (pos - crowdCentre / crowd).Normalized();
        }

        if (inDanger && player.Dodge is { Charges: > 0 } dodge && dodge.TryDodge(escape == Vector2.Zero ? move : escape.Normalized()))
        {
            _dodges++;
        }

        if (reality.Elapsed >= _nextReport)
        {
            _nextReport += ReportEvery;
            string boss = reality.BossFightActive && reality.Boss is { } b ? $" boss@{b.LifeFraction:P0}" : "";
            Log($"[BOT]   t={UI.UiTheme.FormatTime(reality.Elapsed)} lv={_character?.Level} life={player.Stats.Life?.Current:0}/{player.Stats.Life?.Max:0} kills={reality.Kills}/{reality.MonstersTotal} portals={reality.PortalsLeft}{boss} pos={reality.Field.CellOf(pos)} goal={reality.Field.CellOf(_goal)} enemy={_goalIsEnemy} path={_path.Count}");
        }
    }

    /// <summary>BFS over walkable cells to the nearest pickup (close by) or monster.</summary>
    private void Repath(RealityController reality, Vector2 pos)
    {
        FlowField field = reality.Field;
        var targets = new Dictionary<Vector2I, (Vector2 At, bool Enemy)>();
        foreach (Node n in GetTree().GetNodesInGroup(RunState.PickupGroup))
        {
            // A life orb stays on the floor while the flasks are full; standing on
            // one waiting for it to be collected stalls the bot forever.
            bool uncollectable = n is Pickup { Kind: PickupKind.Life } && Items.Flasks.Of(reality.Player) is { AllFull: true };
            if (n is Node2D p && !uncollectable && p.GlobalPosition.DistanceTo(pos) < 520f)
            {
                targets[field.CellOf(p.GlobalPosition)] = (p.GlobalPosition, false);
            }
        }

        bool bossOnly = true;
        foreach (Node n in GetTree().GetNodesInGroup(CombatQuery.EnemyGroup))
        {
            if (n is Enemy e && !e.IsBoss && !e.Health.IsDead)
            {
                bossOnly = false;
                targets.TryAdd(field.CellOf(e.GlobalPosition), (e.GlobalPosition, true));
            }
        }

        // The boss only once the rest is (mostly) cleared, or when it is already awake.
        if (reality.Boss is { } boss && IsInstanceValid(boss) && (bossOnly || reality.BossFightActive || reality.Kills >= reality.MonstersTotal * 0.8f))
        {
            targets[field.CellOf(boss.GlobalPosition)] = (boss.GlobalPosition, true);
        }

        _path.Clear();
        if (targets.Count == 0)
        {
            return;
        }

        Vector2I start = field.CellOf(pos);
        var prev = new Dictionary<Vector2I, Vector2I> { [start] = start };
        var queue = new Queue<Vector2I>();
        queue.Enqueue(start);
        Vector2I[] dirs = { Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right };
        while (queue.Count > 0)
        {
            Vector2I c = queue.Dequeue();
            if (targets.TryGetValue(c, out (Vector2 At, bool Enemy) t))
            {
                _goal = t.At;
                _goalIsEnemy = t.Enemy;
                for (Vector2I k = c; k != start; k = prev[k])
                {
                    _path.Insert(0, k);
                }

                _path.Insert(0, start);
                return;
            }

            foreach (Vector2I d in dirs)
            {
                Vector2I nb = c + d;
                if (field.IsWalkable(nb) && !prev.ContainsKey(nb))
                {
                    prev[nb] = c;
                    queue.Enqueue(nb);
                }
            }
        }
    }
}
