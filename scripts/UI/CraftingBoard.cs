#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Core;
using ArpgGame.Items;

namespace ArpgGame.UI;

/// <summary>
/// The Crafting tab of the inventory (I) — docs/design/crafting.md -> "Using orbs".
/// Lists the account's orbs and dust; click an orb to pick it up, then left-click an
/// item in the grid below to use it (Shift keeps it in hand, right-click or Esc puts
/// it away). The Artificer's Seal works the same way: pick it up, click an item,
/// then choose which modifier to protect from the next orb.
/// </summary>
public partial class CraftingBoard : Control
{
    public const float WidthPx = 384f;
    public const float HeightPx = 300f;

    public Player Player { get; set; } = null!;
    public InventoryGridControl Grid { get; set; } = null!;

    public CraftingCurrency? Armed { get; private set; }
    public bool SealArmed { get; private set; }
    public bool Holding => Armed is not null || SealArmed;
    public string LastMessage => _message.Text;

    private readonly RandomNumberGenerator _rng = new();
    private readonly List<(CraftingCurrency Orb, Button Button)> _orbButtons = new();
    private Button _sealButton = null!;
    private Label _dust = null!;
    private Label _message = null!;
    private Label _cursor = null!;
    private PanelContainer _chooser = null!;
    private VBoxContainer _chooserBody = null!;
    private bool _active;

    public override void _Ready()
    {
        _rng.Randomize();
        CustomMinimumSize = new Vector2(WidthPx, HeightPx);
        Size = CustomMinimumSize;
        VBoxContainer column = UiTheme.VBox(this, 6);
        column.Size = CustomMinimumSize;
        column.AddChild(UiTheme.Label("Orbs — shared by all your characters", 15, UiTheme.Gold));

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        column.AddChild(grid);
        foreach (CraftingCurrency orb in Crafting.Orbs)
        {
            CraftingCurrency picked = orb;
            Button b = UiTheme.Button("", () => Arm(picked), 12, 125f);
            b.CustomMinimumSize = new Vector2(125f, 40f);
            b.AddThemeColorOverride("font_color", orb.Color);
            b.AddThemeColorOverride("font_hover_color", orb.Color.Lightened(0.3f));
            b.AddThemeColorOverride("font_disabled_color", orb.Color with { A = 0.35f });
            b.TooltipText = $"{orb.Name}\n{orb.Effect}";
            grid.AddChild(b);
            _orbButtons.Add((orb, b));
        }

        _sealButton = UiTheme.Button("Artificer's Seal — protect a modifier from the next orb", ArmSeal, 12, WidthPx);
        _sealButton.CustomMinimumSize = new Vector2(WidthPx, 30f);
        _sealButton.AddThemeColorOverride("font_color", UiTheme.Gold);
        column.AddChild(_sealButton);
        _dust = UiTheme.Label("", 13, UiTheme.TextDim);
        column.AddChild(_dust);
        _message = UiTheme.Label("Click an orb, then click an item. Shift keeps it in hand.", 13, UiTheme.TextDim);
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _message.CustomMinimumSize = new Vector2(WidthPx, 0f);
        column.AddChild(_message);

        _chooser = new PanelContainer { Visible = false, Position = new Vector2(0f, 26f), CustomMinimumSize = new Vector2(WidthPx, 0f) };
        _chooser.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.06f, 0.05f, 0.09f, 0.98f), UiTheme.Gold, 2, 4, 8));
        AddChild(_chooser);
        _chooserBody = UiTheme.VBox(_chooser, 4);

        _cursor = UiTheme.Label("", 14, Colors.White);
        _cursor.MouseFilter = MouseFilterEnum.Ignore;
        _cursor.TopLevel = true;
        _cursor.ZIndex = 50;
        AddChild(_cursor);
        Refresh();
    }

    /// <summary>Takes over the grid's clicks while the tab is shown.</summary>
    public void Activate()
    {
        _active = true;
        Visible = true;
        Grid.LeftClickOverride = OnLeftClick;
        Grid.RightClickOverride = _ => { Disarm(); return true; };
        Grid.RightClickEmpty = Disarm;
        Grid.Tint = TintFor;
        ItemTooltipControl.Footer = FooterFor;
        Refresh();
    }

    public void Deactivate()
    {
        _active = false;
        Disarm();
        Visible = false;
        Grid.LeftClickOverride = null;
        Grid.RightClickOverride = null;
        Grid.RightClickEmpty = null;
        Grid.Tint = null;
        if (ItemTooltipControl.Footer == FooterFor)
        {
            ItemTooltipControl.Footer = null;
        }
    }

    public void Refresh()
    {
        // The player's wallet only exists once the player itself is ready.
        if (_dust is null || Player?.Wallet is null)
        {
            return;
        }

        foreach ((CraftingCurrency orb, Button b) in _orbButtons)
        {
            long n = Player.Wallet.GetBalance(orb.Id);
            b.Text = $"{orb.Name}\n×{n}";
            b.Disabled = n <= 0;
            bool held = Armed == orb;
            b.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.1f, 0.085f, 0.14f), held ? orb.Color : UiTheme.PanelBorder, held ? 3 : 1, 4, 4));
        }

        _sealButton.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.1f, 0.085f, 0.14f), SealArmed ? UiTheme.Gold : UiTheme.PanelBorder, SealArmed ? 3 : 1, 4, 4));
        _dust.Text = "Dust  " + string.Join("   ", Crafting.Dusts.Select(d => $"{d.Name.Split(' ')[0]} {Player.Wallet.GetBalance(d.Id)}"))
            + $"   ·   Gold {Player.Wallet.GetBalance(Profile.GoldId)}";
    }

    public void Arm(CraftingCurrency orb)
    {
        if (Player.Wallet.GetBalance(orb.Id) <= 0)
        {
            return;
        }

        CloseChooser();
        Armed = orb;
        SealArmed = false;
        Say($"{orb.Name} in hand — {orb.Effect}", orb.Color);
        Refresh();
    }

    public void ArmSeal()
    {
        CloseChooser();
        Armed = null;
        SealArmed = true;
        Say("Seal in hand — click an item, then choose the modifier to protect. Each seal on the same item costs more.", UiTheme.Gold);
        Refresh();
    }

    public void Disarm()
    {
        if (!Holding && !_chooser.Visible)
        {
            return;
        }

        Armed = null;
        SealArmed = false;
        CloseChooser();
        Say("", UiTheme.TextDim);
        Refresh();
    }

    private bool OnLeftClick(EquipmentItem item)
    {
        if (SealArmed)
        {
            OpenChooser(item);
            return true;
        }

        return ApplyArmed(item);
    }

    /// <summary>Uses the orb in hand on <paramref name="item"/> (a left-click).</summary>
    public bool ApplyArmed(EquipmentItem item)
    {
        if (Armed is not { } orb)
        {
            return false;
        }

        if (Crafting.CannotApply(orb, item) is { } reason)
        {
            Say(reason, new Color(1f, 0.45f, 0.35f));
            return true;
        }

        if (!Player.Wallet.TrySpend(orb.Id, 1) || !Crafting.Apply(orb, item, _rng, out string message))
        {
            return true;
        }

        Sfx.Play(this, "levelup", -10f);
        Say(message, orb.Color);
        if (!Input.IsKeyPressed(Key.Shift) || Player.Wallet.GetBalance(orb.Id) <= 0)
        {
            Armed = null;
        }

        Refresh();
        return true;
    }

    private void OpenChooser(EquipmentItem item)
    {
        foreach (Node child in _chooserBody.GetChildren())
        {
            child.QueueFree();
        }

        if (Crafting.CannotSeal(item) is { } reason)
        {
            Say(reason, new Color(1f, 0.45f, 0.35f));
            return;
        }

        (long gold, CraftingCurrency dust, int amount) = Crafting.SealCost(item);
        _chooserBody.AddChild(UiTheme.Label($"Seal which modifier of {item.Descriptor.DisplayName}?", 14, UiTheme.Gold));
        _chooserBody.AddChild(UiTheme.Label($"Costs {gold} gold and {amount} {dust.Name} (seal #{item.SealCount + 1} on this item)", 12, UiTheme.TextDim));
        for (int i = 0; i < item.RolledAffixes.Count; i++)
        {
            int index = i;
            AffixRoller.RolledAffix affix = item.RolledAffixes[i];
            Button b = UiTheme.Button(ItemTooltipText.AffixLine(affix) + (affix.Sealed ? "  (sealed)" : ""), () => SealChosen(item, index), 13, WidthPx - 16f);
            b.CustomMinimumSize = new Vector2(WidthPx - 16f, 28f);
            _chooserBody.AddChild(b);
        }

        _chooserBody.AddChild(UiTheme.Button("Cancel", Disarm, 13, 100f));
        _chooser.Visible = true;
    }

    private void SealChosen(EquipmentItem item, int index)
    {
        bool ok = Crafting.Seal(item, index, Player.Wallet, out string message);
        Say(message, ok ? UiTheme.Gold : new Color(1f, 0.45f, 0.35f));
        if (ok)
        {
            Sfx.Play(this, "levelup", -10f);
            SealArmed = false;
            CloseChooser();
        }

        Refresh();
    }

    /// <summary>For tests: seal modifier <paramref name="index"/> of an item.</summary>
    public bool SealForTest(EquipmentItem item, int index)
    {
        ArmSeal();
        OpenChooser(item);
        SealChosen(item, index);
        return item.RolledAffixes[index].Sealed;
    }

    private void CloseChooser()
    {
        if (_chooser is not null)
        {
            _chooser.Visible = false;
        }
    }

    private void Say(string text, Color color)
    {
        _message.Text = text;
        _message.AddThemeColorOverride("font_color", color);
    }

    private Color? TintFor(EquipmentItem item)
    {
        if (SealArmed)
        {
            return Crafting.CannotSeal(item) is null ? new Color(1f, 0.85f, 0.35f, 0.2f) : new Color(0f, 0f, 0f, 0.55f);
        }

        return Armed is { } orb
            ? Crafting.CannotApply(orb, item) is null ? new Color(0.4f, 1f, 0.5f, 0.22f) : new Color(0f, 0f, 0f, 0.55f)
            : null;
    }

    private string? FooterFor(EquipmentItem item)
    {
        if (!_active)
        {
            return null;
        }

        if (SealArmed)
        {
            if (Crafting.CannotSeal(item) is { } why)
            {
                return $"[color=#ff6a5a]{why}[/color]";
            }

            (long gold, CraftingCurrency dust, int amount) = Crafting.SealCost(item);
            return $"[color=#e8b84a]Seal: {gold} gold + {amount} {dust.Name}[/color]";
        }

        if (Armed is { } orb)
        {
            return Crafting.CannotApply(orb, item) is { } reason
                ? $"[color=#ff6a5a]{reason}[/color]"
                : $"[color=#{orb.Color.ToHtml(false)}]{orb.Name}: {orb.Effect}[/color]";
        }

        return null;
    }

    public override void _Process(double delta)
    {
        _cursor.Visible = _active && IsVisibleInTree() && Holding;
        if (_cursor.Visible)
        {
            _cursor.Text = Armed is { } a ? $"◆ {a.Name} ×{Player.Wallet.GetBalance(a.Id)}" : "◆ Artificer's Seal";
            _cursor.AddThemeColorOverride("font_color", Armed?.Color ?? UiTheme.Gold);
            _cursor.GlobalPosition = GetGlobalMousePosition() + new Vector2(16f, -6f);
        }

        if (_active && IsVisibleInTree())
        {
            Grid.QueueRedraw();
        }
    }
}
