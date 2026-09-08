#nullable enable
using Godot;

namespace PathToExile.Items;

/// <summary>
/// docs/design/items.md -> "Item filter": "DisplayStyle // all nullable, so rules
/// cascade and merge." Concretely: a rule need not specify every field, and a
/// null field here means "this rule didn't say" rather than "off" —
/// <see cref="GroundLabelPool"/> fills a genuinely-null field with its own
/// baseline (e.g. white text). This is a different question from *which* rule's
/// style gets used at all: exactly one rule's <see cref="DisplayStyle"/> is used
/// per item, never a merge of several — see
/// <see cref="FilterChain.Evaluate"/> and "First matching rule wins".
/// </summary>
public sealed class DisplayStyle
{
    public Color? TextColor { get; init; }

    public Color? BackgroundColor { get; init; }

    public Color? BorderColor { get; init; }

    public int? FontSize { get; init; }

    /// <summary>Reserved — no audio system exists yet. Authored but not consumed
    /// by <see cref="GroundLabelPool"/> in milestone 1, same idiom as
    /// <c>ProjectileParams</c>' authored-but-inert capability fields.</summary>
    public string? Sound { get; init; }

    /// <summary>Reserved — no beam/FX renderer exists yet. Not consumed in
    /// milestone 1.</summary>
    public bool? Beam { get; init; }

    /// <summary>
    /// The one field with real teeth today: gates whether the ground label is
    /// drawn at all. Governs <i>display only</i> — see <see cref="IDroppable"/>'s
    /// class doc and <see cref="CurrencyDrop.OnPickup"/> for why a Hidden currency
    /// pile is still collected on walk-over. Nullable like the rest of this type
    /// so "unset" (no rule said anything about visibility) is distinct from
    /// "explicitly false."
    /// </summary>
    public bool? Hidden { get; init; }

    /// <summary>The style used when no rule in a <see cref="FilterChain"/>
    /// matches: every field null, i.e. "use the renderer's own baseline."</summary>
    public static readonly DisplayStyle Baseline = new();
}
