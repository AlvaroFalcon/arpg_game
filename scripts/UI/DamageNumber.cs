using System.Collections.Generic;
using Godot;

namespace ArpgGame.UI;

/// <summary>
/// Minimal floating damage number. Not polish — per docs/design/skills.md ->
/// priority item 7, this is "the instrument": without seeing numbers land you
/// cannot judge whether a skill's damage/use-time feel right. Rises and fades.
/// Numbers made through <see cref="Spawn"/> are pooled (docs/design/performance.md
/// -> "Mass deaths"): when one finishes it hides and waits to be reused instead of
/// being freed, because a pack dying at once spawned dozens of scene instances in
/// one frame.
/// </summary>
[GlobalClass]
public partial class DamageNumber : Node2D
{
    private const float Lifetime = 0.6f;
    private static readonly Vector2 RiseVelocity = new(0f, -40f);

    private Label _label = null!;
    private float _elapsed;
    private Color _baseColor = Colors.White;
    private bool _pooled;

    /// <summary>Finished numbers waiting to be reused (they stay in their scene, hidden).</summary>
    private static readonly Stack<DamageNumber> Idle = new();
    private const int MaxIdle = 128;

    public override void _Ready()
    {
        _label = GetNode<Label>("Label");
        _baseColor = _label.GetThemeColor("font_color");
    }

    /// <summary>A number at <paramref name="at"/> under <paramref name="parent"/>:
    /// a finished one from the pool when there is one there, else a new instance.</summary>
    public static DamageNumber Spawn(PackedScene scene, Node parent, Vector2 at)
    {
        while (Idle.Count > 0)
        {
            DamageNumber n = Idle.Pop();
            if (!IsInstanceValid(n) || n.IsQueuedForDeletion())
            {
                continue;
            }

            if (n.GetParent() != parent)
            {
                // Left behind under another parent (a test scene, say): let it go.
                n.QueueFree();
                continue;
            }

            n._elapsed = 0f;
            n.Modulate = Colors.White;
            n._label.AddThemeColorOverride("font_color", n._baseColor);
            n.GlobalPosition = at;
            n.Visible = true;
            n.SetProcess(true);
            return n;
        }

        var fresh = scene.Instantiate<DamageNumber>();
        fresh._pooled = true;
        parent.AddChild(fresh);
        fresh.GlobalPosition = at;
        return fresh;
    }

    public void Display(float amount)
    {
        _label.Text = Mathf.RoundToInt(amount).ToString();
    }

    /// <summary>Same as <see cref="Display(float)"/> with a custom colour
    /// (red for damage the player takes).</summary>
    public void Display(float amount, Color color)
    {
        Display(amount);
        _label.AddThemeColorOverride("font_color", color);
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        GlobalPosition += RiseVelocity * (float)delta;
        Modulate = new Color(1f, 1f, 1f, 1f - _elapsed / Lifetime);

        if (_elapsed >= Lifetime)
        {
            if (_pooled && Idle.Count < MaxIdle)
            {
                Visible = false;
                SetProcess(false);
                Idle.Push(this);
            }
            else
            {
                QueueFree();
            }
        }
    }
}
