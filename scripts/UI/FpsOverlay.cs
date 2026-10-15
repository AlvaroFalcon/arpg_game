#nullable enable
using Godot;

namespace ArpgGame.UI;

/// <summary>
/// Frame-rate readout for debugging — docs/design/menus.md -> "Settings" (Show
/// FPS; F3 toggles it anywhere). Lives under the <see cref="Core.GameSettings"/>
/// autoload so it survives scene changes, on top of every other layer.
/// Shows frames per second, the average frame time and the worst frame of the
/// last second: a hitch (one 80 ms frame among sixty fast ones) barely moves
/// the FPS figure but shows up plainly in the worst-frame number.
/// </summary>
public partial class FpsOverlay : CanvasLayer
{
    private Label _label = null!;
    private float _window;
    private int _frames;
    private float _worst;
    private float _sum;

    public override void _Ready()
    {
        Layer = 128;
        ProcessMode = ProcessModeEnum.Always;
        _label = new Label
        {
            Position = new Vector2(8f, 4f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontSizeOverride("font_size", 14);
        _label.AddThemeColorOverride("font_color", new Color(0.75f, 1f, 0.75f));
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 4);
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        float ms = (float)delta * 1000f;
        _frames++;
        _sum += ms;
        _worst = Mathf.Max(_worst, ms);
        _window += (float)delta;
        if (_window < 1f)
        {
            return;
        }

        float fps = _frames / _window;
        float avg = _sum / _frames;
        _label.Text = $"{fps:0} FPS  ·  {avg:0.0} ms  ·  worst {_worst:0.0} ms";
        _label.AddThemeColorOverride("font_color", _worst > 50f ? new Color(1f, 0.45f, 0.4f) : _worst > 25f ? new Color(1f, 0.85f, 0.4f) : new Color(0.75f, 1f, 0.75f));
        _window = 0f;
        _frames = 0;
        _sum = 0f;
        _worst = 0f;
    }
}
