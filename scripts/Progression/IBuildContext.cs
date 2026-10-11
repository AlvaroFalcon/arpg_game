#nullable enable
using System;
using System.Linq;
using Godot;
using PathToExile.Combat;
using PathToExile.Skills;
using PathToExile.Stats;

namespace PathToExile.Progression;

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
    /// <summary>True if any build on the player satisfies <paramref name="test"/>.</summary>
    public static bool Any(Node? player, Func<IBuildContext, bool> test) =>
        player is not null && player.GetChildren().OfType<IBuildContext>().Any(test);
}
