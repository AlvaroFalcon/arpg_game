using Godot;

namespace PathToExile.Items;

/// <summary>
/// docs/design/items.md -> "Currency": authored data for one currency type. Gold
/// is instance 1 of N, built generically from the start — a second currency (a
/// future crafting currency, per the doc) is a new <c>.tres</c>, never a new class.
/// </summary>
[GlobalClass]
public partial class CurrencyDefinition : Resource
{
    /// <summary>
    /// Stable string identifier (e.g. "gold"). Kept as a plain string field —
    /// Godot's [Export] inspector has no serializer for a custom struct — and
    /// implicitly boxed into a typed <see cref="Items.CurrencyId"/> wherever code
    /// needs one (<see cref="Wallet"/>, <see cref="ItemDescriptor.CurrencyId"/>).
    /// </summary>
    [Export]
    public string Id = "";

    [Export]
    public string DisplayName = "";

    /// <summary>
    /// Default ground-label colour for this currency, read by the built-in
    /// filter's currency rule (<see cref="BuiltInDefaultRules"/>) — never read
    /// directly by label code. The design doc's prose spells this "Colour"
    /// (British spelling); this codebase follows Godot's own API and spells the
    /// type/field <see cref="Godot.Color"/>.
    /// </summary>
    [Export]
    public Color Color = Colors.White;

    /// <summary>
    /// Per-currency, not a global rule (docs/design/items.md -> "Currency": "a
    /// future crafting currency may want manual pickup or stash routing"). No
    /// manual-pickup path exists yet in milestone 1 — setting this false simply
    /// means the currency is never collected today; see
    /// <see cref="CurrencyDrop.OnPickup"/>.
    /// </summary>
    [Export]
    public bool AutoPickup = true;

    /// <summary>
    /// Format string applied to the drop amount to produce
    /// <see cref="ItemDescriptor.DisplayName"/>, e.g. <c>"{0} Gold"</c>.
    /// </summary>
    [Export]
    public string StackFormat = "{0}";
}
