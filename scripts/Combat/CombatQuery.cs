#nullable enable
using System.Collections.Generic;
using Godot;

namespace PathToExile.Combat;

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
        foreach (Node node in tree.GetNodesInGroup(HostileGroupFor(faction)))
        {
            if (node is not Node2D n2d || !GodotObject.IsInstanceValid(n2d))
            {
                continue;
            }

            if (exclude is not null && exclude.Contains(n2d.GetInstanceId()))
            {
                continue;
            }

            if (n2d.GetNodeOrNull<Health>("Health") is not { IsDead: false })
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
        foreach (Node node in tree.GetNodesInGroup(HostileGroupFor(faction)))
        {
            if (node is not Node2D n2d || !GodotObject.IsInstanceValid(n2d))
            {
                continue;
            }

            if (centre.DistanceSquaredTo(n2d.GlobalPosition) > rSq)
            {
                continue;
            }

            if (n2d.GetNodeOrNull<Health>("Health") is { IsDead: false } health)
            {
                result.Add(health);
            }
        }

        return result;
    }
}
