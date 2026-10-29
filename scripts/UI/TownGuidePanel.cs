#nullable enable
using System;
using System.Linq;
using Godot;
using ArpgGame.Items;
using ArpgGame.World;

namespace ArpgGame.UI;

/// <summary>
/// "Your hideout" — the town guide (docs/design/menus.md -> "Hideout HUD"). Opens by
/// itself on an account's first visit and from the ? key cap afterwards: a card per
/// station (what it's for and where it stands now) and the controls that matter here.
/// </summary>
public partial class TownGuidePanel : ModalPanel
{
    public HideoutData Data { get; set; } = null!;

    /// <summary>Where "the guide has been seen" is kept: the account's own town (a guest
    /// browses the host's town but the flag is theirs). Null: <see cref="Data"/>.</summary>
    public HideoutData? SeenOn { get; set; }

    /// <summary>Walks to a station when its card's "Show me" is pressed.</summary>
    public Action<string>? ShowMe { get; set; }

    protected override string Title => "Your hideout";

    protected override Vector2 FrameSize => new(1060f, 600f);

    private GridContainer _cards = null!;

    protected override void Build(VBoxContainer body)
    {
        Label intro = UiTheme.Label("Your town between realities. Walk up to a station and press F — or click a place in the Town list (left) to walk there.", 15, UiTheme.TextDim);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(intro);
        HBoxContainer columns = UiTheme.HBox(body, 20);
        _cards = new GridContainer { Columns = 2 };
        _cards.AddThemeConstantOverride("h_separation", 10);
        _cards.AddThemeConstantOverride("v_separation", 8);
        columns.AddChild(_cards);

        VBoxContainer keys = UiTheme.VBox(columns, 6);
        keys.CustomMinimumSize = new Vector2(250f, 0f);
        keys.AddChild(UiTheme.Label("Controls", 18, UiTheme.Gold));
        foreach ((string key, string what) in new[]
        {
            ("Click", "walk there"), ("F", "use the station you're next to"), ("I", "inventory · Crafting tab for orbs"),
            ("C · K · P", "character · skills · passive tree"), ("J", "quest log"), ("H", "edit your town"), ("1 · 2", "life and mana flasks"),
            ("Space · Shift", "dodge · sprint"), ("Tab", "town map (click a place to walk there)"), ("Esc", "menu (settings, Edit HUD)"),
        })
        {
            HBoxContainer row = UiTheme.HBox(keys, 8);
            PanelContainer capBox = UiTheme.KeyCap(key, 13, 100f);
            capBox.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(capBox);
            Label text = UiTheme.Label(what, 13, UiTheme.Text);
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.CustomMinimumSize = new Vector2(150f, 0f);
            row.AddChild(text);
        }

        HBoxContainer footer = UiTheme.HBox(body, 10);
        footer.Alignment = BoxContainer.AlignmentMode.End;
        footer.AddChild(UiTheme.Button("Let's go", Close, 16, 160f));
    }

    public override void Refresh()
    {
        ClearChildren(_cards);
        foreach (PropDef def in HideoutCatalog.Props.Where(p => p.Station))
        {
            var card = new PanelContainer { CustomMinimumSize = new Vector2(370f, 0f) };
            var style = new StyleBoxFlat { BgColor = new Color(0.08f, 0.065f, 0.12f), BorderColor = def.Accent };
            style.BorderWidthLeft = 3;
            style.SetCornerRadiusAll(4);
            style.SetContentMarginAll(7f);
            card.AddThemeStyleboxOverride("panel", style);
            HBoxContainer row = UiTheme.HBox(card, 8);
            // Altar and rift are drawn in code; an orb stands in for their picture.
            Texture2D? picture = def.Sprite.Length > 0 ? ItemArt.ForProp(def.Sprite)
                : ItemArt.ForCurrency(def.Id == "altar" ? "ascendant_orb" : "tempest_orb");
            row.AddChild(UiTheme.PixelIcon(picture, 40f));
            VBoxContainer text = UiTheme.VBox(row, 0);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            text.AddChild(UiTheme.Label(def.Name, 15, def.Accent));
            Label blurb = UiTheme.Label(def.Blurb, 12, UiTheme.Text);
            blurb.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            blurb.CustomMinimumSize = new Vector2(230f, 0f);
            text.AddChild(blurb);
            string where = Data.Find(def.Id) is { } p ? HideoutTown.QuarterOf(p.Position) : "—";
            text.AddChild(UiTheme.Label($"In the {where.ToLowerInvariant()}", 11, UiTheme.TextDim));
            string id = def.Id;
            Button go = UiTheme.Button("Show me", () => { Close(); ShowMe?.Invoke(id); }, 12, 80f);
            go.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(go);
            _cards.AddChild(card);
        }
    }

    protected override void OnClosed()
    {
        (SeenOn ?? Data).GuideSeen = true;
    }
}
