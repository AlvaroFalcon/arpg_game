#nullable enable
using Godot;
using PathToExile.Skills;

namespace PathToExile.UI;

/// <summary>One dodge charge: a diamond that fills while recharging.</summary>
public partial class DodgePip : Control
{
    private float _fill;

    public float Fill
    {
        get => _fill;
        set
        {
            if (!Mathf.IsEqualApprox(_fill, value))
            {
                _fill = value;
                QueueRedraw();
            }
        }
    }

    public DodgePip()
    {
        CustomMinimumSize = new Vector2(22f, 22f);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        Vector2 c = Size / 2f;
        Vector2[] diamond = { c + new Vector2(0, -10), c + new Vector2(10, 0), c + new Vector2(0, 10), c + new Vector2(-10, 0) };
        DrawColoredPolygon(diamond, new Color(0.08f, 0.1f, 0.14f, 0.9f));
        if (_fill >= 1f)
        {
            DrawColoredPolygon(diamond, new Color(0.45f, 0.9f, 1f));
        }
        else if (_fill > 0f)
        {
            float h = 20f * _fill;
            DrawRect(new Rect2(c.X - 4f, c.Y + 10f - h, 8f, h), new Color(0.3f, 0.55f, 0.7f));
        }

        DrawPolyline(new[] { diamond[0], diamond[1], diamond[2], diamond[3], diamond[0] }, new Color(0.7f, 0.95f, 1f), 2f);
    }
}

/// <summary>A skill bar slot: icon, cooldown sweep, level, support pips.</summary>
public partial class SkillSlotView : Control
{
    public SkillInstance Skill { get; }
    private readonly int _key;
    private float _cooldown;

    public float Cooldown
    {
        set
        {
            if (!Mathf.IsEqualApprox(_cooldown, value))
            {
                _cooldown = value;
                QueueRedraw();
            }
        }
    }

    public SkillSlotView(SkillInstance skill, int key)
    {
        Skill = skill;
        _key = key;
        CustomMinimumSize = new Vector2(64f, 64f);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, new Color(0.06f, 0.05f, 0.08f, 0.9f));
        if (Skill.Definition.Icon is { } icon)
        {
            DrawTextureRect(icon, rect.Grow(-4f), false);
        }

        if (_cooldown > 0f)
        {
            DrawRect(new Rect2(0f, 0f, Size.X, Size.Y * _cooldown), new Color(0f, 0f, 0f, 0.6f));
        }

        DrawRect(rect, UiTheme.Gold with { A = 0.8f }, false, 2f);
        Font font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(5f, 16f), _key.ToString(), HorizontalAlignment.Left, -1, 14, UiTheme.TextDim);
        DrawString(font, new Vector2(0f, Size.Y - 5f), $"Lv{Skill.Level}", HorizontalAlignment.Right, Size.X - 4f, 14, UiTheme.Gold);
        for (int i = 0; i < Skill.Attached.Count; i++)
        {
            DrawCircle(new Vector2(8f + i * 10f, Size.Y - 9f), 3.5f, new Color(0.3f, 0.9f, 0.45f));
        }
    }
}
