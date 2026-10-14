#nullable enable
using System;
using System.Linq;
using Godot;
using ArpgGame.World;

namespace ArpgGame.UI;

/// <summary>
/// The Mirror's jewel tree, drawn — docs/design/endgame.md -> "Mirror jewels". The
/// Mirror's Heart in the middle, six coloured branches; a socket glows when it
/// holds a jewel and is ringed green when one can go in. Left-click to socket,
/// right-click to take a jewel back out, drag to pan, wheel to zoom.
/// </summary>
public partial class JewelTreeView : Control
{
    public JewelData Data = new();

    /// <summary>Hovered socket (or null), for the panel's tooltip.</summary>
    public Action<JewelNode?>? Hovered;

    /// <summary>A socket was clicked: (node, remove?).</summary>
    public Action<JewelNode, bool>? Clicked;

    public Vector2 Offset;
    public float Zoom = 0.44f;
    private bool _dragging;
    private bool _moved;
    private Vector2 _dragStart;
    private JewelNode? _hover;
    private float _time;

    public override void _Ready()
    {
        ClipContents = true;
        MouseExited += () =>
        {
            _hover = null;
            Hovered?.Invoke(null);
            QueueRedraw();
        };
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (IsVisibleInTree())
        {
            QueueRedraw();
        }
    }

    private Vector2 ToScreen(Vector2 p) => Size / 2f + (p + Offset) * Zoom;

    private float Radius(JewelNode n) => (n.Kind switch
    {
        JewelNodeKind.Heart => 30f,
        JewelNodeKind.Notable => 20f,
        _ => 11f,
    }) * Mathf.Clamp(Zoom / 0.5f, 0.75f, 1.8f);

    private static Color BranchColor(JewelNode n) =>
        n.Branch >= 0 ? JewelTree.BranchColors[n.Branch] : new Color(0.85f, 0.7f, 1f);

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.03f, 0.025f, 0.05f, 0.96f));
        // A faint mirror ring behind the tree.
        DrawArc(ToScreen(Vector2.Zero), 540f * Zoom, 0f, Mathf.Tau, 96, new Color(0.5f, 0.4f, 0.8f, 0.12f), 2f, true);
        DrawArc(ToScreen(Vector2.Zero), 300f * Zoom, 0f, Mathf.Tau, 96, new Color(0.5f, 0.4f, 0.8f, 0.07f), 1f, true);

        foreach (JewelNode n in JewelTree.Nodes.Values)
        {
            foreach (string l in n.Links)
            {
                if (string.CompareOrdinal(n.Id, l) > 0)
                {
                    continue;
                }

                JewelNode m = JewelTree.Nodes[l];
                bool lit = JewelTree.IsActive(Data, n.Id) && JewelTree.IsActive(Data, l);
                Color c = lit ? BranchColor(n.Kind == JewelNodeKind.Heart ? m : n) : new Color(0.25f, 0.22f, 0.32f);
                DrawLine(ToScreen(n.Position), ToScreen(m.Position), c, lit ? 3.5f : 1.5f, true);
            }
        }

        Font font = ThemeDB.FallbackFont;
        foreach (JewelNode n in JewelTree.Nodes.Values)
        {
            Vector2 p = ToScreen(n.Position);
            float r = Radius(n);
            bool on = JewelTree.IsActive(Data, n.Id);
            bool can = JewelTree.CanSocket(Data, n.Id);
            Color branch = BranchColor(n);
            if (n.Kind == JewelNodeKind.Heart)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 2f);
                DrawCircle(p, r + 6f + pulse * 3f, new Color(0.6f, 0.45f, 1f, 0.15f));
                DrawCircle(p, r, new Color(0.12f, 0.08f, 0.2f));
                DrawArc(p, r, 0f, Mathf.Tau, 48, new Color(0.85f, 0.75f, 1f), 3f, true);
                Run.Pickup.DrawJewel(this, p, r / 13f, _time);
                continue;
            }

            // Socket: a dark well; a jewel sits in it when filled.
            DrawCircle(p, r, new Color(0.07f, 0.05f, 0.1f));
            if (on)
            {
                DrawCircle(p, r + 4f, branch with { A = 0.18f });
                Run.Pickup.DrawJewel(this, p, r / 11f, _time + n.Position.X);
            }

            Color ring = on ? branch : can ? new Color(0.5f, 1f, 0.6f) : branch.Darkened(0.65f);
            DrawArc(p, r, 0f, Mathf.Tau, 32, ring, on || can ? 2.5f : 1.2f, true);
            if (n.Kind == JewelNodeKind.Notable)
            {
                DrawArc(p, r + 4f, 0f, Mathf.Tau, 32, ring with { A = 0.6f }, 1.5f, true);
            }

            if (n == _hover)
            {
                DrawArc(p, r + 7f, 0f, Mathf.Tau, 32, Colors.White, 1.5f, true);
            }

            if (n.Kind == JewelNodeKind.Notable && Zoom > 0.35f)
            {
                DrawString(font, p + new Vector2(-80f, r + 18f), n.Name, HorizontalAlignment.Center, 160f, 13, on ? branch : new Color(0.7f, 0.66f, 0.78f));
            }
        }
    }

    public JewelNode? NodeAt(Vector2 screen) =>
        JewelTree.Nodes.Values
            .Select(n => (n, d: ToScreen(n.Position).DistanceTo(screen)))
            .Where(t => t.d <= Radius(t.n) + 4f)
            .OrderBy(t => t.d)
            .Select(t => t.n)
            .FirstOrDefault();

    /// <summary>Where a socket is drawn, in this control's coordinates (tests, captures).</summary>
    public Vector2 ScreenOf(string id) => JewelTree.Get(id) is { } n ? ToScreen(n.Position) : Vector2.Zero;

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                Zoom = Mathf.Min(1.4f, Zoom * 1.1f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                Zoom = Mathf.Max(0.3f, Zoom / 1.1f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                if (mb.Pressed)
                {
                    _dragging = true;
                    _moved = false;
                    _dragStart = mb.Position;
                }
                else
                {
                    _dragging = false;
                    if (!_moved && NodeAt(mb.Position) is { } n)
                    {
                        Clicked?.Invoke(n, false);
                    }
                }

                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } rb:
                if (NodeAt(rb.Position) is { } node)
                {
                    Clicked?.Invoke(node, true);
                }

                break;
            case InputEventMouseMotion mm:
                if (_dragging)
                {
                    if (mm.Position.DistanceTo(_dragStart) > 4f)
                    {
                        _moved = true;
                    }

                    Offset += mm.Relative / Zoom;
                }

                JewelNode? hover = NodeAt(mm.Position);
                if (hover != _hover)
                {
                    _hover = hover;
                    Hovered?.Invoke(hover);
                }

                break;
        }
    }
}
