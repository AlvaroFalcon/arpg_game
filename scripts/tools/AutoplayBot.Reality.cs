#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PathToExile.Characters;
using PathToExile.Combat;
using PathToExile.Core;
using PathToExile.Enemies;
using PathToExile.Run;
using PathToExile.World;

namespace PathToExile.Tools;

/// <summary>
/// Reality mode for the autoplay bot — docs/design/endgame.md -> "Tuning".
/// <c>-- --mode=reality --class=warrior --tier=1 --runs=6</c> creates a fresh
/// in-memory character (the real profile is never touched), then plays
/// realities back to back: it paths to the nearest monster or pickup with a
/// BFS over the layout grid, fights with the same threat avoidance as the
/// survival bot, spends passive points as it levels, moves up a tier after
/// each clear and stays put after a collapse. It never equips drops, so it
/// measures the "level and gems only" floor of progression.
/// </summary>
public partial class AutoplayBot
{
    private string _mode = "";
    private string _classId = "warrior";
    private int _startTier = 1;
    private int _startLevel = 1;
    private readonly List<Vector2I> _path = new();
    private float _repath;
    private Vector2 _goal;
    private bool _goalIsEnemy;
    private CharacterData? _character;

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
        _character.Level = Mathf.Max(1, _startLevel);
        int tier = Mathf.Clamp(_startTier, 1, RealityTiers.MaxTier);
        int clears = 0;
        for (_runIndex = 0; _runIndex < _runs; _runIndex++)
        {
            SpendPassives(_character);
            string layout = LayoutCatalog.Layouts[_runIndex % LayoutCatalog.Layouts.Count].Id;
            RealityLaunch.Tier = tier;
            RealityLaunch.LayoutId = layout;
            RealityLaunch.Modifiers = new List<string>();
            GetTree().Paused = false;
            GetTree().ChangeSceneToFile(Scenes.Reality);
            await ToSignal(GetTree().CreateTimer(0.5, processAlways: true), SceneTreeTimer.SignalName.Timeout);
            if (GetTree().CurrentScene is not RealityController reality)
            {
                Log("[BOT] reality scene failed to load");
                break;
            }

            _damageTaken = 0f;
            _lastLife = -1f;
            _dodges = 0;
            _damageBySource.Clear();
            _hooked = null;
            _nextReport = ReportEvery;
            int levelBefore = _character.Level;
            Log($"[BOT] reality {_runIndex + 1}/{_runs}: {layout} tier {tier} (char lv {_character.Level})");

            const float timeLimit = 480f;
            while (IsInstanceValid(reality) && !reality.Completed && !reality.Collapsed && reality.Elapsed < timeLimit)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            }

            if (!IsInstanceValid(reality))
            {
                Log("[BOT] reality ended unexpectedly");
                continue;
            }

            string outcome = reality.Completed ? "CLEARED" : reality.Collapsed ? "COLLAPSED" : "TIMEOUT";
            string boss = reality.Boss is { } b && IsInstanceValid(b) ? $" boss@{b.LifeFraction:P0}" : "";
            Log($"[BOT] RESULT reality={_runIndex + 1} tier={tier} layout={layout} outcome={outcome} time={UI.UiTheme.FormatTime(reality.Elapsed)} kills={reality.Kills}/{reality.MonstersTotal} portalsLeft={reality.PortalsLeft} level={levelBefore}->{_character.Level} dodges={_dodges} dmgTaken={_damageTaken:0}{boss}");
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

        Log($"[BOT] done: {clears}/{_runs} cleared, reached tier {tier}, character level {_character.Level}");
        GetTree().Quit();
    }

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
            if (n is Node2D p && p.GlobalPosition.DistanceTo(pos) < 520f)
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
