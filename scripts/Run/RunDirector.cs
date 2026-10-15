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
        _waves = WaveNoise((int)(_rng.Randi() & 0x7fffffff));
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
        if (!Spawning || State.Ended || State.Players.Count == 0)
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

        // The director (docs/design/run.md -> "The director"): the budget fills at the
        // danger curve's rate times the current intensity — waves from 1D noise, eased
        // off while the player is in trouble — and is spent on enemies by cost.
        UpdateIntensity(t);
        int party = State.PartySize;
        float rate = SpawnRate(t) * Net.PartyRules.SpawnRate(party) * AverageCost * CurrentIntensity * (HeraldAlive ? 0.15f : 1f);
        _budget += rate * dt;
        int cap = Mathf.RoundToInt(EnemyCap(t) * Net.PartyRules.EnemyCap(party));
        _leashIn -= dt;
        if (_leashIn <= 0f)
        {
            _leashIn = 0.5f;
            Leash();
        }

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

        while (true)
        {
            PackedScene? next = _pending ?? PickEnemy(t);
            float cost = Cost(next);
            if (_budget < cost)
            {
                // Save up for it: the next spawn is the same pick.
                _pending = next;
                break;
            }

            _pending = null;
            _budget -= cost;
            if (alive >= cap)
            {
                _budget = Mathf.Min(_budget, cost);
                break;
            }

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

    // --- the director --------------------------------------------------------------

    /// <summary>Budget points per enemy: the heavier the enemy, the more it costs.
    /// Elites roll on top of an ordinary spawn and cost nothing extra.</summary>
    public float Cost(PackedScene? scene) =>
        scene == Brute ? 3f : scene == Caster ? 2f : scene == Imp ? 1.5f : 1f;

    /// <summary>Roughly what an average spawn costs: <see cref="SpawnRate"/> stays in
    /// enemies per second, the budget fills in points.</summary>
    public const float AverageCost = 1.3f;

    /// <summary>The intensity right now (0.55–1.5 from the waves, eased when the player is hurt).</summary>
    public float CurrentIntensity { get; private set; } = 1f;

    /// <summary>A surge is on (the waves are high): the HUD says so.</summary>
    public bool Surging { get; private set; }

    private FastNoiseLite _waves = WaveNoise(1);
    private PackedScene? _pending;
    private float _leashIn;

    /// <summary>The wave field: smooth 1D simplex noise (one octave), a surge or a lull
    /// roughly every half minute to a minute.</summary>
    public static FastNoiseLite WaveNoise(int seed) =>
        new() { Seed = seed, NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 1f / 55f, FractalType = FastNoiseLite.FractalTypeEnum.None };

    /// <summary>
    /// The waves: 1D noise over time, smoothstepped so it lingers at the highs and
    /// lows, mapped to 0.55–1.5. Instead of a flat ramp the pressure surges and
    /// eases on its own, never on a visible timer.
    /// </summary>
    public static float WaveIntensity(FastNoiseLite noise, float t)
    {
        float n = Mathf.Clamp(noise.GetNoise1D(t) * 0.5f + 0.5f, 0f, 1f);
        return 0.55f + 0.95f * Mathf.SmoothStep(0f, 1f, n);
    }

    /// <summary>Stress: a player low on life gets a breather (×0.65 under 35 %, ×0.85 under 60 %).</summary>
    public static float StressEase(float lifeFraction) => lifeFraction < 0.35f ? 0.65f : lifeFraction < 0.6f ? 0.85f : 1f;

    private void UpdateIntensity(float t)
    {
        float wave = WaveIntensity(_waves, t);
        float life = PartyLifeFraction();
        CurrentIntensity = wave * StressEase(life);
        if (!Surging && wave > 1.3f && t > 45f)
        {
            Surging = true;
            EmitSignal(SignalName.Announced, "A surge rises", "", new Color(1f, 0.45f, 0.35f));
        }
        else if (Surging && wave < 1f)
        {
            Surging = false;
        }
    }

    /// <summary>The party's average life fraction (a downed player counts as 0):
    /// the breather comes when the party as a whole is in trouble.</summary>
    private float PartyLifeFraction()
    {
        float sum = 0f;
        int n = 0;
        foreach (RunPlayer rp in State.Players)
        {
            n++;
            if (rp.Player?.Stats.Life is { Max: > 0f } pool && !rp.Downed)
            {
                sum += pool.Current / pool.Max;
            }
        }

        return n == 0 ? 1f : sum / n;
    }

    /// <summary>Players still standing (co-op: the downed are skipped).</summary>
    private List<Player> LivingPlayers()
    {
        _living.Clear();
        foreach (RunPlayer rp in State.Players)
        {
            if (rp.Player is { } p && IsInstanceValid(p) && !rp.Downed)
            {
                _living.Add(p);
            }
        }

        if (_living.Count == 0 && State.Player is { } local)
        {
            _living.Add(local);
        }

        return _living;
    }

    private readonly List<Player> _living = new();

    /// <summary>A living player picked at random (whose screen an edge spawn borders).</summary>
    private Player RandomPlayer()
    {
        List<Player> living = LivingPlayers();
        return living[_rng.RandiRange(0, living.Count - 1)];
    }

    /// <summary>Where the party is: the average position of the living players.</summary>
    private Vector2 PartyCentre()
    {
        List<Player> living = LivingPlayers();
        Vector2 sum = Vector2.Zero;
        foreach (Player p in living)
        {
            sum += p.GlobalPosition;
        }

        return sum / Mathf.Max(1, living.Count);
    }

    /// <summary>What the camera shows, in world space (one player's).</summary>
    private Rect2 View() => State.ViewOf(State.Player!);

    /// <summary>
    /// A spawn point just outside the view: a random point on the view's rectangle
    /// grown by <paramref name="margin"/> (sides weighted by length), clamped into the
    /// arena and kept only if it is still out of sight (near an arena wall the clamp
    /// can pull it on screen; then another side is tried).
    /// </summary>
    public static Vector2 EdgePointFor(Rect2 view, float margin, System.Func<Vector2, Vector2> clamp, RandomNumberGenerator rng)
    {
        Rect2 ring = view.Grow(margin);
        Rect2 seen = view.Grow(-8f);
        Vector2 last = ring.Position;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float w = ring.Size.X;
            float h = ring.Size.Y;
            float s = rng.Randf() * 2f * (w + h);
            Vector2 p = s < w ? ring.Position + new Vector2(s, 0f)
                : s < w + h ? ring.Position + new Vector2(w, s - w)
                : s < 2f * w + h ? ring.Position + new Vector2(2f * w + h - s, h)
                : ring.Position + new Vector2(0f, 2f * (w + h) - s);
            last = clamp(p);
            if (!seen.HasPoint(last))
            {
                return last;
            }
        }

        return last;
    }

    /// <summary>
    /// The leash: an ordinary enemy left more than a full view diagonal behind is
    /// moved back to the view's edge, so the pressure follows the player instead of
    /// a crowd trailing far behind (bosses never move).
    /// </summary>
    private void Leash()
    {
        List<Player> living = LivingPlayers();
        float limit = View().Size.Length();
        foreach (Node node in GetTree().GetNodesInGroup(CombatQuery.EnemyGroup))
        {
            if (node is not Enemy { IsBoss: false } enemy || enemy.Health.IsDead)
            {
                continue;
            }

            // Co-op: only an enemy far from *every* living player is pulled back.
            bool near = false;
            foreach (Player p in living)
            {
                if (enemy.GlobalPosition.DistanceTo(p.GlobalPosition) <= limit)
                {
                    near = true;
                    break;
                }
            }

            if (!near)
            {
                enemy.GlobalPosition = EdgePoint();
            }
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

    /// <summary>
    /// A spawn point just outside one living player's view. Co-op: a point that
    /// lands inside another player's view is re-rolled (around another player),
    /// so nobody sees monsters pop in.
    /// </summary>
    private Vector2 EdgePoint(float extra = 60f)
    {
        List<Player> living = LivingPlayers();
        if (living.Count <= 1)
        {
            return EdgePointFor(View(), extra, p => Arena.Clamp(p, 40f), _rng);
        }

        Vector2 point = Vector2.Zero;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            Player around = living[_rng.RandiRange(0, living.Count - 1)];
            point = EdgePointFor(State.ViewOf(around), extra, p => Arena.Clamp(p, 40f), _rng);
            bool seen = false;
            foreach (Player other in living)
            {
                if (other != around && State.ViewOf(other).Grow(-8f).HasPoint(point))
                {
                    seen = true;
                    break;
                }
            }

            if (!seen)
            {
                break;
            }
        }

        return point;
    }

    public Enemy? SpawnAtEdge(PackedScene? scene, bool elite = false) => scene is null ? null : SpawnAt(scene, EdgePoint(), elite);

    public Enemy SpawnAt(PackedScene scene, Vector2 position, bool elite = false)
    {
        var enemy = scene.Instantiate<Enemy>();
        GetTree().CurrentScene.AddChild(enemy);
        enemy.GlobalPosition = position;
        (float life, float damage) = Scaling(State.Elapsed);
        enemy.ApplyScaling(life, damage);
        int party = State.PartySize;
        if (party > 1)
        {
            // docs/design/multiplayer.md -> "Party rules": tougher monsters per extra player.
            enemy.ApplyTierScaling(enemy.IsBoss ? Net.PartyRules.BossLife(party) : Net.PartyRules.MonsterLife(party), 1f);
        }

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

        // Co-op: a ring around every living player, the party's total scaled like the spawn rate.
        List<Player> living = LivingPlayers();
        int total = Mathf.RoundToInt((16 + Mathf.RoundToInt(t / 30f)) * Net.PartyRules.SpawnRate(State.PartySize));
        int count = Mathf.Max(8, total / living.Count);
        foreach (Player player in living.ToArray())
        {
            Vector2 centre = player.GlobalPosition;
            float radius = State.ViewOf(player).Size.Length() / 2f + 40f;
            for (int i = 0; i < count; i++)
            {
                Vector2 p = Arena.Clamp(centre + Vector2.Right.Rotated(Mathf.Tau * i / count) * radius, 40f);
                SpawnAt(scene, p);
            }
        }

        EmitSignal(SignalName.Announced, "Swarm!", "They come from every side", new Color(1f, 0.5f, 0.4f));
    }

    private Enemy? SpawnBoss(PackedScene? scene, string name, string subtitle)
    {
        if (scene is null)
        {
            return null;
        }

        Vector2 at = Arena.Clamp(PartyCentre() + new Vector2(0f, -260f), 120f);
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

        State.AttractAll();

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
        State.AttractAll();

        ExitPortal = new Portal { Name = "ExitPortal", ZIndex = -1 };
        GetTree().CurrentScene.AddChild(ExitPortal);
        ExitPortal.GlobalPosition = Arena.Clamp(at, 80f);
        PortalOpened?.Invoke(ExitPortal.GlobalPosition);
    }

    /// <summary>The exit portal opened (co-op: clients draw it too).</summary>
    public event System.Action<Vector2>? PortalOpened;

    // --- co-op client mirror -------------------------------------------------------

    /// <summary>Co-op client: shows a banner the host's director announced.</summary>
    public void Announce(string title, string subtitle, Color color) => EmitSignal(SignalName.Announced, title, subtitle, color);

    /// <summary>Co-op client: a boss replica appeared (boss bar).</summary>
    public void NotifyBossSpawned(Enemy boss)
    {
        if (boss.Name.ToString().Contains("Colossus") || boss is EmberColossus)
        {
            _colossus = boss;
        }
        else
        {
            _herald = boss;
        }

        EmitSignal(SignalName.BossSpawned, boss);
    }

    /// <summary>Co-op client: the host's surge and victory state (HUD gauge).</summary>
    public void ApplyRemoteFlags(bool surging, bool colossusDefeated)
    {
        Surging = surging;
        ColossusDefeated = colossusDefeated;
    }

    /// <summary>Co-op client: the portal the host opened.</summary>
    public void ShowRemotePortal(Vector2 at)
    {
        if (ExitPortal is not null && IsInstanceValid(ExitPortal))
        {
            return;
        }

        ExitPortal = new Portal { Name = "ExitPortal", ZIndex = -1 };
        GetTree().CurrentScene.AddChild(ExitPortal);
        ExitPortal.GlobalPosition = at;
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
    /// leave by accident before choosing whether to stay. Co-op: per player, and
    /// the party leaves together — every living player must stand in it.</summary>
    private readonly HashSet<Player> _portalArmed = new();

    private void CheckExitPortal()
    {
        if (ExitPortal is null)
        {
            return;
        }

        List<Player> living = LivingPlayers();
        if (living.Count == 0)
        {
            return;
        }

        float radius = living.Count > 1 ? 60f : 34f;
        bool allIn = true;
        foreach (Player p in living)
        {
            float distance = p.GlobalPosition.DistanceTo(ExitPortal.GlobalPosition);
            if (distance > 110f)
            {
                _portalArmed.Add(p);
            }

            if (!_portalArmed.Contains(p) || distance >= radius)
            {
                allIn = false;
            }
        }

        if (allIn)
        {
            TakeExitPortal();
        }
    }
}
