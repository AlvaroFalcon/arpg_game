#nullable enable
using Godot;

namespace ArpgGame.UI;

/// <summary>
/// Small life and mana bars floating over the player — docs/design/menus.md ->
/// "Overhead bars". The owner's ask: "so you don't have to keep looking down".
/// A world-space child of the Player (it follows the character, and lines up
/// with the voxel figure because the 2D canvas and the 3D ground are aligned),
/// drawn above everything in the world. Turn it off in Settings.
/// </summary>
public partial class OverheadBars : Node2D
{
    private const float Width = 40f;
    private const float LifeH = 4f;
    private const float ManaH = 3f;

    /// <summary>Height above the character's feet, in world pixels.</summary>
    [Export]
    public float Lift = 46f;

    private Player _player = null!;
    private float _trail = 1f;

    public override void _Ready()
    {
        _player = GetParent<Player>();
        ZIndex = 100;
        ZAsRelative = false;
        Position = new Vector2(0f, -Lift);
    }

    public override void _Process(double delta)
    {
        bool on = Core.GameSettings.Get(this)?.OverheadBars ?? true;
        Visible = on && !(_player.Health?.IsDead ?? true) && _player.IsVisibleInTree();
        if (!Visible)
        {
            return;
        }

        float life = Fraction(_player.Stats.Life);
        _trail = life > _trail ? life : Mathf.MoveToward(_trail, life, (float)delta * 0.7f);
        QueueRedraw();
    }

    private static float Fraction(Stats.ResourcePool? pool) =>
        pool is null || pool.Max <= 0f ? 0f : Mathf.Clamp(pool.Current / pool.Max, 0f, 1f);

    public override void _Draw()
    {
        bool mana = !_player.Stats.IgnoresResourceCosts && _player.Stats.Mana is not null;
        float height = LifeH + (mana ? ManaH + 1f : 0f);
        var frame = new Rect2(-Width / 2f - 1f, -1f, Width + 2f, height + 2f);
        DrawRect(frame, new Color(0.03f, 0.02f, 0.05f, 0.85f));

        float life = Fraction(_player.Stats.Life);
        if (_trail > life)
        {
            DrawRect(new Rect2(-Width / 2f, 0f, Width * _trail, LifeH), new Color(1f, 0.85f, 0.6f, 0.6f));
        }

        // Low life turns the bar brighter and makes it pulse, so it reads at a glance.
        Color lifeColor = life < 0.3f
            ? new Color(1f, 0.25f + 0.2f * Mathf.Sin((float)Time.GetTicksMsec() * 0.012f), 0.2f)
            : new Color(0.9f, 0.18f, 0.16f);
        DrawRect(new Rect2(-Width / 2f, 0f, Width * life, LifeH), lifeColor);
        DrawRect(new Rect2(-Width / 2f, 0f, Width * life, 1f), new Color(1f, 1f, 1f, 0.3f));
        if (mana)
        {
            DrawRect(new Rect2(-Width / 2f, LifeH + 1f, Width * Fraction(_player.Stats.Mana), ManaH), new Color(0.2f, 0.7f, 1f));
        }

        DrawRect(frame, new Color(0f, 0f, 0f, 0.9f), false, 1f);
    }
}
