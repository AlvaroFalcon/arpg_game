#nullable enable
using System;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Skills;
using ArpgGame.Stats;

namespace ArpgGame.Progression;

/// <summary>
/// What a keystone or passive needs to apply itself, whoever owns the build:
/// the persistent character (<see cref="Characters.CharacterBuild"/>) or a
/// Survival Rift's temporary card build (<see cref="RunBuild"/>). Both are child
/// nodes of the player; effects that are checked later (Chain Reaction on kill,
/// Phase Dancer on dodge) are flags here and queried through <see cref="BuildQuery"/>.
/// </summary>
public interface IBuildContext
{
    ActorStats Stats { get; }
    SkillUser SkillUser { get; }
    object Source { get; }
    bool ChainReaction { get; set; }
    bool PhaseDancer { get; set; }
    SkillTags ForcedElement { get; set; }
}

public static class BuildQuery
{
    /// <summary>True if any build on the player satisfies <paramref name="test"/>.
    /// Asked on every kill, so it walks the children without allocating
    /// (docs/design/performance.md -> "Mass deaths").</summary>
    public static bool Any(Node? player, Func<IBuildContext, bool> test)
    {
        if (player is null)
        {
            return false;
        }

        int count = player.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            if (player.GetChild(i) is IBuildContext build && test(build))
            {
                return true;
            }
        }

        return false;
    }
}
