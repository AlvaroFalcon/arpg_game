#nullable enable
using Godot;
using PathToExile.Items;
using PathToExile.UI;

namespace PathToExile.Tools;

/// <summary>
/// Drives the inventory UI in a real (windowed) run and saves a devlog screenshot.
/// Same rationale as <see cref="SkillCapture"/> and <see cref="DropCapture"/>, plus
/// one specific to this milestone: every InventorySmokeTest assertion exercises the
/// <see cref="Inventory"/> *model*, not the Control nodes. A grid that computes
/// placements perfectly and draws nothing would pass all 34 of them. This is the
/// check that the UI actually renders.
///
/// Run with:
///   ./scripts/capture_inventory_screenshot.sh
/// Must run windowed: --headless uses the dummy renderer and captures an empty image.
/// </summary>
public partial class InventoryCapture : Node2D
{
    /// <summary>The EquipmentDrop scene. EquipmentItem is an Area2D, so it has to
    /// be instantiated from its scene and added to the tree before Initialize —
    /// `new EquipmentItem()` produces a detached node and throws. Same path
    /// EquipmentDropSpawner uses.</summary>
    /// <summary>Centre of the top-left grid cell, where the first item lands.</summary>
    private static readonly Vector2 HoverPoint = new(345f, 180f);

    [Export]
    public PackedScene DropScene = null!;

    [Export]
    public PackedScene CurrencyDropScene = null!;

    [Export]
    public CurrencyDefinition GoldCurrency = null!;

    private Player _player = null!;
    private AffixLibrary _library = null!;
    private RandomNumberGenerator _rng = null!;
    private Label _caption = null!;
    private int _phase;
    private int _frame;
    private bool _busy;

    public override void _Ready()
    {
        _player = GetNode<Player>("Player");
        _player.GetNode<Camera2D>("Camera2D").Enabled = false;
        _player.GetNode<CanvasLayer>("DebugReadout").Visible = false;

        var inventory = _player.GetNode<Inventory>("Inventory");
        var library = GD.Load<AffixLibrary>("res://resources/items/AffixLibrary.tres");

        // A spread of rarities and footprints, so the shot shows rarity colouring,
        // multi-cell items and the reading-order packing all at once rather than a
        // tidy row of identical squares.
        (string Base, Rarity Rarity, int Ilvl)[] loadout =
        {
            ("ringmail_vest", Rarity.Rare, 120),
            ("iron_wand", Rarity.Magic, 60),
            ("leather_cap", Rarity.Rare, 90),
            ("oak_buckler", Rarity.Magic, 45),
            ("rawhide_gloves", Rarity.Common, 10),
            ("rawhide_boots", Rarity.Rare, 110),
            ("copper_ring", Rarity.Magic, 70),
            ("copper_amulet", Rarity.Rare, 100),
            ("rope_belt", Rarity.Common, 5),
        };

        _library = library;
        _rng = new RandomNumberGenerator();
        _rng.Randomize();
        RandomNumberGenerator rng = _rng;

        _caption = new Label { Position = new Vector2(40f, 40f) };
        _caption.AddThemeFontSizeOverride("font_size", 20);
        _caption.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.95f));
        AddChild(_caption);

        foreach ((string baseId, Rarity rarity, int ilvl) in loadout)
        {
            var definition = GD.Load<BaseItemDefinition>($"res://resources/items/bases/{baseId}.tres");
            var item = DropScene.Instantiate<EquipmentItem>();
            AddChild(item);
            item.Initialize(definition, rarity, ilvl, library.Entries, rng);

            // TryAdd, then detach — exactly what EquipmentItem.OnPickup does. The
            // inventory's dictionary becomes the only reference keeping the node
            // alive, so this must not QueueFree.
            if (inventory.TryAdd(item))
            {
                item.GetParent()?.RemoveChild(item);
            }
        }

        _player.GetNode<CanvasLayer>("InventoryUI").GetNode<Control>("Panel").Visible = true;

        _caption.Text = "Hover an item: name in its rarity colour, base, item level, affixes with tiers";
        _phase = 1;

        // Drive the tooltip directly rather than faking a mouse position: the point
        // of the shot is what the tooltip renders, and warping the cursor makes the
        // capture depend on where the window happens to sit on screen.
        EquipmentItem? rare = null;
        foreach (EquipmentItem candidate in inventory.Placements.Keys)
        {
            if (candidate.Rarity == Rarity.Rare)
            {
                rare = candidate;
                break;
            }
        }

        // Park the cursor over the first item's cell and let the real hover path
        // run. An earlier version called tooltip.Show() directly and it never
        // appeared: InventoryGridControl polls hover every frame and hides the
        // tooltip whenever the cursor is not over an item, so a direct Show was
        // being undone on the very next frame. Driving the actual cursor means the
        // screenshot shows the feature working rather than a tooltip propped up by
        // the capture tool.
        Input.WarpMouse(HoverPoint);
    }

    /// <summary>
    /// Phase 2: the manual-pickup rule from docs/design/items.md. The player walks
    /// straight through a gold pile and two pieces of equipment; the gold is
    /// collected on contact and the equipment is not.
    /// </summary>
    private void StartGroundPhase()
    {
        _phase = 2;
        _frame = 0;

        _player.GetNode<CanvasLayer>("InventoryUI").GetNode<Control>("Panel").Visible = false;
        GetNode<ItemTooltipControl>("ItemTooltip").Hide();
        _player.GlobalPosition = new Vector2(200f, 350f);

        var gold = CurrencyDropScene.Instantiate<CurrencyDrop>();
        AddChild(gold);
        gold.GlobalPosition = new Vector2(480f, 350f);
        gold.Initialize(GoldCurrency, 240);

        foreach ((float x, Rarity rarity, string baseId) in new (float, Rarity, string)[]
                 {
                     (700f, Rarity.Rare, "leather_cap"),
                     (900f, Rarity.Magic, "iron_wand"),
                 })
        {
            var definition = GD.Load<BaseItemDefinition>($"res://resources/items/bases/{baseId}.tres");
            var drop = DropScene.Instantiate<EquipmentItem>();
            AddChild(drop);
            drop.GlobalPosition = new Vector2(x, 350f);
            drop.Initialize(definition, rarity, 100, _library.Entries, _rng);
        }
    }

    public override void _Process(double delta)
    {
        if (_busy)
        {
            return;
        }

        _frame++;

        if (_phase == 1)
        {
            Input.WarpMouse(HoverPoint);
            if (_frame > 20)
            {
                Capture("res://docs/devlog/screenshots/inventory-tooltip.png", StartGroundPhase);
            }

            return;
        }

        // Walk right, straight over everything on the ground.
        _player.GlobalPosition = new Vector2(
            Mathf.Min(200f + (_frame * 9f), 1040f), 350f);

        _caption.Text =
            "Walked over all three. Gold auto-collected; equipment was not.\n" +
            $"Wallet: {_player.Wallet.GetBalance("gold")} Gold   -   equipment still on the ground, waiting to be clicked.";

        if (_frame > 110)
        {
            Capture("res://docs/devlog/screenshots/pickup-manual.png", () => GetTree().Quit());
        }
    }

    private async void Capture(string resPath, System.Action onDone)
    {
        _busy = true;

        // Let a frame finish drawing, otherwise the viewport texture is empty.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        Image image = GetViewport().GetTexture().GetImage();
        string absolutePath = ProjectSettings.GlobalizePath(resPath);
        Error err = image.SavePng(absolutePath);
        GD.Print(err == Error.Ok
            ? $"[CAPTURE] Saved {absolutePath}"
            : $"[CAPTURE] FAILED to save {absolutePath}: {err}");

        _busy = false;
        onDone();
    }
}
