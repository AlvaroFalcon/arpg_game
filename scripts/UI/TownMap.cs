#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Quests;
using ArpgGame.World;

namespace ArpgGame.UI;

/// <summary>
/// The town's map — docs/design/hideout.md -> "Town map". The whole town fits, so
/// it never scrolls: the ground drawn from <see cref="HideoutTown.Map"/>, every
/// station as a dot in its colour, you, and the route while a station walk is
/// on. Small, it sits on top of the town directory (click it to open the big
/// one); big (<see cref="Big"/>, M or Tab) it covers the screen, each station
/// an icon in a badge with its name and what it does, decorations as dots, and
/// clicking a station closes the map and walks you there, like the directory.
/// </summary>
public partial class TownMap : Control
{
    public Hideout Hideout = null!;

    /// <summary>The overlay version: names, decorations, clickable stations.</summary>
    public bool Big;

    /// <summary>The quest log's version (docs/design/quests.md -> "The quest log"): the small map with
    /// this station ringed in gold and pulsing, the others dimmed to 40 %. Not clickable.</summary>
    public string? Highlight;

    /// <summary>False for the quest log's map: it neither opens the big map nor shows its tooltip.</summary>
    public bool Interactive = true;

    private float _t;

    private static ImageTexture? _ground;
    private string? _hovered;
    private string? _hoveredNow;
    private Rect2 _area;
    private float _px;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        if (!Interactive)
        {
            MouseFilter = MouseFilterEnum.Ignore;
        }
        else if (!Big)
        {
            UiTheme.InstantTooltip(this, "Town map\nClick or press M for the big map");
            MouseDefaultCursorShape = CursorShape.PointingHand;
        }
    }

    public override void _Process(double delta)
    {
        if (IsVisibleInTree())
        {
            _t += (float)delta;
            QueueRedraw();
        }
    }

    /// <summary>The quest marker of a station, as drawn on the map (tests).</summary>
    public QuestMarker QuestMarkerOf(string uid) => Hideout is not null && IsInstanceValid(Hideout) ? QuestService.MarkerAt(Hideout, uid) : QuestMarker.None;

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

    private static readonly Dictionary<string, Texture2D?> Icons = new();

    /// <summary>The station's map icon (tools/art/sprites/mapicons.ts), or null.</summary>
    private static Texture2D? Icon(string id)
    {
        if (!Icons.TryGetValue(id, out Texture2D? tex))
        {
            string path = $"res://assets/ui/mapicons/map_{id}.png";
            tex = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
            Icons[id] = tex;
        }

        return tex;
    }

    /// <summary>Centred text with a dark drop shadow.</summary>
    private void Label(Font font, string text, Vector2 centre, int size, Color color)
    {
        Vector2 at = centre - new Vector2(font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X / 2f, 0f);
        DrawString(font, at + Vector2.One, text, HorizontalAlignment.Left, -1, size, Colors.Black with { A = 0.85f });
        DrawString(font, at, text, HorizontalAlignment.Left, -1, size, color);
    }

    /// <summary>Where a world point is drawn, in this control's coordinates (captures, tests).</summary>
    public Vector2 MapPointOf(Vector2 world) => ToMap(world);

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
        var placed = new List<Rect2>();
        Vector2 mouse = GetLocalMousePosition();
        foreach (PlacedProp prop in Hideout.Data.Props)
        {
            if (HideoutCatalog.Prop(prop.Kind) is not { Station: true } def)
            {
                continue;
            }

            Vector2 at = ToMap(prop.Position);
            bool hover = Big && at.DistanceTo(mouse) < 24f;
            if (hover)
            {
                _hoveredNow = prop.Uid;
            }

            QuestMarker quest = QuestService.MarkerAt(Hideout, prop.Uid);
            if (!Big)
            {
                bool dim = Highlight is not null && Highlight != prop.Uid;
                float a = dim ? 0.4f : 1f;
                DrawCircle(at, 5f, Colors.Black with { A = 0.7f * a });
                DrawCircle(at, 3.5f, def.Accent with { A = a });
                if (Highlight == prop.Uid)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(_t * 5f);
                    DrawArc(at, 8f + 2.5f * pulse, 0f, Mathf.Tau, 28, UiTheme.Gold with { A = 0.55f + 0.45f * pulse }, 2.5f, true);
                    DrawArc(at, 6.5f, 0f, Mathf.Tau, 24, UiTheme.Gold, 1.5f, true);
                }
                else if (Highlight is null && quest != QuestMarker.None)
                {
                    QuestGlyphs.DrawMarker(this, at + new Vector2(0f, -16f), 7f, quest, 10f);
                }

                continue;
            }

            // Big: the station's icon in a badge ringed with its colour, its name
            // above (or below) and, on hover, what it does in the header.
            bool target = Hideout.WalkingTo == prop.Uid;
            float r = hover ? 20f : 17f;
            DrawCircle(at + new Vector2(0f, 2f), r + 2f, Colors.Black with { A = 0.5f });
            DrawCircle(at, r + 2f, hover || target ? Colors.White : def.Accent);
            DrawCircle(at, r, new Color(0.08f, 0.06f, 0.12f));
            if (Icon(def.Id) is { } tex)
            {
                float side = hover ? 32f : 28f;
                DrawTextureRect(tex, new Rect2(at - Vector2.One * side / 2f, Vector2.One * side), false);
            }

            // The name goes above the badge, or below when it would run into a
            // name already placed (Ysolde and Odran stand side by side).
            Color text = hover || target ? Colors.White : UiTheme.Text;
            // A quest adds a small gold line to the name (above it, or below when the name goes below).
            string tail = quest switch { QuestMarker.New => "New quest", QuestMarker.Ready => "Turn in", _ => "" };
            float extra = tail.Length > 0 ? 14f : 0f;
            float width = Mathf.Max(font.GetStringSize(def.Name, HorizontalAlignment.Left, -1, 15).X, tail.Length > 0 ? 60f : 0f);
            Vector2 above = at + new Vector2(0f, -r - 8f);
            var rect = new Rect2(above.X - width / 2f - 12f, above.Y - 15f - extra, width + 24f, 19f + extra);
            bool below = placed.Exists(o => o.Intersects(rect));
            if (below)
            {
                rect.Position += new Vector2(0f, 2f * r + 26f);
            }

            placed.Add(rect);
            Vector2 nameAt = below ? at + new Vector2(0f, r + 19f) : above;
            Label(font, def.Name, nameAt, 15, text);
            if (tail.Length > 0)
            {
                Label(font, tail, nameAt + new Vector2(0f, below ? 14f : -15f), 12, UiTheme.Gold);
            }

            if (quest != QuestMarker.None)
            {
                QuestGlyphs.DrawMarker(this, at + new Vector2(0.7f * r, -0.7f * r), 9f, quest, 12f);
            }

            if (hover)
            {
                // What it does, in the header (under the badge it would cover other names).
                string questTitle = quest != QuestMarker.None && QuestService.FirstAt(Hideout, prop.Uid) is { } q ? $"  ·  Quest: {q.Title}" : "";
                string info = $"{def.Name} — {def.Verb}{questTitle}  ·  click to walk there";
                float w = font.GetStringSize(info, HorizontalAlignment.Left, -1, 15).X;
                DrawString(font, new Vector2(_area.End.X - w, _area.Position.Y - 29f), info, HorizontalAlignment.Left, -1, 15, def.Accent);
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
            if (_hovered is null)
            {
                DrawString(font, new Vector2(_area.Position.X + 130f, _area.Position.Y - 29f), "click a station to walk there  ·  M or Esc to close",
                    HorizontalAlignment.Left, -1, 14, UiTheme.TextDim);
            }
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
            PickStation(uid);
        }
    }

    /// <summary>A click on a station: close the map and walk there.</summary>
    public void PickStation(string uid)
    {
        Hideout.Hud.CloseBigMap();
        Hideout.WalkTo(uid);
    }
}
