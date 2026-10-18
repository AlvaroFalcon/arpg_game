#nullable enable
using System.Linq;
using Godot;

namespace ArpgGame.UI;

/// <summary>
/// A small floating tooltip that follows the mouse — docs/design/menus.md ->
/// "Tooltips". It is top-level and ignores the mouse, so it never takes part in
/// a container's layout (long text can't resize the panel underneath) and never
/// steals hover from what it describes. Clamped to the viewport.
/// </summary>
public partial class HoverTooltip : PanelContainer
{
    private const float MaxWidth = 360f;
    private VBoxContainer _lines = null!;

    /// <summary>The control this tooltip is currently showing for (UiTheme.InstantTooltip).</summary>
    public Control? Source { get; set; }

    public override void _Ready()
    {
        TopLevel = true;
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 100;
        Visible = false;
        AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.06f, 0.05f, 0.09f, 0.97f), UiTheme.PanelBorder, 2, 4, 8));
        _lines = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _lines.AddThemeConstantOverride("separation", 2);
        AddChild(_lines);
    }

    /// <summary>Clears and starts a new tooltip with a coloured title.</summary>
    public void Begin(string title, Color color)
    {
        Clear();
        AddLine(title, color, 18);
    }

    /// <summary>Removes every line (the caller adds its own, title included).</summary>
    public void Clear()
    {
        foreach (Node child in _lines.GetChildren())
        {
            _lines.RemoveChild(child);
            child.QueueFree();
        }
    }

    public bool HasContent => _lines.GetChildCount() > 0;

    /// <summary>The text of every line, top to bottom (tests).</summary>
    public string AllText => string.Join("\n", _lines.GetChildren().OfType<Label>().Select(l => l.Text));

    public void AddLine(string text, Color color, int size = 15)
    {
        Label label = UiTheme.Label(text, size, color);
        label.MouseFilter = MouseFilterEnum.Ignore;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(Mathf.Min(MaxWidth, Mathf.Max(160f, text.Length * size * 0.5f)), 0f);
        _lines.AddChild(label);
    }

    public void ShowTooltip()
    {
        Size = Vector2.Zero;
        Visible = true;
        Reposition();
    }

    public void HideTooltip() => Visible = false;

    public override void _Process(double delta)
    {
        if (Visible)
        {
            Reposition();
        }
    }

    private void Reposition()
    {
        Vector2 mouse = GetViewport().GetMousePosition();
        Vector2 screen = GetViewportRect().Size;
        Vector2 size = GetCombinedMinimumSize();
        Size = size;
        Vector2 at = mouse + new Vector2(18f, 18f);
        if (at.X + size.X > screen.X - 8f)
        {
            at.X = mouse.X - size.X - 12f;
        }

        if (at.Y + size.Y > screen.Y - 8f)
        {
            at.Y = mouse.Y - size.Y - 12f;
        }

        GlobalPosition = new Vector2(Mathf.Max(8f, at.X), Mathf.Max(8f, at.Y));
    }
}
