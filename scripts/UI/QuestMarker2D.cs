#nullable enable
using Godot;
using ArpgGame.Core;
using ArpgGame.Quests;

namespace ArpgGame.UI;

/// <summary>
/// The "!" / "?" over a hideout station — docs/design/quests.md -> "Markers". A child of the
/// station's node (so it follows the station when you rearrange the town), 34 px above the
/// name label, bobbing; a ready quest's marker also pulses. Hidden while the E prompt shows
/// for its station (the prompt's card carries the marker). Drawn, not a sprite, so the voxel
/// view's 2D overlay shows it too.
/// </summary>
public partial class QuestMarker2D : Node2D
{
    /// <summary>The station's uid ("altar").</summary>
    public string Station { get; set; } = "";

    /// <summary>The station's name-label height (<see cref="Hideout.LabelHeightOf"/>).</summary>
    public float LabelHeight { get; set; }

    public Hideout? Hideout { get; set; }

    /// <summary>What the book says about the station right now.</summary>
    public QuestMarker Marker { get; private set; }

    private Profile? _profile;
    private float _t;
    private float _baseY;

    public override void _Ready()
    {
        ZIndex = 10;
        Name = "QuestMarker";
        _baseY = -(LabelHeight + 34f);
        Position = new Vector2(0f, _baseY);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        _profile ??= Profile.Get(this);
        QuestMarker marker = _profile is null ? QuestMarker.None : QuestService.MarkerAt(_profile, Station);
        if (marker != Marker)
        {
            Marker = marker;
            QueueRedraw();
        }

        bool shown = marker != QuestMarker.None && Hideout?.HighlightedStation != Station;
        Visible = shown;
        if (!shown)
        {
            return;
        }

        _t += (float)delta;
        Position = new Vector2(0f, _baseY + Mathf.Sin(_t * Mathf.Tau * 1.2f) * 3f);
        float pulse = marker == QuestMarker.Ready ? 1.06f + 0.06f * Mathf.Sin(_t * Mathf.Tau * 1.5f) : 1f;
        Scale = new Vector2(pulse, pulse);
    }

    public override void _Draw() => QuestGlyphs.DrawMarker(this, Vector2.Zero, 15f, Marker, 22f);
}
