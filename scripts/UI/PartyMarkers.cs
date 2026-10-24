#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Net;

namespace ArpgGame.UI;

/// <summary>One ally as a map shows it (docs/design/multiplayer.md -> "Allies on the maps").</summary>
public readonly record struct AllyMarker(long PeerId, string Name, Vector2 Position, Color Color, bool Downed, float Revive = 0f);

/// <summary>
/// The other party members on every map — docs/design/multiplayer.md -> "Allies on the
/// maps": the hideout's town map (small and big), a reality's minimap and Tab overlay,
/// and the Survival Rift's party minimap. Each ally is a dot in their party colour (the
/// colour of their name tag over their head), hollow while they are down; the big maps
/// add their name. You stay the green arrow. Offline there is nobody to show and nothing
/// changes.
/// </summary>
public static class PartyMarkers
{
    /// <summary>Party colours by lobby slot (the host is slot 0). Chosen away from the
    /// green "you" arrow and the red of monsters: blue, orange, pink, yellow.</summary>
    public static readonly Color[] Palette =
    {
        new(0.4f, 0.75f, 1f),
        new(1f, 0.6f, 0.2f),
        new(0.95f, 0.45f, 0.9f),
        new(1f, 0.9f, 0.35f),
    };

    /// <summary>The party colour of <paramref name="peerId"/>: their slot in the lobby, the same
    /// on every machine (the roster is the host's). Outside a session, the first colour.</summary>
    public static Color ColorOf(Node context, long peerId)
    {
        if (NetSession.Get(context) is { Active: true } session)
        {
            int slot = session.Members.FindIndex(m => m.PeerId == peerId);
            if (slot >= 0)
            {
                return Palette[slot % Palette.Length];
            }
        }

        return Palette[(int)(Mathf.Abs(peerId) % Palette.Length)];
    }

    /// <summary>Every other player on screen in this scene (the Rift's and a reality's party,
    /// the hideout's puppets), never <paramref name="local"/>.</summary>
    public static List<AllyMarker> Allies(Node context, Player? local)
    {
        var allies = new List<AllyMarker>();
        if (!context.IsInsideTree())
        {
            return allies;
        }

        foreach (Node node in context.GetTree().GetNodesInGroup(CombatQuery.PlayerGroup))
        {
            if (node is not Player p || p == local || p.IsLocal || !GodotObject.IsInstanceValid(p) || !p.IsVisibleInTree() || p.IsQueuedForDeletion())
            {
                continue;
            }

            string name = p.NetName.Length > 0 ? p.NetName : "Ally";
            allies.Add(new AllyMarker(p.PeerId, name, p.GlobalPosition, ColorOf(context, p.PeerId), p.ShowsDowned, p.ShowsDowned ? p.ReviveProgress : 0f));
        }

        return allies;
    }

    /// <summary>One ally's dot (hollow while down) and, with <paramref name="label"/>, its name under it.</summary>
    public static void Draw(CanvasItem c, Vector2 at, AllyMarker m, float radius, bool label)
    {
        c.DrawCircle(at, radius + 1.6f, new Color(0f, 0f, 0f, 0.8f));
        if (m.Downed)
        {
            c.DrawArc(at, radius - 0.6f, 0f, Mathf.Tau, 20, m.Color with { A = 0.85f }, 2f, true);
            if (m.Revive > 0f)
            {
                // An ally's revive channel on them (docs/design/multiplayer.md -> "Deaths and revives").
                c.DrawArc(at, radius + 2.5f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * Mathf.Min(1f, m.Revive), 20, new Color(0.45f, 1f, 0.55f), 2f, true);
            }
        }
        else
        {
            c.DrawCircle(at, radius, m.Color);
            c.DrawCircle(at + new Vector2(-radius * 0.3f, -radius * 0.3f), radius * 0.3f, new Color(1f, 1f, 1f, 0.6f));
        }

        if (!label)
        {
            return;
        }

        Font font = ThemeDB.FallbackFont;
        string text = m.Downed ? (m.Revive > 0f ? $"{m.Name} · being revived" : $"{m.Name} · down") : m.Name;
        const int size = 13;
        float w = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        Vector2 p = at + new Vector2(-w / 2f, radius + 14f);
        c.DrawStringOutline(font, p, text, HorizontalAlignment.Left, -1, size, 4, new Color(0f, 0f, 0f, 0.85f));
        c.DrawString(font, p, text, HorizontalAlignment.Left, -1, size, m.Downed ? m.Color.Darkened(0.3f) : m.Color);
    }

    /// <summary>A point kept inside <paramref name="area"/> (an ally beyond the edge of a map
    /// that follows you shows at the edge, in their direction).</summary>
    public static Vector2 ClampInto(Rect2 area, Vector2 at, float inset) =>
        new(Mathf.Clamp(at.X, area.Position.X + inset, area.End.X - inset), Mathf.Clamp(at.Y, area.Position.Y + inset, area.End.Y - inset));
}
