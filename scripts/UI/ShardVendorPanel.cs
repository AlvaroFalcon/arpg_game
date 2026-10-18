#nullable enable
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Items;
using ArpgGame.World;

namespace ArpgGame.UI;

/// <summary>
/// Arehucaz's cloth of wares — docs/design/endgame.md -> "Arehucaz". Left: the
/// dealer, his stock (five shard marks, lit while unsold) and when it comes back;
/// right: one row per tier the character has cleared, with the price and how many
/// they own. Purchases go straight to the character's shards.
/// </summary>
public partial class ShardVendorPanel : ModalPanel
{
    public Player Player { get; set; } = null!;

    /// <summary>The character to deal with; defaults to the profile's active one (tests set it).</summary>
    public CharacterData? Character { get; set; }

    protected override string Title => "Arehucaz";

    protected override Vector2 FrameSize => new(900f, 560f);

    private static readonly Color Violet = new(0.68f, 0.45f, 1f);

    private HBoxContainer _stock = null!;
    private Label _restockInfo = null!;
    private Button _restock = null!;
    private VBoxContainer _rows = null!;
    private Label _gold = null!;
    private Label _message = null!;

    private CharacterData? Dealer => Character ?? Profile.Get(this)?.Active;

    protected override void Build(VBoxContainer body)
    {
        TitleLabel.AddThemeColorOverride("font_color", Violet);
        Label quote = UiTheme.Label("\"Shards... for those who have already walked through. Only those. Never more than I have.\"", 15, UiTheme.TextDim);
        quote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(quote);

        HBoxContainer columns = UiTheme.HBox(body, 24);
        VBoxContainer left = UiTheme.VBox(columns, 10);
        left.CustomMinimumSize = new Vector2(280f, 0f);
        var portraitBox = new PanelContainer();
        portraitBox.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.05f, 0.03f, 0.08f), Violet with { A = 0.6f }, 2, 6, 8));
        TextureRect portrait = UiTheme.PixelIcon(ItemArt.ForProp("props/arehucaz"), 176f);
        portrait.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        portraitBox.AddChild(portrait);
        left.AddChild(portraitBox);
        left.AddChild(UiTheme.Label("On the cloth", 16, Violet));
        _stock = UiTheme.HBox(left, 6);
        _restockInfo = UiTheme.Label("", 13, UiTheme.TextDim);
        _restockInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _restockInfo.CustomMinimumSize = new Vector2(270f, 0f);
        left.AddChild(_restockInfo);
        _restock = UiTheme.Button($"Restock now ({ShardVendor.RestockGold:N0} gold)", Restock, 15, 270f);
        left.AddChild(_restock);
        _gold = UiTheme.Label("", 16, UiTheme.Gold);
        left.AddChild(_gold);

        VBoxContainer right = UiTheme.VBox(columns, 6);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.AddChild(UiTheme.Label("Mirror Shards", 18, UiTheme.Gold));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0f, 330f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        right.AddChild(scroll);
        _rows = UiTheme.VBox(scroll, 4);
        _rows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _message = UiTheme.Label("", 15, UiTheme.Text);
        right.AddChild(_message);
    }

    public override void Refresh()
    {
        CharacterData? c = Dealer;
        if (c is null)
        {
            return;
        }

        if (ShardVendor.Update(c))
        {
            _message.Text = "New shards glint on the cloth.";
        }

        ClearChildren(_stock);
        for (int i = 0; i < ShardVendor.StockSize; i++)
        {
            TextureRect mark = UiTheme.PixelIcon(ItemArt.ForShard(10), 30f);
            mark.Modulate = i < c.ShardVendorStock ? Colors.White : new Color(0.25f, 0.2f, 0.3f, 0.6f);
            _stock.AddChild(mark);
        }

        int until = ShardVendor.ClearsUntilRestock(c);
        _restockInfo.Text = c.ShardVendorStock >= ShardVendor.StockSize
            ? $"{c.ShardVendorStock} of {ShardVendor.StockSize} left. He restocks after you clear {ShardVendor.RestockClears} realities once his cloth is bare."
            : $"{c.ShardVendorStock} of {ShardVendor.StockSize} left. Restocks after {until} more cleared {(until == 1 ? "reality" : "realities")} — or now, for a price.";
        long gold = Player.Wallet.GetBalance(Profile.GoldId);
        _restock.Disabled = c.ShardVendorStock >= ShardVendor.StockSize || gold < ShardVendor.RestockGold;
        _gold.Text = $"Gold: {gold:N0}";

        ClearChildren(_rows);
        int max = ShardVendor.MaxTier(c);
        if (max < 2)
        {
            Label none = UiTheme.Label("\"Come back when you have cleared a reality beyond the first tier.\"\nTier 1 is always free at the Mirror.", 15, UiTheme.TextDim);
            none.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _rows.AddChild(none);
            return;
        }

        for (int tier = max; tier >= 2; tier--)
        {
            _rows.AddChild(TierRow(c, tier));
        }
    }

    private Control TierRow(CharacterData c, int tier)
    {
        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.07f, 0.05f, 0.1f), RealityTiers.ShardColor(tier) with { A = 0.35f }, 1, 4, 6));
        HBoxContainer line = UiTheme.HBox(row, 10);
        line.AddChild(UiTheme.PixelIcon(ItemArt.ForShard(tier), 32f));
        VBoxContainer text = UiTheme.VBox(line, 0);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        text.AddChild(UiTheme.Label($"Tier {tier} Mirror Shard", 16, RealityTiers.ShardColor(tier)));
        text.AddChild(UiTheme.Label($"You own {c.ShardCount(tier)}  ·  monster level {RealityTiers.MonsterLevel(tier)}", 12, UiTheme.TextDim));
        Label price = UiTheme.Label($"{ShardVendor.Price(tier):N0} gold", 15, UiTheme.Gold);
        price.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        line.AddChild(price);
        int t = tier;
        Button buy = UiTheme.Button("Buy", () => Buy(t), 14, 80f);
        buy.Disabled = ShardVendor.CannotBuy(c, Player.Wallet, tier) is not null;
        string? why = ShardVendor.CannotBuy(c, Player.Wallet, tier);
        UiTheme.InstantTooltip(buy, () => why is null ? "" : "Can't buy yet\n" + why);
        line.AddChild(buy);
        return row;
    }

    public bool Buy(int tier)
    {
        if (Dealer is not { } c)
        {
            return false;
        }

        bool ok = ShardVendor.TryBuy(c, Player.Wallet, tier, out string message);
        _message.Text = message;
        if (ok)
        {
            Sfx.Play(this, "pickup");
            Save();
        }

        Refresh();
        return ok;
    }

    private void Restock()
    {
        if (Dealer is not { } c)
        {
            return;
        }

        bool ok = ShardVendor.TryRestock(c, Player.Wallet, out string message);
        _message.Text = message;
        if (ok)
        {
            Sfx.Play(this, "levelup", -6f);
            Save();
        }

        Refresh();
    }

    /// <summary>Gold lives in the wallet; capture it into the character and save.</summary>
    private void Save() => Profile.Get(this)?.CaptureFrom(Player);
}
