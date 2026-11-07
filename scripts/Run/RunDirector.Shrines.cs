#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Enemies;

namespace ArpgGame.Run;

/// <summary>
/// The arena's shrines — docs/design/survival-roadmap.md -> M3. Three at the start, spread out
/// and away from the centre; a new one every <see cref="ShrineInterval"/> while fewer than three
/// stand (never during a boss fight). Used once each. Host-side (and offline); a co-op client
/// draws replicas (<see cref="ShowRemoteShrine"/>).
/// </summary>
public partial class RunDirector
{
    public const int ShrinesAtOnce = 3;
    public const float ShrineInterval = 150f;
    public const float ShrineMinSpacing = 500f;

    private readonly Dictionary<ushort, Shrine> _shrines = new();
    private ushort _nextShrineId;
    private float _nextShrineAt = 1f;
    private bool _firstShrines;

    /// <summary>Every shrine standing (tests, HUD, co-op catch-up).</summary>
    public IEnumerable<Shrine> Shrines => _shrines.Values.Where(s => IsInstanceValid(s) && !s.IsQueuedForDeletion());

    /// <summary>A shrine appeared or changed (co-op host: replicate it).</summary>
    public event System.Action<Shrine>? ShrineChanged;

    /// <summary>A shrine was used, and by whom (null: the party).</summary>
    public event System.Action<Shrine, Player?>? ShrineUsed;

    private void StepShrines(float t, float dt)
    {
        if (!Core.Features.RiftShrines || State.Mirror)
        {
            return;
        }

        if (!_firstShrines && t >= _nextShrineAt)
        {
            _firstShrines = true;
            for (int i = 0; i < ShrinesAtOnce; i++)
            {
                PlaceShrine((ShrineKind)(i % 4 == 0 ? 0 : i), null);
            }

            _nextShrineAt = t + ShrineInterval;
        }
        else if (_firstShrines && t >= _nextShrineAt && ActiveBoss is null)
        {
            _nextShrineAt = t + ShrineInterval;
            if (Shrines.Count(s => !s.Used) < ShrinesAtOnce)
            {
                PlaceShrine((ShrineKind)_rng.RandiRange(0, 3), null);
            }
        }

        TickShrines(dt);
    }

    /// <summary>Charges and touches (the director's step; tests call it with spawning off).</summary>
    public void TickShrines(float dt)
    {
        foreach (Shrine shrine in Shrines.ToList())
        {
            if (!shrine.Used)
            {
                TickShrine(shrine, dt);
            }
        }
    }

    /// <summary>Places a shrine of <paramref name="kind"/> at <paramref name="at"/> (null: a free spot). Public for tests.</summary>
    public Shrine PlaceShrine(ShrineKind kind, Vector2? at)
    {
        Vector2 position = at ?? FreeShrineSpot();
        var shrine = new Shrine { Kind = kind, Id = ++_nextShrineId, Name = $"Shrine{_nextShrineId}" };
        GetTree().CurrentScene.AddChild(shrine);
        shrine.GlobalPosition = position;
        _shrines[shrine.Id] = shrine;
        ShrineChanged?.Invoke(shrine);
        return shrine;
    }

    /// <summary>A spot at least <see cref="ShrineMinSpacing"/> from the centre, the players and every
    /// standing shrine (the best of 40 tries).</summary>
    public Vector2 FreeShrineSpot()
    {
        Rect2 b = Arena.Bounds.Grow(-160f);
        Vector2 best = b.GetCenter();
        float bestScore = -1f;
        for (int i = 0; i < 40; i++)
        {
            var p = new Vector2(_rng.RandfRange(b.Position.X, b.End.X), _rng.RandfRange(b.Position.Y, b.End.Y));
            float score = p.DistanceTo(Arena.Center);
            foreach (Shrine s in Shrines)
            {
                score = Mathf.Min(score, p.DistanceTo(s.GlobalPosition));
            }

            foreach (Player pl in LivingPlayers())
            {
                score = Mathf.Min(score, p.DistanceTo(pl.GlobalPosition) * 1.5f);
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = p;
            }

            if (score >= ShrineMinSpacing && i >= 8)
            {
                break;
            }
        }

        return best;
    }

    private void TickShrine(Shrine shrine, float dt)
    {
        List<Player> living = LivingPlayers();
        if (shrine.Kind == ShrineKind.Charge)
        {
            bool inside = living.Any(p => p.GlobalPosition.DistanceTo(shrine.GlobalPosition) <= Shrine.ChargeRadius);
            float before = shrine.Progress;
            shrine.Progress = inside ? Mathf.Min(1f, shrine.Progress + dt / Shrine.ChargeSeconds) : 0f;
            if (shrine.Progress >= 1f)
            {
                UseShrine(shrine, null);
            }
            else if (Mathf.Abs(shrine.Progress - before) > 0f && (Mathf.FloorToInt(shrine.Progress * 10f) != Mathf.FloorToInt(before * 10f) || shrine.Progress == 0f))
            {
                ShrineChanged?.Invoke(shrine);
            }

            return;
        }

        foreach (Player p in living)
        {
            if (p.GlobalPosition.DistanceTo(shrine.GlobalPosition) <= Shrine.TouchRadius)
            {
                UseShrine(shrine, p);
                return;
            }
        }
    }

    /// <summary>Uses a shrine (once). Public for tests.</summary>
    public void UseShrine(Shrine shrine, Player? by)
    {
        if (shrine.Used)
        {
            return;
        }

        shrine.MarkUsed();
        Color c = Shrine.ColorOf(shrine.Kind);
        switch (shrine.Kind)
        {
            case ShrineKind.Greed:
                State.Difficulty.Greed = Mathf.Min(RiftDifficulty.MaxGreed, State.Difficulty.Greed + 1);
                EmitSignal(SignalName.Announced, $"Greed ×{State.Difficulty.Greed}", "The Rift grows richer, and deadlier", c);
                break;
            case ShrineKind.Challenge:
            {
                PackedScene? scene = PickEnemy(State.Elapsed) ?? Slime;
                if (scene is null)
                {
                    break;
                }

                Vector2 at = shrine.GlobalPosition;
                Enemy champion = SpawnAt(scene, Arena.Clamp(at + new Vector2(0f, -70f), 40f), elite: true);
                champion.Announced = true;
                champion.GuaranteedChest = 3;
                for (int i = 0; i < 2; i++)
                {
                    Enemy guard = SpawnAt(scene, Arena.Clamp(at + Vector2.Right.Rotated(Mathf.Pi * i + 0.4f) * 80f, 40f));
                    guard.ApplyRarity(MonsterRarity.Magic);
                }

                EmitSignal(SignalName.Announced, "A challenger rises", "Slay it for a chest", c);
                break;
            }

            case ShrineKind.Charge:
                foreach (RunPlayer rp in State.Players)
                {
                    if (rp.Player is { } p && p.Health is { IsDead: false } health && p.Stats.Life is { } life)
                    {
                        health.Heal(life.Max * 0.3f);
                        State.DropChest(shrine.GlobalPosition + new Vector2(_rng.RandfRange(-30f, 30f), 30f), 1, State.Coop ? p : null);
                    }
                }

                EmitSignal(SignalName.Announced, "The shrine answers", "Healed, and a chest each", c);
                break;
            case ShrineKind.Magnet:
                foreach (RunPlayer rp in State.Players)
                {
                    State.Magnetize(State.Coop ? rp.Player : null);
                }

                EmitSignal(SignalName.Announced, "Magnet shrine", "Everything flies to you", c);
                break;
        }

        Core.Sfx.Play(this, "levelup", -4f);
        Core.GameEvents.Raise(Core.GameEvents.RiftShrineUsed, State.Difficulty.Greed, Shrine.NameOf(shrine.Kind).ToLowerInvariant() + ";" + State.Context);
        ShrineChanged?.Invoke(shrine);
        ShrineUsed?.Invoke(shrine, by);
    }

    // --- co-op client mirror ------------------------------------------------------------------

    /// <summary>Co-op client: a shrine the host placed or changed.</summary>
    public void ShowRemoteShrine(ushort id, ShrineKind kind, Vector2 at, float progress, bool used)
    {
        if (!_shrines.TryGetValue(id, out Shrine? shrine) || !IsInstanceValid(shrine))
        {
            if (used)
            {
                return;
            }

            shrine = new Shrine { Kind = kind, Id = id, Name = $"Shrine{id}" };
            GetTree().CurrentScene.AddChild(shrine);
            shrine.GlobalPosition = at;
            _shrines[id] = shrine;
        }

        shrine.Progress = progress;
        if (used && !shrine.Used)
        {
            shrine.MarkUsed();
        }
    }
}
