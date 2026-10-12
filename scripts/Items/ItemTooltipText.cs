#nullable enable
using System.Linq;
using System.Text;
using Godot;

namespace PathToExile.Items;

/// <summary>
/// Builds the BBCode body for <see cref="UI.ItemTooltipControl"/> —
/// docs/design/items.md -&gt; "Item tooltips": "Hovering an item — in the grid,
/// in an equipment slot, or on the ground — shows its name in its rarity
/// colour, its base type, item level, and its rolled affixes with their tier
/// numbers." Tiers are shown deliberately (see that section's own rationale,
/// and docs/design/affixes.md's "Tiers are player-visible": PoE hides them and
/// its playerbase installed third-party tools to read them anyway).
///
/// A static text builder, not a <see cref="Node"/> — same "pure function over
/// an item's identity" shape <see cref="AffixRoller"/> and <see cref="ItemNaming"/>
/// already use, and for the same reason: it is directly unit-testable without a
/// scene, and <see cref="UI.ItemTooltipControl"/> stays a dumb renderer that
/// never has an opinion about rarity, tiers, or formatting.
///
/// <b>Rarity colour comes from <see cref="FilterChain"/>, never from reading
/// <see cref="Rarity"/> directly</b> — the same structural rule
/// <see cref="EquipmentItem.RegisterLabel"/> already follows for ground labels
/// (docs/design/items.md -&gt; "Rarity colour is filter output, not label
/// state"). A tooltip is exactly as much "label code" as the floating ground
/// label is, so it must not become the one place that rule gets a silent
/// exception.
/// </summary>
public static class ItemTooltipText
{
    /// <summary>
    /// The full BBCode string for <paramref name="item"/>'s tooltip, name line
    /// coloured via <c>[color=#rrggbb]</c>. Assumes <see cref="EquipmentItem.Initialize"/>
    /// has already run — every caller (grid/strip hover, ground hover) only
    /// reaches an <see cref="EquipmentItem"/> that is already sitting on the
    /// ground or in the grid, both of which require initialization first.
    /// </summary>
    public static string Build(EquipmentItem item)
    {
        DisplayStyle style = FilterChain.Default.Evaluate(item.Descriptor);
        Color nameColor = style.TextColor ?? Colors.White;

        var sb = new StringBuilder();
        sb.Append("[color=#").Append(nameColor.ToHtml(false)).Append(']')
            .Append(Escape(item.Descriptor.DisplayName)).Append("[/color]\n");

        if (item.Base is not null)
        {
            sb.Append(Escape(item.Base.DisplayName)).Append('\n');
        }

        sb.Append("Item Level ").Append(item.Descriptor.ItemLevel);

        foreach ((Stats.StatId stat, Stats.ModifierType type, float value, _) in item.RolledBaseStats)
        {
            string unit = type == Stats.ModifierType.Flat ? "" : "%";
            sb.Append("\n[color=#c8c8d0]").Append(value >= 0f ? "+" : "").Append(Number(value)).Append(unit).Append(' ')
                .Append(SpacedStatName(stat)).Append("[/color]");
        }

        foreach (AffixRoller.RolledAffix affix in item.RolledAffixes.OrderByDescending(a => a.Tier))
        {
            sb.Append('\n').Append(FormatAffix(affix));
        }

        return sb.ToString();
    }

    /// <summary>Whole numbers for anything of magnitude 1 or more (+17, not +17.5);
    /// small fractional values keep two decimals.</summary>
    public static string Number(float v) => Mathf.Abs(v) >= 1f ? Mathf.RoundToInt(v).ToString() : v.ToString("0.##");

    private static string FormatAffix(AffixRoller.RolledAffix affix)
    {
        AffixDefinition definition = affix.Definition;
        string sign = affix.Value >= 0f ? "+" : "";
        string unit = definition.Type switch
        {
            Stats.ModifierType.Flat => "",
            Stats.ModifierType.Increased => "%",
            Stats.ModifierType.More => "% more",
            _ => "",
        };

        return $"T{affix.Tier} {Escape(definition.DisplayWord)} ({sign}{Number(affix.Value)}{unit} {SpacedStatName(definition.Stat)})";
    }

    /// <summary>"FireResistance" -&gt; "Fire Resistance". <see cref="Stats.StatId"/>
    /// has no author-facing display name of its own yet (docs/design/stats.md
    /// doesn't specify one) — this is a minimal, mechanical readability pass
    /// rather than a real localisation/display-name system, which is out of
    /// scope for this milestone.</summary>
    private static string SpacedStatName(Stats.StatId stat)
    {
        string raw = stat.ToString();
        var sb = new StringBuilder(raw.Length + 4);
        for (int i = 0; i < raw.Length; i++)
        {
            if (i > 0 && char.IsUpper(raw[i]))
            {
                sb.Append(' ');
            }

            sb.Append(raw[i]);
        }

        return sb.ToString();
    }

    /// <summary>Every string this method touches is authored data (base names,
    /// affix words, generated Rare-name word pools), never player input, so this
    /// only has to guard against BBCode's own metacharacter rather than any kind
    /// of injection attack — a future authored string containing a literal
    /// <c>[</c> would otherwise be parsed as a tag by
    /// <see cref="Godot.RichTextLabel"/>.</summary>
    private static string Escape(string text) => text.Replace("[", "[lb]");
}
