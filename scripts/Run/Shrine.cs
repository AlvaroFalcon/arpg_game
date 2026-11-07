#nullable enable
using Godot;

namespace ArpgGame.Run;

public enum ShrineKind
{
    /// <summary>Touch: +1 Greed for the rest of the run (harder, richer — RiftDifficulty).</summary>
    Greed,

    /// <summary>Touch: an elite and two champions rise; the elite drops a chest for everyone.</summary>
    Challenge,

    /// <summary>Stand inside for <see cref="Shrine.ChargeSeconds"/>: heals the party and drops a chest each.</summary>
    Charge,

    /// <summary>Touch: every experience gem and coin on the map flies to its owner.</summary>
    Magnet,
}

/// <summary>
/// A shrine in the Survival Rift's arena — docs/design/survival-roadmap.md -> M3. Places worth
/// walking to in a mode where skills fire on their own. The host's director places, charges and
/// uses them (<see cref="RunDirector"/>, RunDirector.Shrines.cs); a co-op client draws replicas
/// from the host's records. A distance check like <see cref="Pickup"/>, not an Area2D.
/// </summary>
public partial class Shrine : Node2D
{
    public const float TouchRadius = 42f;
    public const float ChargeRadius = 80f;
    public const float ChargeSeconds = 3f;

    public ShrineKind Kind;
    public ushort Id;

    /// <summary>Charge shrines: 0..1.</summary>
    public float Progress;

    public bool Used { get; private set; }

    private float _t;
    private float _fade = 1f;

    public static Color ColorOf(ShrineKind kind) => kind switch
    {
        ShrineKind.Greed => new Color(1f, 0.78f, 0.25f),
        ShrineKind.Challenge => new Color(1f, 0.32f, 0.3f),
        ShrineKind.Charge => new Color(0.45f, 0.95f, 0.6f),
        _ => new Color(0.45f, 0.75f, 1f),
    };

    public static string NameOf(ShrineKind kind) => kind switch
    {
        ShrineKind.Greed => "Greed",
        ShrineKind.Challenge => "Challenge",
        ShrineKind.Charge => "Charge",
        _ => "Magnet",
    };

    public static string HintOf(ShrineKind kind) => kind switch
    {
        ShrineKind.Greed => "Touch: harder and richer",
        ShrineKind.Challenge => "Touch: fight an elite for a chest",
        ShrineKind.Charge => "Stand inside: heal and a chest",
        _ => "Touch: every gem flies to you",
    };

    public override void _Ready()
    {
        ZIndex = -1;
        AddToGroup(Group);
    }

    public const string Group = "rift_shrine";

    public void MarkUsed()
    {
        Used = true;
        Progress = 1f;
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (Used)
        {
            _fade -= (float)delta / 1.2f;
            Modulate = new Color(1f, 1f, 1f, Mathf.Max(0f, _fade));
            if (_fade <= 0f)
            {
                QueueFree();
            }
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        Color c = ColorOf(Kind);
        float pulse = 0.5f + 0.5f * Mathf.Sin(_t * 2.2f);
        DrawCircle(new Vector2(0f, 10f), 24f, new Color(0f, 0f, 0f, 0.3f));
        if (Kind == ShrineKind.Charge)
        {
            DrawArc(Vector2.Zero, ChargeRadius, 0f, Mathf.Tau, 48, c with { A = 0.35f + 0.15f * pulse }, 2f, true);
            if (Progress > 0f)
            {
                DrawArc(Vector2.Zero, ChargeRadius - 6f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * Progress, 48, c, 5f, true);
            }
        }
        else
        {
            DrawArc(Vector2.Zero, TouchRadius, 0f, Mathf.Tau, 36, c with { A = 0.25f + 0.2f * pulse }, 1.5f, true);
        }

        // A stone pillar with a glowing rune.
        var stone = new Color(0.28f, 0.25f, 0.3f);
        DrawRect(new Rect2(-10f, -30f, 20f, 36f), stone);
        DrawRect(new Rect2(-14f, 4f, 28f, 6f), stone.Darkened(0.2f));
        DrawRect(new Rect2(-12f, -34f, 24f, 6f), stone.Lightened(0.15f));
        DrawRect(new Rect2(-10f, -30f, 20f, 36f), c with { A = 0.6f }, false, 1.5f);
        DrawCircle(new Vector2(0f, -14f), 6f + pulse * 1.5f, c with { A = 0.35f });
        DrawCircle(new Vector2(0f, -14f), 3.5f, c);
        DrawRect(new Rect2(-3f, -140f, 6f, 106f), c with { A = 0.1f + 0.06f * pulse });
        Font font = ThemeDB.FallbackFont;
        string label = Used ? "" : NameOf(Kind);
        DrawStringOutline(font, new Vector2(-60f, -44f), ArpgGame.Core.Loc.T(label), HorizontalAlignment.Center, 120f, 13, 3, new Color(0f, 0f, 0f, 0.9f));
        DrawString(font, new Vector2(-60f, -44f), ArpgGame.Core.Loc.T(label), HorizontalAlignment.Center, 120f, 13, c);
    }
}
