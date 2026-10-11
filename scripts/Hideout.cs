#nullable enable
using Godot;
using PathToExile.Core;
using PathToExile.UI;

namespace PathToExile;

/// <summary>
/// The hideout — docs/design/menus.md -> "Hideout". The safe room between
/// runs, PoE style: your persistent character with its gear, the inventory
/// (<c>I</c>) to equip what the last run gave you, unkillable training dummies
/// to try skills on, and the portal that starts a run. Leaving (portal or
/// menu) saves the character through <see cref="Profile"/>.
/// </summary>
public partial class Hideout : Node2D
{
    private Player _player = null!;
    private Node2D _portal = null!;
    private bool _leaving;

    public override void _Ready()
    {
        GetTree().Paused = false;
        _player = GetNode<Player>("Player");
        _portal = GetNode<Node2D>("Portal");
        Profile.Get(this)?.ApplyTo(_player);
        Sfx.Music(this, "music_menu");

        AddChild(new PauseMenu { LeaveText = "Main menu", OnLeave = () => Leave(Scenes.MainMenu) });

        var layer = new CanvasLayer { Layer = 4 };
        AddChild(layer);
        Control root = UiTheme.FullRect(layer);
        Label hint = UiTheme.Label(
            "HIDEOUT — walk into the portal to begin a run\nI: inventory & equipment · RMB: Fireball on the dummies · Space: dodge · Esc: menu",
            17, UiTheme.TextDim, HorizontalAlignment.Center);
        root.AddChild(hint);
        hint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        hint.OffsetLeft = -520f;
        hint.OffsetRight = 520f;
        hint.OffsetTop = -64f;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_leaving && _player.GlobalPosition.DistanceTo(_portal.GlobalPosition) < 42f)
        {
            Leave(Scenes.Run);
        }
    }

    private void Leave(string scene)
    {
        if (_leaving)
        {
            return;
        }

        _leaving = true;
        Profile.Get(this)?.CaptureFrom(_player);
        Scenes.Go(this, scene);
    }
}
