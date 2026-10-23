#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Run;

namespace ArpgGame.UI;

/// <summary>
/// The Survival Rift's party minimap — docs/design/multiplayer.md -> "Allies on the maps".
/// Co-op only (a party of two or more; solo play has no minimap and is unchanged): the
/// arena's outline, your green arrow, every ally as a dot in their party colour (hollow
/// while down), the boss once it is up and the exit portal once it opens. The arena is
/// one open room, so this is all it needs to tell you where your friends are.
/// </summary>
public partial class RiftMinimap : Control
{
    public RunState State = null!;
    public RunDirector Director = null!;
    public Player Player = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(184f, 138f);
        Size = CustomMinimumSize;
        Visible = false;
    }

    /// <summary>Shown only with a party (re-checked as people leave).</summary>
    public bool Party => State is not null && State.Players.Count > 1;

    public override void _Process(double delta)
    {
        Visible = Party || HudEditing;
        if (Visible)
        {
            QueueRedraw();
        }
    }

    /// <summary>The HUD editor shows it even solo, so it can be placed.</summary>
    public bool HudEditing { get; set; }

    /// <summary>The party members this map shows (tests).</summary>
    public List<AllyMarker> Allies() => Player is not null && IsInstanceValid(Player) ? PartyMarkers.Allies(Player, Player) : new List<AllyMarker>();

    public override void _Draw()
    {
        if (Director?.Arena is not { } arena || !IsInstanceValid(arena) || Player is null || !IsInstanceValid(Player))
        {
            return;
        }

        Rect2 bounds = arena.Bounds;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.02f, 0.01f, 0.04f, 0.6f));
        float px = Mathf.Min((Size.X - 8f) / bounds.Size.X, (Size.Y - 8f) / bounds.Size.Y);
        Vector2 mapSize = bounds.Size * px;
        Vector2 origin = (Size - mapSize) / 2f;
        Vector2 ToMap(Vector2 world) => origin + (world - bounds.Position) * px;

        DrawRect(new Rect2(origin, mapSize), new Color(0.55f, 0.45f, 0.4f, 0.22f));
        DrawRect(new Rect2(origin, mapSize), new Color(0.75f, 0.6f, 0.5f, 0.55f), false, 1.5f);

        if (Director.ExitPortal is { } portal && IsInstanceValid(portal))
        {
            Vector2 at = ToMap(portal.GlobalPosition);
            DrawArc(at, 5f, 0f, Mathf.Tau, 20, new Color(0.75f, 0.55f, 1f), 2f, true);
            DrawCircle(at, 2f, new Color(0.75f, 0.55f, 1f));
        }

        foreach (Node node in GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup))
        {
            if (node is Enemies.Enemy { IsBoss: true } boss && !boss.Health.IsDead)
            {
                DrawCircle(ToMap(boss.GlobalPosition), 4.5f, new Color(1f, 0.25f, 0.2f));
            }
        }

        foreach (AllyMarker ally in Allies())
        {
            PartyMarkers.Draw(this, ToMap(bounds.GetCenter() + (ally.Position - bounds.GetCenter()).Clamp(-bounds.Size / 2f, bounds.Size / 2f)), ally, 3.5f, label: false);
        }

        Vector2 me = ToMap(Player.GlobalPosition);
        Vector2 f = Player.AimPoint - Player.GlobalPosition;
        f = f.LengthSquared() > 1f ? f.Normalized() : Vector2.Up;
        Vector2 s = new(-f.Y, f.X);
        const float k = 6f;
        var arrow = new[] { me + f * k, me - f * k * 0.6f + s * k * 0.6f, me - f * k * 0.25f, me - f * k * 0.6f - s * k * 0.6f };
        DrawColoredPolygon(arrow, new Color(0.45f, 1f, 0.55f));
        DrawPolyline(new[] { arrow[0], arrow[1], arrow[2], arrow[3], arrow[0] }, Colors.Black with { A = 0.8f }, 1f, true);
    }
}
