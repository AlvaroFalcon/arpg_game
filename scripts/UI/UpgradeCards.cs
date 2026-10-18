#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using ArpgGame.Progression;

namespace ArpgGame.UI;

/// <summary>
/// A row of clickable upgrade cards — the level-up screen and the starting
/// skill pick share it (docs/design/menus.md -> "Level-up"). Keys 1-9 pick a
/// card too (handled by <see cref="LevelUpScreen"/>, which owns the keyboard
/// while it is open). Cards take no keyboard focus, so Space or Enter pressed in
/// the heat of a fight never picks one, and clicks only count once the row is
/// <see cref="Armed"/>.
///
/// Each card has its own small reroll button underneath (docs/design/menus.md ->
/// "Level-up and results"): it raises <see cref="RerollRequested"/> with the
/// card's index and <see cref="ReplaceOffer"/> swaps just that card.
/// </summary>
public partial class UpgradeCards : HBoxContainer
{
    public event Action<UpgradeOffer>? Chosen;

    /// <summary>The reroll button under card <c>index</c> was pressed (only once armed).</summary>
    public event Action<int>? RerollRequested;

    private readonly List<UpgradeOffer> _offers = new();
    private readonly List<Button> _rerollButtons = new();
    private float _cardWidth = 250f;

    /// <summary>Show a reroll button under each card.</summary>
    public bool ShowRerolls { get; set; } = true;

    public IReadOnlyList<UpgradeOffer> Offers => _offers;

    /// <summary>Clicks before this moment (Time.GetTicksMsec) are ignored.</summary>
    public ulong ArmedAtMsec { get; set; }

    public bool Armed => Time.GetTicksMsec() >= ArmedAtMsec;

    public void SetOffers(IReadOnlyList<UpgradeOffer> offers)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        _offers.Clear();
        _offers.AddRange(offers);
        _rerollButtons.Clear();
        AddThemeConstantOverride("separation", 18);
        Alignment = AlignmentMode.Center;
        _cardWidth = offers.Count > 4 ? 214f : 250f;
        for (int i = 0; i < offers.Count; i++)
        {
            AddChild(BuildColumn(offers[i], i));
        }
    }

    /// <summary>Swaps the card at <paramref name="index"/> for <paramref name="offer"/>, leaving the others alone.</summary>
    public void ReplaceOffer(int index, UpgradeOffer offer)
    {
        if (index < 0 || index >= _offers.Count || index >= GetChildCount())
        {
            return;
        }

        _offers[index] = offer;
        Node old = GetChild(index);
        Control column = BuildColumn(offer, index);
        AddChild(column);
        MoveChild(column, index);
        RemoveChild(old);
        old.QueueFree();
    }

    /// <summary>Enables or disables every reroll button (no charges left), with its label.</summary>
    public void SetRerollState(bool enabled, string tooltip = "")
    {
        foreach (Button b in _rerollButtons)
        {
            if (IsInstanceValid(b))
            {
                b.Disabled = !enabled;
                b.TooltipText = tooltip;
            }
        }
    }

    private Control BuildColumn(UpgradeOffer offer, int index)
    {
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 6);
        column.AddChild(BuildCard(offer, index + 1, _cardWidth));
        var reroll = new Button
        {
            Text = $"Reroll  [Shift+{index + 1}]",
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0f, 30f),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            Visible = ShowRerolls,
            Name = $"Reroll{index + 1}",
        };
        reroll.AddThemeFontSizeOverride("font_size", 14);
        reroll.AddThemeColorOverride("font_color", UiTheme.TextDim);
        reroll.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.Panel, UiTheme.TextDim with { A = 0.4f }, 1, 6, 6));
        reroll.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(0.16f, 0.13f, 0.22f), UiTheme.Gold, 1, 6, 6));
        reroll.AddThemeStyleboxOverride("pressed", UiTheme.Box(new Color(0.22f, 0.18f, 0.3f), UiTheme.Gold, 1, 6, 6));
        reroll.AddThemeStyleboxOverride("disabled", UiTheme.Box(UiTheme.Panel with { A = 0.5f }, UiTheme.TextDim with { A = 0.15f }, 1, 6, 6));
        reroll.Pressed += () =>
        {
            int at = _rerollButtons.IndexOf(reroll);
            if (Armed && at >= 0)
            {
                RerollRequested?.Invoke(at);
            }
        };
        if (index < _rerollButtons.Count)
        {
            _rerollButtons[index] = reroll;
        }
        else
        {
            _rerollButtons.Add(reroll);
        }

        column.AddChild(reroll);
        return column;
    }

    public static Color KindColor(UpgradeOffer offer) => offer.Kind switch
    {
        UpgradeKind.Keystone => offer.KeystoneTier == KeystoneTier.Great ? UiTheme.Gold : new Color(0.86f, 0.82f, 0.62f),
        UpgradeKind.NewSkill => UiTheme.Ember,
        UpgradeKind.SkillLevel => new Color(1f, 0.7f, 0.45f),
        UpgradeKind.Support => new Color(0.4f, 0.9f, 0.5f),
        _ => offer.Rarity switch
        {
            UpgradeRarity.Rare => new Color(1f, 0.9f, 0.35f),
            UpgradeRarity.Magic => new Color(0.5f, 0.65f, 1f),
            _ => new Color(0.82f, 0.82f, 0.86f),
        },
    };

    private Control BuildCard(UpgradeOffer offer, int key, float width)
    {
        Color accent = KindColor(offer);
        var card = new Button
        {
            CustomMinimumSize = new Vector2(width, 330f),
            FocusMode = FocusModeEnum.None,
            ClipContents = true,
        };
        card.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.Panel, accent with { A = 0.55f }, 2, 8, 14));
        card.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(0.16f, 0.13f, 0.22f), accent, 3, 8, 14));
        card.AddThemeStyleboxOverride("focus", UiTheme.Box(new Color(0.16f, 0.13f, 0.22f, 0f), accent, 3, 8, 14));
        card.AddThemeStyleboxOverride("pressed", UiTheme.Box(new Color(0.22f, 0.18f, 0.3f), accent, 3, 8, 14));
        card.Pressed += () =>
        {
            if (Armed)
            {
                Choose(offer);
            }
        };

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 8);
        card.AddChild(box);
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        box.OffsetLeft = 14f;
        box.OffsetRight = -14f;
        box.OffsetTop = 14f;
        box.OffsetBottom = -14f;

        Label keyLabel = UiTheme.Label($"[{key}]", 14, UiTheme.TextDim, HorizontalAlignment.Right);
        keyLabel.MouseFilter = MouseFilterEnum.Ignore;
        box.AddChild(keyLabel);

        TextureRect icon = UiTheme.Icon(offer.Icon, 72f);
        icon.MouseFilter = MouseFilterEnum.Ignore;
        icon.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        box.AddChild(icon);

        Label title = UiTheme.Label(offer.Title, 22, accent, HorizontalAlignment.Center);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.MouseFilter = MouseFilterEnum.Ignore;
        box.AddChild(title);

        Label subtitle = UiTheme.Label(offer.Subtitle, 15, UiTheme.TextDim, HorizontalAlignment.Center);
        subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        subtitle.MouseFilter = MouseFilterEnum.Ignore;
        box.AddChild(subtitle);

        Label desc = UiTheme.Label(offer.Description, 16, UiTheme.Text, HorizontalAlignment.Center);
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        desc.MouseFilter = MouseFilterEnum.Ignore;
        desc.SizeFlagsVertical = SizeFlags.ExpandFill;
        box.AddChild(desc);

        return card;
    }

    private void Choose(UpgradeOffer offer)
    {
        if (_offers.Count == 0)
        {
            return;
        }

        _offers.Clear();
        Chosen?.Invoke(offer);
    }

    /// <summary>Picks the card at <paramref name="index"/> as if clicked (tools, tests).</summary>
    public void ChooseIndex(int index)
    {
        if (index >= 0 && index < _offers.Count)
        {
            Choose(_offers[index]);
        }
    }

    public int OfferCount => _offers.Count;
}
