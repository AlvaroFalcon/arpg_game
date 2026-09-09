#nullable enable
using Godot;
using PathToExile.Items;

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
    [Export]
    public PackedScene DropScene = null!;

    private Player _player = null!;
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

        var rng = new RandomNumberGenerator();
        rng.Randomize();

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
    }

    public override void _Process(double delta)
    {
        if (_busy)
        {
            return;
        }

        _frame++;
        if (_frame > 20)
        {
            Capture("res://docs/devlog/screenshots/inventory.png");
        }
    }

    private async void Capture(string resPath)
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

        GetTree().Quit();
    }
}
