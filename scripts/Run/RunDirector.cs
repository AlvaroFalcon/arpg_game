#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Enemies;

namespace ArpgGame.Run;

/// <summary>
/// Decides what spawns, where and when — docs/design/run.md -> "The timeline".
///
/// A run is about seven and a half minutes:
/// <list type="bullet">
/// <item>0:00 – 3:00: escalating waves; new enemy types unlock every half minute
/// or so; an elite every 45 s; a swarm event every minute.</item>
/// <item>3:00: <b>the Bone Herald</b> (mid boss). Normal spawns drop to a trickle
/// while it lives.</item>
/// <item>After the Herald: the pressure resumes, harder, until 7:00.</item>
/// <item>5:30: the Herald returns.</item>
/// <item>7:00: <b>the Ember Colossus</b>. The arena is cleared of normal enemies
/// (they burn away, no experience) and the fight is one on one, plus whatever
/// the boss summons. Killing it wins the run.</item>
/// <item>After the Colossus: <b>the endless phase</b> — a portal home opens where
/// it fell, spawning resumes and keeps getting harder, a boss comes every two
/// minutes, and the reward keeps growing until the player steps through (or
/// falls — the run still counts as a victory).</item>
/// </list>
///
/// Everything is driven by the <see cref="Danger"/> level, which rises every
/// minute: enemy life and damage (compounding), spawn rate, the enemy cap, how
/// often elites appear and the monster level.
///
/// Spawns appear just outside the camera so enemies walk <i>in</i> rather than
/// popping into existence on top of the player.
/// </summary>
[GlobalClass]
public partial class RunDirector : Node
{
    [Signal]
    public delegate void AnnouncedEventHandler(string title, string subtitle, Color color);

    [Signal]
    public delegate void BossSpawnedEventHandler(Node2D boss);

    [Export] public PackedScene? Slime;
    [Export] public PackedScene? Bat;
    [Export] public PackedScene? Caster;
    [Export] public PackedScene? Brute;
    [Export] public PackedScene? Imp;
    [Export] public PackedScene? Herald;
    [Export] public PackedScene? Colossus;

    [Export] public float HeraldTime = 180f;
    [Export] public float HeraldReturnTime = 330f;
    [Export] public float ColossusTime = 420f;

    /// <summary>Endless phase: seconds between bosses.</summary>
    [Export] public float EndlessBossInterval = 120f;
    [Export] public float SwarmInterval = 60f;

    public RunState State { get; set; } = null!;
    public Arena Arena { get; set; } = null!;

    /// <summary>When false, nothing spawns (tests, capture tools).</summary>
    public bool Spawning { get; set; } = true;

    private readonly RandomNumberGenerator _rng = new();
    private float _budget;
    private float _nextElite;
    private float _nextSwarm;
    private bool _heraldSpawned;
    private bool _heraldReturned;
    private bool _colossusSpawned;
    private Enemy? _herald;
    private Enemy? _colossus;
    private float _nextEndlessBoss;
    private int _endlessBosses;

    /// <summary>The Colossus has fallen: the run is won and the endless phase is on.</summary>
    public bool ColossusDefeated { get; private set; }

    /// <summary>The portal home that opens where the Colossus fell.</summary>
    public Node2D? ExitPortal { get; private set; }

    /// <summary>When the endless phase began (0 before).</summary>
    public float EndlessSince { get; private set; }

    public Enemy? ActiveBoss => _colossus is not null && IsInstanceValid(_colossus) ? _colossus
        : _herald is not null && IsInstanceValid(_herald) ? _herald : null;

    /// <summary>The first elite waits until the player has a few levels.</summary>
    [Export] public float FirstEliteTime = 90f;

    public override void _Ready()
    {
        _rng.Randomize();
        _nextElite = FirstEliteTime;
        _nextSwarm = SwarmInterval;
    }

    /// <summary>Re-aligns event timers after <see cref="RunState.SkipTo"/>.</summary>
    public void SyncTimers()
    {
        float t = State.Elapsed;
        _nextElite = Mathf.Max(FirstEliteTime, t + EliteIntervalAt(t));
        _nextSwarm = Mathf.Ceil((t + 1f) / SwarmInterval) * SwarmInterval;
        _heraldSpawned = t > HeraldTime + 1f;
        _heraldReturned = t > HeraldReturnTime + 1f;
    }

    private bool HeraldAlive => _herald is not null && IsInstanceValid(_herald) && !_herald.Health.IsDead;

    public override void _PhysicsProcess(double delta)
    {
        if (!Spawning || State.Ended || State.Player is null)
        {
            return;
        }

        float t = State.Elapsed;
        float dt = (float)delta;

        if (!_colossusSpawned && t >= ColossusTime)
        {
            StartFinalBoss();
            return;
        }

        if (_colossusSpawned && !ColossusDefeated)
        {
            return;
        }

        if (ColossusDefeated)
        {
            CheckExitPortal();
            if (State.Ended)
            {
                return;
            }

            if (t >= _nextEndlessBoss && ActiveBoss is null)
            {
                _nextEndlessBoss = t + EndlessBossInterval;
                _endlessBosses++;
                Enemy? boss = _endlessBosses % 2 == 1
                    ? SpawnBoss(Herald, "The Bone Herald", "It will not stay dead")
                    : SpawnBoss(Colossus, "An Ember Colossus", "Another heart kindles");
                if (_endlessBosses % 2 == 1)
                {
                    _herald = boss;
                }
            }
        }
        else if (!_heraldSpawned && t >= HeraldTime)
        {
            _heraldSpawned = true;
            _herald = SpawnBoss(Herald, "The Bone Herald", "A herald of the dead answers the slaughter");
        }
        else if (!_heraldReturned && t >= HeraldReturnTime && !HeraldAlive)
        {
            _heraldReturned = true;
            _herald = SpawnBoss(Herald, "The Bone Herald returns", "Stronger for its rest");
        }

        float rate = SpawnRate(t) * (HeraldAlive ? 0.15f : 1f);
        _budget += rate * dt;
        int cap = EnemyCap(t);
        Godot.Collections.Array<Node> enemies = GetTree().GetNodesInGroup(CombatQuery.EnemyGroup);
        int alive = enemies.Count;
        _rangedAlive = 0;
        foreach (Node n in enemies)
        {
            if (n is RangedEnemy)
            {
                _rangedAlive++;
            }
        }

        while (_budget >= 1f)
        {
            _budget -= 1f;
            if (alive >= cap)
            {
                _budget = 0f;
                break;
            }

            PackedScene? next = PickEnemy(t);
            if (next == Caster)
            {
                _rangedAlive++;
            }

            // Past the first minutes any spawn may come as an elite.
            SpawnAtEdge(next, elite: _rng.Randf() < EliteChance(t));
            alive++;
        }

        if (t >= _nextElite && !HeraldAlive)
        {
            _nextElite = t + EliteIntervalAt(t);
            SpawnAtEdge(PickEnemy(t), elite: true);
            EmitSignal(SignalName.Announced, "An elite approaches", "", new Color(1f, 0.85f, 0.4f));
        }

        if (t >= _nextSwarm && !HeraldAlive)
        {
            _nextSwarm += SwarmInterval;
            SpawnSwarm(t);
        }
    }

    /// <summary>The danger level: 1 for the first minute, +1 every minute after.
    /// Shown on the HUD; everything below scales with the same clock.</summary>
    public static int Danger(float t) => 1 + Mathf.FloorToInt(t / 60f);

    /// <summary>Spawns per second: about 1.1/s at the start, ~9/s at six
    /// minutes, and still climbing in the endless phase.</summary>
    public static float SpawnRate(float t) => 1.1f + t / 60f * 1.35f;

    /// <summary>How many enemies may be alive at once.</summary>
    public static int EnemyCap(float t) => Mathf.RoundToInt(115f + t / 60f * 24f);

    /// <summary>Life and damage multipliers for an enemy spawned at time t: a
    /// linear ramp times a compounding one, so an endless run keeps outpacing
    /// a finished build instead of flattening out (×5.5 life at 7:00, ×16 at 15:00).</summary>
    public static (float Life, float Damage) Scaling(float t)
    {
        float m = t / 60f;
        return ((1f + m * 0.38f) * Mathf.Pow(1.06f, m), (1f + m * 0.13f) * Mathf.Pow(1.03f, m));
    }

    /// <summary>Seconds between announced elites: 45 s early, down to 14 s.</summary>
    public static float EliteIntervalAt(float t) => Mathf.Max(14f, 45f - t / 60f * 4f);

    /// <summary>Chance that an ordinary spawn comes as an elite: none for two
    /// minutes, then rising to 4%.</summary>
    public static float EliteChance(float t) => Mathf.Clamp((t / 60f - 2f) * 0.005f, 0f, 0.04f);

    /// <summary>The monster level of a spawn (character experience).</summary>
    public static int MonsterLevelAt(float t) => 1 + Mathf.FloorToInt(t / 60f * 2f);

    /// <summary>Ranged enemies alive right now (refreshed once per spawning frame).</summary>
    private int _rangedAlive;

    /// <summary>
    /// docs/design/enemies.md -> "Bone Caster": ranged enemies are capped. Every
    /// caster adds an unmarked projectile stream, and the first autoplay pass
    /// showed a field of 30 casters doing most of a run's damage. A handful
    /// at a time keeps them a priority target rather than artillery.
    /// </summary>
    public static int MaxRanged(float t) => 5 + Mathf.FloorToInt(t / 60f) * 2;

    private PackedScene? PickEnemy(float t)
    {
        var table = new List<(PackedScene? Scene, float Weight)>
        {
            (Slime, Mathf.Max(3f, 10f - t / 30f)),
        };
        if (t > 25f) table.Add((Bat, 6f));
        if (t > 70f && _rangedAlive < MaxRanged(t)) table.Add((Caster, 1.4f + t / 240f));
        if (t > 115f) table.Add((Brute, 1.0f + t / 240f));
        if (t > 150f) table.Add((Imp, 2f + t / 120f));

        float total = 0f;
        foreach ((PackedScene? _, float w) in table)
        {
            total += w;
        }

        float roll = _rng.Randf() * total;
        foreach ((PackedScene? scene, float w) in table)
        {
            roll -= w;
            if (roll <= 0f)
            {
                return scene;
            }
        }

        return Slime;
    }

    private Vector2 EdgePoint(float extra = 60f)
    {
        Vector2 playerPos = State.Player!.GlobalPosition;
        Vector2 half = State.Player.GetViewportRect().Size / 2f;
        float radius = half.Length() + extra;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 p = playerPos + Vector2.Right.Rotated(_rng.Randf() * Mathf.Tau) * radius;
            Vector2 clamped = Arena.Clamp(p, 40f);
            if (clamped.DistanceTo(playerPos) > half.Y * 0.9f)
            {
                return clamped;
            }
        }

        return Arena.Clamp(playerPos + Vector2.Right.Rotated(_rng.Randf() * Mathf.Tau) * radius, 40f);
    }

    public Enemy? SpawnAtEdge(PackedScene? scene, bool elite = false) => scene is null ? null : SpawnAt(scene, EdgePoint(), elite);

    public Enemy SpawnAt(PackedScene scene, Vector2 position, bool elite = false)
    {
        var enemy = scene.Instantiate<Enemy>();
        GetTree().CurrentScene.AddChild(enemy);
        enemy.GlobalPosition = position;
        (float life, float damage) = Scaling(State.Elapsed);
        enemy.ApplyScaling(life, damage);
        enemy.MonsterLevel = MonsterLevelAt(State.Elapsed);
        if (elite)
        {
            enemy.MakeElite();
        }

        State.RegisterEnemy(enemy);
        return enemy;
    }

    /// <summary>A ring of fast enemies closing in from every side.</summary>
    private void SpawnSwarm(float t)
    {
        PackedScene? scene = t > 150f && _rng.Randf() < 0.4f ? Imp : t > 25f ? Bat : Slime;
        if (scene is null)
        {
            return;
        }

        int count = 16 + Mathf.RoundToInt(t / 30f);
        Vector2 centre = State.Player!.GlobalPosition;
        float radius = State.Player.GetViewportRect().Size.Length() / 2f + 40f;
        for (int i = 0; i < count; i++)
        {
            Vector2 p = Arena.Clamp(centre + Vector2.Right.Rotated(Mathf.Tau * i / count) * radius, 40f);
            SpawnAt(scene, p);
        }

        EmitSignal(SignalName.Announced, "Swarm!", "They come from every side", new Color(1f, 0.5f, 0.4f));
    }

    private Enemy? SpawnBoss(PackedScene? scene, string name, string subtitle)
    {
        if (scene is null)
        {
            return null;
        }

        Vector2 at = Arena.Clamp(State.Player!.GlobalPosition + new Vector2(0f, -260f), 120f);
        Enemy boss = SpawnAt(scene, at);
        Core.Sfx.Play(this, "boss_warn");
        EmitSignal(SignalName.Announced, name, subtitle, new Color(1f, 0.35f, 0.25f));
        EmitSignal(SignalName.BossSpawned, boss);
        return boss;
    }

    /// <summary>
    /// docs/design/bosses.md -> "The Ember Colossus": clears the arena so the
    /// final fight is readable, then brings the boss in at the rune circle.
    /// Gems on the floor are pulled to the player first so nothing is lost.
    /// </summary>
    public void StartFinalBoss()
    {
        _colossusSpawned = true;
        _herald = null;
        foreach (Node node in GetTree().GetNodesInGroup(CombatQuery.EnemyGroup))
        {
            if (node is Enemy enemy && !enemy.IsBoss)
            {
                enemy.ExperienceValue = 0;
                enemy.Health.ApplyResolvedDamage(float.MaxValue / 4f, null);
            }
            else if (node is Enemy boss && boss.IsBoss && boss != _colossus)
            {
                boss.ExperienceValue = 0;
                boss.Health.ApplyResolvedDamage(float.MaxValue / 4f, null);
            }
        }

        foreach (Node node in GetTree().GetNodesInGroup(RunState.PickupGroup))
        {
            (node as Pickup)?.Attract();
        }

        Core.Sfx.Music(this, "music_boss");
        _colossus = SpawnBoss(Colossus, "The Ember Colossus", "The arena's heart awakens");
        if (_colossus is not null)
        {
            _colossus.GlobalPosition = Arena.Center;
            _colossus.Killed += _ => OnColossusKilled();
        }
    }

    /// <summary>
    /// The run is won. Instead of ending it, a portal home opens where the
    /// Colossus fell and the endless phase starts: the waves come back
    /// stronger, and every extra minute adds to the reward (ResultsScreen).
    /// </summary>
    private void OnColossusKilled()
    {
        if (ColossusDefeated)
        {
            return;
        }

        ColossusDefeated = true;
        EndlessSince = State.Elapsed;
        _nextEndlessBoss = State.Elapsed + EndlessBossInterval;
        _nextElite = State.Elapsed + EliteIntervalAt(State.Elapsed);
        _nextSwarm = State.Elapsed + SwarmInterval / 2f;
        Vector2 at = _colossus is not null && IsInstanceValid(_colossus) ? _colossus.GlobalPosition : Arena.Center;
        EmitSignal(SignalName.Announced, "Victory", "A portal home opens — or stay for greater rewards", new Color(1f, 0.85f, 0.4f));
        Core.Sfx.Play(this, "victory");
        Core.Sfx.Music(this, "music_run");
        foreach (Node node in GetTree().GetNodesInGroup(RunState.PickupGroup))
        {
            (node as Pickup)?.Attract();
        }

        ExitPortal = new Portal { Name = "ExitPortal", ZIndex = -1 };
        GetTree().CurrentScene.AddChild(ExitPortal);
        ExitPortal.GlobalPosition = Arena.Clamp(at, 80f);
    }

    /// <summary>Leaves through the portal: the run ends as a victory.</summary>
    public void TakeExitPortal()
    {
        if (ColossusDefeated && !State.Ended)
        {
            State.EndRun(true);
        }
    }

    /// <summary>The portal opens where the Colossus fell — often right on top of the
    /// player — so it only works once they have stepped away from it: nobody should
    /// leave by accident before choosing whether to stay.</summary>
    private bool _portalArmed;

    private void CheckExitPortal()
    {
        if (ExitPortal is null || State.Player is null)
        {
            return;
        }

        float distance = State.Player.GlobalPosition.DistanceTo(ExitPortal.GlobalPosition);
        if (distance > 110f)
        {
            _portalArmed = true;
        }
        else if (_portalArmed && distance < 34f)
        {
            TakeExitPortal();
        }
    }
}
