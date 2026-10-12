#nullable enable
using System.Linq;
using Godot;

namespace ArpgGame.Enemies;

/// <summary>
/// Rarity readability for a monster — docs/design/endgame.md -> "Monster rarity":
/// a coloured ring on the floor (blue magic, yellow rare, violet unique-rare,
/// pulsing for unique-rares), and above it the generated name, its modifiers
/// and a life bar. Normal monsters show nothing; magic ones show a small bar
/// once hurt. Counter-scaled so the text stays readable on a scaled-up monster.
/// </summary>
public partial class RarityPlate : Node2D
{
    private Enemy _enemy = null!;
    private Header _header = null!;

    public override void _Ready()
    {
        _enemy = GetParent<Enemy>();
        ZIndex = -1;
        _header = new Header { ZIndex = 30, Plate = this };
        AddChild(_header);
    }

    public override void _Process(double delta)
    {
        if (_enemy.Rarity == MonsterRarity.Normal)
        {
            Visible = false;
            return;
        }

        Visible = true;
        Scale = new Vector2(1f / Mathf.Max(0.01f, _enemy.Scale.X), 1f / Mathf.Max(0.01f, _enemy.Scale.Y));
        QueueRedraw();
        _header.QueueRedraw();
    }

    public override void _Draw()
    {
        Color c = MonsterRarities.Color(_enemy.Rarity);
        float r = 16f * _enemy.Scale.X;
        float pulse = _enemy.Rarity == MonsterRarity.Unique ? 0.5f + 0.5f * Mathf.Sin((float)Time.GetTicksMsec() * 0.006f) : 0.6f;
        DrawSetTransform(new Vector2(0f, 6f * _enemy.Scale.Y), 0f, new Vector2(1f, 0.5f));
        DrawCircle(Vector2.Zero, r + 4f, c with { A = 0.18f + 0.12f * pulse });
        DrawArc(Vector2.Zero, r + 4f, 0f, Mathf.Tau, 32, c with { A = 0.85f }, 2.5f);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private sealed partial class Header : Node2D
    {
        public RarityPlate Plate = null!;

        public override void _Draw()
        {
            Enemy e = Plate._enemy;
            Color c = MonsterRarities.Color(e.Rarity);
            Font font = ThemeDB.FallbackFont;
            float top = -26f * e.Scale.Y - 8f;
            const float barW = 54f;
            bool showName = e.Rarity >= MonsterRarity.Rare;

            if (showName || e.LifeFraction < 0.999f)
            {
                DrawRect(new Rect2(-barW / 2f - 1f, top - 1f, barW + 2f, 6f), new Color(0f, 0f, 0f, 0.75f));
                DrawRect(new Rect2(-barW / 2f, top, barW * e.LifeFraction, 4f), c.Lerp(new Color(0.9f, 0.15f, 0.15f), 0.35f));
            }

            if (!showName)
            {
                return;
            }

            DrawString(font, new Vector2(-150f, top - 6f), e.ShownName, HorizontalAlignment.Center, 300f, 14, c);
            string mods = string.Join(" · ", e.Mods.Select(m => m.Name));
            if (mods.Length > 0)
            {
                DrawString(font, new Vector2(-150f, top - 22f), mods, HorizontalAlignment.Center, 300f, 11, new Color(0.85f, 0.85f, 0.9f, 0.9f));
            }
        }
    }
}
