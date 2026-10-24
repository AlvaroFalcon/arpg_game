#nullable enable
using System;
using Godot;

namespace ArpgGame.UI;

/// <summary>
/// The shared look of every menu and overlay — docs/design/menus.md -> "Look".
/// Built in code (one place to tune) and matched to the EasyPixel palette: deep
/// violet panels, gold accents for anything selectable, ember orange for
/// warnings. Screens are assembled from these helpers rather than hand-placed
/// .tscn layouts so they stay consistent and survive resolution changes.
/// </summary>
public static class UiTheme
{
    /// <summary>Gives <paramref name="control"/> keyboard focus at the end of the frame, if it is
    /// still in the tree then (a rebuilt view may have freed or detached it meanwhile).</summary>
    public static void FocusLater(Control control) =>
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(control) && control.IsInsideTree())
            {
                control.GrabFocus();
            }
        }).CallDeferred();

    public static readonly Color Background = new(0.07f, 0.06f, 0.1f, 0.94f);
    public static readonly Color Panel = new(0.1f, 0.085f, 0.14f, 0.96f);
    public static readonly Color PanelBorder = new(0.35f, 0.28f, 0.62f);
    public static readonly Color Gold = new(0.91f, 0.72f, 0.29f);
    public static readonly Color Ember = new(1f, 0.48f, 0.18f);
    public static readonly Color Text = new(0.92f, 0.9f, 0.86f);
    public static readonly Color TextDim = new(0.62f, 0.6f, 0.66f);
    public static readonly Color Life = new(0.85f, 0.18f, 0.22f);
    public static readonly Color Mana = new(0.25f, 0.45f, 0.95f);
    public static readonly Color Experience = new(0.4f, 0.85f, 0.45f);

    // Damage numbers (docs/design/menus.md -> "Damage numbers"). Warm gold is the
    // player's own damage; each element keeps a hue that reads on dark caverns and
    // stays apart from the gold; crits go brighter and more orange; damage taken is red.
    public static readonly Color DamagePhysical = new(1f, 0.85f, 0.52f);
    public static readonly Color DamageFire = new(1f, 0.55f, 0.24f);
    public static readonly Color DamageCold = new(0.55f, 0.85f, 1f);
    public static readonly Color DamageLightning = new(0.84f, 0.74f, 1f);
    public static readonly Color DamagePoison = new(0.56f, 0.95f, 0.42f);
    public static readonly Color DamageCrit = new(1f, 0.72f, 0.16f);
    public static readonly Color DamageStreak = new(1f, 0.74f, 0.22f);
    public static readonly Color DamageTaken = new(1f, 0.3f, 0.28f);
    public static readonly Color NumberOutline = new(0.07f, 0.035f, 0.02f, 0.92f);

    public static Color DamageColor(Combat.DamageType type) => type switch
    {
        Combat.DamageType.Fire => DamageFire,
        Combat.DamageType.Cold => DamageCold,
        Combat.DamageType.Lightning => DamageLightning,
        _ => DamagePhysical,
    };

    private static FontVariation? _numberFont;
    private static FontVariation? _bigNumberFont;

    /// <summary>The default font, emboldened: damage numbers and their tags.</summary>
    public static Font NumberFont => _numberFont ??= new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.6f };

    /// <summary>Bolder and slanted: the big-hit number.</summary>
    public static Font BigNumberFont => _bigNumberFont ??= new FontVariation
    {
        BaseFont = ThemeDB.FallbackFont,
        VariationEmbolden = 0.9f,
        VariationTransform = new Transform2D(new Vector2(1f, 0f), new Vector2(-0.18f, 1f), Vector2.Zero),
    };

    private static Theme? _theme;

    public static Theme Theme => _theme ??= Build();

    public static StyleBoxFlat Box(Color bg, Color border, int borderWidth = 2, int radius = 4, int padding = 10)
    {
        var box = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            ContentMarginLeft = padding,
            ContentMarginRight = padding,
            ContentMarginTop = padding * 0.6f,
            ContentMarginBottom = padding * 0.6f,
        };
        box.SetBorderWidthAll(borderWidth);
        return box;
    }

    private static Theme Build()
    {
        var theme = new Theme { DefaultFontSize = 18 };
        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_hover_color", "Button", Gold);
        theme.SetColor("font_focus_color", "Button", Gold);
        theme.SetColor("font_pressed_color", "Button", Colors.White);
        theme.SetColor("font_color", "CheckBox", Text);
        theme.SetColor("font_hover_color", "CheckBox", Gold);
        theme.SetStylebox("normal", "Button", Box(new Color(0.16f, 0.14f, 0.22f), new Color(0.3f, 0.26f, 0.42f)));
        theme.SetStylebox("hover", "Button", Box(new Color(0.22f, 0.19f, 0.3f), Gold));
        theme.SetStylebox("focus", "Button", Box(new Color(0.22f, 0.19f, 0.3f, 0f), Gold));
        theme.SetStylebox("pressed", "Button", Box(new Color(0.35f, 0.28f, 0.62f), Gold));
        theme.SetStylebox("disabled", "Button", Box(new Color(0.12f, 0.11f, 0.15f), new Color(0.2f, 0.2f, 0.25f)));
        theme.SetStylebox("panel", "PanelContainer", Box(Panel, PanelBorder, 2, 6, 16));
        theme.SetStylebox("panel", "Panel", Box(Panel, PanelBorder, 2, 6, 16));
        theme.SetStylebox("slider", "HSlider", Box(new Color(0.16f, 0.14f, 0.22f), new Color(0.3f, 0.26f, 0.42f), 1, 3, 2));
        theme.SetStylebox("grabber_area", "HSlider", Box(PanelBorder, PanelBorder, 1, 3, 2));
        theme.SetStylebox("grabber_area_highlight", "HSlider", Box(Gold, Gold, 1, 3, 2));
        // Text fields: the same dark glass as the panels, gold when focused
        // (docs/design/menus.md -> "Look"); an error turns the border ember (FieldError).
        theme.SetStylebox("normal", "LineEdit", Box(new Color(0.06f, 0.05f, 0.09f), new Color(0.3f, 0.26f, 0.42f), 2, 4, 10));
        theme.SetStylebox("focus", "LineEdit", Box(new Color(0f, 0f, 0f, 0f), Gold, 2, 4, 10));
        theme.SetStylebox("read_only", "LineEdit", Box(new Color(0.08f, 0.07f, 0.1f), new Color(0.2f, 0.2f, 0.25f), 2, 4, 10));
        theme.SetColor("font_placeholder_color", "LineEdit", new Color(0.62f, 0.6f, 0.66f, 0.7f));
        theme.SetColor("caret_color", "LineEdit", Gold);
        // Safety net: anything that still sets a built-in TooltipText looks like ours
        // (crafting.md -> "Orb tooltips"); the screens use InstantTooltip instead.
        theme.SetStylebox("panel", "TooltipPanel", Box(new Color(0.06f, 0.05f, 0.09f, 0.97f), PanelBorder, 2, 4, 8));
        theme.SetColor("font_color", "TooltipLabel", Text);
        theme.SetFontSize("font_size", "TooltipLabel", 15);
        theme.SetStylebox("background", "ProgressBar", Box(new Color(0.05f, 0.04f, 0.07f, 0.9f), new Color(0.25f, 0.22f, 0.32f), 1, 2, 0));
        return theme;
    }

    public static Label Label(string text, int size = 18, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label { Text = text, HorizontalAlignment = align };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c)
        {
            label.AddThemeColorOverride("font_color", c);
        }

        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.6f));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        return label;
    }

    public static Button Button(string text, Action onPressed, int size = 20, float minWidth = 260f)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 44f), FocusMode = Control.FocusModeEnum.All };
        button.AddThemeFontSizeOverride("font_size", size);
        button.Pressed += () =>
        {
            Core.Sfx.Play(button, "ui");
            onPressed();
        };
        return button;
    }

    /// <summary>The one dominant action of a screen: warm fill, gold border
    /// (docs/design/menus.md -> "Look" -> one primary action per screen).</summary>
    public static Button PrimaryButton(string text, Action onPressed, int size = 20, float minWidth = 260f)
    {
        Button button = Button(text, onPressed, size, minWidth);
        button.AddThemeStyleboxOverride("normal", Box(new Color(0.36f, 0.24f, 0.1f), Gold, 2));
        button.AddThemeStyleboxOverride("hover", Box(new Color(0.48f, 0.32f, 0.12f), new Color(1f, 0.86f, 0.5f), 2));
        button.AddThemeStyleboxOverride("pressed", Box(new Color(0.6f, 0.42f, 0.16f), new Color(1f, 0.86f, 0.5f), 2));
        button.AddThemeStyleboxOverride("focus", Box(new Color(0f, 0f, 0f, 0f), new Color(1f, 0.92f, 0.65f), 3));
        button.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.78f));
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_focus_color", Colors.White);
        return button;
    }

    /// <summary>A text field with a placeholder, themed (see Build).</summary>
    public static LineEdit Field(string placeholder, float minWidth, int size = 18)
    {
        var field = new LineEdit { PlaceholderText = placeholder, CustomMinimumSize = new Vector2(minWidth, 42f) };
        field.AddThemeFontSizeOverride("font_size", size);
        return field;
    }

    /// <summary>Marks a field wrong (ember border) or clears it.</summary>
    public static void FieldError(LineEdit field, bool wrong)
    {
        if (wrong)
        {
            field.AddThemeStyleboxOverride("normal", Box(new Color(0.12f, 0.05f, 0.04f), Ember, 2, 4, 10));
            field.AddThemeStyleboxOverride("focus", Box(new Color(0f, 0f, 0f, 0f), Ember, 2, 4, 10));
        }
        else
        {
            field.RemoveThemeStyleboxOverride("normal");
            field.RemoveThemeStyleboxOverride("focus");
        }
    }

    public static ProgressBar Bar(Color fill, float height = 18f)
    {
        var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0f, height), MaxValue = 1, Step = 0 };
        bar.AddThemeStyleboxOverride("fill", Box(fill, fill.Lightened(0.25f), 1, 2, 0));
        return bar;
    }

    /// <summary>A full-screen root Control with the theme applied.</summary>
    public static Control FullRect(Node parent)
    {
        var root = new Control { Theme = Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(root);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return root;
    }

    /// <summary>A dimmed full-screen backdrop that eats clicks.</summary>
    public static ColorRect Dim(Control parent, float alpha = 0.7f)
    {
        var dim = new ColorRect { Color = new Color(0.02f, 0.01f, 0.04f, alpha), MouseFilter = Control.MouseFilterEnum.Stop };
        parent.AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return dim;
    }

    public static CenterContainer Center(Control parent)
    {
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return center;
    }

    public static VBoxContainer VBox(Node parent, int separation = 10)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        parent.AddChild(box);
        return box;
    }

    public static HBoxContainer HBox(Node parent, int separation = 10)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        parent.AddChild(box);
        return box;
    }

    /// <summary>A pixel-art icon (orbs, dust, items) kept crisp at <paramref name="size"/>.</summary>
    public static TextureRect PixelIcon(Texture2D? texture, float size) => new()
    {
        Texture = texture,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(size, size),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>A light key cap ("E", "Esc", "C · K · P") with crisp dark text.</summary>
    public static PanelContainer KeyCap(string key, int size = 14, float minWidth = 0f)
    {
        var box = new PanelContainer { CustomMinimumSize = new Vector2(minWidth, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(0.9f, 0.86f, 0.77f), BorderColor = new Color(0.45f, 0.4f, 0.33f) };
        style.SetCornerRadiusAll(5);
        style.BorderWidthBottom = 3;
        style.SetContentMarginAll(2f);
        style.ContentMarginLeft = 8f;
        style.ContentMarginRight = 8f;
        box.AddThemeStyleboxOverride("panel", style);
        Label text = Label(key, size, new Color(0.12f, 0.1f, 0.08f), HorizontalAlignment.Center);
        text.AddThemeColorOverride("font_shadow_color", Colors.Transparent);
        text.AddThemeConstantOverride("outline_size", 0);
        box.AddChild(text);
        return box;
    }

    public static TextureRect Icon(Texture2D? texture, float size)
    {
        return new TextureRect
        {
            Texture = texture,
            CustomMinimumSize = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
    }

    /// <summary>
    /// A character's bust (assets/portraits, drawn by tools/art/sprites/anime.ts)
    /// in a gold frame; falls back to the idle frame of the class sprite.
    /// </summary>
    public static Control Portrait(string sprite, float size)
    {
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(size, size) };
        frame.AddThemeStyleboxOverride("panel", Box(new Color(0.1f, 0.08f, 0.14f), Gold, 2, 4, 2));
        string portrait = $"res://assets/portraits/{sprite}.png";
        string frames = $"res://assets/sprites/player/{sprite}.tres";
        Texture2D? texture = null;
        if (ResourceLoader.Exists(portrait))
        {
            texture = GD.Load<Texture2D>(portrait);
        }
        else if (ResourceLoader.Exists(frames) && GD.Load<SpriteFrames>(frames) is { } sf && sf.HasAnimation("idle"))
        {
            texture = sf.GetFrameTexture("idle", 0);
        }

        frame.AddChild(Icon(texture, size - 6f));
        return frame;
    }

    private const string InstantTooltipName = "InstantTooltip";

    /// <summary>
    /// The one tooltip of the UI: shows on the very frame the mouse enters
    /// <paramref name="target"/> (Godot's built-in one waits for the mouse to rest
    /// and is unthemed) — docs/design/crafting.md -> "Orb tooltips". One
    /// <see cref="HoverTooltip"/> per canvas layer is shared by every control on it.
    /// <paramref name="fill"/> runs on every enter and builds the content with
    /// <see cref="HoverTooltip.Begin"/> / <see cref="HoverTooltip.AddLine"/>.
    /// Disabled buttons still get it (a disabled BaseButton keeps its mouse filter).
    /// </summary>
    public static void InstantTooltip(Control target, Action<HoverTooltip> fill)
    {
        if (target.MouseFilter == Control.MouseFilterEnum.Ignore)
        {
            target.MouseFilter = Control.MouseFilterEnum.Pass;
        }

        // Disabled or hidden: hide only if this control's tooltip is the one up.
        void Hide()
        {
            if (FindTooltip(target) is { } tip && tip.Source == target)
            {
                tip.Source = null;
                tip.HideTooltip();
            }
        }

        target.MouseEntered += () =>
        {
            if (EnsureTooltip(target) is not { } tip)
            {
                return;
            }

            tip.Source = target;
            tip.HideTooltip();
            tip.Clear();
            fill(tip);
            if (tip.HasContent)
            {
                tip.ShowTooltip();
            }
        };
        target.MouseExited += Hide;
        target.VisibilityChanged += () =>
        {
            if (!target.IsVisibleInTree())
            {
                Hide();
            }
        };
        target.TreeExiting += Hide;
    }

    /// <summary>A plain tooltip: the first line is the title, the rest dim text.</summary>
    public static void InstantTooltip(Control target, Func<string> text) => InstantTooltip(target, tip =>
    {
        string[] lines = text().Split('\n');
        if (lines.Length == 0 || lines[0].Length == 0)
        {
            return;
        }

        tip.Begin(lines[0], Gold);
        foreach (string line in lines[1..])
        {
            tip.AddLine(line, Text);
        }
    });

    public static void InstantTooltip(Control target, string text) => InstantTooltip(target, () => text);

    private static Node? TooltipParent(Control target)
    {
        Node? n = target.GetParent();
        while (n is not null and not CanvasLayer)
        {
            n = n.GetParent();
        }

        return n ?? target.GetWindow();
    }

    private static HoverTooltip? FindTooltip(Control target) => TooltipParent(target)?.GetNodeOrNull<HoverTooltip>(InstantTooltipName);

    private static HoverTooltip? EnsureTooltip(Control target)
    {
        Node? parent = TooltipParent(target);
        if (parent is null)
        {
            return null;
        }

        if (parent.GetNodeOrNull<HoverTooltip>(InstantTooltipName) is { } existing)
        {
            return existing;
        }

        var tip = new HoverTooltip { Name = InstantTooltipName, Theme = Theme };
        parent.AddChild(tip);
        return tip;
    }

    public static string FormatTime(float seconds)
    {
        int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{s / 60:00}:{s % 60:00}";
    }
}
