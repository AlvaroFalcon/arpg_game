#nullable enable
using Godot;

namespace PathToExile;

/// <summary>A swirling portal (drawn, not a sprite): the way into a run.</summary>
public partial class Portal : Node2D
{
    private float _t;

    public override void _Process(double delta)
    {
        _t += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, 46f, new Color(0.15f, 0.05f, 0.25f, 0.8f));
        for (int i = 0; i < 5; i++)
        {
            float r = 44f - i * 8f;
            float a = _t * (1.5f + i * 0.4f) * (i % 2 == 0 ? 1f : -1f);
            var color = new Color(0.6f + i * 0.08f, 0.35f + i * 0.1f, 1f, 0.85f);
            DrawArc(Vector2.Zero, r, a, a + Mathf.Pi * 1.3f, 32, color, 3f);
        }

        DrawCircle(Vector2.Zero, 8f + Mathf.Sin(_t * 4f) * 2f, new Color(1f, 0.9f, 1f, 0.9f));
    }
}
