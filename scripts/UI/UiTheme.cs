#nullable enable
using System;
using Godot;

namespace PathToExile.UI;

/// <summary>
/// The shared look of every menu and overlay — docs/design/menus.md -> "Look".
/// Built in code (one place to tune) and matched to the EasyPixel palette: deep
/// violet panels, gold accents for anything selectable, ember orange for
/// warnings. Screens are assembled from these helpers rather than hand-placed
/// .tscn layouts so they stay consistent and survive resolution changes.
/// </summary>
public static class UiTheme
{
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

    public static string FormatTime(float seconds)
    {
        int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{s / 60:00}:{s % 60:00}";
    }
}
