#nullable enable
using System.Collections.Generic;
using Godot;
using PathToExile.Combat;
using PathToExile.Enemies;

namespace PathToExile.Run;

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
/// <item>7:00: <b>the Ember Colossus</b>. The arena is cleared of normal enemies
/// (they burn away, no experience) and the fight is one on one, plus whatever
/// the boss summons. Killing it wins the run.</item>
/// </list>
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
    [Export] public float ColossusTime = 420f;
    [Export] public float EliteInterval = 45f;
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
    private bool _colossusSpawned;
    private Enemy? _herald;
    private Enemy? _colossus;

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
        _nextElite = Mathf.Max(FirstEliteTime, Mathf.Ceil((t + 1f) / EliteInterval) * EliteInterval);
        _nextSwarm = Mathf.Ceil((t + 1f) / SwarmInterval) * SwarmInterval;
        _heraldSpawned = t > HeraldTime + 1f;
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

        if (_colossusSpawned)
        {
            return;
        }

        if (!_heraldSpawned && t >= HeraldTime)
        {
            _heraldSpawned = true;
            _herald = SpawnBoss(Herald, "The Bone Herald", "A herald of the dead answers the slaughter");
        }

        float rate = SpawnRate(t) * (HeraldAlive ? 0.15f : 1f);
        _budget += rate * dt;
        int cap = Mathf.RoundToInt(110 + t / 60f * 18f);
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

            SpawnAtEdge(next);
            alive++;
        }

        if (t >= _nextElite && !HeraldAlive)
        {
            _nextElite += EliteInterval;
            SpawnAtEdge(PickEnemy(t), elite: true);
            EmitSignal(SignalName.Announced, "An elite approaches", "", new Color(1f, 0.85f, 0.4f));
        }

        if (t >= _nextSwarm && !HeraldAlive)
        {
            _nextSwarm += SwarmInterval;
            SpawnSwarm(t);
        }
    }

    /// <summary>Spawns per second: gentle ramp, roughly 1.4/s at the start and
    /// ~8/s by the six-minute mark.</summary>
    public static float SpawnRate(float t) => 1.1f + t / 60f * 1.15f;

    /// <summary>Life and damage multipliers for an enemy spawned at time t.</summary>
    public static (float Life, float Damage) Scaling(float t) => (1f + t / 60f * 0.38f, 1f + t / 60f * 0.13f);

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

    private async void OnColossusKilled()
    {
        EmitSignal(SignalName.Announced, "Victory", "The Ember Colossus falls", new Color(1f, 0.85f, 0.4f));
        Core.Sfx.Play(this, "victory");
        foreach (Node node in GetTree().GetNodesInGroup(RunState.PickupGroup))
        {
            (node as Pickup)?.Attract();
        }

        await ToSignal(GetTree().CreateTimer(2.5, processAlways: false), SceneTreeTimer.SignalName.Timeout);
        State.EndRun(true);
    }
}
