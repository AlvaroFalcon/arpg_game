#nullable enable
using Godot;

namespace ArpgGame.Combat;

/// <summary>A melee swing's visual: a bright arc that sweeps and fades in ~0.15 s.</summary>
public partial class SlashEffect : Node2D
{
    private const float Lifetime = 0.16f;
    private float _elapsed;
    private float _range;
    private float _arc;
    private Color _tint;

    public static void Spawn(Node parent, Vector2 origin, Vector2 direction, float range, float arcDegrees, Color tint)
    {
        var fx = new SlashEffect { _range = range, _arc = Mathf.DegToRad(arcDegrees), _tint = tint, ZIndex = 3 };
        parent.AddChild(fx);
        fx.GlobalPosition = origin;
        fx.Rotation = direction.Angle();
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
        float sweep = _arc * Mathf.Min(1f, t * 2.2f);
        float start = -_arc / 2f;
        Color c = _tint with { A = 0.85f * (1f - t) };
        DrawArc(Vector2.Zero, _range * 0.85f, start, start + sweep, 24, c, _range * 0.28f);
        DrawArc(Vector2.Zero, _range, start, start + sweep, 24, Colors.White with { A = 0.7f * (1f - t) }, 2f);
    }
}
