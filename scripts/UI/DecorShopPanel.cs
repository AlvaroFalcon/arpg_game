#nullable enable
using System.Linq;
using Godot;
using ArpgGame.Core;
using ArpgGame.World;

namespace ArpgGame.UI;

/// <summary>
/// Ysolde's Furnishings, in the hideout plaza — docs/design/hideout.md -> "Shop".
/// Sells floor tiles in packs and decorations for gold. What you buy goes into the
/// hideout's storage (shared by every character); you lay it out in edit mode (H).
/// </summary>
public partial class DecorShopPanel : ModalPanel
{
    public Player Player { get; set; } = null!;
    public HideoutData Data { get; set; } = null!;

    /// <summary>Set when shopping in a friend's town: whose town this is (purchases still go to your own storage).</summary>
    public string? HostName { get; set; }

    protected override string Title => "Ysolde's Furnishings";

    protected override Vector2 FrameSize => new(1000f, 600f);

    private VBoxContainer _floors = null!;
    private VBoxContainer _decor = null!;
    private Label _gold = null!;

    protected override void Build(VBoxContainer body)
    {
        Label intro = UiTheme.Label(HostName is { } host
            ? $"You're in {host}'s town: everything you buy is delivered to your own town's storage, for your whole account."
            : "Everything you buy is stored for your whole account. Press H in the hideout to pave floors and place decorations.", 14, UiTheme.TextDim);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(intro);
        HBoxContainer columns = UiTheme.HBox(body, 24);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _floors = Column(columns, $"Floor tiles (packs of {HideoutCatalog.FloorPack})");
        _decor = Column(columns, "Decorations");
        _gold = UiTheme.Label("", 17, UiTheme.Gold);
        body.AddChild(_gold);
    }

    private static VBoxContainer Column(HBoxContainer parent, string title)
    {
        VBoxContainer column = UiTheme.VBox(parent, 6);
        column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        column.AddChild(UiTheme.Label(title, 18, UiTheme.Gold));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(460f, 430f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(scroll);
        VBoxContainer list = UiTheme.VBox(scroll, 4);
        list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return list;
    }

    public override void Refresh()
    {
        long gold = Player.Wallet.GetBalance(Profile.GoldId);
        ClearChildren(_floors);
        foreach (FloorStyle style in HideoutCatalog.FloorsForSale)
        {
            FloorStyle s = style;
            HBoxContainer row = Row(_floors);
            row.AddChild(new ColorRect { Color = style.Swatch, CustomMinimumSize = new Vector2(26f, 26f) });
            row.AddChild(Text($"{style.Name}", $"{Data.StockOf(style.Id)} in storage · {Data.Floors.Values.Count(v => v == style.Id)} laid"));
            Button buy = UiTheme.Button($"Buy · {style.PackPrice}g", () => BuyFloor(s), 14, 120f);
            buy.Disabled = gold < style.PackPrice;
            row.AddChild(buy);
        }

        ClearChildren(_decor);
        foreach (PropDef def in HideoutCatalog.Decorations.Where(d => d.Price > 0))
        {
            PropDef d = def;
            HBoxContainer row = Row(_decor);
            row.AddChild(UiTheme.PixelIcon(Items.ItemArt.ForProp(def.Sprite), 32f));
            row.AddChild(Text(def.Name, $"{Data.StockOf(def.Id)} in storage · {Data.Props.Count(p => p.Kind == def.Id)} placed"));
            Button buy = UiTheme.Button($"Buy · {def.Price}g", () => BuyDecor(d), 14, 120f);
            buy.Disabled = gold < def.Price;
            row.AddChild(buy);
        }

        _gold.Text = $"Gold: {gold}";
    }

    private static HBoxContainer Row(VBoxContainer list)
    {
        HBoxContainer row = UiTheme.HBox(list, 10);
        row.CustomMinimumSize = new Vector2(440f, 34f);
        return row;
    }

    private static VBoxContainer Text(string name, string detail)
    {
        var text = new VBoxContainer();
        text.AddThemeConstantOverride("separation", 0);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        text.AddChild(UiTheme.Label(name, 15, UiTheme.Text));
        text.AddChild(UiTheme.Label(detail, 12, UiTheme.TextDim));
        return text;
    }

    public bool BuyFloor(FloorStyle style)
    {
        if (style.PackPrice <= 0 || !Player.Wallet.TrySpend(Profile.GoldId, style.PackPrice))
        {
            return false;
        }

        Data.AddStock(style.Id, HideoutCatalog.FloorPack);
        Sfx.Play(this, "pickup", -4f);
        Refresh();
        return true;
    }

    public bool BuyDecor(PropDef def)
    {
        if (def.Station || def.Price <= 0 || !Player.Wallet.TrySpend(Profile.GoldId, def.Price))
        {
            return false;
        }

        Data.AddStock(def.Id, 1);
        Sfx.Play(this, "pickup", -4f);
        Refresh();
        return true;
    }

    protected override void OnClosed()
    {
        if (Profile.Get(this) is { } profile)
        {
            profile.Hideout = Data;
            profile.CaptureFrom(Player);
        }
    }
}
