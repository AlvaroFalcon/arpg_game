#nullable enable
using Godot;

namespace ArpgGame.Enemies;

/// <summary>A short burst of ash particles where something died. Pure feedback.</summary>
public partial class DeathPuff : Node2D
{
    private const float Lifetime = 0.45f;
    private float _elapsed;
    private float _size = 1f;
    private Color _color = Colors.White;
    private readonly Vector2[] _dirs = new Vector2[8];

    public static void Spawn(Node parent, Vector2 position, float size, Color color)
    {
        var puff = new DeathPuff { _size = size, _color = color, ZIndex = 5 };
        for (int i = 0; i < puff._dirs.Length; i++)
        {
            puff._dirs[i] = Vector2.Right.Rotated(Mathf.Tau * i / puff._dirs.Length + GD.Randf() * 0.6f) * (0.6f + GD.Randf() * 0.6f);
        }

        parent.AddChild(puff);
        puff.GlobalPosition = position;
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        if (_elapsed >= Lifetime)
        {
            QueueFree();
            return;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        float t = _elapsed / Lifetime;
        var dust = new Color(0.85f, 0.8f, 0.75f, 1f - t);
        foreach (Vector2 d in _dirs)
        {
            DrawRect(new Rect2(d * 28f * _size * t - Vector2.One * 2f * _size, Vector2.One * 4f * _size * (1f - t * 0.5f)), dust);
        }

        DrawArc(Vector2.Zero, 10f * _size + 20f * _size * t, 0f, Mathf.Tau, 24, (_color with { A = 0.6f * (1f - t) }), 2f);
    }
}
