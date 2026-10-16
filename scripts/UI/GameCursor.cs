#nullable enable
using Godot;

namespace ArpgGame.UI;

/// <summary>
/// The game's own mouse cursors — docs/design/menus.md -> "Cursor". Pixel art
/// from tools/art (sprites/cursors.ts), scaled up with nearest filtering to
/// suit the window (2× up to 1080p, 3× at 1440p, 4× at 4K) and installed as
/// hardware cursors, so they cost nothing per frame and never lag behind the
/// mouse. Four looks: the bronze arrow, a white-hot arrow over buttons, a sword
/// over a monster (<see cref="SetAttack"/>, driven by the target frame) and a
/// gauntlet hand over an item on the ground (<see cref="SetItemHover"/>, its
/// sprite; its ground label carries the shape itself). The hand wins over the
/// sword: an item dropped among monsters is what the click will act on.
/// Lives under the <see cref="Core.GameSettings"/> autoload.
/// </summary>
public partial class GameCursor : Node
{
    private const string Dir = "res://assets/ui/cursors/";

    /// <summary>The hardware cursor shapes that stand for "over a monster" and
    /// "over an item" (shapes the game's UI never uses for anything else).</summary>
    private const Input.CursorShape AttackShape = Input.CursorShape.Cross;
    public const Control.CursorShape ItemShape = Control.CursorShape.Help;

    private int _scale;
    private bool _attack;
    private readonly System.Collections.Generic.HashSet<ulong> _items = new();

    public static GameCursor? Get(Node context) =>
        context.IsInsideTree() ? context.GetTree().Root.GetNodeOrNull<GameCursor>("GameSettings/GameCursor") : null;

    /// <summary>Sword cursor while the mouse is over a monster (no-op without the autoload).</summary>
    public static void SetAttack(Node context, bool on) => Get(context)?.ShowAttack(on);

    /// <summary>Hand cursor while the mouse is over <paramref name="item"/>; call
    /// with false when it leaves or the item goes away.</summary>
    public static void SetItemHover(Node item, bool on)
    {
        if (Get(item) is not { } cursor)
        {
            return;
        }

        bool changed = on ? cursor._items.Add(item.GetInstanceId()) : cursor._items.Remove(item.GetInstanceId());
        if (changed)
        {
            cursor.Refresh();
        }
    }

    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        GetTree().Root.SizeChanged += Apply;
        GetTree().NodeAdded += OnNodeAdded;
        Apply();
    }

    public override void _ExitTree()
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        GetTree().Root.SizeChanged -= Apply;
        GetTree().NodeAdded -= OnNodeAdded;
    }

    /// <summary>Buttons show the hover arrow (Godot's default for them is the plain arrow).</summary>
    private static void OnNodeAdded(Node node)
    {
        if (node is BaseButton button && button.MouseDefaultCursorShape == Control.CursorShape.Arrow)
        {
            button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        }
    }

    private void ShowAttack(bool on)
    {
        if (on == _attack)
        {
            return;
        }

        _attack = on;
        Refresh();
    }

    private void Refresh()
    {
        Input.CursorShape shape = _items.Count > 0 ? (Input.CursorShape)ItemShape : _attack ? AttackShape : Input.CursorShape.Arrow;
        if (DisplayServer.GetName() != "headless")
        {
            Input.SetDefaultCursorShape(shape);
        }
    }

    private void Apply()
    {
        int scale = Mathf.Clamp(Mathf.RoundToInt(DisplayServer.WindowGetSize().Y / 540f), 2, 4);
        if (scale == _scale)
        {
            return;
        }

        _scale = scale;
        Install("cursor_arrow", Input.CursorShape.Arrow, scale, Vector2.Zero);
        Install("cursor_arrow", Input.CursorShape.Drag, scale, Vector2.Zero);
        Install("cursor_arrow", Input.CursorShape.CanDrop, scale, Vector2.Zero);
        Install("cursor_hover", Input.CursorShape.PointingHand, scale, Vector2.Zero);
        Install("cursor_attack", AttackShape, scale, Vector2.Zero);
        Install("cursor_grab", (Input.CursorShape)ItemShape, scale, new Vector2(5.5f, 0.5f));
    }

    /// <summary>Installs one cursor; <paramref name="hotspot"/> is in source pixels
    /// (the arrow's and the sword's tip is the top-left pixel, the hand's the index fingertip).</summary>
    private static void Install(string name, Input.CursorShape shape, int scale, Vector2 hotspot)
    {
        if (GD.Load<Texture2D>(Dir + name + ".png")?.GetImage() is not { } image)
        {
            return;
        }

        if (image.IsCompressed())
        {
            image.Decompress();
        }

        image.Resize(image.GetWidth() * scale, image.GetHeight() * scale, Image.Interpolation.Nearest);
        Input.SetCustomMouseCursor(ImageTexture.CreateFromImage(image), shape, (hotspot * scale).Floor());
    }
}
