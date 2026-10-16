#nullable enable
using Godot;

namespace ArpgGame.UI;

/// <summary>A small turning arc for anything that takes more than a moment
/// (docs/design/menus.md -> "Look": slow actions show progress). Drawn, so it
/// needs no glyphs from the font.</summary>
public partial class Spinner : Control
{
    public Color Colour { get; set; } = UiTheme.Gold;

    private float _angle;

    public Spinner(float size = 28f)
    {
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Always;
    }

    public Spinner() : this(28f)
    {
    }

    public override void _Process(double delta)
    {
        _angle = Mathf.Wrap(_angle + (float)delta * 5f, 0f, Mathf.Tau);
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 centre = Size / 2f;
        float radius = Mathf.Min(Size.X, Size.Y) / 2f - 3f;
        DrawArc(centre, radius, 0f, Mathf.Tau, 32, Colour with { A = 0.18f }, 3f, true);
        DrawArc(centre, radius, _angle, _angle + Mathf.Pi * 0.6f, 16, Colour, 3f, true);
    }
}
