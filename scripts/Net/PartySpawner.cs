#nullable enable
using System;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;

namespace ArpgGame.Net;

/// <summary>
/// Builds the player node of another party member, dressed from the character
/// their machine sent (docs/design/multiplayer.md -> "Players"). Used by the
/// Survival Rift (<c>RunController.SpawnParty</c>) and the shared hideout's
/// puppets (<c>HideoutParty</c>), so both build remote players the same way.
/// </summary>
public static class PartySpawner
{
    private const string PlayerScene = "res://scenes/Player.tscn";

    /// <summary>The member's character, or null when it can't be read (that player then
    /// joins with a fresh build: friends-only, but malformed data must not crash anyone).</summary>
    public static CharacterData? CharacterOf(LobbyMember member)
    {
        if (member.Character.Length == 0)
        {
            return null;
        }

        try
        {
            return Profile.DeserializeCharacter(member.Character);
        }
        catch (Exception e)
        {
            GD.PushWarning($"Co-op: could not read {member.Name}'s character: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// A remote <see cref="Player"/> for <paramref name="member"/> under
    /// <paramref name="parent"/> at <paramref name="at"/>: a puppet placed from the
    /// network, or (on the host in a rift) driven by a <see cref="PlayerCommand"/>.
    /// <paramref name="beforeCharacter"/> runs once it is in the tree, before the
    /// character is applied (the run adds its run build there).
    /// </summary>
    public static Player Build(Node parent, LobbyMember member, bool puppet, bool commanded, Vector2 at, Action<Player>? beforeCharacter = null)
    {
        CharacterData? character = CharacterOf(member);
        var p = GD.Load<PackedScene>(PlayerScene).Instantiate<Player>();
        p.Name = $"Player_{member.PeerId}";
        p.IsLocal = false;
        p.PeerId = member.PeerId;
        p.NetName = member.Name;
        p.Puppet = puppet;
        if (commanded)
        {
            p.Command = new PlayerCommand();
        }

        parent.AddChild(p);
        p.GlobalPosition = at;
        beforeCharacter?.Invoke(p);
        if (character is not null)
        {
            Profile.ApplyCharacterTo(p, character);
        }

        return p;
    }
}
