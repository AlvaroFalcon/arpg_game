#nullable enable
using System.Collections.Generic;
using Godot;

namespace ArpgGame.Combat;

/// <summary>
/// Spatial lookups over combatants, by group. Every combat actor that can be
/// targeted joins <see cref="EnemyGroup"/> or <see cref="PlayerGroup"/> on
/// <c>_Ready</c>; this is cheaper and simpler than physics queries for "who is
/// nearest" questions (chains, auto-aim, AI), and a few hundred distance checks
/// a frame is nothing.
/// </summary>
public static class CombatQuery
{
    public const string EnemyGroup = "enemies";
    public const string PlayerGroup = "players";

    /// <summary>The player this machine controls (camera, HUD, pickups it sees).
    /// In single-player it is the only player; in co-op the others are remote.</summary>
    public const string LocalPlayerGroup = "local_player";

    /// <summary>The local player, falling back to any player (bare test scenes).</summary>
    public static Player? LocalPlayer(SceneTree tree)
    {
        // Every pickup asks every frame: look it up once per frame.
        ulong physics = Engine.GetPhysicsFrames();
        ulong process = Engine.GetProcessFrames();
        if (physics != _localPhysicsFrame || process != _localProcessFrame || tree != _localTree
            || (_local is not null && (!GodotObject.IsInstanceValid(_local) || !_local.IsInsideTree())))
        {
            _localPhysicsFrame = physics;
            _localProcessFrame = process;
            _localTree = tree;
            _local = tree.GetFirstNodeInGroup(LocalPlayerGroupName) as Player ?? tree.GetFirstNodeInGroup(PlayerGroupName) as Player;
        }

        return _local;
    }

    private static readonly StringName LocalPlayerGroupName = LocalPlayerGroup;
    private static Player? _local;
    private static ulong _localPhysicsFrame = ulong.MaxValue;
    private static ulong _localProcessFrame = ulong.MaxValue;
    private static SceneTree? _localTree;

    /// <summary>
    /// The player a monster should go for: the nearest one that is alive and
    /// not warded (choosing a level-up card in co-op). Null when nobody qualifies.
    /// </summary>
    public static Player? NearestTargetablePlayer(SceneTree tree, Vector2 from)
    {
        Player? best = null;
        float bestSq = float.MaxValue;
        foreach (Player p in PlayersThisFrame(tree))
        {
            if (!GodotObject.IsInstanceValid(p) || !p.IsInsideTree() || p.Warded || p.Health is { IsDead: true })
            {
                continue;
            }

            float d = from.DistanceSquaredTo(p.GlobalPosition);
            if (d < bestSq)
            {
                bestSq = d;
                best = p;
            }
        }

        return best;
    }

    // Monsters ask for the nearest player hundreds of times a frame; listing the
    // group (a native array, marshalled element by element) once per frame
    // instead keeps that cheap — docs/design/performance.md -> "Co-op".
    private static readonly List<Player> _players = new();
    private static ulong _playersPhysicsFrame = ulong.MaxValue;
    private static ulong _playersProcessFrame = ulong.MaxValue;
    private static SceneTree? _playersTree;

    private static List<Player> PlayersThisFrame(SceneTree tree)
    {
        ulong physics = Engine.GetPhysicsFrames();
        ulong process = Engine.GetProcessFrames();
        if (physics != _playersPhysicsFrame || process != _playersProcessFrame || tree != _playersTree)
        {
            _playersPhysicsFrame = physics;
            _playersProcessFrame = process;
            _playersTree = tree;
            _players.Clear();
            foreach (Node node in tree.GetNodesInGroup(PlayerGroupName))
            {
                if (node is Player p)
                {
                    _players.Add(p);
                }
            }
        }

        return _players;
    }

    private static readonly StringName PlayerGroupName = PlayerGroup;
    private static readonly StringName EnemyGroupName = EnemyGroup;
    private static readonly NodePath HealthPath = "Health";

    /// <summary>A combatant's <see cref="Health"/>: the cached property on monsters
    /// and players, the "Health" child otherwise. Avoids building a NodePath from
    /// a string per lookup, which auto-aim did for every monster every frame.</summary>
    public static Health? HealthOf(Node node) => node switch
    {
        Enemies.Enemy { Health: { } h } => h,
        Player { Health: { } h } => h,
        _ => node.GetNodeOrNull<Health>(HealthPath),
    };

    private static StringName HostileGroupNameFor(Faction faction) => faction == Faction.Player ? EnemyGroupName : PlayerGroupName;

    /// <summary>The group whose members are hostile to <paramref name="faction"/>.</summary>
    public static string HostileGroupFor(Faction faction) => faction == Faction.Player ? EnemyGroup : PlayerGroup;

    public static string GroupFor(Faction faction) => faction == Faction.Player ? PlayerGroup : EnemyGroup;

    /// <summary>Nearest living hostile within <paramref name="range"/>, skipping
    /// any whose instance id is in <paramref name="exclude"/>. With
    /// <paramref name="requireSight"/>, inside a reality (a flow field exists)
    /// only hostiles in line of sight count — auto-aim must not pick a monster
    /// behind a wall over one in the open.</summary>
    public static Node2D? NearestHostile(SceneTree tree, Vector2 from, Faction faction, float range, ISet<ulong>? exclude = null, bool requireSight = false)
    {
        World.FlowField? field = requireSight ? World.FlowField.Current : null;
        Node2D? best = null;
        float bestSq = range * range;
        foreach (Node node in tree.GetNodesInGroup(HostileGroupNameFor(faction)))
        {
            if (node is not Node2D n2d || !GodotObject.IsInstanceValid(n2d))
            {
                continue;
            }

            if (exclude is not null && exclude.Contains(n2d.GetInstanceId()))
            {
                continue;
            }

            if (HealthOf(n2d) is not { IsDead: false })
            {
                continue;
            }

            float d = from.DistanceSquaredTo(n2d.GlobalPosition);
            if (d < bestSq && (field is null || field.HasLineOfSight(from, n2d.GlobalPosition)))
            {
                bestSq = d;
                best = n2d;
            }
        }

        return best;
    }

    /// <summary>Every living hostile whose position lies within
    /// <paramref name="radius"/> (+ its own body radius, approximated by
    /// <paramref name="bodyAllowance"/>) of <paramref name="centre"/>.</summary>
    public static List<Health> HostilesInRadius(SceneTree tree, Vector2 centre, float radius, Faction faction, float bodyAllowance = 12f)
    {
        var result = new List<Health>();
        float r = radius + bodyAllowance;
        float rSq = r * r;
        foreach (Node node in tree.GetNodesInGroup(HostileGroupNameFor(faction)))
        {
            if (node is not Node2D n2d || !GodotObject.IsInstanceValid(n2d))
            {
                continue;
            }

            if (centre.DistanceSquaredTo(n2d.GlobalPosition) > rSq)
            {
                continue;
            }

            if (HealthOf(n2d) is { IsDead: false } health)
            {
                result.Add(health);
            }
        }

        return result;
    }
}
