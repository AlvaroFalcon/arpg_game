#nullable enable
using System;
using System.Linq;
using Godot;
using ArpgGame.World;

namespace ArpgGame.UI;

/// <summary>
/// Hideout edit mode — docs/design/hideout.md -> "Edit mode". H (or Esc menu →
/// Edit hideout) opens it; the world keeps running and WASD still walks, so the
/// player can look around the town while arranging it. Three tools:
/// <list type="bullet">
/// <item><b>Move</b>: drag any prop (stations too) to a new spot; right-click a
/// decoration to put it back in storage.</item>
/// <item><b>Floors</b>: pick a floor you own tiles of and paint cells with the
/// left button; the right button restores the town's own floor and refunds the tile.</item>
/// <item><b>Decorations</b>: pick a stored decoration and click to place it.</item>
/// </list>
/// Done, H or Esc saves the layout to the account.
/// </summary>
public partial class HideoutEditor : CanvasLayer, IClosablePanel
{
    public enum Tool
    {
        Move,
        Floors,
        Decorations,
    }

    public Hideout Hideout { get; set; } = null!;

    public event Action? Closed;

    public Tool Current { get; private set; } = Tool.Move;
    public string? SelectedFloor { get; private set; }
    public string? SelectedDecor { get; private set; }

    private Control _catcher = null!;
    private HBoxContainer _tools = null!;
    private HBoxContainer _palette = null!;
    private Label _hint = null!;
    private Gizmo _gizmo = null!;
    private string? _dragging;
    private Vector2 _grab;
    private bool _painting;
    private bool _erasing;
    private ulong _openedFrame;
    private bool _closed;

    public override void _Ready()
    {
        Layer = 15;
        _openedFrame = Engine.GetProcessFrames();
        PanelStack.Push(this);
        Control root = UiTheme.FullRect(this);
        root.MouseFilter = Control.MouseFilterEnum.Ignore;

        // Clicks on the town land here (so they never move the player).
        _catcher = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _catcher.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _catcher.GuiInput += OnWorldInput;
        root.AddChild(_catcher);

        var top = new PanelContainer();
        root.AddChild(top);
        top.SetAnchorsPreset(Control.LayoutPreset.CenterTop, true);
        top.OffsetLeft = -420f;
        top.OffsetRight = 420f;
        top.OffsetTop = 34f;
        VBoxContainer topBox = UiTheme.VBox(top, 4);
        HBoxContainer header = UiTheme.HBox(topBox, 8);
        Label title = UiTheme.Label("Edit hideout", 22, UiTheme.Gold);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title);
        _tools = UiTheme.HBox(header, 4);
        header.AddChild(UiTheme.Button("Done", Close, 15, 90f));
        _hint = UiTheme.Label("", 13, UiTheme.TextDim);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        topBox.AddChild(_hint);

        var bottom = new PanelContainer();
        root.AddChild(bottom);
        bottom.SetAnchorsPreset(Control.LayoutPreset.CenterBottom, true);
        bottom.OffsetLeft = -560f;
        bottom.OffsetRight = 560f;
        bottom.OffsetTop = -150f;
        bottom.OffsetBottom = -82f;
        var scroll = new ScrollContainer { VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        bottom.AddChild(scroll);
        _palette = UiTheme.HBox(scroll, 6);

        _gizmo = new Gizmo { Editor = this, ZIndex = 200, Name = "EditGizmo" };
        Hideout.AddChild(_gizmo);
        Use(Tool.Move);
    }

    public void Use(Tool tool)
    {
        Current = tool;
        _dragging = null;
        RebuildTools();
        RebuildPalette();
        _hint.Text = tool switch
        {
            Tool.Move => "Drag anything to move it — stations too. Right-click a decoration to put it in storage. WASD to walk around.",
            Tool.Floors => "Pick a floor below, then paint with the left button. Right button restores the town's floor and refunds the tile. Buy tiles from Ysolde.",
            _ => "Pick a stored decoration below, then click to place it. Buy decorations from Ysolde in the plaza.",
        };
    }

    private void RebuildTools()
    {
        foreach (Node child in _tools.GetChildren())
        {
            child.QueueFree();
        }

        foreach (Tool tool in Enum.GetValues<Tool>())
        {
            Tool t = tool;
            Button b = UiTheme.Button(tool.ToString(), () => Use(t), 14, 110f);
            if (tool == Current)
            {
                b.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.24f, 0.18f, 0.32f), UiTheme.Gold, 2));
            }

            _tools.AddChild(b);
        }
    }

    private void RebuildPalette()
    {
        foreach (Node child in _palette.GetChildren())
        {
            child.QueueFree();
        }

        HideoutData data = Hideout.Data;
        if (Current == Tool.Move)
        {
            _palette.AddChild(UiTheme.Label($"{data.Props.Count} props in town · {data.Stock.Where(s => HideoutCatalog.Prop(s.Key) is not null).Sum(s => s.Value)} decorations in storage", 15, UiTheme.TextDim));
            return;
        }

        if (Current == Tool.Floors)
        {
            foreach (FloorStyle style in HideoutCatalog.FloorsForSale)
            {
                FloorStyle s = style;
                Button b = Chip($"{style.Name}\n×{data.StockOf(style.Id)}", style.Id == SelectedFloor, () => { SelectedFloor = s.Id; RebuildPalette(); });
                b.Disabled = data.StockOf(style.Id) <= 0 && style.Id != SelectedFloor;
                b.Icon = Swatch(style.Swatch);
                _palette.AddChild(b);
            }

            return;
        }

        foreach (PropDef def in HideoutCatalog.Decorations)
        {
            if (data.StockOf(def.Id) <= 0)
            {
                continue;
            }

            PropDef d = def;
            _palette.AddChild(Chip($"{def.Name}\n×{data.StockOf(def.Id)}", def.Id == SelectedDecor, () => { SelectedDecor = d.Id; RebuildPalette(); }));
        }

        if (_palette.GetChildCount() == 0)
        {
            _palette.AddChild(UiTheme.Label("Storage is empty — Ysolde in the plaza sells decorations. Right-click one in Move to store it.", 15, UiTheme.TextDim));
        }
    }

    private static Button Chip(string text, bool selected, Action pick)
    {
        Button b = UiTheme.Button(text, pick, 13, 120f);
        b.CustomMinimumSize = new Vector2(120f, 50f);
        if (selected)
        {
            b.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.24f, 0.18f, 0.32f), UiTheme.Gold, 2));
        }

        return b;
    }

    private static Texture2D Swatch(Color c)
    {
        Image image = Image.Create(14, 14, false, Image.Format.Rgba8);
        image.Fill(c);
        return ImageTexture.CreateFromImage(image);
    }

    public void SelectFloor(string id)
    {
        SelectedFloor = id;
        RebuildPalette();
    }

    public void SelectDecor(string id)
    {
        SelectedDecor = id;
        RebuildPalette();
    }

    // --- world input -------------------------------------------------------------------

    public Vector2 Mouse => Hideout.GetGlobalMousePosition();

    private void OnWorldInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
                if (b.Pressed)
                {
                    PressAt(Mouse);
                }
                else
                {
                    Release();
                }

                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } r:
                if (r.Pressed)
                {
                    RightAt(Mouse);
                }
                else
                {
                    _erasing = false;
                }

                break;
            case InputEventMouseMotion:
                DragTo(Mouse);
                break;
        }

        _catcher.AcceptEvent();
    }

    /// <summary>A left press at a world point with the current tool (tests call this directly).</summary>
    public void PressAt(Vector2 world)
    {
        switch (Current)
        {
            case Tool.Move:
                _dragging = Hideout.PropAt(world);
                if (_dragging is not null && Hideout.Data.Find(_dragging) is { } p)
                {
                    _grab = p.Position - world;
                }

                break;
            case Tool.Floors:
                _painting = true;
                Paint(world);
                break;
            case Tool.Decorations when SelectedDecor is { } kind && Valid(world):
                Hideout.PlaceFromStock(kind, Snap(world));
                if (Hideout.Data.StockOf(kind) <= 0)
                {
                    SelectedDecor = null;
                }

                RebuildPalette();
                break;
        }
    }

    public void DragTo(Vector2 world)
    {
        if (_dragging is { } uid && Valid(world + _grab))
        {
            Hideout.MoveProp(uid, Snap(world + _grab));
        }
        else if (_painting)
        {
            Paint(world);
        }
        else if (_erasing)
        {
            Hideout.Town.Unpaint(HideoutTown.CellOf(world));
        }
    }

    public void Release()
    {
        _dragging = null;
        _painting = false;
        RebuildPaletteIfFloors();
    }

    public void RightAt(Vector2 world)
    {
        if (Current == Tool.Move && Hideout.PropAt(world) is { } uid && Hideout.StoreProp(uid))
        {
            RebuildPalette();
        }
        else if (Current == Tool.Floors)
        {
            _erasing = true;
            Hideout.Town.Unpaint(HideoutTown.CellOf(world));
            RebuildPalette();
        }
        else if (Current == Tool.Decorations)
        {
            SelectedDecor = null;
            RebuildPalette();
        }
    }

    private void Paint(Vector2 world)
    {
        if (SelectedFloor is { } style)
        {
            Hideout.Town.Paint(HideoutTown.CellOf(world), style);
        }
    }

    private void RebuildPaletteIfFloors()
    {
        if (Current == Tool.Floors)
        {
            RebuildPalette();
        }
    }

    /// <summary>Props stand on open ground inside the town.</summary>
    public static bool Valid(Vector2 world) => HideoutTown.Bounds.Grow(-HideoutTown.CellPx).HasPoint(world) && HideoutTown.Walkable(HideoutTown.CellOf(world));

    private static Vector2 Snap(Vector2 p) => (p / 8f).Round() * 8f;

    // --- frame -------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (Engine.GetProcessFrames() != _openedFrame && Input.IsActionJustPressed(Hideout.EditAction))
        {
            Close();
        }
    }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        PanelStack.Remove(this);
        _gizmo.QueueFree();
        QueueFree();
        Closed?.Invoke();
    }

    /// <summary>World-space highlights: the prop under the cursor, the cell being
    /// painted, a ghost where a decoration would land.</summary>
    private sealed partial class Gizmo : Node2D
    {
        public HideoutEditor Editor = null!;

        public override void _Process(double delta) => QueueRedraw();

        public override void _Draw()
        {
            if (!IsInstanceValid(Editor))
            {
                return;
            }

            Vector2 mouse = GetGlobalMousePosition();
            Hideout h = Editor.Hideout;
            foreach (PlacedProp p in h.Data.Props)
            {
                PropDef? def = HideoutCatalog.Prop(p.Kind);
                // A footprint under every prop: gold for stations, blue for decorations.
                Color c = def?.Station == true ? new Color(1f, 0.82f, 0.4f, 0.8f) : new Color(0.6f, 0.8f, 1f, 0.7f);
                DrawSetTransform(p.Position, 0f, new Vector2(1f, 0.5f));
                DrawArc(Vector2.Zero, 16f, 0f, Mathf.Tau, 24, c, 2f);
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }

            switch (Editor.Current)
            {
                case Tool.Move:
                    string? uid = Editor._dragging ?? h.PropAt(mouse);
                    if (uid is not null && h.Data.Find(uid) is { } prop)
                    {
                        float r = HideoutCatalog.Prop(prop.Kind)?.PickRadius ?? 24f;
                        DrawArc(prop.Position + new Vector2(0f, -r * 0.6f), r, 0f, Mathf.Tau, 40, new Color(1f, 0.9f, 0.5f), 2.5f);
                        DrawString(ThemeDB.FallbackFont, prop.Position + new Vector2(-60f, 18f), HideoutCatalog.Prop(prop.Kind)?.Name ?? prop.Kind,
                            HorizontalAlignment.Center, 120f, 13, Colors.White);
                    }

                    break;
                case Tool.Floors:
                    Vector2I cell = HideoutTown.CellOf(mouse);
                    bool ok = HideoutTown.Walkable(cell);
                    DrawRect(new Rect2(new Vector2(cell.X, cell.Y) * HideoutTown.CellPx, Vector2.One * HideoutTown.CellPx),
                        ok ? new Color(1f, 0.9f, 0.5f, 0.9f) : new Color(1f, 0.3f, 0.3f, 0.7f), false, 2f);
                    break;
                case Tool.Decorations when Editor.SelectedDecor is not null:
                    bool valid = Valid(mouse);
                    DrawCircle(mouse, 12f, valid ? new Color(0.5f, 1f, 0.6f, 0.45f) : new Color(1f, 0.3f, 0.3f, 0.45f));
                    break;
            }
        }
    }
}
