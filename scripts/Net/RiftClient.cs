#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Enemies;
using ArpgGame.Run;

namespace ArpgGame.Net;

/// <summary>
/// A co-op client's half of the Survival Rift — docs/design/multiplayer.md.
/// The client simulates nothing that matters (<see cref="NetMode.ClientSide"/>):
/// <list type="bullet">
/// <item>its own player moves at once from the keyboard (prediction), the
/// inputs go to the host every physics frame, and the host's position for an
/// acknowledged input corrects it when they disagree (reconciliation);</item>
/// <item>monsters and the other players are replicas, drawn
/// <see cref="RiftProtocol.InterpolationDelay"/> in the past, between snapshots;</item>
/// <item>effects (projectiles, telegraphs, bullets, swings, orbits) are replayed
/// locally from the host's events, which are stamped with the host clock and
/// played when the interpolated view reaches that moment;</item>
/// <item>only this player's own drops exist here, and what they give comes from
/// the host's "collected" event into the local wallet and profile.</item>
/// </list>
/// Runs after the player in the physics frame (priority 1000).
/// </summary>
public partial class RiftClient : Node, IRunLink
{
    private const string ProjectileScenePath = "res://scenes/combat/Projectile.tscn";

    // --- limits on what a host may make this machine do ---------------------------------
    // docs/design/multiplayer.md -> "Security" -> "A hostile host". The host is a
    // friend, but a broken or hostile one must not hang, exhaust or corrupt this
    // machine: every number from the wire is made finite and kept in a sane range,
    // node creation is capped, and only monster scenes can be instantiated.

    /// <summary>Monster replicas are instantiated only from here.</summary>
    public const string EnemySceneDir = "res://scenes/enemies/";

    /// <summary>Above this many children under the run scene, nothing new is
    /// spawned from host events (a late wave is well under 2 000).</summary>
    public const int MaxSceneChildren = 6000;

    /// <summary>Event batches waiting for their moment; past this the oldest is applied at once.</summary>
    public const int MaxQueuedBatches = 600;

    /// <summary>Longest interned string kept (paths and ids are far shorter).</summary>
    public const int MaxStringChars = 512;

    /// <summary>Longest banner, toast or callout text shown.</summary>
    public const int MaxTextChars = 200;

    /// <summary>Largest amount of gold one collected drop may credit.</summary>
    public const float MaxGoldPerPickup = 1_000_000f;

    /// <summary>Largest character experience one event may grant.</summary>
    public const float MaxCharacterXp = 1e9f;

    /// <summary>A float from the host: finite, clamped to [min, max], else <paramref name="fallback"/>.</summary>
    public static float Sane(float v, float min, float max, float fallback = 0f) =>
        float.IsFinite(v) ? Mathf.Clamp(v, min, max) : fallback;

    private static string Text(string s) => s.Length > MaxTextChars ? s[..MaxTextChars] : s;

    /// <summary>Room for one more node spawned from a host event?</summary>
    private bool Room => Scene.GetChildCount() < MaxSceneChildren;

    /// <summary>Host events skipped by the limits above (tests, diagnostics).</summary>
    public int Refused { get; private set; }

    /// <summary>Test hook: when set, the local player walks this way (both locally and on the host).</summary>
    public static Vector2? TestMove { get; set; }

    public RunController Run { get; set; } = null!;

    private RunState State => Run.State;
    private RunDirector Director => Run.Director;
    private Player Local => Run.Player;

    /// <summary>Test hook: a message the host sent with <see cref="RiftHost.SendTest"/>.</summary>
    public event Action<string>? TestMessage;

    private NetSession _session = null!;
    private readonly Dictionary<ushort, string> _strings = new();
    private readonly Dictionary<ushort, Enemy> _enemies = new();
    private readonly Dictionary<ushort, Track> _enemyTracks = new();
    private readonly Dictionary<long, Track> _playerTracks = new();
    private readonly Dictionary<ushort, Projectile> _projectiles = new();
    private readonly Dictionary<uint, Pickup> _pickups = new();
    private readonly Dictionary<string, PackedScene?> _scenes = new();
    private readonly Queue<(double Time, byte[] Data)> _eventBatches = new();
    private readonly (ushort Seq, Vector2 Pos)[] _history = new (ushort, Vector2)[128];
    private readonly NetWriter _input = new(32);
    private readonly NetWriter _events = new(64);
    private double _offset;
    private bool _haveOffset;
    private ushort _seq;
    private byte _dodgeCount;
    private Vector2 _dodgeDir = Vector2.Right;
    private byte _lifeCount;
    private byte _manaCount;
    private float _lastLife = -1f;
    private PlayerCommand? _testCommand;

    /// <summary>Monster replicas alive here (tests, HUD).</summary>
    public int EnemyCount => _enemies.Count;

    public IEnumerable<Enemy> Enemies => _enemies.Values;

    /// <summary>This player's drops shown here.</summary>
    public int PickupCount => _pickups.Count;

    /// <summary>Total corrections applied to the predicted player (diagnostics).</summary>
    public int Corrections { get; private set; }

    /// <summary>Snapshots received (diagnostics).</summary>
    public int Snapshots { get; private set; }

    /// <summary>The last local physics step's cost in microseconds (perf measurement).</summary>
    public double LastProcessMicros { get; private set; }

    public override void _Ready()
    {
        ProcessPhysicsPriority = 1000;
        NetMode.ClientSide = true;
        _session = NetSession.Get(this)!;
        _session.Run = this;
        State.Mirror = true;
        State.ClockRunning = false;

        foreach (RunPlayer rp in State.Players)
        {
            if (rp.Player is { } p)
            {
                _players[rp.PeerId] = p;
                if (!p.IsLocal)
                {
                    _playerTracks[rp.PeerId] = new Track(p.GlobalPosition);
                }
            }
        }

        if (Local.Dodge is { } dodge)
        {
            dodge.Dodged += OnDodged;
        }

        if (Items.Flasks.Of(Local) is { } flasks)
        {
            flasks.Drank += OnDrank;
        }

        // Loaded: the host starts sending (and starts the run once everyone is here).
        CallDeferred(MethodName.SendReady);
    }

    private readonly Dictionary<long, Player> _players = new();

    /// <summary>The host has answered (any event batch): stop repeating "ready".</summary>
    private bool _heardFromHost;

    /// <summary>The host has sent something (so it is listening to this client).</summary>
    public bool HeardFromHost => _heardFromHost;
    private float _readyAgain;

    private void SendReady()
    {
        _events.Clear();
        _events.U8(RiftProtocol.ClientReady);
        _session.SendClientEvents(_events.ToArray());
    }

    public override void _ExitTree()
    {
        NetMode.ClientSide = false;
        TestMove = null;
        if (_session.Run == this)
        {
            _session.Run = null;
        }
    }

    /// <summary>Tells the host which card this player took.</summary>
    public void SendPick(string key)
    {
        _events.Clear();
        _events.U8(RiftProtocol.ClientPick);
        _events.Str(key);
        _session.SendClientEvents(_events.ToArray());
    }

    /// <summary>Test hook: a text message to the host's <see cref="RiftHost.TestMessage"/>.</summary>
    public void SendTest(string message)
    {
        _events.Clear();
        _events.U8(RiftProtocol.ClientTest);
        _events.Str(message);
        _session.SendClientEvents(_events.ToArray());
    }

    private void OnDodged()
    {
        _dodgeCount++;
        if (Local.Dodge is { } dodge && dodge.DashVelocity != Vector2.Zero)
        {
            _dodgeDir = dodge.DashVelocity.Normalized();
        }
    }

    private void OnDrank(string flask)
    {
        if (Items.Flasks.Of(Local) is not { } flasks)
        {
            return;
        }

        if (flask == flasks.Life.Name)
        {
            _lifeCount++;
        }
        else
        {
            _manaCount++;
        }
    }

    // --- clock ----------------------------------------------------------------------------

    private static double Now => Time.GetTicksUsec() / 1_000_000.0;

    /// <summary>The host moment this client is drawing right now.</summary>
    private double RenderTime => Now + _offset - RiftProtocol.InterpolationDelay;

    private void ObserveHostTime(double hostTime)
    {
        // The smallest delay seen so far is the best estimate of the clock offset;
        // drifting down slowly follows clocks that run at slightly different rates.
        double offset = hostTime - Now;
        if (!_haveOffset || offset > _offset)
        {
            _offset = offset;
            _haveOffset = true;
        }
        else
        {
            _offset = Math.Max(offset, _offset - 0.001);
        }
    }

    // --- the frame ---------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (State.Ended)
        {
            return;
        }

        // The host may still be loading its own copy of the run when this one is
        // ready: say so again until it answers.
        if (!_heardFromHost)
        {
            _readyAgain -= (float)delta;
            if (_readyAgain <= 0f)
            {
                _readyAgain = 0.5f;
                SendReady();
            }
        }

        // Test: drive the local player the same way the host drives its copy.
        if (TestMove is { } move)
        {
            _testCommand ??= new PlayerCommand();
            _testCommand.Move = move;
            Local.Command = _testCommand;
        }
        else if (Local.Command == _testCommand && _testCommand is not null)
        {
            Local.Command = null;
            _testCommand = null;
        }

        _seq++;
        _history[_seq % _history.Length] = (_seq, Local.GlobalPosition);
        _input.Clear();
        _input.U16(_seq);
        _input.Dir8(TestMove ?? Local.LastMoveDir);
        _input.Pos(Local.GetGlobalMousePosition());
        byte flags = 0;
        if (Local.IsSprinting)
        {
            flags |= 1;
        }

        if (Run.LevelUp.IsOpen)
        {
            flags |= 2;
        }

        _input.U8(flags);
        _input.U8(_dodgeCount);
        _input.Angle(_dodgeDir.Angle());
        _input.U8(_lifeCount);
        _input.U8(_manaCount);
        _session.SendInput(_input.ToArray());
    }

    public override void _Process(double delta)
    {
        ulong start = Time.GetTicksUsec();
        double render = RenderTime;

        // Events whose moment has come (all of them before the first snapshot).
        while (_eventBatches.Count > 0 && (!_haveOffset || _eventBatches.Peek().Time <= render))
        {
            ApplyEvents(_eventBatches.Dequeue().Data);
        }

        float dt = Mathf.Max((float)delta, 1e-4f);
        foreach ((ushort id, Enemy enemy) in _enemies)
        {
            if (!IsInstanceValid(enemy) || !_enemyTracks.TryGetValue(id, out Track? track))
            {
                continue;
            }

            Vector2 at = track.Sample(render);
            enemy.Velocity = (at - enemy.GlobalPosition) / dt;
            enemy.GlobalPosition = at;
        }

        foreach ((long peer, Track track) in _playerTracks)
        {
            if (_players.TryGetValue(peer, out Player? p) && IsInstanceValid(p))
            {
                Vector2 at = track.Sample(render);
                p.Velocity = (at - p.GlobalPosition) / dt;
                p.GlobalPosition = at;
            }
        }

        LastProcessMicros = Time.GetTicksUsec() - start;
    }

    // --- snapshots --------------------------------------------------------------------------

    public void OnSnapshot(byte[] data)
    {
        var r = new NetReader(data);
        r.U32();
        double hostTime = r.F32();
        byte part = r.U8();
        if (!double.IsFinite(hostTime) || hostTime < 0 || hostTime > 1e7)
        {
            Refused++;
            return; // a broken clock would make every position NaN or frozen
        }

        ObserveHostTime(hostTime);
        Snapshots++;
        if (part == 0)
        {
            ReadShared(r, hostTime);
        }

        int count = r.U16();
        for (int i = 0; i < count; i++)
        {
            ushort id = r.U16();
            Vector2 at = r.Pos();
            byte life = r.U8();
            byte flags = r.U8();
            if (!_enemies.TryGetValue(id, out Enemy? enemy) || !IsInstanceValid(enemy))
            {
                continue;
            }

            _enemyTracks[id].Push(hostTime, at);
            float max = enemy.Stats.Life?.Max ?? 0f;
            enemy.Health.SetNetState(max * life / 255f, false);
            enemy.ReplicaTint = (byte)((flags >> 1) & 7);
            if ((flags & RiftProtocol.EnemyHurt) != 0)
            {
                enemy.ReplicaFlash();
            }
        }
    }

    private void ReadShared(NetReader r, double hostTime)
    {
        // These numbers become this player's rewards and best time in their profile:
        // keep them finite and within a day of play.
        float elapsed = Sane(r.F32(), 0f, 86_400f);
        int kills = Math.Max(0, r.I32());
        int elites = r.U16();
        int bosses = r.U16();
        byte flags = r.U8();
        State.ApplyShared(elapsed, kills, elites, bosses);
        Director.ApplyRemoteFlags((flags & 1) != 0, (flags & 2) != 0);
        int players = r.U8();
        Vector2 myHostPos = Vector2.Zero;
        bool haveMine = false;
        for (int i = 0; i < players; i++)
        {
            long peer = r.I32();
            Vector2 at = r.Pos();
            float life = r.U16();
            r.U16();
            byte pflags = r.U8();
            float revive = r.U8() / 255f;
            if (!_players.TryGetValue(peer, out Player? p) || !IsInstanceValid(p))
            {
                continue;
            }

            bool dead = (pflags & RiftProtocol.PlayerDead) != 0;
            p.ReviveProgress = revive;
            if (p == Local)
            {
                myHostPos = at;
                haveMine = true;
                if (_lastLife >= 0f && life < _lastLife - 0.5f && !dead)
                {
                    p.ShowHurt(_lastLife - life);
                }

                _lastLife = life;
                p.Health?.SetNetState(life, dead);
                continue;
            }

            p.Health?.SetNetState(life, dead);
            p.Warded = (pflags & RiftProtocol.PlayerWarded) != 0;
            if (_playerTracks.TryGetValue(peer, out Track? track))
            {
                track.Push(hostTime, at);
            }
        }

        ushort ack = r.U16();
        float charges = r.U8() / 20f;
        ReadOnlySpan<byte> fractions = r.Bytes(6);
        if (Items.Flasks.Of(Local) is { } flasks)
        {
            flasks.Life.Charges = Mathf.Min(charges, flasks.Life.MaxCharges);
        }

        Local.GetNodeOrNull<PlayerSkillInput>("PlayerSkillInput")?.SetRemoteFractions(fractions);
        if (haveMine)
        {
            Reconcile(ack, myHostPos);
        }
    }

    /// <summary>
    /// Server reconciliation: where the host has this player after input
    /// <paramref name="ack"/>, against where this machine predicted it then. Small
    /// differences are noise; anything else is applied at once (the camera's own
    /// smoothing hides it) and the newer predictions are shifted by the same
    /// amount so the error is never counted twice.
    /// </summary>
    private void Reconcile(ushort ack, Vector2 hostPos)
    {
        (ushort seq, Vector2 predicted) = _history[ack % _history.Length];
        if (seq != ack || Local.Health is { IsDead: true })
        {
            if (Local.Health is { IsDead: true })
            {
                Local.GlobalPosition = hostPos;
            }

            return;
        }

        Vector2 error = hostPos - predicted;
        if (error.Length() < 3f)
        {
            return;
        }

        Corrections++;
        if (error.Length() > 300f)
        {
            // Far off (a long stall, a teleport): take the host's word outright.
            Local.GlobalPosition = hostPos;
            for (int i = 0; i < _history.Length; i++)
            {
                _history[i] = (_history[i].Seq, hostPos);
            }

            return;
        }

        Local.GlobalPosition += error;
        // The acknowledged entry is shifted too: a second snapshot with the same
        // ack (the host got no newer input meanwhile) must see no error left.
        for (int i = 0; i < _history.Length; i++)
        {
            (ushort s, Vector2 pos) = _history[i];
            if ((short)(s - ack) >= 0)
            {
                _history[i] = (s, pos + error);
            }
        }
    }

    // --- events ----------------------------------------------------------------------------

    public void OnHostEvents(byte[] data)
    {
        _heardFromHost = true;
        var r = new NetReader(data);
        double stamp = r.F32();
        if (!double.IsFinite(stamp))
        {
            stamp = 0; // play it now rather than never (or block every batch behind it)
        }

        _eventBatches.Enqueue((stamp, data));
        while (_eventBatches.Count > MaxQueuedBatches)
        {
            ApplyEvents(_eventBatches.Dequeue().Data);
        }
    }

    private string Str(ushort id) => id != 0 && _strings.TryGetValue(id, out string? s) ? s : "";

    private readonly Dictionary<string, SpriteFrames?> _frames = new();

    private SpriteFrames? Frames(ushort id)
    {
        string path = Str(id);
        if (path.Length == 0)
        {
            return null;
        }

        if (!_frames.TryGetValue(path, out SpriteFrames? frames))
        {
            // Only game resources, and only sprite frames (anything else is ignored).
            frames = path.StartsWith("res://") && !path.Contains("..") && ResourceLoader.Exists(path)
                ? ResourceLoader.Load(path) as SpriteFrames
                : null;
            _frames[path] = frames;
        }

        return frames;
    }

    private Node Scene => GetTree().CurrentScene;

    private void ApplyEvents(byte[] data)
    {
        var r = new NetReader(data);
        r.F32();
        try
        {
            while (r.More)
            {
                byte type = r.U8();
                if (!ApplyEvent(type, r))
                {
                    GD.PushWarning($"RiftClient: unknown event {type}, dropping the rest of the batch");
                    return;
                }
            }
        }
        catch (Exception e)
        {
            // A malformed batch costs the rest of that batch, never the run.
            Refused++;
            GD.PushWarning($"RiftClient: bad event batch from the host ({e.GetType().Name}: {e.Message})");
        }
    }

    private bool ApplyEvent(byte type, NetReader r)
    {
        switch (type)
        {
            case RiftProtocol.StringDef:
            {
                ushort id = r.U16();
                string text = r.Str();
                _strings[id] = text.Length > MaxStringChars ? "" : text;
                return true;
            }

            case RiftProtocol.EnemySpawn:
                SpawnEnemy(r);
                return true;
            case RiftProtocol.EnemyGone:
            {
                ushort id = r.U16();
                bool died = r.Bool();
                if (_enemies.Remove(id, out Enemy? enemy) && IsInstanceValid(enemy))
                {
                    if (_enemyTracks.TryGetValue(id, out Track? track))
                    {
                        enemy.GlobalPosition = track.Latest;
                    }

                    enemy.ReplicaDie(died);
                }

                _enemyTracks.Remove(id);
                return true;
            }

            case RiftProtocol.PlayerLeft:
            {
                long peer = r.I32();
                if (_players.TryGetValue(peer, out Player? mine) && mine == Local)
                {
                    return true; // this machine's own player is never removed by the host
                }

                if (_players.Remove(peer, out Player? p) && IsInstanceValid(p))
                {
                    if (State.PlayerOf(p) is { } rp)
                    {
                        State.RemovePlayer(rp);
                    }

                    p.QueueFree();
                }

                _playerTracks.Remove(peer);
                return true;
            }

            case RiftProtocol.Burst:
            {
                Vector2 at = r.Pos();
                float radius = Sane(r.F32(), 0f, 4000f);
                float delay = Sane(r.F32(), 0f, 10f);
                Color tint = r.Colour();
                float inner = Sane(r.F32(), 0f, 4000f);
                byte flags = r.U8();
                if (!Room)
                {
                    Refused++;
                    return true;
                }

                AreaBurst.Spawn(Scene, at, radius, delay, tint, new HitProfile { Faction = (flags & 1) != 0 ? Faction.Player : Faction.Enemy }, inner, (flags & 2) != 0);
                return true;
            }

            case RiftProtocol.Line:
            {
                Vector2 at = r.Pos();
                float angle = r.Angle();
                float length = Sane(r.F32(), 0f, 6000f);
                float width = Sane(r.F32(), 0f, 2000f);
                float delay = Sane(r.F32(), 0f, 10f);
                byte flags = r.U8();
                if (!Room)
                {
                    Refused++;
                    return true;
                }

                LineStrike line = LineStrike.Spawn(Scene, at, Vector2.Right.Rotated(angle), length, width, delay, new HitProfile { Faction = (flags & 1) != 0 ? Faction.Player : Faction.Enemy });
                line.Harmless = (flags & 2) != 0;
                return true;
            }

            case RiftProtocol.Bullet:
            {
                Vector2 at = r.Pos();
                var velocity = new Vector2(Sane(r.F32(), -10000f, 10000f), Sane(r.F32(), -10000f, 10000f));
                float angular = Sane(r.F32(), -100f, 100f);
                float scale = Sane(r.F32(), 0.05f, 20f, 1f);
                float range = Sane(r.F32(), 0f, 20000f);
                SpriteFrames? frames = Frames(r.U16());
                bool player = r.Bool();
                if (!Room)
                {
                    Refused++;
                    return true;
                }

                BossBullet bullet = BossBullet.Spawn(Scene, at, velocity, new HitProfile { Faction = player ? Faction.Player : Faction.Enemy }, frames, scale);
                bullet.AngularVelocity = angular;
                bullet.Range = range;
                return true;
            }

            case RiftProtocol.Slash:
            {
                Vector2 at = r.Pos();
                float angle = r.Angle();
                float range = Sane(r.F32(), 0f, 4000f);
                float arc = Sane(r.F32(), 0f, Mathf.Tau);
                Color tint = r.Colour();
                if (!Room)
                {
                    Refused++;
                    return true;
                }

                SlashEffect.Spawn(Scene, at, Vector2.Right.Rotated(angle), range, arc, tint);
                return true;
            }

            case RiftProtocol.Orbit:
            {
                long peer = r.I32();
                int count = Math.Min((int)r.U8(), 32);
                float radius = Sane(r.F32(), 0f, 2000f);
                float duration = Sane(r.F32(), 0f, 60f);
                float speed = Sane(r.F32(), -100f, 100f);
                SpriteFrames? frames = Frames(r.U16());
                if (_players.TryGetValue(peer, out Player? anchor) && IsInstanceValid(anchor) && Room)
                {
                    var blades = new OrbitBlades { Anchor = anchor, Count = count, Radius = radius, Duration = duration, AngularSpeed = speed, Frames = frames, Hit = new HitProfile { Faction = Faction.Player } };
                    Scene.AddChild(blades);
                    blades.GlobalPosition = anchor.GlobalPosition;
                }

                return true;
            }

            case RiftProtocol.ProjSpawn:
            {
                ushort id = r.U16();
                Vector2 at = r.Pos();
                float angle = r.Angle();
                float speed = Sane(r.F32(), 0f, 20000f);
                float range = Sane(r.F32(), 0f, 20000f);
                float scale = Sane(r.F32(), 0.05f, 20f, 1f);
                SpriteFrames? frames = Frames(r.U16());
                r.Bool();
                if (!Room)
                {
                    Refused++;
                    return true;
                }

                PackedScene scene = SceneFor(ProjectileScenePath)!;
                var projectile = scene.Instantiate<Projectile>();
                Scene.AddChild(projectile);
                projectile.GlobalPosition = at;
                projectile.InitializeCosmetic(Vector2.Right.Rotated(angle), speed, range, scale, frames);
                if (_projectiles.Remove(id, out Projectile? old) && IsInstanceValid(old))
                {
                    old.QueueFree();
                }

                _projectiles[id] = projectile;
                projectile.TreeExiting += () =>
                {
                    if (_projectiles.TryGetValue(id, out Projectile? current) && current == projectile)
                    {
                        _projectiles.Remove(id);
                    }
                };
                return true;
            }

            case RiftProtocol.ProjTurn:
            {
                ushort id = r.U16();
                Vector2 at = r.Pos();
                float angle = r.Angle();
                if (_projectiles.TryGetValue(id, out Projectile? p) && IsInstanceValid(p))
                {
                    p.NetTurn(at, Vector2.Right.Rotated(angle));
                }

                return true;
            }

            case RiftProtocol.ProjEnd:
            {
                ushort id = r.U16();
                if (_projectiles.Remove(id, out Projectile? p) && IsInstanceValid(p))
                {
                    p.QueueFree();
                }

                return true;
            }

            case RiftProtocol.PickupSpawn:
            {
                uint id = r.U32();
                var kind = (PickupKind)r.U8();
                float value = Sane(r.F32(), 0f, MaxGoldPerPickup);
                Vector2 at = r.Pos();
                string key = Str(r.U16());
                if (!Enum.IsDefined(kind) || !Room)
                {
                    Refused++;
                    return true;
                }

                if (_pickups.Remove(id, out Pickup? old) && IsInstanceValid(old))
                {
                    old.QueueFree();
                }

                Pickup pickup = Pickup.Spawn(Scene, kind, value, at);
                pickup.Cosmetic = true;
                pickup.NetId = id;
                pickup.CurrencyKey = key;
                pickup.QueueRedraw();
                _pickups[id] = pickup;
                return true;
            }

            case RiftProtocol.PickupGone:
            {
                uint id = r.U32();
                if (_pickups.Remove(id, out Pickup? p) && IsInstanceValid(p))
                {
                    p.QueueFree();
                }

                return true;
            }

            case RiftProtocol.Collected:
            {
                var kind = (PickupKind)r.U8();
                // Credited to this player's wallet and saved with their profile.
                float value = Sane(r.F32(), 0f, MaxGoldPerPickup);
                string key = Str(r.U16());
                int detail = Math.Max(0, r.I32());
                Collected(kind, value, key, detail);
                return true;
            }

            case RiftProtocol.AttractAll:
                foreach (Pickup p in _pickups.Values)
                {
                    if (IsInstanceValid(p))
                    {
                        p.Attract();
                    }
                }

                return true;
            case RiftProtocol.Progress:
            {
                int level = Math.Clamp((int)r.U16(), 1, 999);
                float xp = Sane(r.F32(), 0f, 1e12f);
                int levels = r.U8();
                State.ApplyLocalProgress(level, xp, levels);
                return true;
            }

            case RiftProtocol.CharacterXp:
                // Saved with this player's character: never NaN, infinite or negative.
                Characters.CharacterBuild.Of(Local)?.GrantExperience(Sane(r.F32(), 0f, MaxCharacterXp));
                return true;
            case RiftProtocol.ItemDrop:
            {
                Vector2 at = r.Pos();
                string json = r.Str();
                if (Core.Profile.ValidateItemJson(json) is not null || !Room)
                {
                    Refused++;
                    return true;
                }

                if (Core.Profile.DeserializeItem(json) is { } item)
                {
                    Scene.AddChild(item);
                    item.GlobalPosition = at;
                }

                return true;
            }

            case RiftProtocol.Banner:
            {
                string title = Text(r.Str());
                string sub = Text(r.Str());
                Color color = r.Colour();
                Director.Announce(title, sub, color);
                return true;
            }

            case RiftProtocol.BossSpawned:
            {
                ushort id = r.U16();
                if (_enemies.TryGetValue(id, out Enemy? boss) && IsInstanceValid(boss))
                {
                    Core.Sfx.Play(this, "boss_warn");
                    Director.NotifyBossSpawned(boss);
                }

                return true;
            }

            case RiftProtocol.BossInfo:
            {
                ushort id = r.U16();
                int phase = r.U8();
                string attack = Text(r.Str());
                if (_enemies.TryGetValue(id, out Enemy? e) && e is BossEnemy boss && IsInstanceValid(boss))
                {
                    boss.SetReplicaInfo(phase, attack);
                }

                return true;
            }

            case RiftProtocol.Callout:
            {
                ushort id = r.U16();
                string text = Text(r.Str());
                Color color = r.Colour();
                if (_enemies.TryGetValue(id, out Enemy? e) && e is BossEnemy boss && IsInstanceValid(boss))
                {
                    boss.ReplicaCall(text, color);
                }

                return true;
            }

            case RiftProtocol.Portal:
                Director.ShowRemotePortal(r.Pos());
                return true;
            case RiftProtocol.RunBegin:
                State.ClockRunning = true;
                return true;
            case RiftProtocol.RunEnded:
                State.EndRun(r.Bool());
                return true;
            case RiftProtocol.DamageNumber:
            {
                ushort id = r.U16();
                float amount = Sane(r.F32(), 0f, 1e12f);
                if (_enemies.TryGetValue(id, out Enemy? e) && IsInstanceValid(e) && Room)
                {
                    e.ReplicaDamageNumber(amount);
                }

                return true;
            }

            case RiftProtocol.Toast:
            {
                string text = Text(r.Str());
                Color color = r.Colour();
                State.ShowToast(text, color);
                return true;
            }

            case RiftProtocol.TestPing:
                TestMessage?.Invoke(r.Str());
                return true;
        }

        return false;
    }

    private PackedScene? SceneFor(string path)
    {
        if (!_scenes.TryGetValue(path, out PackedScene? scene))
        {
            scene = ResourceLoader.Load(path) as PackedScene;
            _scenes[path] = scene;
        }

        return scene;
    }

    private void SpawnEnemy(NetReader r)
    {
        ushort id = r.U16();
        string path = Str(r.U16());
        Vector2 at = r.Pos();
        var rarity = (MonsterRarity)r.U8();
        float scale = Sane(r.F32(), 0.1f, 10f, 1f);
        string name = Text(r.Str());
        int modCount = r.U8();
        var mods = new List<string>(modCount);
        for (int i = 0; i < modCount; i++)
        {
            mods.Add(Str(r.U16()));
        }

        float maxLife = Sane(r.F32(), 1f, 1e12f, 1f);
        bool dormant = r.U8() != 0;
        // Only monster scenes, and only if the root really is a monster: a host
        // can't make this machine instantiate any other scene of the game.
        if (!Enum.IsDefined(rarity) || !path.StartsWith(EnemySceneDir) || !path.EndsWith(".tscn") || path.Contains("..")
            || !ResourceLoader.Exists(path) || !Room || SceneFor(path) is not { } packed)
        {
            Refused++;
            return;
        }

        if (packed.Instantiate() is not Enemy enemy)
        {
            Refused++;
            return;
        }

        if (_enemies.Remove(id, out Enemy? stale) && IsInstanceValid(stale))
        {
            stale.QueueFree();
        }

        enemy.IsReplica = true;
        enemy.NetId = id;
        enemy.Dormant = dormant;
        Scene.AddChild(enemy);
        enemy.GlobalPosition = at;
        enemy.ApplyReplicaLook(rarity, mods, name, scale, maxLife);
        _enemies[id] = enemy;
        _enemyTracks[id] = new Track(at);
    }

    /// <summary>The host credited something to this player: into the local wallet
    /// (saved with this machine's profile at the end), with the usual sounds.</summary>
    private void Collected(PickupKind kind, float value, string key, int detail)
    {
        switch (kind)
        {
            case PickupKind.Experience:
                Core.Sfx.Play(this, "pickup", -10f);
                break;
            case PickupKind.Life:
                Core.Sfx.Play(this, "pickup");
                break;
            case PickupKind.Gold:
                Core.Sfx.Play(this, "pickup");
                Local.Wallet.Add(Core.Profile.GoldId, (long)value);
                State.ApplyLocalCollected(kind, (long)value);
                break;
            case PickupKind.Currency when Items.Crafting.Find(key) is { } currency:
                Core.Sfx.Play(this, "pickup", -2f);
                Local.Wallet.Add(currency.Id, 1);
                State.ApplyLocalCollected(kind, 1);
                State.ShowToast(currency.Name, currency.Color);
                break;
            case PickupKind.Nuke:
                Core.Sfx.Play(this, "boss_warn");
                ScreenFlash.Spawn(this, new Color(1f, 0.55f, 0.3f));
                State.ApplyLocalCollected(kind, 1);
                State.ShowToast($"Cataclysm — {detail} slain", new Color(1f, 0.45f, 0.3f));
                break;
            case PickupKind.Magnet:
                Core.Sfx.Play(this, "levelup", -6f);
                ScreenFlash.Spawn(this, new Color(0.45f, 0.75f, 1f), 0.25f);
                State.ShowToast("Lodestone — everything comes to you", new Color(0.55f, 0.8f, 1f));
                break;
        }
    }

    // --- leaving ------------------------------------------------------------------------------

    public void OnClientInput(long peer, byte[] data)
    {
    }

    public void OnClientEvents(long peer, byte[] data)
    {
    }

    public void OnPeerLeft(long peer)
    {
    }

    /// <summary>The host is gone (docs/design/multiplayer.md -> "Disconnects"):
    /// the run ends here and now, with this player's rewards for what they did.</summary>
    public void OnHostLost(string reason)
    {
        // Whatever the host already sent still counts.
        while (_eventBatches.Count > 0)
        {
            ApplyEvents(_eventBatches.Dequeue().Data);
        }

        if (!State.Ended)
        {
            Director.Announce("The host left", reason, new Color(1f, 0.6f, 0.4f));
            State.EndRun(Director.ColossusDefeated);
        }
    }

    /// <summary>
    /// A few host-time samples of one entity's position, sampled at the render
    /// time: interpolated between the two around it, held (with a short
    /// extrapolation) past the newest when a snapshot is late.
    /// </summary>
    private sealed class Track
    {
        private readonly double[] _times = new double[4];
        private readonly Vector2[] _positions = new Vector2[4];
        private int _count;

        public Track(Vector2 at)
        {
            _positions[0] = at;
            _times[0] = double.MinValue;
            _count = 1;
        }

        public Vector2 Latest => _positions[_count - 1];

        public void Push(double time, Vector2 at)
        {
            if (_count > 0 && time <= _times[_count - 1])
            {
                return;
            }

            if (_count == _times.Length)
            {
                Array.Copy(_times, 1, _times, 0, _count - 1);
                Array.Copy(_positions, 1, _positions, 0, _count - 1);
                _count--;
            }

            _times[_count] = time;
            _positions[_count] = at;
            _count++;
        }

        public Vector2 Sample(double time)
        {
            if (_count == 1 || time <= _times[0])
            {
                return _positions[0];
            }

            for (int i = 1; i < _count; i++)
            {
                if (time <= _times[i])
                {
                    double span = _times[i] - _times[i - 1];
                    float t = span <= 0 ? 1f : (float)((time - _times[i - 1]) / span);
                    return _positions[i - 1].Lerp(_positions[i], t);
                }
            }

            // Past the newest sample: extrapolate briefly, then hold.
            int last = _count - 1;
            double gap = _times[last] - _times[last - 1];
            if (gap <= 0 || gap > 0.5)
            {
                return _positions[last];
            }

            float ahead = (float)Math.Min((time - _times[last]) / gap, 0.5);
            return _positions[last] + (_positions[last] - _positions[last - 1]) * ahead;
        }
    }
}
