#nullable enable
using System.Linq;
using Godot;
using ArpgGame.Enemies;
using ArpgGame.World;

namespace ArpgGame.UI;

/// <summary>
/// The full-screen overlay map — docs/design/menus.md -> "Overlay map". Tab
/// toggles it, Diablo / Path of Exile style: a large, see-through map drawn
/// over the game and centred on the player, so you can keep fighting while
/// you read it. Explored floor is a faint wash, walls are bright outlines, and
/// markers show you, the entrance, the boss arena (or the exit portal once the
/// boss is down) and the monsters you can currently see.
/// </summary>
public partial class MapOverlay : Control
{
    public const string Action = "toggle_map";

    public RealityController Reality = null!;

    /// <summary>Pixels per layout cell on the overlay.</summary>
    [Export]
    public float CellPx = 14f;

    private static readonly Color Floor = new(0.55f, 0.6f, 0.95f, 0.13f);
    private static readonly Color BossFloor = new(1f, 0.3f, 0.25f, 0.12f);
    private static readonly Color Wall = new(0.72f, 0.8f, 1f, 0.75f);
    private static readonly Color Gate = new(1f, 0.55f, 0.2f, 0.9f);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;
    }

    public override void _Input(InputEvent e)
    {
        if (!GetTree().Paused && InputMap.HasAction(Action) && e.IsActionPressed(Action) && !e.IsEcho())
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Toggle() => Visible = !Visible;

    public override void _Process(double delta)
    {
        if (Visible)
        {
            QueueRedraw();
        }
    }

    /// <summary>Screen position of a world point, with the player at the centre.</summary>
    private Vector2 ToScreen(Vector2 world) =>
        Size / 2f + (world - Reality.Player.GlobalPosition) / RasterLayout.CellPx * CellPx;

    private Vector2 CellCorner(Vector2I c) => ToScreen(new Vector2(c.X, c.Y) * RasterLayout.CellPx);

    public override void _Draw()
    {
        RasterLayout r = Reality.Raster;
        var gate = r.Gate.ToHashSet();
        var screen = new Rect2(Vector2.Zero, Size);
        foreach (Vector2I c in Reality.Explored)
        {
            if (!r.Walkable(c) && !gate.Contains(c))
            {
                continue;
            }

            Vector2 at = CellCorner(c);
            if (!screen.Grow(CellPx).HasPoint(at))
            {
                continue;
            }

            DrawRect(new Rect2(at, Vector2.One * CellPx), r.BossRegion.Contains(c) ? BossFloor : Floor);
            // Walls: an outline on every side that borders something solid.
            Edge(c, new Vector2I(1, 0), at + new Vector2(CellPx, 0f), at + new Vector2(CellPx, CellPx));
            Edge(c, new Vector2I(-1, 0), at, at + new Vector2(0f, CellPx));
            Edge(c, new Vector2I(0, 1), at + new Vector2(0f, CellPx), at + new Vector2(CellPx, CellPx));
            Edge(c, new Vector2I(0, -1), at, at + new Vector2(CellPx, 0f));
        }

        foreach (Vector2I c in r.Gate.Where(Reality.Explored.Contains))
        {
            Vector2 at = CellCorner(c);
            DrawRect(new Rect2(at + Vector2.One, Vector2.One * (CellPx - 2f)), Gate with { A = Reality.BossFightActive ? 0.9f : 0.35f });
        }

        // Entrance (a blue waypoint), boss arena or exit portal.
        Marker(RasterLayout.World(r.Entrance), new Color(0.4f, 0.7f, 1f), 5f, square: true);
        if (Reality.ExitPortal is { } portal && IsInstanceValid(portal))
        {
            Marker(portal.GlobalPosition, new Color(0.75f, 0.55f, 1f), 6f, ring: true);
        }
        else if (Reality.Explored.Contains(r.BossCell) && !Reality.Completed)
        {
            Marker(RasterLayout.World(r.BossCell), new Color(1f, 0.3f, 0.25f), 6f, ring: true);
        }

        // Monsters in explored ground: red dots (bosses and rares bigger).
        foreach (Enemy e in Reality.GetChildren().OfType<Enemy>())
        {
            if (e.Health.IsDead || !Reality.Explored.Contains(Reality.Field.CellOf(e.GlobalPosition)))
            {
                continue;
            }

            float size = e.IsBoss ? 4.5f : e.Rarity >= MonsterRarity.Rare ? 3.2f : 2f;
            Color color = e.IsBoss ? new Color(1f, 0.25f, 0.2f) : e.Rarity switch
            {
                MonsterRarity.Magic => new Color(0.45f, 0.6f, 1f),
                MonsterRarity.Rare or MonsterRarity.Unique => new Color(1f, 0.85f, 0.3f),
                _ => new Color(0.95f, 0.3f, 0.25f, 0.9f),
            };
            DrawCircle(ToScreen(e.GlobalPosition), size, color);
        }

        // The player, at the centre: a bright arrow in the facing direction.
        Vector2 me = Size / 2f;
        Vector2 dir = Reality.Player.Velocity.LengthSquared() > 25f ? Reality.Player.Velocity.Normalized() : Vector2.Up;
        Vector2 side = new(-dir.Y, dir.X);
        DrawColoredPolygon(new[] { me + dir * 8f, me - dir * 5f + side * 5f, me - dir * 2f, me - dir * 5f - side * 5f }, new Color(0.4f, 1f, 0.55f));
        DrawPolyline(new[] { me + dir * 8f, me - dir * 5f + side * 5f, me - dir * 2f, me - dir * 5f - side * 5f, me + dir * 8f }, new Color(0f, 0f, 0f, 0.7f), 1.5f, true);

        Font font = ThemeDB.FallbackFont;
        string title = $"{Reality.Layout.Name}  ·  Tab to close";
        float w = font.GetStringSize(title, HorizontalAlignment.Left, -1, 15).X;
        DrawStringOutline(font, new Vector2((Size.X - w) / 2f, 88f), title, HorizontalAlignment.Left, -1, 15, 4, new Color(0f, 0f, 0f, 0.8f));
        DrawString(font, new Vector2((Size.X - w) / 2f, 88f), title, HorizontalAlignment.Left, -1, 15, new Color(0.8f, 0.85f, 1f, 0.9f));
    }

    private void Edge(Vector2I c, Vector2I dir, Vector2 a, Vector2 b)
    {
        Vector2I n = c + dir;
        if (!Reality.Raster.Walkable(n) && !Reality.Raster.Gate.Contains(n))
        {
            DrawLine(a, b, Wall, 2f);
        }
    }

    private void Marker(Vector2 world, Color color, float size, bool square = false, bool ring = false)
    {
        Vector2 p = ToScreen(world);
        if (square)
        {
            DrawRect(new Rect2(p - Vector2.One * size / 2f, Vector2.One * size), color);
        }
        else if (ring)
        {
            DrawArc(p, size, 0f, Mathf.Tau, 24, color, 2f, true);
            DrawCircle(p, size * 0.4f, color);
        }
    }
}
