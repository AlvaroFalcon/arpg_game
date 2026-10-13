#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace ArpgGame.Items;

/// <summary>
/// Builds the BBCode body for <see cref="UI.ItemTooltipControl"/> —
/// docs/design/items.md -&gt; "Item tooltips": name in its rarity colour, base
/// type, item level and base stats, then the rolled modifiers (tempers before
/// sigils). Tiers are hidden by default and shown, small and dim, while Alt is
/// held (docs/design/crafting.md -&gt; "Tooltip": the Path of Exile convention —
/// a clean read at a glance, the crafting detail on demand).
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
    public static string Build(EquipmentItem item, bool detailed = false)
    {
        DisplayStyle style = FilterChain.Default.Evaluate(item.Descriptor);
        Color nameColor = style.TextColor ?? Colors.White;

        var sb = new StringBuilder();
        sb.Append("[color=#").Append(nameColor.ToHtml(false)).Append("][font_size=17]")
            .Append(Escape(item.Descriptor.DisplayName)).Append("[/font_size][/color]");

        // The base type, unless the name already is the base (common items).
        if (item.Base is not null && item.Descriptor.DisplayName != item.Base.DisplayName)
        {
            sb.Append("\n[color=#").Append(BaseName).Append(']').Append(Escape(item.Base.DisplayName)).Append("[/color]");
        }

        sb.Append('\n').Append(Separator);
        sb.Append("\n[color=#").Append(Muted).Append("]Item Level ").Append(item.Descriptor.ItemLevel).Append("[/color]");

        foreach ((Stats.StatId stat, Stats.ModifierType type, float value, _) in item.RolledBaseStats)
        {
            sb.Append("\n[color=#").Append(BaseStat).Append(']').Append(value >= 0f ? "+" : "").Append(Number(value)).Append(Unit(type)).Append(' ')
                .Append(SpacedStatName(stat)).Append("[/color]");
        }

        if (item.RolledAffixes.Count > 0)
        {
            sb.Append('\n').Append(Separator);
            // Tempers (prefix-like) first, then sigils, each in rolled order.
            foreach (AffixRoller.RolledAffix affix in item.RolledAffixes.OrderBy(a => a.Definition.Pool))
            {
                sb.Append('\n').Append(FormatAffix(affix, detailed));
            }

            if (!detailed)
            {
                sb.Append("\n[font_size=12][color=#").Append(Hint).Append("]Hold Alt for modifier tiers[/color][/font_size]");
            }
        }

        return sb.ToString();
    }

    public const string Separator = "[color=#3a3450]────────────────[/color]";
    private const string BaseName = "b8b0c8";
    private const string BaseStat = "c8c8d0";
    private const string Explicit = "8fb0ff";
    private const string CraftedMod = "b8f0ff";
    private const string Detail = "6f6987";
    private const string Hint = "7a7390";

    private const string Better = "6ee06e";
    private const string Worse = "ff6a5a";
    private const string Muted = "9a94b0";

    /// <summary>
    /// "What changes if I equip this?" — docs/design/items.md -&gt; "Item
    /// tooltips". Sums every stat line of both items (base stats and affixes,
    /// per stat and modifier type) and lists the differences, green when the
    /// candidate adds, red when it takes away. <paramref name="equipped"/> null
    /// means the slot is empty, so everything is a gain. Equal lines are left
    /// out; identical items say so.
    /// </summary>
    public static string BuildComparison(EquipmentItem candidate, EquipmentItem? equipped)
    {
        Dictionary<(Stats.StatId, Stats.ModifierType), float> mine = Totals(candidate);
        Dictionary<(Stats.StatId, Stats.ModifierType), float> theirs = equipped is null ? new() : Totals(equipped);

        var sb = new StringBuilder();
        sb.Append("[color=#").Append(Muted).Append(']')
            .Append(equipped is null ? "Empty slot — equipping adds:" : $"Compared to {Escape(equipped.Descriptor.DisplayName)}:")
            .Append("[/color]");

        bool any = false;
        foreach ((Stats.StatId stat, Stats.ModifierType type) in mine.Keys.Union(theirs.Keys).OrderBy(k => k.Item1).ThenBy(k => k.Item2))
        {
            float delta = mine.GetValueOrDefault((stat, type)) - theirs.GetValueOrDefault((stat, type));
            if (Mathf.Abs(delta) < 0.005f)
            {
                continue;
            }

            any = true;
            sb.Append("\n[color=#").Append(delta > 0f ? Better : Worse).Append(']')
                .Append(delta > 0f ? "+" : "").Append(Number(delta)).Append(Unit(type)).Append(' ')
                .Append(SpacedStatName(stat)).Append("[/color]");
        }

        if (!any)
        {
            sb.Append("\n[color=#").Append(Muted).Append("]No stat changes[/color]");
        }

        return sb.ToString();
    }

    private static Dictionary<(Stats.StatId, Stats.ModifierType), float> Totals(EquipmentItem item)
    {
        var totals = new Dictionary<(Stats.StatId, Stats.ModifierType), float>();
        foreach ((Stats.StatId stat, Stats.ModifierType type, float value, _) in item.RolledBaseStats)
        {
            totals[(stat, type)] = totals.GetValueOrDefault((stat, type)) + value;
        }

        foreach (AffixRoller.RolledAffix affix in item.RolledAffixes)
        {
            var key = (affix.Definition.Stat, affix.Definition.Type);
            totals[key] = totals.GetValueOrDefault(key) + affix.Value;
        }

        return totals;
    }

    public static string Unit(Stats.ModifierType type) => type switch
    {
        Stats.ModifierType.Flat => "",
        Stats.ModifierType.Increased => "%",
        Stats.ModifierType.More => "% more",
        _ => "",
    };

    /// <summary>Whole numbers for anything of magnitude 1 or more (+17, not +17.5);
    /// small fractional values keep two decimals.</summary>
    public static string Number(float v) => Mathf.Abs(v) >= 1f ? Mathf.RoundToInt(v).ToString() : v.ToString("0.##");

    /// <summary>"+7 Fire Damage" — and with <paramref name="detailed"/> (Alt held),
    /// the tier, pool, name and roll range after it in small dim text.</summary>
    private static string FormatAffix(AffixRoller.RolledAffix affix, bool detailed)
    {
        AffixDefinition definition = affix.Definition;
        string sign = affix.Value >= 0f ? "+" : "";
        string unit = Unit(definition.Type);
        var sb = new StringBuilder();
        sb.Append("[color=#").Append(affix.Crafted ? CraftedMod : Explicit).Append(']')
            .Append(sign).Append(Number(affix.Value)).Append(unit).Append(' ').Append(SpacedStatName(definition.Stat))
            .Append("[/color]");
        if (detailed)
        {
            (float min, float max) = definition.ValueRangeForTier(affix.Tier);
            string range = Mathf.IsEqualApprox(min, max) ? Number(min) : $"{Number(min)}–{Number(max)}";
            sb.Append(" [font_size=11][color=#").Append(Detail).Append("](T").Append(affix.Tier).Append(' ')
                .Append(definition.Pool).Append(" \"").Append(Escape(definition.DisplayWord)).Append("\" · ")
                .Append(range).Append(unit)
                .Append(affix.Crafted ? " · crafted" : "").Append(")[/color][/font_size]");
        }
        else if (affix.Crafted)
        {
            sb.Append(" [font_size=11][color=#").Append(Detail).Append("](crafted)[/color][/font_size]");
        }

        return sb.ToString();
    }

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
