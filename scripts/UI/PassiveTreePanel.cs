#nullable enable
using System.Linq;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;

namespace ArpgGame.UI;

/// <summary>
/// The passive tree (P) — docs/design/classes.md -> "Passive tree". One shared
/// tree, three class starts. Left-click a node next to your allocation to take
/// it (one point per level); right-click an allocated node to refund it for
/// gold, if nothing beyond it would be cut off. Drag to pan, wheel to zoom.
/// </summary>
public partial class PassiveTreePanel : ModalPanel
{
    public Player Player { get; set; } = null!;

    protected override string Title => "Passive Tree";

    protected override Vector2 FrameSize => new(1180f, 680f);

    private Label _points = null!;
    private HoverTooltip _tooltip = null!;
    private TreeView _view = null!;

    private CharacterData? Character => CharacterBuild.Of(Player)?.Data ?? Profile.Get(this)?.Active;

    protected override void Build(VBoxContainer body)
    {
        _points = UiTheme.Label("", 17, UiTheme.Text);
        body.AddChild(_points);
        _view = new TreeView { Panel = this, CustomMinimumSize = new Vector2(1140f, 520f), ClipContents = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddChild(_view);
        _tooltip = new HoverTooltip();
        Root.AddChild(_tooltip);
    }

    public override void Refresh()
    {
        if (Character is not { } c)
        {
            return;
        }

        long gold = Player.Wallet.GetBalance(Profile.GoldId);
        _points.Text = $"{c.Name} — level {c.Level} {c.Class.Name}   ·   {c.PassivePointsFree} point{(c.PassivePointsFree == 1 ? "" : "s")} to spend   ·   refund: {PassiveTree.RefundCost(c)} gold (you have {gold})   ·   LMB allocate · RMB refund · drag pan · wheel zoom";
        if (!_view.Centred)
        {
            _view.Centred = true;
            _view.Offset = -PassiveTree.Get(c.Class.TreeStart)!.Position * 0.6f;
        }

        _view.QueueRedraw();
    }

    private void Hover(PassiveNode? node)
    {
        if (node is null || Character is not { } c)
        {
            _tooltip.HideTooltip();
            return;
        }

        bool taken = c.Passives.Contains(node.Id);
        bool can = !taken && PassiveTree.CanAllocate(c, node.Id);
        Color title = node.Kind switch
        {
            PassiveKind.Keystone => new Color(0.8f, 0.6f, 1f),
            PassiveKind.Notable => new Color(0.55f, 0.8f, 1f),
            PassiveKind.Start => UiTheme.Ember,
            _ => UiTheme.Text,
        };
        _tooltip.Begin(node.Name, title);
        _tooltip.AddLine(node.Kind == PassiveKind.Small ? "Passive" : node.Kind.ToString(), UiTheme.TextDim, 13);
        foreach (string line in node.Describe().Split('\n'))
        {
            _tooltip.AddLine(line, new Color(0.62f, 0.75f, 1f));
        }

        string state = taken
            ? (PassiveTree.CanRefund(c, node.Id) ? $"Allocated — right-click to refund ({PassiveTree.RefundCost(c)} gold)" : "Allocated")
            : can ? (c.PassivePointsFree > 0 ? "Left-click to allocate" : "No passive points left") : "Not connected to your tree";
        _tooltip.AddLine(state, taken ? UiTheme.Gold : can ? UiTheme.Experience : UiTheme.TextDim, 13);
        _tooltip.ShowTooltip();
    }

    protected override void OnClosed() => _tooltip.HideTooltip();

    private void Click(PassiveNode node, bool refund)
    {
        if (Character is not { } c)
        {
            return;
        }

        if (!refund && c.PassivePointsFree > 0 && PassiveTree.CanAllocate(c, node.Id))
        {
            c.Passives.Add(node.Id);
            Sfx.Play(this, "levelup", -8f);
        }
        else if (refund && PassiveTree.CanRefund(c, node.Id) && Player.Wallet.TrySpend(Profile.GoldId, PassiveTree.RefundCost(c)))
        {
            c.Passives.Remove(node.Id);
            Sfx.Play(this, "ui");
        }
        else
        {
            return;
        }

        CharacterBuild.Of(Player)?.Rebuild();
        Refresh();
        Hover(node);
    }

    private sealed partial class TreeView : Control
    {
        public PassiveTreePanel Panel = null!;
        public Vector2 Offset;
        public float Zoom = 0.9f;
        public bool Centred;
        private bool _dragging;
        private Vector2 _dragStart;
        private bool _moved;
        private PassiveNode? _hover;

        public override void _Ready()
        {
            MouseExited += () =>
            {
                _hover = null;
                Panel.Hover(null);
                QueueRedraw();
            };
        }

        private Vector2 ToScreen(Vector2 p) => Size / 2f + (p + Offset) * Zoom;

        private static float Radius(PassiveNode n) => n.Kind switch
        {
            PassiveKind.Start => 18f,
            PassiveKind.Keystone => 15f,
            PassiveKind.Notable => 11f,
            _ => 6.5f,
        };

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.04f, 0.03f, 0.06f, 0.95f));
            if (Panel.Character is not { } c)
            {
                return;
            }

            var gold = new Color(0.95f, 0.78f, 0.35f);
            foreach (PassiveNode n in PassiveTree.Nodes.Values)
            {
                foreach (string l in n.Links)
                {
                    if (string.CompareOrdinal(n.Id, l) > 0)
                    {
                        continue;
                    }

                    PassiveNode m = PassiveTree.Nodes[l];
                    bool both = c.Passives.Contains(n.Id) && c.Passives.Contains(l);
                    DrawLine(ToScreen(n.Position), ToScreen(m.Position), both ? gold : new Color(0.3f, 0.28f, 0.38f), both ? 3f : 1.5f);
                }
            }

            Font font = ThemeDB.FallbackFont;
            foreach (PassiveNode n in PassiveTree.Nodes.Values)
            {
                Vector2 p = ToScreen(n.Position);
                float r = Radius(n) * Mathf.Clamp(Zoom / 0.9f, 0.7f, 1.6f);
                bool taken = c.Passives.Contains(n.Id);
                bool can = !taken && PassiveTree.CanAllocate(c, n.Id);
                Color fill = n.Kind switch
                {
                    PassiveKind.Start => n.ClassId == c.ClassId ? new Color(0.9f, 0.5f, 0.25f) : new Color(0.35f, 0.3f, 0.3f),
                    PassiveKind.Keystone => new Color(0.55f, 0.3f, 0.8f),
                    PassiveKind.Notable => new Color(0.3f, 0.55f, 0.75f),
                    _ => new Color(0.45f, 0.45f, 0.5f),
                };
                if (!taken && n.Kind != PassiveKind.Start)
                {
                    fill = fill.Darkened(0.55f);
                }

                DrawCircle(p, r, fill);
                Color ring = taken ? gold : can ? new Color(0.5f, 1f, 0.6f) : new Color(0.2f, 0.2f, 0.25f);
                DrawArc(p, r + 1.5f, 0f, Mathf.Tau, 24, ring, can || taken ? 2.5f : 1f);
                if (n == _hover)
                {
                    DrawArc(p, r + 5f, 0f, Mathf.Tau, 24, Colors.White, 1.5f);
                }

                if (n.Kind is PassiveKind.Start or PassiveKind.Keystone or PassiveKind.Notable && Zoom > 0.6f)
                {
                    DrawString(font, p + new Vector2(-60f, r + 14f), n.Name, HorizontalAlignment.Center, 120f, 12, taken ? gold : new Color(0.75f, 0.72f, 0.8f));
                }
            }
        }

        private PassiveNode? NodeAt(Vector2 screen) =>
            PassiveTree.Nodes.Values
                .Select(n => (n, d: ToScreen(n.Position).DistanceTo(screen)))
                .Where(t => t.d <= Radius(t.n) * Mathf.Clamp(Zoom / 0.9f, 0.7f, 1.6f) + 4f)
                .OrderBy(t => t.d)
                .Select(t => t.n)
                .FirstOrDefault();

        public override void _GuiInput(InputEvent e)
        {
            switch (e)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                    Zoom = Mathf.Min(2f, Zoom * 1.1f);
                    QueueRedraw();
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                    Zoom = Mathf.Max(0.45f, Zoom / 1.1f);
                    QueueRedraw();
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
                            Panel.Click(n, false);
                        }
                    }

                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } rb:
                    if (NodeAt(rb.Position) is { } node)
                    {
                        Panel.Click(node, true);
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
                        QueueRedraw();
                    }

                    PassiveNode? hover = NodeAt(mm.Position);
                    if (hover != _hover)
                    {
                        _hover = hover;
                        Panel.Hover(hover);
                        QueueRedraw();
                    }

                    break;
            }
        }
    }
}
