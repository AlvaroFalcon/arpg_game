#nullable enable
using System.Collections.Generic;
using Godot;

namespace ArpgGame.Items;

/// <summary>
/// docs/design/items.md -> "Rarity colour is filter output, not label state": the
/// built-in default filter that assigns rarity colours, "so there is no special
/// case anywhere in label code." Currency gets a default rule too, for the same
/// reason.
///
/// Order matters. The currency rule is listed <i>first</i>: every descriptor,
/// including currency, carries a <see cref="Rarity"/> field that defaults to
/// <see cref="Rarity.Common"/> (see that enum's doc comment) — if the Common rule
/// were checked first, a gold pile would match "Common -> white" under first-
/// match-wins semantics before ever reaching its own rule.
/// </summary>
public static class BuiltInDefaultRules
{
    // PoE/D2-standard rarity palette (docs/design/items.md -> "Rarity" table).
    private static readonly Color CurrencyGold = new(1f, 0.84f, 0f);
    private static readonly Color MagicBlue = new(0.4f, 0.55f, 1f);
    private static readonly Color MajesticPink = new(1f, 0.45f, 0.75f);
    private static readonly Color SetOrange = new(1f, 0.55f, 0f);

    public static readonly IReadOnlyList<FilterRule> All = new[]
    {
        new FilterRule(d => d.CurrencyId is not null, new DisplayStyle { TextColor = CurrencyGold }),
        new FilterRule(d => d.Rarity == Rarity.Common, new DisplayStyle { TextColor = Colors.White }),
        new FilterRule(d => d.Rarity == Rarity.Magic, new DisplayStyle { TextColor = MagicBlue }),
        new FilterRule(d => d.Rarity == Rarity.Rare, new DisplayStyle { TextColor = Colors.Yellow }),
        new FilterRule(d => d.Rarity == Rarity.Majestic, new DisplayStyle { TextColor = MajesticPink }),
        new FilterRule(d => d.Rarity == Rarity.Set, new DisplayStyle { TextColor = SetOrange }),
    };
}
