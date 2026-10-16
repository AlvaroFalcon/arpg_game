#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.World;

namespace ArpgGame.UI;

/// <summary>
/// The town's map — docs/design/hideout.md -> "Town map". The whole town fits, so
/// it never scrolls: the ground drawn from <see cref="HideoutTown.Map"/>, every
/// station as a dot in its colour, you, and the route while a station walk is
/// on. Small, it sits on top of the town directory (click it to open the big
/// one); big (<see cref="Big"/>, M or Tab) it is a see-through overlay with
/// names and decorations, and clicking a station walks you there, like the
/// directory does.
/// </summary>
public partial class TownMap : Control
{
    public Hideout Hideout = null!;

    /// <summary>The overlay version: names, decorations, clickable stations.</summary>
    public bool Big;

    private static ImageTexture? _ground;
    private string? _hovered;
    private string? _hoveredNow;
    private Rect2 _area;
    private float _px;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        if (!Big)
        {
            TooltipText = "Town map — click or press M for the big map";
            MouseDefaultCursorShape = CursorShape.PointingHand;
        }
    }

    public override void _Process(double delta)
    {
        if (IsVisibleInTree())
        {
            QueueRedraw();
        }
    }

    /// <summary>One pixel per town tile, coloured by its base ground.</summary>
    private static ImageTexture Ground()
    {
        if (_ground is not null)
        {
            return _ground;
        }

        var image = Image.Create(HideoutTown.Width, HideoutTown.Height, false, Image.Format.Rgba8);
        for (int y = 0; y < HideoutTown.Height; y++)
        {
            for (int x = 0; x < HideoutTown.Width; x++)
            {
                image.SetPixel(x, y, HideoutTown.Map[y][x] switch
                {
                    '#' => new Color(0.13f, 0.11f, 0.17f, 0.95f),
                    '~' => new Color(0.22f, 0.42f, 0.78f, 0.9f),
                    'o' => new Color(0.66f, 0.6f, 0.5f, 0.85f),
                    'p' => new Color(0.52f, 0.38f, 0.24f, 0.85f),
                    'g' => new Color(0.32f, 0.52f, 0.27f, 0.85f),
                    'm' => new Color(0.72f, 0.72f, 0.78f, 0.85f),
                    'c' => new Color(0.56f, 0.24f, 0.28f, 0.85f),
                    _ => new Color(0.44f, 0.44f, 0.48f, 0.85f),
                });
            }
        }

        _ground = ImageTexture.CreateFromImage(image);
        return _ground;
    }

    private Vector2 ToMap(Vector2 world) => _area.Position + world / HideoutTown.CellPx * _px;

    public override void _Draw()
    {
        if (Hideout is null || !IsInstanceValid(Hideout) || Hideout.Player is null)
        {
            return;
        }

        // Big: a full-screen map over a dark veil (the town keeps going under it),
        // with room above for the title. Small: the map fills its box.
        Vector2 padMin = Big ? new Vector2(40f, 120f) : new Vector2(4f, 4f);
        Vector2 padMax = Big ? new Vector2(40f, 28f) : new Vector2(4f, 4f);
        Vector2 room = Size - padMin - padMax;
        _px = Mathf.Min(room.X / HideoutTown.Width, room.Y / HideoutTown.Height);
        Vector2 mapSize = new Vector2(HideoutTown.Width, HideoutTown.Height) * _px;
        _area = new Rect2(padMin + (room - mapSize) / 2f, mapSize);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.02f, 0.01f, 0.04f, Big ? 0.8f : 0.6f));
        if (Big)
        {
            DrawRect(_area, new Color(0.04f, 0.03f, 0.07f));
        }

        DrawTextureRect(Ground(), _area, false);

        Font font = ThemeDB.FallbackFont;
        if (Big)
        {
            // Decorations: small grey dots (they are what a walk goes around).
            foreach (PlacedProp prop in Hideout.Data.Props)
            {
                if (HideoutCatalog.Prop(prop.Kind) is { Station: false })
                {
                    DrawCircle(ToMap(prop.Position), Mathf.Max(1.5f, _px * 0.18f), new Color(0.8f, 0.8f, 0.85f, 0.55f));
                }
            }
        }

        // The route of the walk under way.
        if (Hideout.WalkingTo is { } walking && Hideout.Paths is { } paths)
        {
            List<Vector2> route = paths.Route(walking, Hideout.Player.GlobalPosition);
            if (route.Count >= 2)
            {
                var points = new Vector2[route.Count];
                for (int i = 0; i < route.Count; i++)
                {
                    points[i] = ToMap(route[i]);
                }

                Color accent = HideoutCatalog.Prop(Hideout.Data.Find(walking)?.Kind ?? "")?.Accent ?? Colors.White;
                DrawPolyline(points, accent with { A = 0.9f }, Big ? 3f : 2f, true);
            }
        }

        // Stations.
        _hoveredNow = null;
        Vector2 mouse = GetLocalMousePosition();
        foreach (PlacedProp prop in Hideout.Data.Props)
        {
            if (HideoutCatalog.Prop(prop.Kind) is not { Station: true } def)
            {
                continue;
            }

            Vector2 at = ToMap(prop.Position);
            bool hover = Big && at.DistanceTo(mouse) < 14f;
            if (hover)
            {
                _hoveredNow = prop.Uid;
            }

            float r = Big ? (hover ? 8f : 6f) : 3.5f;
            DrawCircle(at, r + 1.5f, Colors.Black with { A = 0.7f });
            DrawCircle(at, r, def.Accent);
            if (Big)
            {
                Color text = hover || Hideout.WalkingTo == prop.Uid ? Colors.White : UiTheme.Text;
                Vector2 size = font.GetStringSize(def.Name, HorizontalAlignment.Left, -1, 14);
                Vector2 label = at + new Vector2(-size.X / 2f, -12f);
                DrawString(font, label + Vector2.One, def.Name, HorizontalAlignment.Left, -1, 14, Colors.Black with { A = 0.8f });
                DrawString(font, label, def.Name, HorizontalAlignment.Left, -1, 14, text);
            }
        }

        if (_hoveredNow != _hovered)
        {
            _hovered = _hoveredNow;
            MouseDefaultCursorShape = _hovered is not null || !Big ? CursorShape.PointingHand : CursorShape.Arrow;
        }

        // You: a green arrow pointing the way you face.
        Vector2 me = ToMap(Hideout.Player.GlobalPosition);
        Vector2 f = Hideout.Player.AimPoint - Hideout.Player.GlobalPosition;
        f = f.LengthSquared() > 1f ? f.Normalized() : Vector2.Up;
        Vector2 s = new(-f.Y, f.X);
        float k = Big ? 9f : 6f;
        var arrow = new[] { me + f * k, me - f * k * 0.6f + s * k * 0.6f, me - f * k * 0.25f, me - f * k * 0.6f - s * k * 0.6f };
        DrawColoredPolygon(arrow, new Color(0.45f, 1f, 0.55f));
        DrawPolyline(new[] { arrow[0], arrow[1], arrow[2], arrow[3], arrow[0] }, Colors.Black with { A = 0.8f }, 1f, true);

        if (Big)
        {
            DrawString(font, new Vector2(_area.Position.X, _area.Position.Y - 26f), "TOWN MAP", HorizontalAlignment.Left, -1, 22, UiTheme.Gold);
            DrawString(font, new Vector2(_area.Position.X + 130f, _area.Position.Y - 29f), "click a station to walk there  ·  M or Esc to close",
                HorizontalAlignment.Left, -1, 14, UiTheme.TextDim);
        }

        DrawRect(Big ? _area.Grow(2f) : new Rect2(Vector2.Zero, Size), new Color(0.35f, 0.5f, 0.8f, Big ? 0.8f : 0.5f), false, Big ? 2f : 1.5f);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            return;
        }

        AcceptEvent();
        if (!Big)
        {
            Hideout.Hud.ToggleBigMap();
        }
        else if (_hovered is { } uid)
        {
            Hideout.WalkTo(uid);
        }
    }
}
