#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Enemies;
using ArpgGame.Run;

namespace ArpgGame.Net;

/// <summary>
/// The host's half of the co-op Survival Rift — docs/design/multiplayer.md.
/// The host runs the whole game as offline (director, monsters, damage, drops,
/// experience); this node only watches it and tells each client what it needs:
/// <list type="bullet">
/// <item><b>Snapshots</b> at <see cref="RiftProtocol.SnapshotHz"/> (unreliable):
/// party numbers, every player, and the monsters near that client's player
/// (far ones at a quarter rate), quantised and split under the MTU.</item>
/// <item><b>Events</b> once per physics frame (reliable batches): monsters and
/// projectiles appearing and leaving, telegraphs, bullets and swings to replay,
/// that player's own drops and progress, banners, bosses, the end.</item>
/// </list>
/// It also applies the clients' inputs to their players here, their card picks,
/// the revive rule and the begin-when-everyone-has-loaded handshake.
/// Runs last in the physics frame (priority 1000), after everything it reports.
/// </summary>
public partial class RiftHost : Node, IRunLink
{
    private sealed class Link
    {
        public long Peer;
        public RunPlayer Player = null!;
        public PlayerCommand Command = null!;
        public bool Ready;
        public readonly NetWriter Events = new(2048);
        public ushort AckSeq;
        public int SentLevel = -1;
        public float SentExperience = -1f;
        public float ProgressTimer;
        public float CharacterXpTimer;
        public long SnapshotBytes;
        public long EventBytes;
    }

    public RunController Run { get; set; } = null!;

    private RunState State => Run.State;
    private RunDirector Director => Run.Director;

    private NetSession _session = null!;
    private readonly Dictionary<long, Link> _links = new();
    private readonly List<Node> _pending = new();
    private readonly List<Enemy> _enemies = new();
    private readonly List<Action> _afterCapture = new();
    private readonly Dictionary<string, ushort> _strings = new();
    private readonly List<string> _stringList = new();
    private readonly NetWriter _record = new(256);
    private readonly NetWriter _chunk = new(RiftProtocol.ChunkBytes + 64);
    private readonly NetWriter _flush = new(4096);
    private ushort _nextEnemyId;
    private ushort _nextProjectileId;
    private uint _nextPickupId;
    private uint _tick;
    private float _snapshotClock;
    private ulong _lastSnapshotMsec;
    private bool _begun;
    private Action? _onBegin;
    private float _waiting;
    private bool _closing;
    private BossEnemy? _boss;
    private int _bossPhase;
    private string _bossAttack = "";
    private readonly Stopwatch _cpu = new();

    /// <summary>Seconds the host waits for slow loaders before starting anyway.</summary>
    public const float LoadTimeout = 20f;

    /// <summary>Test hook: a test message from a client (see <see cref="SendTest"/>).</summary>
    public event Action<long, string>? TestMessage;

    /// <summary>Peers that have loaded the run and get traffic.</summary>
    public int ReadyPeers
    {
        get
        {
            int n = 0;
            foreach (Link l in _links.Values)
            {
                n += l.Ready ? 1 : 0;
            }

            return n;
        }
    }

    public int Peers => _links.Count;

    /// <summary>Microseconds this node spent in its last physics step (perf measurement).</summary>
    public double LastStepMicros { get; private set; }

    /// <summary>Payload bytes sent to <paramref name="peer"/> so far (snapshots, events).</summary>
    public (long Snapshots, long Events) BytesTo(long peer) =>
        _links.TryGetValue(peer, out Link? l) ? (l.SnapshotBytes, l.EventBytes) : (0, 0);

    public override void _Ready()
    {
        ProcessPhysicsPriority = 1000;
        _session = NetSession.Get(this)!;
        _session.Run = this;
        foreach (RunPlayer rp in State.Players)
        {
            if (!rp.IsLocal && rp.Player is { } p)
            {
                p.Command ??= new PlayerCommand();
                _links[rp.PeerId] = new Link { Peer = rp.PeerId, Player = rp, Command = p.Command };
            }
        }

        GetTree().NodeAdded += OnNodeAdded;
        State.PickupSpawned += OnPickupSpawned;
        State.PickupCollected += OnPickupCollected;
        State.Magnetized += OnMagnetized;
        State.RemoteItemDropped += OnRemoteItem;
        State.RunEnded += OnRunEnded;
        State.Levelled += OnLevelled;
        Director.Announced += OnAnnounced;
        Director.BossSpawned += OnBossSpawned;
        Director.PortalOpened += OnPortalOpened;
        Health.AnyHitResolved += OnAnyHit;
    }

    public override void _ExitTree()
    {
        _closing = true;
        GetTree().NodeAdded -= OnNodeAdded;
        State.PickupSpawned -= OnPickupSpawned;
        State.PickupCollected -= OnPickupCollected;
        State.Magnetized -= OnMagnetized;
        State.RemoteItemDropped -= OnRemoteItem;
        State.RunEnded -= OnRunEnded;
        State.Levelled -= OnLevelled;
        Director.Announced -= OnAnnounced;
        Director.BossSpawned -= OnBossSpawned;
        Director.PortalOpened -= OnPortalOpened;
        Health.AnyHitResolved -= OnAnyHit;
        if (_session.Run == this)
        {
            _session.Run = null;
        }
    }

    /// <summary>Holds the clock and the director until every client has loaded
    /// (or <see cref="LoadTimeout"/> passes), then runs <paramref name="begin"/>.</summary>
    public void BeginWhenReady(Action begin)
    {
        _onBegin = begin;
        State.ClockRunning = false;
    }

    // --- the frame ------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        _cpu.Restart();
        float dt = (float)delta;
        if (!_begun && _onBegin is not null)
        {
            _waiting += dt;
            if (ReadyPeers == _links.Count || _waiting >= LoadTimeout)
            {
                _begun = true;
                State.ClockRunning = true;
                _onBegin();
                Broadcast(w => w.U8(RiftProtocol.RunBegin));
            }
        }

        CapturePending();
        ApplyCommands();
        Revives(dt);
        TrackBoss();
        Progress(dt);
        _snapshotClock += dt;
        if (_snapshotClock >= 1f / RiftProtocol.SnapshotHz)
        {
            _snapshotClock = 0f;
            _tick++;
            SendSnapshots();
            _lastSnapshotMsec = Time.GetTicksMsec();
        }

        FlushEvents();
        LastStepMicros = _cpu.Elapsed.TotalMilliseconds * 1000.0;
    }

    private void ApplyCommands()
    {
        ulong now = Time.GetTicksMsec();
        foreach (Link link in _links.Values)
        {
            if (link.Player.Player is not { } p || !IsInstanceValid(p))
            {
                continue;
            }

            // A player whose packets stopped stands still rather than walking on.
            if (now - link.Command.ReceivedMsec > 300)
            {
                link.Command.Move = Vector2.Zero;
                link.Command.Sprint = false;
            }

            p.Warded = link.Command.Choosing && !State.Ended;
        }
    }

    /// <summary>
    /// docs/design/multiplayer.md -> "Deaths": a downed player is revived by an
    /// ally standing within <see cref="PartyRules.ReviveRadius"/> for
    /// <see cref="PartyRules.ReviveTime"/> seconds (progress drains when they step away).
    /// </summary>
    private void Revives(float dt)
    {
        if (State.Ended)
        {
            return;
        }

        foreach (RunPlayer rp in State.Players)
        {
            if (rp.Player is not { } downed || !rp.Downed)
            {
                continue;
            }

            bool helped = false;
            foreach (RunPlayer other in State.Players)
            {
                if (other != rp && !other.Downed && other.Player is { Warded: false } ally
                    && ally.GlobalPosition.DistanceTo(downed.GlobalPosition) <= PartyRules.ReviveRadius)
                {
                    helped = true;
                    break;
                }
            }

            float progress = Mathf.Clamp(downed.ReviveProgress + (helped ? dt : -dt) / PartyRules.ReviveTime, 0f, 1f);
            if (progress >= 1f)
            {
                progress = 0f;
                downed.Health!.Revive(PartyRules.ReviveLife);
                downed.Health.GrantInvulnerability(PartyRules.ReviveGrace);
                ToastAll($"{NameOf(rp)} is back on their feet!", new Color(0.5f, 1f, 0.6f));
            }

            downed.ReviveProgress = progress;
        }
    }

    public static string NameOf(RunPlayer rp) => rp.Player is { } p && p.NetName.Length > 0 ? p.NetName : "You";

    /// <summary>A toast on every machine (the host's own HUD too).</summary>
    public void ToastAll(string text, Color color)
    {
        State.ShowToast(text, color);
        Broadcast(w =>
        {
            w.U8(RiftProtocol.Toast);
            w.Str(text);
            w.Colour(color);
        });
    }

    private void TrackBoss()
    {
        if (_boss is null || !IsInstanceValid(_boss) || _boss.NetId == 0)
        {
            return;
        }

        if (_boss.Phase != _bossPhase || _boss.CurrentAttack != _bossAttack)
        {
            _bossPhase = _boss.Phase;
            _bossAttack = _boss.CurrentAttack;
            ushort id = _boss.NetId;
            string attack = _bossAttack;
            int phase = _bossPhase;
            Broadcast(w =>
            {
                w.U8(RiftProtocol.BossInfo);
                w.U16(id);
                w.U8((byte)phase);
                w.Str(attack);
            });
        }
    }

    private void Progress(float dt)
    {
        foreach (Link link in _links.Values)
        {
            if (!link.Ready)
            {
                continue;
            }

            RunPlayer rp = link.Player;
            link.ProgressTimer -= dt;
            bool changed = rp.Level != link.SentLevel || !Mathf.IsEqualApprox(rp.Experience, link.SentExperience);
            if (rp.UnreportedLevelUps > 0 || (changed && link.ProgressTimer <= 0f))
            {
                link.ProgressTimer = 0.1f;
                link.SentLevel = rp.Level;
                link.SentExperience = rp.Experience;
                int levels = rp.UnreportedLevelUps;
                rp.UnreportedLevelUps = 0;
                link.Events.U8(RiftProtocol.Progress);
                link.Events.U16((ushort)rp.Level);
                link.Events.F32(rp.Experience);
                link.Events.U8((byte)Mathf.Min(levels, 255));
            }

            link.CharacterXpTimer -= dt;
            if (link.CharacterXpTimer <= 0f && rp.PendingCharacterExperience > 0f)
            {
                link.CharacterXpTimer = 0.5f;
                link.Events.U8(RiftProtocol.CharacterXp);
                link.Events.F32(rp.PendingCharacterExperience);
                rp.PendingCharacterExperience = 0f;
            }
        }
    }

    // --- events ----------------------------------------------------------------------

    private ushort Intern(string s)
    {
        if (s.Length == 0)
        {
            return 0;
        }

        if (_strings.TryGetValue(s, out ushort id))
        {
            return id;
        }

        id = (ushort)(_stringList.Count + 1);
        _strings[s] = id;
        _stringList.Add(s);
        foreach (Link link in _links.Values)
        {
            if (link.Ready)
            {
                WriteStringDef(link.Events, id, s);
            }
        }

        return id;
    }

    private static void WriteStringDef(NetWriter w, ushort id, string s)
    {
        w.U8(RiftProtocol.StringDef);
        w.U16(id);
        w.Str(s);
    }

    /// <summary>Writes one record to every loaded client (or, with
    /// <paramref name="near"/>, to those whose player is within <paramref name="radius"/>).</summary>
    private void Broadcast(Action<NetWriter> write, Vector2? near = null, float radius = 1800f)
    {
        if (_closing)
        {
            return;
        }

        _record.Clear();
        write(_record);
        byte[]? bytes = null;
        foreach (Link link in _links.Values)
        {
            if (!link.Ready)
            {
                continue;
            }

            if (near is { } at && link.Player.Player is { } p && p.GlobalPosition.DistanceTo(at) > radius)
            {
                continue;
            }

            bytes ??= _record.ToArray();
            link.Events.Bytes(bytes);
        }
    }

    private Link? LinkOf(Player? player) =>
        player is { IsLocal: false } p && _links.TryGetValue(p.PeerId, out Link? l) && l.Ready ? l : null;

    private void FlushEvents()
    {
        foreach (Link link in _links.Values)
        {
            if (!link.Ready || link.Events.Length == 0)
            {
                continue;
            }

            // Each batch is stamped with the host clock: clients replay it when their
            // interpolated view reaches that moment, so effects line up with monsters.
            _flush.Clear();
            _flush.F32(HostTime);
            _flush.Bytes(link.Events.ToArray());
            link.Events.Clear();
            byte[] data = _flush.ToArray();
            link.EventBytes += data.Length;
            _session.SendHostEvents(link.Peer, data);
        }
    }

    private static float HostTime => (float)(Time.GetTicksUsec() / 1_000_000.0);

    // --- capture: what appeared this frame --------------------------------------------

    private void OnNodeAdded(Node node)
    {
        if (node is Enemy or Projectile or AreaBurst or LineStrike or BossBullet or SlashEffect or OrbitBlades)
        {
            _pending.Add(node);
        }
    }

    private void CapturePending()
    {
        if (_pending.Count > 0)
        {
            // Copy: capturing can add nodes (nothing does today, but cheap insurance).
            Node[] nodes = _pending.ToArray();
            _pending.Clear();
            foreach (Node node in nodes)
            {
                if (!IsInstanceValid(node) || !node.IsInsideTree() || node.IsQueuedForDeletion())
                {
                    continue;
                }

                switch (node)
                {
                    case Enemy enemy:
                        CaptureEnemy(enemy);
                        break;
                    case Projectile projectile when !projectile.Cosmetic:
                        CaptureProjectile(projectile);
                        break;
                    case AreaBurst burst:
                        CaptureBurst(burst);
                        break;
                    case LineStrike line:
                        CaptureLine(line);
                        break;
                    case BossBullet bullet:
                        CaptureBullet(bullet);
                        break;
                    case SlashEffect slash:
                        CaptureSlash(slash);
                        break;
                    case OrbitBlades orbit:
                        CaptureOrbit(orbit);
                        break;
                }
            }
        }

        foreach (Action action in _afterCapture)
        {
            action();
        }

        _afterCapture.Clear();
    }

    private ushort NextEnemyId()
    {
        _nextEnemyId++;
        if (_nextEnemyId == 0)
        {
            _nextEnemyId = 1;
        }

        return _nextEnemyId;
    }

    private void CaptureEnemy(Enemy enemy)
    {
        enemy.NetId = NextEnemyId();
        _enemies.Add(enemy);
        ushort id = enemy.NetId;
        enemy.TreeExiting += () => OnEnemyExiting(enemy, id);
        PrepareEnemyStrings(enemy);
        Broadcast(w => WriteEnemySpawn(w, enemy));
    }

    private void PrepareEnemyStrings(Enemy enemy)
    {
        Intern(enemy.SceneFilePath);
        foreach (MonsterMod mod in enemy.Mods)
        {
            Intern(mod.Id);
        }
    }

    private void WriteEnemySpawn(NetWriter w, Enemy enemy)
    {
        w.U8(RiftProtocol.EnemySpawn);
        w.U16(enemy.NetId);
        w.U16(Intern(enemy.SceneFilePath));
        w.Pos(enemy.GlobalPosition);
        w.U8((byte)enemy.Rarity);
        w.F32(enemy.Scale.X);
        w.Str(enemy.Rarity == MonsterRarity.Normal ? "" : enemy.ShownName);
        w.U8((byte)Mathf.Min(enemy.Mods.Count, 255));
        foreach (MonsterMod mod in enemy.Mods)
        {
            w.U16(Intern(mod.Id));
        }

        w.F32(enemy.Stats.Life?.Max ?? 0f);
        w.U8((byte)(enemy.Dormant ? 1 : 0));
    }

    private void OnEnemyExiting(Enemy enemy, ushort id)
    {
        _enemies.Remove(enemy);
        bool died = enemy.Health.IsDead;
        Broadcast(w =>
        {
            w.U8(RiftProtocol.EnemyGone);
            w.U16(id);
            w.Bool(died);
        });
    }

    private void CaptureProjectile(Projectile p)
    {
        _nextProjectileId++;
        if (_nextProjectileId == 0)
        {
            _nextProjectileId = 1;
        }

        ushort id = _nextProjectileId;
        ushort frames = Intern(FramesPath(p.Frames));
        Vector2 at = p.GlobalPosition;
        float angle = p.Direction.Angle();
        float speed = p.Speed;
        float range = p.RangeLeft;
        float scale = p.Scale.X;
        bool player = p.SourceFaction == Faction.Player;
        Broadcast(w =>
        {
            w.U8(RiftProtocol.ProjSpawn);
            w.U16(id);
            w.Pos(at);
            w.Angle(angle);
            w.F32(speed);
            w.F32(range);
            w.F32(scale);
            w.U16(frames);
            w.Bool(player);
        });
        p.Turned += turned =>
        {
            Vector2 pos = turned.GlobalPosition;
            float a = turned.Direction.Angle();
            Broadcast(w =>
            {
                w.U8(RiftProtocol.ProjTurn);
                w.U16(id);
                w.Pos(pos);
                w.Angle(a);
            });
        };
        p.TreeExiting += () => Broadcast(w =>
        {
            w.U8(RiftProtocol.ProjEnd);
            w.U16(id);
        });
    }

    /// <summary>A sprite animation's file path, or "" when it cannot be reloaded by path.</summary>
    private static string FramesPath(SpriteFrames? frames) =>
        frames is not null && frames.ResourcePath.StartsWith("res://") && !frames.ResourcePath.Contains("::") ? frames.ResourcePath : "";

    private void CaptureBurst(AreaBurst b)
    {
        Vector2 at = b.GlobalPosition;
        float radius = b.Radius;
        float delay = Mathf.Max(0f, b.TimeToImpact);
        Color tint = b.Tint;
        float inner = b.InnerRadius;
        byte flags = (byte)((b.Hit.Faction == Faction.Player ? 1 : 0) | (b.Harmless ? 2 : 0));
        Broadcast(w =>
        {
            w.U8(RiftProtocol.Burst);
            w.Pos(at);
            w.F32(radius);
            w.F32(delay);
            w.Colour(tint);
            w.F32(inner);
            w.U8(flags);
        }, at);
    }

    private void CaptureLine(LineStrike l)
    {
        Vector2 at = l.GlobalPosition;
        float angle = l.Rotation;
        float length = l.Length;
        float width = l.Width;
        float delay = Mathf.Max(0f, l.TimeToImpact);
        byte flags = (byte)((l.Hit.Faction == Faction.Player ? 1 : 0) | (l.Harmless ? 2 : 0));
        Broadcast(w =>
        {
            w.U8(RiftProtocol.Line);
            w.Pos(at);
            w.Angle(angle);
            w.F32(length);
            w.F32(width);
            w.F32(delay);
            w.U8(flags);
        }, at);
    }

    private void CaptureBullet(BossBullet b)
    {
        Vector2 at = b.GlobalPosition;
        Vector2 velocity = b.Velocity;
        float angular = b.AngularVelocity;
        float scale = b.Radius / 5f;
        float range = b.Range;
        ushort frames = Intern(FramesPath(b.Frames));
        bool player = b.Hit.Faction == Faction.Player;
        Broadcast(w =>
        {
            w.U8(RiftProtocol.Bullet);
            w.Pos(at);
            w.F32(velocity.X);
            w.F32(velocity.Y);
            w.F32(angular);
            w.F32(scale);
            w.F32(range);
            w.U16(frames);
            w.Bool(player);
        }, at);
    }

    private void CaptureSlash(SlashEffect s)
    {
        Vector2 at = s.GlobalPosition;
        float angle = s.Rotation;
        float range = s.Range;
        float arc = s.ArcDegrees;
        Color tint = s.Tint;
        Broadcast(w =>
        {
            w.U8(RiftProtocol.Slash);
            w.Pos(at);
            w.Angle(angle);
            w.F32(range);
            w.F32(arc);
            w.Colour(tint);
        }, at);
    }

    private void CaptureOrbit(OrbitBlades o)
    {
        if (o.Anchor is not Player anchor)
        {
            return;
        }

        int peer = (int)anchor.PeerId;
        int count = o.Count;
        float radius = o.Radius;
        float duration = o.Duration;
        float speed = o.AngularSpeed;
        ushort frames = Intern(FramesPath(o.Frames));
        Broadcast(w =>
        {
            w.U8(RiftProtocol.Orbit);
            w.I32(peer);
            w.U8((byte)Mathf.Clamp(count, 0, 255));
            w.F32(radius);
            w.F32(duration);
            w.F32(speed);
            w.U16(frames);
        }, anchor.GlobalPosition);
    }

    // --- run events ------------------------------------------------------------------

    private void OnAnnounced(string title, string subtitle, Color color) => Broadcast(w =>
    {
        w.U8(RiftProtocol.Banner);
        w.Str(title);
        w.Str(subtitle);
        w.Colour(color);
    });

    private void OnBossSpawned(Node2D boss)
    {
        if (boss is not Enemy enemy)
        {
            return;
        }

        if (enemy is BossEnemy b)
        {
            _boss = b;
            _bossPhase = 0;
            _bossAttack = "\u0001";
            b.MechanicCalled += (text, color) =>
            {
                if (IsInstanceValid(b) && b.NetId != 0)
                {
                    ushort bossId = b.NetId;
                    Broadcast(w =>
                    {
                        w.U8(RiftProtocol.Callout);
                        w.U16(bossId);
                        w.Str(text);
                        w.Colour(color);
                    });
                }
            };
        }

        // After the capture, so the spawn record goes out before this one.
        _afterCapture.Add(() =>
        {
            if (IsInstanceValid(enemy) && enemy.NetId != 0)
            {
                ushort id = enemy.NetId;
                Broadcast(w =>
                {
                    w.U8(RiftProtocol.BossSpawned);
                    w.U16(id);
                });
            }
        });
    }

    private void OnPortalOpened(Vector2 at) => Broadcast(w =>
    {
        w.U8(RiftProtocol.Portal);
        w.Pos(at);
    });

    private static void OnLevelled(RunPlayer who, int levels)
    {
        if (!who.IsLocal)
        {
            who.UnreportedLevelUps += levels;
        }
    }

    private void OnRunEnded(bool victory)
    {
        Broadcast(w =>
        {
            w.U8(RiftProtocol.RunEnded);
            w.Bool(victory);
        });
        FlushEvents();
    }

    private void OnAnyHit(Health target, DamagePayload payload, float taken)
    {
        // Damage numbers on a client, for that client's own hits only.
        if (taken < 0.5f || payload.SourceActor is not Player { IsLocal: false } source || target.GetParent() is not Enemy { NetId: > 0 } enemy)
        {
            return;
        }

        if (LinkOf(source) is { } link)
        {
            link.Events.U8(RiftProtocol.DamageNumber);
            link.Events.U16(enemy.NetId);
            link.Events.F32(taken);
        }
    }

    // --- instanced loot ----------------------------------------------------------------

    private void OnPickupSpawned(Pickup pickup)
    {
        if (pickup.OwnerPlayer is not { IsLocal: false } owner)
        {
            return;
        }

        // Somebody else's drop: simulated here, seen only on their machine.
        pickup.Visible = false;
        pickup.NetId = ++_nextPickupId;
        if (LinkOf(owner) is { } link)
        {
            WritePickupSpawn(link, pickup);
        }
    }

    private void WritePickupSpawn(Link link, Pickup pickup)
    {
        ushort key = Intern(pickup.CurrencyKey);
        link.Events.U8(RiftProtocol.PickupSpawn);
        link.Events.U32(pickup.NetId);
        link.Events.U8((byte)pickup.Kind);
        link.Events.F32(pickup.Value);
        link.Events.Pos(pickup.GlobalPosition);
        link.Events.U16(key);
    }

    private void OnPickupCollected(Pickup pickup, Player player, int detail)
    {
        if (LinkOf(player) is not { } link)
        {
            return;
        }

        if (pickup.NetId != 0)
        {
            link.Events.U8(RiftProtocol.PickupGone);
            link.Events.U32(pickup.NetId);
        }

        ushort key = Intern(pickup.CurrencyKey);
        link.Events.U8(RiftProtocol.Collected);
        link.Events.U8((byte)pickup.Kind);
        link.Events.F32(pickup.Value);
        link.Events.U16(key);
        link.Events.I32(detail);
    }

    private void OnMagnetized(Player? owner)
    {
        if (owner is null)
        {
            Broadcast(w => w.U8(RiftProtocol.AttractAll));
        }
        else if (LinkOf(owner) is { } link)
        {
            link.Events.U8(RiftProtocol.AttractAll);
        }
    }

    private void OnRemoteItem(Player owner, Items.EquipmentItem item, Vector2 at)
    {
        if (LinkOf(owner) is { } link)
        {
            link.Events.U8(RiftProtocol.ItemDrop);
            link.Events.Pos(at);
            link.Events.Str(Core.Profile.SerializeItem(item));
        }
    }

    // --- snapshots ---------------------------------------------------------------------

    private void SendSnapshots()
    {
        float now = HostTime;
        foreach (Link link in _links.Values)
        {
            if (!link.Ready)
            {
                continue;
            }

            Vector2 focus = link.Player.Player?.GlobalPosition ?? Vector2.Zero;
            byte part = 0;
            BeginChunk(now, part);
            WriteShared(_chunk, link);
            int countAt = _chunk.Length;
            _chunk.U16(0);
            int count = 0;
            foreach (Enemy enemy in _enemies)
            {
                if (!IsInstanceValid(enemy) || enemy.NetId == 0)
                {
                    continue;
                }

                bool near = enemy.IsBoss || enemy.GlobalPosition.DistanceSquaredTo(focus) <= RiftProtocol.NearRadius * RiftProtocol.NearRadius;
                if (!near && (_tick + enemy.NetId) % 4 != 0)
                {
                    continue;
                }

                if (_chunk.Length + 8 > RiftProtocol.ChunkBytes)
                {
                    PatchCount(countAt, count);
                    SendChunk(link);
                    part++;
                    BeginChunk(now, part);
                    countAt = _chunk.Length;
                    _chunk.U16(0);
                    count = 0;
                }

                WriteEnemy(_chunk, enemy);
                count++;
            }

            PatchCount(countAt, count);
            SendChunk(link);
        }
    }

    private void BeginChunk(float now, byte part)
    {
        _chunk.Clear();
        _chunk.U32(_tick);
        _chunk.F32(now);
        _chunk.U8(part);
    }

    /// <summary>The count was written as a placeholder; rewrite it in place.</summary>
    private void PatchCount(int at, int count) => _chunk.PatchU16(at, (ushort)count);

    private void SendChunk(Link link)
    {
        byte[] data = _chunk.ToArray();
        link.SnapshotBytes += data.Length;
        _session.SendSnapshot(link.Peer, data);
    }

    private void WriteShared(NetWriter w, Link link)
    {
        w.F32(State.Elapsed);
        w.I32(State.Kills);
        w.U16((ushort)Mathf.Min(State.ElitesKilled, ushort.MaxValue));
        w.U16((ushort)Mathf.Min(State.BossesKilled, ushort.MaxValue));
        w.U8((byte)((Director.Surging ? 1 : 0) | (Director.ColossusDefeated ? 2 : 0)));
        w.U8((byte)State.Players.Count);
        foreach (RunPlayer rp in State.Players)
        {
            Player? p = rp.Player;
            w.I32((int)rp.PeerId);
            w.Pos(p?.GlobalPosition ?? Vector2.Zero);
            float life = p?.Stats.Life?.Current ?? 0f;
            float max = p?.Stats.Life?.Max ?? 0f;
            w.U16((ushort)Mathf.Clamp(Mathf.CeilToInt(life), 0, ushort.MaxValue));
            w.U16((ushort)Mathf.Clamp(Mathf.RoundToInt(max), 0, ushort.MaxValue));
            byte flags = 0;
            if (rp.Downed)
            {
                flags |= RiftProtocol.PlayerDead;
            }

            if (p is { Warded: true })
            {
                flags |= RiftProtocol.PlayerWarded;
            }

            if (p?.Health is { InvulnerableTime: > 0f })
            {
                flags |= RiftProtocol.PlayerInvulnerable;
            }

            w.U8(flags);
            w.U8((byte)Mathf.RoundToInt((p?.ReviveProgress ?? 0f) * 255f));
        }

        // The receiver's own extras: input ack, flask charges, cast timers.
        Player? mine = link.Player.Player;
        w.U16(link.AckSeq);
        float charges = mine is null ? 0f : Items.Flasks.Of(mine)?.Life.Charges ?? 0f;
        w.U8((byte)Mathf.Clamp(Mathf.RoundToInt(charges * 20f), 0, 255));
        Span<byte> fractions = stackalloc byte[6];
        fractions.Clear();
        mine?.GetNodeOrNull<PlayerSkillInput>("PlayerSkillInput")?.WriteFractions(fractions);
        w.Bytes(fractions);
    }

    private void WriteEnemy(NetWriter w, Enemy enemy)
    {
        w.U16(enemy.NetId);
        w.Pos(enemy.GlobalPosition);
        w.U8((byte)Mathf.Clamp(Mathf.CeilToInt(enemy.LifeFraction * 255f), 0, 255));
        byte flags = (byte)((enemy.Ailments?.TintCode ?? 0) << 1);
        if (enemy.LastHurtMsec > _lastSnapshotMsec)
        {
            flags |= RiftProtocol.EnemyHurt;
        }

        w.U8(flags);
    }

    // --- from clients --------------------------------------------------------------------

    public void OnSnapshot(byte[] data)
    {
    }

    public void OnHostEvents(byte[] data)
    {
    }

    public void OnHostLost(string reason)
    {
    }

    public void OnClientInput(long peer, byte[] data)
    {
        if (!_links.TryGetValue(peer, out Link? link))
        {
            return;
        }

        var r = new NetReader(data);
        ushort seq = r.U16();
        // Unreliable-ordered already drops stale packets; this also guards the wrap.
        if (link.Command.ReceivedMsec != 0 && (short)(seq - link.Command.Seq) <= 0)
        {
            return;
        }

        PlayerCommand c = link.Command;
        c.Seq = seq;
        c.Move = r.Dir8();
        c.Aim = r.Pos();
        byte flags = r.U8();
        c.Sprint = (flags & 1) != 0;
        c.Choosing = (flags & 2) != 0;
        c.DodgeCount = r.U8();
        c.DodgeDir = Vector2.Right.Rotated(r.Angle());
        c.LifeFlaskCount = r.U8();
        c.ManaFlaskCount = r.U8();
        c.ReceivedMsec = Time.GetTicksMsec();
        link.AckSeq = seq;
    }

    public void OnClientEvents(long peer, byte[] data)
    {
        if (!_links.TryGetValue(peer, out Link? link))
        {
            return;
        }

        var r = new NetReader(data);
        while (r.More)
        {
            switch (r.U8())
            {
                case RiftProtocol.ClientReady:
                    MarkReady(link);
                    break;
                case RiftProtocol.ClientPick:
                    string key = r.Str();
                    if (link.Player.Build?.ApplyKey(key) == true || key.Length > 0)
                    {
                        State.ConsumeLevelUp(link.Player);
                    }

                    break;
                case RiftProtocol.ClientTest:
                    TestMessage?.Invoke(peer, r.Str());
                    break;
                default:
                    return;
            }
        }
    }

    /// <summary>A client has loaded: from now on it gets traffic, starting with
    /// everything it missed (strings, monsters alive, its own drops on the floor).</summary>
    private void MarkReady(Link link)
    {
        if (link.Ready)
        {
            return;
        }

        link.Ready = true;
        for (int i = 0; i < _stringList.Count; i++)
        {
            WriteStringDef(link.Events, (ushort)(i + 1), _stringList[i]);
        }

        foreach (Enemy enemy in _enemies)
        {
            if (IsInstanceValid(enemy) && enemy.NetId != 0)
            {
                WriteEnemySpawn(link.Events, enemy);
            }
        }

        foreach (Node node in GetTree().GetNodesInGroup(RunState.PickupGroup))
        {
            if (node is Pickup p && p.OwnerPlayer == link.Player.Player && p.NetId != 0)
            {
                WritePickupSpawn(link, p);
            }
        }

        if (_begun)
        {
            link.Events.U8(RiftProtocol.RunBegin);
        }
    }

    /// <summary>
    /// A client left or dropped (docs/design/multiplayer.md -> "Disconnects"):
    /// their player and their drops go, everyone is told, and the party size the
    /// director scales by shrinks for every spawn after this one.
    /// </summary>
    public void OnPeerLeft(long peer)
    {
        if (!_links.Remove(peer, out Link? link))
        {
            return;
        }

        RunPlayer rp = link.Player;
        string name = NameOf(rp);
        State.RemovePlayer(rp);
        if (rp.Player is { } p && IsInstanceValid(p))
        {
            foreach (Node node in GetTree().GetNodesInGroup(RunState.PickupGroup))
            {
                if (node is Pickup pickup && pickup.OwnerPlayer == p)
                {
                    pickup.QueueFree();
                }
            }

            p.QueueFree();
        }

        Broadcast(w =>
        {
            w.U8(RiftProtocol.PlayerLeft);
            w.I32((int)peer);
        });
        ToastAll($"{name} left the party", new Color(1f, 0.7f, 0.4f));
        if (!State.Ended && State.AllDown)
        {
            State.EndRun(Director.ColossusDefeated);
        }
    }

    // --- tests ---------------------------------------------------------------------------

    /// <summary>Test hook: a text message to one client's <see cref="RiftClient.TestMessage"/>.</summary>
    public void SendTest(long peer, string message)
    {
        if (_links.TryGetValue(peer, out Link? link))
        {
            link.Events.U8(RiftProtocol.TestPing);
            link.Events.Str(message);
        }
    }

    /// <summary>Test hook: how many drops this host has put down for <paramref name="peer"/>.</summary>
    public int PickupsOwnedBy(long peer)
    {
        int n = 0;
        foreach (Node node in GetTree().GetNodesInGroup(RunState.PickupGroup))
        {
            if (node is Pickup { OwnerPlayer: { } o } && o.PeerId == peer && !node.IsQueuedForDeletion())
            {
                n++;
            }
        }

        return n;
    }
}
