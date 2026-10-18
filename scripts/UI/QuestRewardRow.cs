#nullable enable
using Godot;
using ArpgGame.Items;
using ArpgGame.Quests;

namespace ArpgGame.UI;

/// <summary>Rewards as icons — the gold coin, orbs, dust, a shard, the Mirror Jewel — in a row,
/// for the quest dialogue and the quest log (docs/design/quests.md).</summary>
public static partial class QuestRewardRow
{
    /// <summary>"1,000" or "×2" next to its icon; the tooltip names it.</summary>
    public static HBoxContainer Build(Node parent, QuestDef quest, QuestContext? ctx, float iconSize, int textSize, Color? textColor = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        parent.AddChild(row);
        foreach (QuestReward reward in quest.Rewards)
        {
            var item = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Stop };
            item.AddThemeConstantOverride("separation", 5);
            Control icon = IconOf(reward, ctx, iconSize);
            item.AddChild(icon);
            string text = reward.Kind == RewardKind.Gold ? $"{reward.Amount:N0}" : $"×{reward.Amount}";
            Label label = UiTheme.Label(text, textSize, textColor ?? UiTheme.Text);
            label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            item.AddChild(label);
            row.AddChild(item);
            QuestReward r = reward;
            UiTheme.InstantTooltip(item, () => QuestBook.Describe(r, ctx));
        }

        return row;
    }

    private static Control IconOf(QuestReward reward, QuestContext? ctx, float size)
    {
        switch (reward.Kind)
        {
            case RewardKind.Gold:
                return UiTheme.PixelIcon(ItemArt.ForCurrency("gold"), size);
            case RewardKind.Currency:
                return UiTheme.PixelIcon(ItemArt.ForCurrency(reward.Id), size);
            case RewardKind.Shard:
                int tier = reward.Id == "best" || !int.TryParse(reward.Id, out int fixedTier)
                    ? System.Math.Max(2, ctx?.Character.HighestTierCleared ?? 2)
                    : fixedTier;
                return UiTheme.PixelIcon(ItemArt.ForShard(tier), size);
            case RewardKind.Jewel:
                return new JewelIcon { CustomMinimumSize = new Vector2(size, size), MouseFilter = Control.MouseFilterEnum.Ignore };
            default:
                return UiTheme.PixelIcon(null, size);
        }
    }

    /// <summary>The Mirror Jewel as drawn on the ground and in the jewel tree.</summary>
    private sealed partial class JewelIcon : Control
    {
        public override void _Draw() => Run.Pickup.DrawJewel(this, Size / 2f, Mathf.Min(Size.X, Size.Y) / 20f, 0.8f);
    }
}
