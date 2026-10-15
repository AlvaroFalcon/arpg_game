#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;

namespace ArpgGame.UI;

/// <summary>
/// The passive tree (P) — docs/design/classes.md -> "Passive tree". One shared
/// tree, three class starts. Hovering an unallocated node previews the cheapest
/// path to it; left-click allocates the whole path (one point per node), right-click
/// refunds an allocated node for free if nothing beyond it would be cut off. Search
/// highlights nodes by name/description. Drag to pan, wheel to zoom (at the cursor).
/// </summary>
public partial class PassiveTreePanel : ModalPanel
{
    private const ulong ConfirmWindowMs = 3000;

    public Player Player { get; set; } = null!;

    protected override string Title => "Passive Tree";

    protected override Vector2 FrameSize => new(1180f, 700f);

    private Label _points = null!;
    private Label _status = null!;
    private LineEdit _search = null!;
    private Button _reset = null!;
    private HoverTooltip _tooltip = null!;
    private TreeView _view = null!;
    private ulong _resetArmedAt;

    private CharacterData? Character => CharacterBuild.Of(Player)?.Data ?? Profile.Get(this)?.Active;

    protected override void Build(VBoxContainer body)
    {
        _points = UiTheme.Label("", 17, UiTheme.Text);
        body.AddChild(_points);

        HBoxContainer bar = UiTheme.HBox(body, 8);
        _search = new LineEdit { PlaceholderText = "Search name or effect…", CustomMinimumSize = new Vector2(300f, 0f), ClearButtonEnabled = true };
        _search.TextChanged += text => { _view.SetSearch(text); };
        bar.AddChild(_search);
        bar.AddChild(UiTheme.Button("Fit tree", () => _view.FitAll(), 15, 100f));
        bar.AddChild(UiTheme.Button("My start", () => _view.CentreOnStart(), 15, 100f));
        _reset = UiTheme.Button("Reset tree", ResetPressed, 15, 190f);
        bar.AddChild(_reset);
        _status = UiTheme.Label("", 15, UiTheme.Gold);
        _status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _status.ClipText = true;
        bar.AddChild(_status);

        _view = new TreeView { Panel = this, CustomMinimumSize = new Vector2(1140f, 520f), ClipContents = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddChild(_view);
        body.AddChild(UiTheme.Label("LMB allocate (whole path) · RMB refund (free) · drag pan · wheel zoom · green ring = reachable · gold = allocated · cyan = path preview · yellow glow = search match", 12, UiTheme.TextDim));
        _tooltip = new HoverTooltip();
        Root.AddChild(_tooltip);
    }

    public override void Refresh()
    {
        if (Character is not { } c)
        {
            return;
        }

        _points.Text = $"{c.Name} — level {c.Level} {c.Class.Name}   ·   {c.PassivePointsSpent} / {c.PassivePointsTotal} points   ·   {c.PassivePointsFree} free";
        _view.Sync(c);
    }

    public override void _Process(double delta)
    {
        if (IsOpen && _reset is not null && _resetArmedAt != 0 && Time.GetTicksMsec() - _resetArmedAt > ConfirmWindowMs)
        {
            DisarmReset();
        }

        // Esc first clears an active search; only then does it close the panel.
        if (IsOpen && _search is { Text.Length: > 0 } && Input.IsActionJustPressed("ui_cancel"))
        {
            _search.Text = "";
            _search.ReleaseFocus();
            _view.SetSearch("");
            return;
        }

        base._Process(delta);
    }

    /// <summary>Capture helper: types a search and previews the path to the farthest matching node.</summary>
    public void PreviewForCapture(string search)
    {
        _search.Text = search;
        _view.SetSearch(search);
        if (Character is { } c && _view.FarthestMatch(c) is { } node)
        {
            Hover(node);
        }
    }

    private void DisarmReset()
    {
        _resetArmedAt = 0;
        _reset.Text = "Reset tree";
    }

    private void ResetPressed()
    {
        if (Character is not { } c)
        {
            return;
        }

        if (_resetArmedAt == 0 || Time.GetTicksMsec() - _resetArmedAt > ConfirmWindowMs)
        {
            _resetArmedAt = Time.GetTicksMsec();
            _reset.Text = "Click again to confirm";
            return;
        }

        DisarmReset();
        PassiveTree.ResetAll(c);
        Sfx.Play(this, "ui");
        CharacterBuild.Of(Player)?.Rebuild();
        _status.Text = "Tree reset — all points refunded.";
        Refresh();
        _tooltip.HideTooltip();
    }

    private void Hover(PassiveNode? node)
    {
        if (node is null || Character is not { } c)
        {
            _view.SetPath(null);
            _tooltip.HideTooltip();
            return;
        }

        bool taken = c.Passives.Contains(node.Id);
        List<string>? path = taken ? null : PassiveTree.PathTo(c, node.Id);
        _view.SetPath(path);
        int cost = path?.Count ?? 0;
        int free = c.PassivePointsFree;
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

        if (taken)
        {
            _tooltip.AddLine(PassiveTree.CanRefund(c, node.Id) ? "Allocated — right-click to refund (free)" : "Allocated", UiTheme.Gold, 13);
        }
        else if (cost == 0)
        {
            _tooltip.AddLine("Not connected to your tree", UiTheme.TextDim, 13);
        }
        else
        {
            string pts = $"{cost} point{(cost == 1 ? "" : "s")}";
            _tooltip.AddLine(
                cost <= free ? $"Left-click to allocate: {pts} (you have {free})" : $"Needs {pts}, you have {free}",
                cost <= free ? UiTheme.Experience : new Color(1f, 0.45f, 0.4f),
                13);
        }

        _tooltip.ShowTooltip();
    }

    protected override void OnClosed()
    {
        _tooltip.HideTooltip();
        DisarmReset();
    }

    private void Click(PassiveNode node, bool refund)
    {
        if (Character is not { } c)
        {
            return;
        }

        if (!refund)
        {
            List<string> path = PassiveTree.PathTo(c, node.Id);
            if (path.Count == 0)
            {
                return;
            }

            if (path.Count > c.PassivePointsFree)
            {
                _status.Text = $"Needs {path.Count} point{(path.Count == 1 ? "" : "s")}, you have {c.PassivePointsFree}.";
                Sfx.Play(this, "ui", -10f);
                return;
            }

            foreach (string id in path)
            {
                c.Passives.Add(id);
            }

            _status.Text = path.Count > 1 ? $"Allocated {path.Count} nodes." : "";
            Sfx.Play(this, "levelup", -8f);
        }
        else if (PassiveTree.CanRefund(c, node.Id))
        {
            c.Passives.Remove(node.Id);
            _status.Text = "";
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
        private const float MinZoom = 0.12f;
        private const float MaxZoom = 2.5f;

        private static readonly Color Gold = new(0.95f, 0.78f, 0.35f);
        private static readonly Color PathColor = new(0.35f, 0.9f, 1f);
        private static readonly Color Glow = new(1f, 0.92f, 0.3f);
        private static readonly Color EdgeIdle = new(0.3f, 0.28f, 0.38f);
        private static readonly Color Reach = new(0.5f, 1f, 0.6f);
        private static readonly Color Back = new(0.04f, 0.03f, 0.06f, 0.95f);
        private static readonly Color LabelDim = new(0.75f, 0.72f, 0.8f);

        public PassiveTreePanel Panel = null!;
        public Vector2 Offset;
        public float Zoom = 0.7f;

        private readonly PassiveNode[] _nodes;
        private readonly Vector2[] _pos;
        private readonly string[] _search;
        private readonly Dictionary<string, int> _index = new();
        private readonly (int a, int b)[] _edges;
        private readonly Color[] _fill;
        private readonly bool[] _taken;
        private readonly bool[] _can;
        private readonly bool[] _onPath;
        private readonly bool[] _match;
        private List<int> _pathIdx = new();
        private bool _anyMatch;
        private CharacterData? _centredFor;
        private int _start = -1;
        private bool _dragging;
        private Vector2 _dragStart;
        private bool _moved;
        private int _hover = -1;

        public TreeView()
        {
            var list = new List<PassiveNode>(PassiveTree.Nodes.Values);
            _nodes = list.ToArray();
            int n = _nodes.Length;
            _pos = new Vector2[n];
            _search = new string[n];
            _fill = new Color[n];
            _taken = new bool[n];
            _can = new bool[n];
            _onPath = new bool[n];
            _match = new bool[n];
            for (int i = 0; i < n; i++)
            {
                _index[_nodes[i].Id] = i;
                _pos[i] = _nodes[i].Position;
                _search[i] = (_nodes[i].Name + "\n" + _nodes[i].Describe()).ToLowerInvariant();
            }

            var edges = new List<(int, int)>();
            for (int i = 0; i < n; i++)
            {
                foreach (string l in _nodes[i].Links)
                {
                    if (string.CompareOrdinal(_nodes[i].Id, l) < 0 && _index.TryGetValue(l, out int j))
                    {
                        edges.Add((i, j));
                    }
                }
            }

            _edges = edges.ToArray();
            FocusMode = FocusModeEnum.Click;
        }

        public override void _Ready()
        {
            MouseExited += () =>
            {
                _hover = -1;
                Panel.Hover(null);
                QueueRedraw();
            };
        }

        /// <summary>Re-reads allocation state (once per change, not per frame).</summary>
        public void Sync(CharacterData c)
        {
            for (int i = 0; i < _nodes.Length; i++)
            {
                PassiveNode node = _nodes[i];
                _taken[i] = c.Passives.Contains(node.Id);
                _can[i] = !_taken[i] && PassiveTree.CanAllocate(c, node.Id);
                _fill[i] = node.Kind switch
                {
                    PassiveKind.Start => node.ClassId == c.ClassId ? new Color(0.9f, 0.5f, 0.25f) : new Color(0.35f, 0.3f, 0.3f),
                    PassiveKind.Keystone => new Color(0.55f, 0.3f, 0.8f),
                    PassiveKind.Notable => new Color(0.3f, 0.55f, 0.75f),
                    _ => new Color(0.45f, 0.45f, 0.5f),
                };
                if (!_taken[i] && node.Kind != PassiveKind.Start)
                {
                    _fill[i] = _fill[i].Darkened(0.55f);
                }
            }

            _start = _index.TryGetValue(c.Class.TreeStart, out int s) ? s : -1;
            if (!ReferenceEquals(_centredFor, c))
            {
                _centredFor = c;
                _needCentre = true;
            }

            QueueRedraw();
        }

        private bool _needCentre;

        public void SetSearch(string text)
        {
            string q = text.Trim().ToLowerInvariant();
            _anyMatch = q.Length > 0;
            for (int i = 0; i < _match.Length; i++)
            {
                _match[i] = _anyMatch && _search[i].Contains(q);
            }

            QueueRedraw();
        }

        public PassiveNode? FarthestMatch(CharacterData c)
        {
            PassiveNode? best = null;
            int bestCost = 0;
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (_match[i] && !_taken[i] && PassiveTree.PathTo(c, _nodes[i].Id).Count is var cost && cost > bestCost)
                {
                    bestCost = cost;
                    best = _nodes[i];
                }
            }

            return best;
        }

        public void SetPath(List<string>? path)
        {
            foreach (int i in _pathIdx)
            {
                _onPath[i] = false;
            }

            _pathIdx = new List<int>(path?.Count ?? 0);
            if (path is not null)
            {
                foreach (string id in path)
                {
                    if (_index.TryGetValue(id, out int i))
                    {
                        _onPath[i] = true;
                        _pathIdx.Add(i);
                    }
                }
            }

            QueueRedraw();
        }

        public void FitAll()
        {
            if (_pos.Length == 0 || Size.X < 10f)
            {
                return;
            }

            Vector2 min = _pos[0];
            Vector2 max = _pos[0];
            foreach (Vector2 p in _pos)
            {
                min = new Vector2(Mathf.Min(min.X, p.X), Mathf.Min(min.Y, p.Y));
                max = new Vector2(Mathf.Max(max.X, p.X), Mathf.Max(max.Y, p.Y));
            }

            Vector2 span = max - min + new Vector2(120f, 120f);
            Zoom = Mathf.Clamp(Mathf.Min(Size.X / span.X, Size.Y / span.Y), MinZoom, MaxZoom);
            Offset = -(min + max) / 2f;
            QueueRedraw();
        }

        public void CentreOnStart()
        {
            if (_start >= 0)
            {
                Zoom = 0.7f;
                Offset = -_pos[_start];
                QueueRedraw();
            }
        }

        private Vector2 ToScreen(Vector2 p) => Size / 2f + (p + Offset) * Zoom;

        private float Scale => Mathf.Clamp(Zoom / 0.9f, 0.6f, 1.6f);

        private static float Radius(PassiveNode n) => n.Kind switch
        {
            PassiveKind.Start => 20f,
            PassiveKind.Keystone => 17f,
            PassiveKind.Notable => 11f,
            _ => 5f,
        };

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), Back);
            if (_needCentre && Size.X > 10f)
            {
                _needCentre = false;
                CentreOnStart();
            }

            var view = new Rect2(-40f, -40f, Size.X + 80f, Size.Y + 80f);
            float scale = Scale;

            foreach ((int a, int b) in _edges)
            {
                Vector2 pa = ToScreen(_pos[a]);
                Vector2 pb = ToScreen(_pos[b]);
                if (!view.HasPoint(pa) && !view.HasPoint(pb))
                {
                    continue;
                }

                if ((_onPath[a] && (_onPath[b] || _taken[b])) || (_onPath[b] && _taken[a]))
                {
                    DrawLine(pa, pb, PathColor, 3.5f);
                }
                else if (_taken[a] && _taken[b])
                {
                    DrawLine(pa, pb, Gold, 3f);
                }
                else
                {
                    DrawLine(pa, pb, EdgeIdle, 1.5f);
                }
            }

            Font font = ThemeDB.FallbackFont;
            for (int i = 0; i < _nodes.Length; i++)
            {
                Vector2 p = ToScreen(_pos[i]);
                if (!view.HasPoint(p))
                {
                    continue;
                }

                PassiveNode n = _nodes[i];
                float r = Radius(n) * scale;
                if (_match[i])
                {
                    DrawCircle(p, r + 9f, new Color(Glow, 0.22f));
                    DrawArc(p, r + 6f, 0f, Mathf.Tau, 24, Glow, 3f);
                }

                DrawCircle(p, r, _fill[i]);
                if (_onPath[i])
                {
                    DrawArc(p, r + 2f, 0f, Mathf.Tau, 20, PathColor, 3f);
                }
                else if (_taken[i])
                {
                    DrawArc(p, r + 1.5f, 0f, Mathf.Tau, 20, Gold, 2.5f);
                }
                else if (_can[i])
                {
                    DrawArc(p, r + 1.5f, 0f, Mathf.Tau, 20, Reach, 2f);
                }

                if (i == _hover)
                {
                    DrawArc(p, r + 5f, 0f, Mathf.Tau, 24, Colors.White, 1.5f);
                }

                if (n.Kind != PassiveKind.Small && Zoom > 0.35f)
                {
                    DrawString(font, p + new Vector2(-60f, r + 14f), n.Name, HorizontalAlignment.Center, 120f, 12, _taken[i] ? Gold : LabelDim);
                }
            }
        }

        private int NodeAt(Vector2 screen)
        {
            float scale = Scale;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < _nodes.Length; i++)
            {
                float d = ToScreen(_pos[i]).DistanceTo(screen);
                if (d <= Radius(_nodes[i]) * scale + 4f && d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            return best;
        }

        private void ZoomAt(Vector2 screen, float factor)
        {
            float nz = Mathf.Clamp(Zoom * factor, MinZoom, MaxZoom);
            Vector2 world = (screen - Size / 2f) / Zoom - Offset;
            Zoom = nz;
            Offset = (screen - Size / 2f) / nz - world;
            QueueRedraw();
        }

        public override void _GuiInput(InputEvent e)
        {
            switch (e)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } w:
                    ZoomAt(w.Position, 1.12f);
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } w:
                    ZoomAt(w.Position, 1f / 1.12f);
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                    if (mb.Pressed)
                    {
                        GrabFocus();
                        _dragging = true;
                        _moved = false;
                        _dragStart = mb.Position;
                    }
                    else
                    {
                        _dragging = false;
                        int i = NodeAt(mb.Position);
                        if (!_moved && i >= 0)
                        {
                            Panel.Click(_nodes[i], false);
                        }
                    }

                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } rb:
                    int r = NodeAt(rb.Position);
                    if (r >= 0)
                    {
                        Panel.Click(_nodes[r], true);
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

                    int hover = NodeAt(mm.Position);
                    if (hover != _hover)
                    {
                        _hover = hover;
                        Panel.Hover(hover >= 0 ? _nodes[hover] : null);
                        QueueRedraw();
                    }

                    break;
            }
        }
    }
}
