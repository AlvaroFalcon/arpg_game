#nullable enable
using Godot;

namespace PathToExile.Core;

/// <summary>
/// Trauma-based camera shake (docs/design/bosses.md -> "Feedback"). A
/// <see cref="Camera2D"/> child named "Camera2D" on the current viewport is
/// shaken via its offset; strength decays every frame. Honours the
/// <see cref="GameSettings.ScreenShake"/> toggle so players sensitive to it can
/// turn it off without losing any information (every shake also has a visual
/// telegraph or flash).
/// </summary>
[GlobalClass]
public partial class CameraShake : Node
{
    private float _trauma;
    private readonly RandomNumberGenerator _rng = new();

    /// <summary>Adds shake. <paramref name="strength"/> is roughly pixels of offset.</summary>
    public static void Request(Node context, float strength)
    {
        if (!GameSettings.ShakeEnabled(context))
        {
            return;
        }

        if (context.GetViewport()?.GetCamera2D() is not { } camera)
        {
            return;
        }

        CameraShake? shaker = camera.GetNodeOrNull<CameraShake>("CameraShake");
        if (shaker is null)
        {
            shaker = new CameraShake { Name = "CameraShake" };
            camera.AddChild(shaker);
        }

        shaker._trauma = Mathf.Min(14f, shaker._trauma + strength);
    }

    public override void _Process(double delta)
    {
        if (GetParent() is not Camera2D camera)
        {
            return;
        }

        if (_trauma <= 0.05f)
        {
            _trauma = 0f;
            camera.Offset = Vector2.Zero;
            return;
        }

        camera.Offset = new Vector2(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f)) * _trauma;
        _trauma = Mathf.MoveToward(_trauma, 0f, (float)delta * 30f);
    }
}
