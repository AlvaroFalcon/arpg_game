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
/// </summary>
public partial class UpgradeCards : HBoxContainer
{
    public event Action<UpgradeOffer>? Chosen;

    private readonly List<UpgradeOffer> _offers = new();

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
        AddThemeConstantOverride("separation", 18);
        Alignment = AlignmentMode.Center;
        float width = offers.Count > 4 ? 214f : 250f;
        for (int i = 0; i < offers.Count; i++)
        {
            AddChild(BuildCard(offers[i], i + 1, width));
        }
    }

    public static Color KindColor(UpgradeOffer offer) => offer.Kind switch
    {
        UpgradeKind.Keystone => UiTheme.Gold,
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
