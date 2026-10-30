#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Skills;

namespace ArpgGame.UI;

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

/// <summary>
/// The skill bar either side of the mirror — docs/design/menus.md -> "HUD: the mirror": LMB
/// and RMB on the left, Q and E on the right (every slot shows, empty ones too, so the keys
/// are learnt), with the mirror's width left free between them. The Survival Rift's
/// auto-cast skills (up to six, no keys) split the same way, a little smaller.
/// </summary>
public partial class SkillBar : Control
{
    public const float SlotSize = 46f;
    public const float SmallSlot = 36f;
    public const float SlotGap = 6f;

    /// <summary>Room each side for its slots: two big ones, or three small ones.</summary>
    public const float SideWidth = 3f * SmallSlot + 2f * 6f;

    public const float Gap = 2f * PlayerHudBar.MirrorClearance;
    public const float TotalWidth = Gap + 2f * SideWidth;
    public const float KeyRoom = 0f;

    private readonly List<SkillSlotView> _views = new();
    private string _signature = "";

    public IReadOnlyList<SkillSlotView> Views => _views;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(TotalWidth, SlotSize + KeyRoom);
    }

    /// <summary>Shows these slots (key label, skill or null for an empty slot); rebuilt only
    /// when something changed. The first half goes left of the mirror, the rest right.</summary>
    public void Show(IReadOnlyList<(string Key, SkillInstance? Skill)> slots)
    {
        var sb = new System.Text.StringBuilder();
        foreach ((string key, SkillInstance? skill) in slots)
        {
            sb.Append(key).Append(skill?.Definition.DisplayName).Append(skill?.Level).Append(skill?.Attached.Count).Append('|');
        }

        string signature = sb.ToString();
        if (signature == _signature)
        {
            return;
        }

        _signature = signature;
        foreach (SkillSlotView view in _views)
        {
            view.QueueFree();
        }

        _views.Clear();
        int left = (slots.Count + 1) / 2;
        bool small = left > 2;
        float size = small ? SmallSlot : SlotSize;
        float gap = small ? 6f : SlotGap;
        float centre = TotalWidth / 2f;
        for (int i = 0; i < slots.Count; i++)
        {
            var view = new SkillSlotView(slots[i].Skill, slots[i].Key) { CustomMinimumSize = new Vector2(size, size), Size = new Vector2(size, size) };
            AddChild(view);
            // Outwards from the mirror on each side, keeping the bar's order left to right.
            float x = i < left
                ? centre - Gap / 2f - (left - i) * size - (left - i - 1) * gap
                : centre + Gap / 2f + (i - left) * (size + gap);
            view.Position = new Vector2(x, SlotSize + KeyRoom - size);
            _views.Add(view);
        }
    }

    /// <summary>Updates the cooldown sweeps.</summary>
    public void Cooldowns(System.Func<SkillInstance, float> fraction)
    {
        foreach (SkillSlotView view in _views)
        {
            view.Cooldown = view.Skill is null ? 0f : fraction(view.Skill);
        }
    }

    /// <summary>A small light key cap ("Q", "1") with its top-left corner at <paramref name="at"/>.</summary>
    public static void DrawKeyCap(CanvasItem canvas, Font font, Vector2 at, string key, int size = 12)
    {
        float h = size + 5f;
        float w = Mathf.Max(h, font.GetStringSize(key, HorizontalAlignment.Left, -1, size).X + 7f);
        var cap = new Rect2(at, new Vector2(w, h));
        canvas.DrawRect(new Rect2(cap.Position + new Vector2(0f, 2f), cap.Size), new Color(0.3f, 0.26f, 0.22f));
        canvas.DrawRect(cap, new Color(0.9f, 0.86f, 0.77f));
        canvas.DrawString(font, new Vector2(cap.Position.X, cap.Position.Y + h - 4f), key, HorizontalAlignment.Center, w, size, new Color(0.12f, 0.1f, 0.08f));
    }

    /// <summary>A mouse with the left or right button lit, its top-left corner at <paramref name="at"/>.</summary>
    public static void DrawMouse(CanvasItem canvas, Vector2 at, bool right, float scale = 1f)
    {
        float w = 13f * scale;
        float h = 19f * scale;
        var shell = new Color(0.9f, 0.86f, 0.77f);
        var dark = new Color(0.12f, 0.1f, 0.08f);
        // Rounded body: a rect with circles at the top and bottom.
        canvas.DrawCircle(at + new Vector2(w / 2f, w / 2f), w / 2f + 1f, dark);
        canvas.DrawCircle(at + new Vector2(w / 2f, h - w / 2f), w / 2f + 1f, dark);
        canvas.DrawRect(new Rect2(at.X - 1f, at.Y + w / 2f, w + 2f, h - w), dark);
        canvas.DrawCircle(at + new Vector2(w / 2f, w / 2f), w / 2f, shell);
        canvas.DrawCircle(at + new Vector2(w / 2f, h - w / 2f), w / 2f, shell);
        canvas.DrawRect(new Rect2(at.X, at.Y + w / 2f, w, h - w), shell);
        // The pressed button: the top quarter of its side.
        var lit = UiTheme.Gold;
        float bx = right ? at.X + w / 2f + 0.5f : at.X;
        canvas.DrawRect(new Rect2(bx + (right ? 0f : 1.5f), at.Y + 2f, w / 2f - 2f, h * 0.38f), lit);
        canvas.DrawLine(new Vector2(at.X + w / 2f, at.Y), new Vector2(at.X + w / 2f, at.Y + h * 0.42f), dark, 1.2f);
        canvas.DrawLine(new Vector2(at.X, at.Y + h * 0.42f), new Vector2(at.X + w, at.Y + h * 0.42f), dark, 1.2f);
    }
}

/// <summary>A skill bar slot: icon, cooldown sweep, level, support pips.</summary>
public partial class SkillSlotView : Control
{
    /// <summary>The skill in the slot; null for an empty one (it still shows its key).</summary>
    public SkillInstance? Skill { get; }
    private readonly string _key;
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

    /// <summary>The key a slot is bound to, as shown on the skill bar.</summary>
    public static string LabelOf(SkillSlotId slot) => slot switch
    {
        SkillSlotId.Lmb => "LMB",
        SkillSlotId.Rmb => "RMB",
        SkillSlotId.Slot1 => "Q",
        SkillSlotId.Slot2 => "E",
        SkillSlotId.Slot3 => "3",
        _ => "4",
    };

    public SkillSlotView(SkillInstance? skill, string key)
    {
        Skill = skill;
        _key = key;
        CustomMinimumSize = new Vector2(SkillBar.SlotSize, SkillBar.SlotSize);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, new Color(0.06f, 0.05f, 0.08f, Skill is null ? 0.6f : 0.9f));
        Font font = ThemeDB.FallbackFont;
        if (Skill?.Definition.Icon is { } icon)
        {
            DrawTextureRect(icon, rect.Grow(-4f), false);
        }

        if (_cooldown > 0f)
        {
            DrawRect(new Rect2(0f, 0f, Size.X, Size.Y * _cooldown), new Color(0f, 0f, 0f, 0.6f));
        }

        DrawRect(rect, (Skill is null ? UiTheme.PanelBorder : UiTheme.Gold) with { A = 0.8f }, false, 2f);
        // The key: a mouse with its button lit for LMB/RMB, a key cap otherwise.
        if (_key is "LMB" or "RMB")
        {
            SkillBar.DrawMouse(this, new Vector2(3f, 3f), _key == "RMB", Size.X < 40f ? 0.6f : 0.75f);
        }
        else if (_key.Length > 0)
        {
            SkillBar.DrawKeyCap(this, font, new Vector2(2f, 2f), _key, 10);
        }

        if (Skill is null)
        {
            return;
        }

        DrawStringOutline(font, new Vector2(0f, Size.Y - 4f), $"Lv{Skill.Level}", HorizontalAlignment.Right, Size.X - 3f, 10, 3, new Color(0f, 0f, 0f, 0.85f));
        DrawString(font, new Vector2(0f, Size.Y - 4f), $"Lv{Skill.Level}", HorizontalAlignment.Right, Size.X - 3f, 10, UiTheme.Gold);
        for (int i = 0; i < Skill.Attached.Count; i++)
        {
            DrawCircle(new Vector2(6f + i * 7f, Size.Y - 6f), 2.5f, new Color(0.3f, 0.9f, 0.45f));
        }
    }
}
