#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Core;

namespace ArpgGame.UI;

/// <summary>
/// Damage numbers as columns (docs/design/menus.md -> "Damage numbers"). Every hit on a
/// target joins one column anchored just right of the target's head: the newest line at
/// the bottom, older lines pushed up, the oldest fading out at the top. A crit or a hit
/// far above the recent average is big (huge slanted number, a punch, a golden streak
/// behind it); ordinary hits are compact lines; another player's hits get no number at all;
/// damage over time sums into one small line per target; damage the player takes floats
/// up in red on the other side of the character, never in a column.
///
/// One node per scene (a child of the current scene, above the world at z 90), owning
/// every row: one <c>_Process</c> moves them all, and finished rows are hidden and
/// reused. Under load it keeps the screen readable and the frame cheap: columns get
/// shorter when many targets are hit at once, rapid small hits on one target merge into
/// one line, at most <see cref="FrameBudget"/> new lines start per frame, and past
/// <see cref="MaxLive"/> lines only crits and big hits still get one.
/// </summary>
public partial class DamageNumberLayer : Node2D
{
    /// <summary>Lines alive at once before small hits are skipped.</summary>
    public const int MaxLive = 120;

    /// <summary>New lines per frame before small hits are skipped (a pack dying at once).</summary>
    public const int FrameBudget = 24;

    /// <summary>Column length: Full, Compact, and Full while many targets are being hit.</summary>
    public const int FullCap = 6;
    public const int CompactCap = 3;
    public const int BusyCap = 3;

    /// <summary>A column never grows taller than this (screen px): four big crits, or six small lines.</summary>
    public const float MaxColumnHeight = 250f;

    /// <summary>More columns than this alive = busy: shorter columns, wider merging, fewer flashes.</summary>
    public const int BusyColumns = 6;

    private const int MaxIdle = 160;

    // Counters for tests and benches (never reset by the game).
    public static int Created { get; private set; }
    public static int Reused { get; private set; }
    public static int Skipped { get; private set; }
    public static int MergedHits { get; private set; }

    /// <summary>A column of lines over one target.</summary>
    internal sealed class Column
    {
        public ulong TargetId;
        public Node2D? Target;
        public Vector2 Offset;
        public Vector2 Anchor;
        public readonly List<DamageNumber> Rows = new(FullCap + 1);
        public DamageNumber? TickRow;
        public int Refs;
    }

    private static DamageNumberLayer? _current;

    private readonly Dictionary<ulong, Column> _columns = new();
    private readonly Stack<Column> _idleColumns = new();
    private readonly List<DamageNumber> _live = new(MaxLive);
    private readonly Stack<DamageNumber> _idle = new();
    private readonly Queue<ulong> _bigTimes = new();
    private readonly List<DamageNumber> _leaving = new();
    private DamageNumber? _lastIncoming;
    private int _spawnedThisFrame;
    private float _inv = 1f;
    private float _ema;
    private int _emaSamples;
    private DamageNumberMode _mode = DamageNumberMode.Full;

    public int LiveCount => _live.Count;
    public int ColumnCount => _columns.Count;
    public int IdleCount => _idle.Count;

    /// <summary>The current scene's layer, created on first use (null when off or outside a tree).</summary>
    public static DamageNumberLayer? Get(Node context, bool create = true)
    {
        if (!context.IsInsideTree())
        {
            return null;
        }

        DamageNumberMode mode = GameSettings.Get(context)?.DamageNumberMode ?? DamageNumberMode.Full;
        if (mode == DamageNumberMode.Off)
        {
            return null;
        }

        Node? scene = context.GetTree().CurrentScene;
        if (_current is { } layer && IsInstanceValid(layer) && !layer.IsQueuedForDeletion() && layer.IsInsideTree() && layer.GetParent() == scene)
        {
            layer._mode = mode;
            return layer;
        }

        if (!create || scene is null)
        {
            return null;
        }

        _current = new DamageNumberLayer { Name = "DamageNumbers", _mode = mode };
        scene.AddChild(_current);
        return _current;
    }

    public override void _Ready()
    {
        ZIndex = 90;
        ZAsRelative = false;
        TopLevel = true;

        // A few lines ready before the first pack dies.
        for (int i = 0; i < 24; i++)
        {
            DamageNumber row = NewRow();
            _idle.Push(row);
        }
    }

    public override void _ExitTree()
    {
        if (_current == this)
        {
            _current = null;
        }
    }

    // --- what actors call ---------------------------------------------------------------

    /// <summary>A resolved hit on <paramref name="target"/> (an enemy or a dummy), from
    /// <see cref="Health.HitPresented"/>.</summary>
    public static DamageNumber? ShowHit(Node2D target, DamagePayload payload, float taken, Ailments? ailments, float maxLife, float heavyShare, Vector2 offset)
    {
        // Nobody sees the numbers of hits another player dealt (docs/design/menus.md -> "Damage numbers").
        if (taken < 0.5f || payload.SourceActor is Player { IsLocal: false } || Get(target) is not { } layer)
        {
            return null;
        }

        var hit = new DamageHit
        {
            Amount = taken,
            Type = MainType(payload),
            Tags = TagsOf(payload.IsCrit, ailments?.LastHitApplied ?? AilmentHit.None),
            Icon = payload.Skill?.Icon,
            Kind = DamageNumberKind.Own,
            Heavy = IsHeavy(taken, maxLife, heavyShare),
            Important = heavyShare > 0f,
        };
        return layer.Hit(target, offset, hit);
    }

    /// <summary>Damage that was already resolved (ignite and poison ticks, explosions).</summary>
    public static DamageNumber? ShowTick(Node2D target, float amount, Ailments? ailments, Vector2 offset)
    {
        if (amount < 0.5f || Get(target) is not { } layer)
        {
            return null;
        }

        Color tint = ailments is { IsIgnited: true } ? UiTheme.DamageFire
            : ailments is { PoisonStacks: > 0 } ? UiTheme.DamagePoison
            : UiTheme.DamagePhysical.Darkened(0.15f);
        return layer.Hit(target, offset, new DamageHit { Amount = amount, Kind = DamageNumberKind.Tick, Tint = tint });
    }

    /// <summary>Co-op client: one of this client's own hits, resolved by the host
    /// (<see cref="Encode"/> on the host side).</summary>
    public static DamageNumber? ShowReplicaHit(Node2D target, float amount, byte flags, byte slot, Vector2 offset)
    {
        if (amount < 0.5f || Get(target) is not { } layer)
        {
            return null;
        }

        Texture2D? icon = null;
        if (slot < 6 && CombatQuery.LocalPlayer(target.GetTree()) is { } local)
        {
            icon = local.SkillUser.GetSkill((Skills.SkillSlotId)slot)?.Definition.Icon;
        }

        var hit = new DamageHit
        {
            Amount = amount,
            Type = (DamageType)((flags >> 1) & 3),
            Tags = ((flags & 1) != 0 ? HitTag.Critical : HitTag.None)
                | ((flags & 8) != 0 ? HitTag.Ignite : HitTag.None)
                | ((flags & 16) != 0 ? HitTag.Chill : HitTag.None)
                | ((flags & 32) != 0 ? HitTag.Shock : HitTag.None)
                | ((flags & 64) != 0 ? HitTag.Freeze : HitTag.None),
            Icon = icon,
            Kind = DamageNumberKind.Own,
            Heavy = (flags & 128) != 0,
            Important = (flags & 128) != 0,
        };
        return layer.Hit(target, offset, hit);
    }

    /// <summary>Host: a hit's qualities in one byte for the client that dealt it, and the
    /// loadout slot of the skill (255 = none) so the client can show its icon.</summary>
    public static (byte Flags, byte Slot) Encode(DamagePayload payload, float taken, Ailments? ailments, float maxLife, float heavyShare)
    {
        AilmentHit applied = ailments?.LastHitApplied ?? AilmentHit.None;
        int flags = (payload.IsCrit ? 1 : 0)
            | ((int)MainType(payload) & 3) << 1
            | ((applied & AilmentHit.Ignite) != 0 ? 8 : 0)
            | ((applied & AilmentHit.Chill) != 0 ? 16 : 0)
            | ((applied & AilmentHit.Shock) != 0 ? 32 : 0)
            | ((applied & AilmentHit.Freeze) != 0 ? 64 : 0)
            | (IsHeavy(taken, maxLife, heavyShare) ? 128 : 0);
        byte slot = 255;
        if (payload.Skill is { } skill && payload.SourceActor is Player player && IsInstanceValid(player))
        {
            foreach ((Skills.SkillSlotId id, Skills.SkillInstance instance) in player.SkillUser.EquippedSkills())
            {
                if (instance.Definition == skill)
                {
                    slot = (byte)id;
                    break;
                }
            }
        }

        return ((byte)flags, slot);
    }

    /// <summary>Damage the local player takes: red, floating up on the left of the character.</summary>
    public static DamageNumber? ShowIncoming(Node2D player, float amount, float maxLife)
    {
        if (amount < 0.5f || Get(player) is not { } layer)
        {
            return null;
        }

        return layer.Float(player.GlobalPosition + new Vector2(-24f, -18f), new DamageHit
        {
            Amount = amount,
            Kind = DamageNumberKind.Incoming,
            Heavy = maxLife > 0f && amount >= maxLife * 0.1f,
        });
    }

    /// <summary>A hit the local player blocked or evaded.</summary>
    public static DamageNumber? ShowAvoided(Node2D player)
    {
        return Get(player) is { } layer
            ? layer.Float(player.GlobalPosition + new Vector2(-24f, -18f), new DamageHit { Kind = DamageNumberKind.Incoming, Word = "AVOIDED" })
            : null;
    }

    /// <summary>The share of an elite's or boss's life above which a hit is shown big
    /// (0 = never: ordinary monsters and dummies).</summary>
    public static float HeavyShareFor(Enemies.Enemy enemy) =>
        enemy.IsBoss || enemy.Rarity == Enemies.MonsterRarity.Unique ? 0.08f : enemy.IsElite ? 0.3f : 0f;

    private static bool IsHeavy(float taken, float maxLife, float share) => share > 0f && maxLife > 0f && taken >= maxLife * share;

    private static DamageType MainType(DamagePayload payload)
    {
        DamageType type = DamageType.Physical;
        float best = -1f;
        foreach ((DamageType t, float amount) in payload.Components)
        {
            if (amount > best)
            {
                best = amount;
                type = t;
            }
        }

        return type;
    }

    private static HitTag TagsOf(bool crit, AilmentHit applied) =>
        (crit ? HitTag.Critical : HitTag.None)
        | ((applied & AilmentHit.Ignite) != 0 ? HitTag.Ignite : HitTag.None)
        | ((applied & AilmentHit.Chill) != 0 ? HitTag.Chill : HitTag.None)
        | ((applied & AilmentHit.Shock) != 0 ? HitTag.Shock : HitTag.None)
        | ((applied & AilmentHit.Freeze) != 0 ? HitTag.Freeze : HitTag.None);

    // --- columns --------------------------------------------------------------------------

    /// <summary>The lines of <paramref name="target"/>'s column, newest first (tests).</summary>
    public IReadOnlyList<DamageNumber> RowsOf(Node2D target) =>
        _columns.TryGetValue(target.GetInstanceId(), out Column? c) ? c.Rows : System.Array.Empty<DamageNumber>();

    /// <summary>Adds one hit to <paramref name="target"/>'s column (or merges it into the
    /// newest line). Null when the hit was skipped to keep the screen readable.</summary>
    public DamageNumber? Hit(Node2D target, Vector2 offset, in DamageHit hit)
    {
        bool full = _mode == DamageNumberMode.Full;
        Column col = ColumnFor(target, offset);
        bool busy = _columns.Count > BusyColumns || _live.Count > MaxLive * 3 / 4;
        bool crit = (hit.Tags & HitTag.Critical) != 0;

        if (hit.Kind == DamageNumberKind.Tick)
        {
            if (col.TickRow is { Leaving: false } tick && tick.Age < tick.Life - 0.3f && tick.Since < 3f)
            {
                tick.Add(hit.Amount, 0.1f);
                MergedHits++;
                return tick;
            }
        }

        bool wantBig = full && hit.Kind == DamageNumberKind.Own && (crit || hit.Heavy || (_emaSamples >= 6 && hit.Amount >= _ema * 2.5f));
        if (hit.Kind == DamageNumberKind.Own)
        {
            _ema = _emaSamples == 0 ? hit.Amount : Mathf.Lerp(_ema, hit.Amount, 0.1f);
            _emaSamples++;
        }

        // Rapid small hits of the same kind on one target (orbiting blades, multistrike,
        // a shotgun of projectiles) read as one line with their sum.
        if (!wantBig && !crit && hit.Kind != DamageNumberKind.Tick && hit.Word is null && col.Rows.Count > 0)
        {
            DamageNumber top = col.Rows[0];
            if (!top.Big && !top.Medium && top.Kind == hit.Kind && top.Type == hit.Type && top.Tags == hit.Tags && top.Age < (busy ? 0.35f : 0.12f) && top.Since < (busy ? 0.9f : 0.4f))
            {
                top.Add(hit.Amount, 0f);
                MergedHits++;
                return top;
            }
        }

        bool important = wantBig || (crit && hit.Kind == DamageNumberKind.Own);
        if ((!important && (_live.Count >= MaxLive || _spawnedThisFrame >= FrameBudget)) || _live.Count >= MaxLive + FrameBudget)
        {
            Skipped++;
            ReleaseColumnIfEmpty(col);
            return null;
        }

        // In a crowd only elites, bosses and heavy hits keep the flash; a trash crit is
        // a medium gold line, so overlapping columns never stack huge digits.
        bool crowd = busy && !hit.Important;
        bool big = wantBig && !crowd;
        bool medium = crit && hit.Kind == DamageNumberKind.Own && (!full || crowd);
        if (big && BigRecent() >= (busy ? 2 : 5))
        {
            big = false;
            medium = true;
        }

        if (big)
        {
            _bigTimes.Enqueue(Time.GetTicksMsec());
        }

        DamageNumber row = Rent();
        row.Configure(hit, big, medium, full, crowd);
        row.Column = col;
        col.Refs++;
        col.Rows.Insert(0, row);
        if (hit.Kind == DamageNumberKind.Tick)
        {
            col.TickRow = row;
        }

        int cap = !full ? CompactCap : busy ? BusyCap : FullCap;
        float maxHeight = busy ? MaxColumnHeight * 0.65f : MaxColumnHeight;
        while (col.Rows.Count > cap || (col.Rows.Count > 1 && Height(col) > maxHeight))
        {
            DamageNumber old = col.Rows[^1];
            col.Rows.RemoveAt(col.Rows.Count - 1);
            old.Leaving = true;
            _leaving.Add(old);
        }

        Restack(col);

        // Pushed out of the top: slide above what stays while fading, never through it.
        float stackTop = Height(col);
        foreach (DamageNumber old in _leaving)
        {
            old.TargetY = -stackTop - 6f;
            stackTop += old.Height;
        }

        _leaving.Clear();
        row.Y = row.TargetY + 12f;
        row.Step(0f, _inv, col.Anchor);
        _spawnedThisFrame++;
        return row;
    }

    /// <summary>A free-floating line (damage taken): rises and fades where it started.</summary>
    public DamageNumber? Float(Vector2 at, in DamageHit hit)
    {
        if (hit.Word is null && _lastIncoming is { } last && IsInstanceValid(last) && last.Visible && last.Column is null
            && last.Kind == DamageNumberKind.Incoming && last.Age < 0.12f && last.Text != "AVOIDED")
        {
            last.Add(hit.Amount, 0f);
            MergedHits++;
            return last;
        }

        if (_live.Count >= MaxLive + FrameBudget)
        {
            Skipped++;
            return null;
        }

        DamageNumber row = Rent();
        row.Configure(hit, false, false, _mode == DamageNumberMode.Full);
        row.Column = null;
        row.WorldAnchor = at;
        row.Y = 0f;
        row.TargetY = 0f;
        row.Step(0f, _inv, at);
        _lastIncoming = row;
        return row;
    }

    private Column ColumnFor(Node2D target, Vector2 offset)
    {
        ulong id = target.GetInstanceId();
        if (!_columns.TryGetValue(id, out Column? col))
        {
            col = _idleColumns.Count > 0 ? _idleColumns.Pop() : new Column();
            col.TargetId = id;
            col.Refs = 0;
            col.TickRow = null;
            col.Rows.Clear();
            _columns[id] = col;
        }

        col.Target = target;
        col.Offset = offset;
        col.Anchor = target.GlobalPosition + offset;
        return col;
    }

    private static float Height(Column col)
    {
        float h = 0f;
        foreach (DamageNumber row in col.Rows)
        {
            h += row.Height;
        }

        return h;
    }

    private static void Restack(Column col)
    {
        float y = 0f;
        foreach (DamageNumber row in col.Rows)
        {
            row.TargetY = -y;
            y += row.Height;
        }
    }

    private int BigRecent()
    {
        ulong now = Time.GetTicksMsec();
        while (_bigTimes.Count > 0 && now - _bigTimes.Peek() > 300)
        {
            _bigTimes.Dequeue();
        }

        return _bigTimes.Count;
    }

    // --- pool -------------------------------------------------------------------------------

    private DamageNumber NewRow()
    {
        var row = new DamageNumber { Visible = false };
        AddChild(row);
        Created++;
        return row;
    }

    private DamageNumber Rent()
    {
        DamageNumber row;
        if (_idle.Count > 0)
        {
            row = _idle.Pop();
            Reused++;
        }
        else
        {
            row = NewRow();
        }

        row.Visible = true;
        _live.Add(row);
        return row;
    }

    private void Release(DamageNumber row)
    {
        if (row.Column is { } col)
        {
            col.Rows.Remove(row);
            if (col.TickRow == row)
            {
                col.TickRow = null;
            }

            col.Refs--;
            row.Column = null;
            Restack(col);
            ReleaseColumnIfEmpty(col);
        }

        if (_lastIncoming == row)
        {
            _lastIncoming = null;
        }

        row.Visible = false;
        if (_idle.Count < MaxIdle)
        {
            _idle.Push(row);
        }
        else
        {
            row.QueueFree();
        }
    }

    private void ReleaseColumnIfEmpty(Column col)
    {
        if (col.Refs > 0 || col.Rows.Count > 0)
        {
            return;
        }

        if (_columns.TryGetValue(col.TargetId, out Column? mapped) && mapped == col)
        {
            _columns.Remove(col.TargetId);
        }

        col.Target = null;
        _idleColumns.Push(col);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _spawnedThisFrame = 0;
        Camera2D? camera = GetViewport()?.GetCamera2D();
        float zoom = camera is not null ? Mathf.Max(0.05f, camera.Zoom.X) : 1f;
        _inv = 1f / zoom;

        foreach (Column col in _columns.Values)
        {
            if (col.Target is { } target)
            {
                if (IsInstanceValid(target) && target.IsInsideTree())
                {
                    col.Anchor = target.GlobalPosition + col.Offset;
                }
                else
                {
                    col.Target = null; // gone: the column stays where it was and fades
                }
            }
        }

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            DamageNumber row = _live[i];
            Vector2 anchor = row.Column?.Anchor ?? row.WorldAnchor;
            if (!row.Step(dt, _inv, anchor))
            {
                _live[i] = _live[^1];
                _live.RemoveAt(_live.Count - 1);
                Release(row);
            }
        }
    }
}
