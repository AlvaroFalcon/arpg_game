#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PathToExile.Combat;
using PathToExile.Enemies;
using PathToExile.Progression;
using PathToExile.Run;

namespace PathToExile.Tools;

/// <summary>
/// Plays runs by itself for balance work — docs/design/run.md -> "Tuning".
/// Run headless and unthrottled with scripts/autoplay.sh, e.g.
/// <c>-- --skill=spark --runs=3</c>. Each run it steers with WASD-equivalent
/// input (fleeing nearby enemies, stepping out of telegraphs, collecting gems),
/// dodges hits that are about to land, picks level-up cards with a light
/// preference for skills and supports, and logs a line every 30 s and a
/// summary at the end.
///
/// It plays like a decent but unremarkable player: no prediction, no
/// knowledge of boss scripts beyond "don't stand in red". If it wins
/// comfortably the game is too easy; if it never reaches the Herald it is
/// too hard. The target is: usually reaches the Colossus, sometimes beats it.
/// </summary>
public partial class AutoplayBot : Node
{
    private string _skill = "";
    private float ReportEvery = 30f;
    private int _runs = 1;
    private int _runIndex;
    private float _nextReport;
    private readonly RandomNumberGenerator _rng = new();
    private float _damageTaken;
    private float _lastLife = -1f;
    private int _dodges;
    private bool _shortRange;
    private readonly System.Collections.Generic.Dictionary<string, float> _damageBySource = new();
    private Combat.Health? _hooked;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string[] kv = arg.TrimStart('-').Split('=', 2);
            if (kv.Length == 2 && kv[0] == "skill") _skill = kv[1];
            if (kv.Length == 2 && kv[0] == "runs") _runs = kv[1].ToInt();
            if (kv.Length == 2 && kv[0] == "report") ReportEvery = kv[1].ToFloat();
            if (kv.Length == 2 && kv[0] == "mode") _mode = kv[1];
            if (kv.Length == 2 && kv[0] == "class") _classId = kv[1];
            if (kv.Length == 2 && kv[0] == "tier") _startTier = kv[1].ToInt();
            if (kv.Length == 2 && kv[0] == "level") _startLevel = kv[1].ToInt();
        }

        _rng.Randomize();
        ProcessMode = ProcessModeEnum.Always;
        Core.GameSettings.Get(this)?.SetAutoAim(true);
        CallDeferred(nameof(Detach));
    }

    private void Detach()
    {
        Node root = GetTree().Root;
        GetParent().RemoveChild(this);
        root.AddChild(this);
        _ = _mode == "reality" ? RealityLoop() : Loop();
    }

    /// <summary>Godot block-buffers stdout when it is not a terminal (see
    /// CLAUDE.md), so every line is also appended, flushed, to
    /// <c>user://autoplay.log</c>.</summary>
    private void Log(string line)
    {
        GD.Print(line);
        using FileAccess? f = FileAccess.FileExists("user://autoplay.log")
            ? FileAccess.Open("user://autoplay.log", FileAccess.ModeFlags.ReadWrite)
            : FileAccess.Open("user://autoplay.log", FileAccess.ModeFlags.Write);
        if (f is null)
        {
            return;
        }

        f.SeekEnd();
        f.StoreLine(line);
        f.Flush();
    }

    private RunController? Run() => GetTree().CurrentScene as RunController;

    private async Task Loop()
    {
        for (_runIndex = 0; _runIndex < _runs; _runIndex++)
        {
            string skill = _skill.Length > 0 ? _skill : UpgradeCatalog.Skills[_rng.RandiRange(0, UpgradeCatalog.Skills.Count - 1)].Id;
            RunLaunchOptions.Reset();
            RunLaunchOptions.StartSkill = skill;
            GetTree().Paused = false;
            GetTree().ChangeSceneToFile(Core.Scenes.Run);
            await ToSignal(GetTree().CreateTimer(0.5, processAlways: true), SceneTreeTimer.SignalName.Timeout);
            _nextReport = ReportEvery;
            _damageTaken = 0f;
            _lastLife = -1f;
            _dodges = 0;
            _damageBySource.Clear();
            _hooked = null;
            Log($"[BOT] run {_runIndex + 1}/{_runs} start: {skill}");
            while (Run() is { State.Ended: false })
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            }

            if (Run() is { } run)
            {
                RunState s = run.State;
                Log($"[BOT] RESULT run={_runIndex + 1} skill={skill} victory={s.Victory} time={UI.UiTheme.FormatTime(s.Elapsed)} level={s.Level} kills={s.Kills} bosses={s.BossesKilled} dodges={_dodges} dmgTaken={_damageTaken:0}");
                string sources = string.Join(", ", _damageBySource.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key} {kv.Value:0}"));
                Log($"[BOT] damage by source: {sources}");
                Log($"[BOT] build: {UI.LevelUpScreen.BuildSummary(run.Build).Replace("\n", " | ")}");
            }

            ReleaseAll();
            await ToSignal(GetTree().CreateTimer(0.3, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        }

        Log("[BOT] done");
        GetTree().Quit();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (GetTree().CurrentScene is World.RealityController reality)
        {
            RealityTick(reality, (float)delta);
            return;
        }

        if (Run() is not { } run || run.State.Ended)
        {
            return;
        }

        if (run.LevelUp.IsOpen)
        {
            PickCard(run);
            return;
        }

        if (GetTree().Paused)
        {
            return;
        }

        Player player = run.Player;
        if (_hooked != player.Health && player.Health is { } h)
        {
            _hooked = h;
            h.HitResolved += (payload, taken) =>
            {
                string key = payload.SourceActor is Enemy e && IsInstanceValid(e)
                    ? (e.IsElite ? "Elite " : "") + e.DisplayName + (payload.Tags.HasFlag(SkillTags.Melee) ? " (contact)" : " (attack)")
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

        Steer(run, player);

        if (run.State.Elapsed >= _nextReport)
        {
            _nextReport += ReportEvery;
            int enemies = GetTree().GetNodesInGroup(CombatQuery.EnemyGroup).Count;
            string boss = run.Director.ActiveBoss is { } b ? $" boss={b.DisplayName}@{b.LifeFraction:P0}" : "";
            Log($"[BOT]   t={UI.UiTheme.FormatTime(run.State.Elapsed)} lv={run.State.Level} life={player.Stats.Life?.Current:0}/{player.Stats.Life?.Max:0} kills={run.State.Kills} alive={enemies}{boss} pos={player.GlobalPosition.Round()} v={player.Velocity.Length():0}");
        }
    }

    private void PickCard(RunController run)
    {
        // Random pick: a deliberately average player.
        int pick = _rng.RandiRange(0, 2);
        run.LevelUp.ChooseIndex(pick);
    }

    /// <summary>Everything that pushes the bot away: enemies by range, telegraphs
    /// about to land, charges, bullets. Shared by survival and reality play.</summary>
    private Vector2 Threats(Vector2 pos, ref bool inDanger, ref Vector2 escape, ref int crowd, ref Vector2 crowdCentre)
    {
        Vector2 push = Vector2.Zero;
        foreach (Node node in GetTree().GetNodesInGroup(CombatQuery.EnemyGroup))
        {
            if (node is not Enemy e)
            {
                continue;
            }

            if (e.GlobalPosition.DistanceTo(pos) < 60f)
            {
                crowd++;
                crowdCentre += e.GlobalPosition;
            }

            if (e is ChargerEnemy { IsWindingUp: true } or ChargerEnemy { IsCharging: true })
            {
                var brute = (ChargerEnemy)e;
                Vector2 dir = brute.LaneDirection;
                Vector2 rel = pos - brute.GlobalPosition;
                float along = rel.Dot(dir);
                float across = rel.Cross(dir);
                if (along > -20f && along < brute.ChargeDistance + 30f && Mathf.Abs(across) < 40f)
                {
                    Vector2 side = new Vector2(-dir.Y, dir.X) * -Mathf.Sign(across == 0 ? 1 : across);
                    push += side * 4f;
                    escape += side;
                    if (brute.IsCharging || brute.TimeToCharge < 0.2f) inDanger = true;
                }
            }

            Vector2 away = pos - e.GlobalPosition;
            float d = Mathf.Max(8f, away.Length());
            float reach = e.IsBoss ? (_shortRange ? 110f : 300f) : _shortRange ? 95f : 170f;
            if (d < reach)
            {
                push += away.Normalized() * (reach - d) / reach * (e.IsBoss ? 3f : 1f);
            }
        }

        foreach (Node node in GetTree().CurrentScene.GetChildren())
        {
            switch (node)
            {
                case AreaBurst burst when IsHostile(burst):
                {
                    Vector2 off = pos - burst.GlobalPosition;
                    float d = off.Length();
                    float inner = InnerOf(burst);
                    float radius = RadiusOf(burst);
                    bool inside = d <= radius + 14f && d >= inner - 14f;
                    if (inside)
                    {
                        Vector2 dir = inner > 0f && d - inner < radius - d ? -off.Normalized() : off.Normalized();
                        if (off == Vector2.Zero) dir = Vector2.Right;
                        push += dir * 4f;
                        escape += dir;
                        if (TimeLeft(burst) < 0.22f) inDanger = true;
                    }

                    break;
                }

                case LineStrike lane when LaneHostile(lane):
                {
                    Vector2 dir = Vector2.Right.Rotated(lane.Rotation);
                    Vector2 rel = pos - lane.GlobalPosition;
                    float along = rel.Dot(dir);
                    float across = rel.Cross(dir);
                    if (along > -20f && along < lane.Length + 20f && Mathf.Abs(across) < lane.Width / 2f + 16f)
                    {
                        Vector2 side = new Vector2(-dir.Y, dir.X) * -Mathf.Sign(across == 0 ? 1 : across);
                        push += side * 4f;
                        escape += side;
                        if (LaneTimeLeft(lane) < 0.22f) inDanger = true;
                    }

                    break;
                }

                case Projectile shot when (shot.CollisionMask & CollisionLayers.PlayerFaction) != 0 && shot.GlobalPosition.DistanceTo(pos) < 90f:
                {
                    Vector2 v = Vector2.Right.Rotated(shot.Rotation);
                    Vector2 side = new(-v.Y, v.X);
                    float s = Mathf.Sign((pos - shot.GlobalPosition).Dot(side));
                    push += side * (s == 0 ? 1 : s) * 2f;
                    break;
                }

                case BossBullet bullet when bullet.GlobalPosition.DistanceTo(pos) < 70f:
                {
                    Vector2 v = bullet.Velocity.Normalized();
                    Vector2 side = new(-v.Y, v.X);
                    float s = Mathf.Sign((pos - bullet.GlobalPosition).Dot(side));
                    push += side * (s == 0 ? 1 : s) * 2.5f;
                    if (bullet.GlobalPosition.DistanceTo(pos) < 36f) inDanger = true;
                    break;
                }
            }
        }

        return push;
    }

    private void Steer(RunController run, Player player)
    {
        Vector2 pos = player.GlobalPosition;

        // A melee-range build (Frost Nova, Blades) has to let enemies close in.
        _shortRange = !run.Build.HasTag(SkillTags.Projectile) && !run.Build.Skills.Any(s => s.Definition is Skills.AreaSkillDefinition { AtTarget: true });
        bool inDanger = false;
        Vector2 escape = Vector2.Zero;
        int crowd = 0;
        Vector2 crowdCentre = Vector2.Zero;
        Vector2 push = Threats(pos, ref inDanger, ref escape, ref crowd, ref crowdCentre);

        // Gems: go for the nearest one when not under pressure.
        if (push.Length() < 0.8f)
        {
            Node2D? gem = null;
            float best = 260f * 260f;
            foreach (Node node in GetTree().GetNodesInGroup(RunState.PickupGroup))
            {
                if (node is Node2D n && n.GlobalPosition.DistanceSquaredTo(pos) < best)
                {
                    best = n.GlobalPosition.DistanceSquaredTo(pos);
                    gem = n;
                }
            }

            if (gem is not null)
            {
                push += pos.DirectionTo(gem.GlobalPosition) * 0.9f;
            }
        }

        // Walls: drift back toward the middle near the edges.
        Rect2 bounds = run.Arena.Bounds.Grow(-160f);
        if (!bounds.HasPoint(pos))
        {
            push += pos.DirectionTo(run.Arena.Center) * 1.2f;
        }

        if (push == Vector2.Zero)
        {
            push = pos.DirectionTo(run.Arena.Center) * 0.3f;
        }

        Vector2 move = push.LimitLength(1f);
        Press("move_right", Mathf.Max(0f, move.X));
        Press("move_left", Mathf.Max(0f, -move.X));
        Press("move_down", Mathf.Max(0f, move.Y));
        Press("move_up", Mathf.Max(0f, -move.Y));

        // Surrounded: dodge out through the thinnest side, like a person would.
        if (crowd >= 4 && player.Dodge is { Charges: > 0 })
        {
            inDanger = true;
            escape = (pos - crowdCentre / crowd).Normalized();
            if (escape == Vector2.Zero)
            {
                escape = move;
            }
        }

        if (inDanger && player.Dodge is { Charges: > 0 } dodge && dodge.TryDodge(escape == Vector2.Zero ? move : escape.Normalized()))
        {
            _dodges++;
        }
    }

    private static void Press(string action, float strength)
    {
        if (strength > 0.05f)
        {
            Input.ActionPress(action, strength);
        }
        else
        {
            Input.ActionRelease(action);
        }
    }

    private static void ReleaseAll()
    {
        foreach (string a in new[] { "move_right", "move_left", "move_up", "move_down" })
        {
            Input.ActionRelease(a);
        }
    }

    private static bool IsHostile(AreaBurst burst) => burst.Hit.Faction != Faction.Player && !burst.Harmless;

    private static bool LaneHostile(LineStrike lane) => lane.Hit.Faction != Faction.Player;

    private static float RadiusOf(AreaBurst burst) => burst.Radius;

    private static float InnerOf(AreaBurst burst) => burst.InnerRadius;

    private static float TimeLeft(AreaBurst burst) => burst.TimeToImpact;

    private static float LaneTimeLeft(LineStrike lane) => lane.TimeToImpact;
}
