#nullable enable
using Godot;

namespace PathToExile.UI;

/// <summary>
/// A short line of text that rises and fades above something in the world —
/// "Inventory full", "Not enough gold". Throttled per text so repeated clicks
/// don't stack copies.
/// </summary>
public partial class WorldMessage : Node2D
{
    private const float Life = 1.4f;
    private static string _lastText = "";
    private static ulong _lastTime;
    private float _t;

    public static void Show(Node2D above, string text, Color color)
    {
        ulong now = Time.GetTicksMsec();
        if (text == _lastText && now - _lastTime < 700)
        {
            return;
        }

        _lastText = text;
        _lastTime = now;
        Node parent = above.GetTree().CurrentScene ?? above;
        var msg = new WorldMessage { ZIndex = 50 };
        Label label = UiTheme.Label(text, 18, color, HorizontalAlignment.Center);
        label.Size = new Vector2(240f, 24f);
        label.Position = new Vector2(-120f, -12f);
        msg.AddChild(label);
        parent.AddChild(msg);
        msg.GlobalPosition = above.GlobalPosition + new Vector2(0f, -56f);
        Core.Sfx.Play(above, "ui", -4f);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        Position += new Vector2(0f, -22f * (float)delta);
        Modulate = new Color(1f, 1f, 1f, Mathf.Clamp((Life - _t) / 0.4f, 0f, 1f));
        if (_t >= Life)
        {
            QueueFree();
        }
    }
}
