using Godot;

namespace PathToExile.UI;

/// <summary>
/// Minimal floating damage number. Not polish — per docs/design/skills.md ->
/// priority item 7, this is "the instrument": without seeing numbers land you
/// cannot judge whether a skill's damage/use-time feel right. Rises, fades, then
/// frees itself; no pooling, since hit frequency in the first slice is nowhere
/// near a rate that would matter.
/// </summary>
[GlobalClass]
public partial class DamageNumber : Node2D
{
    private const float Lifetime = 0.6f;
    private static readonly Vector2 RiseVelocity = new(0f, -40f);

    private Label _label = null!;
    private float _elapsed;

    public override void _Ready()
    {
        _label = GetNode<Label>("Label");
    }

    public void Display(float amount)
    {
        _label.Text = Mathf.RoundToInt(amount).ToString();
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        GlobalPosition += RiseVelocity * (float)delta;
        Modulate = new Color(1f, 1f, 1f, 1f - _elapsed / Lifetime);

        if (_elapsed >= Lifetime)
        {
            QueueFree();
        }
    }
}
