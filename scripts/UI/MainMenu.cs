#nullable enable
using Godot;
using PathToExile.Core;
using PathToExile.Run;

namespace PathToExile.UI;

/// <summary>
/// Title screen — docs/design/menus.md -> "Main menu". A slowly drifting view
/// of the arena behind the title, the menu, and a line of lifetime records.
/// </summary>
public partial class MainMenu : Node2D
{
	private Camera2D _camera = null!;
	private Control _menuRoot = null!;
	private VBoxContainer _menu = null!;
	private CenterContainer _center = null!;
	private float _t;

	public override void _Ready()
	{
		GetTree().Paused = false;
		_camera = GetNode<Camera2D>("Camera2D");
		Sfx.Music(this, "music_menu");

		var layer = new CanvasLayer { Layer = 5 };
		AddChild(layer);
		_menuRoot = UiTheme.FullRect(layer);
		var shade = new ColorRect { Color = new Color(0.03f, 0.02f, 0.05f, 0.45f), MouseFilter = Control.MouseFilterEnum.Ignore };
		_menuRoot.AddChild(shade);
		shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		_center = UiTheme.Center(_menuRoot);
		_menu = UiTheme.VBox(_center, 14);
		_menu.AddChild(UiTheme.Label("PATH TO EXILE", 72, UiTheme.Gold, HorizontalAlignment.Center));
		_menu.AddChild(UiTheme.Label("— the Ashen Arena —", 24, UiTheme.Ember, HorizontalAlignment.Center));
		_menu.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 24f) });

		Button play = AddButton("Enter the Hideout", () => Scenes.Go(this, Scenes.Hideout));
		AddButton("Quick Run", () => Scenes.Go(this, Scenes.Run));
		AddButton("Settings", OpenSettings);
		AddButton("Training Grounds (sandbox)", () => Scenes.Go(this, Scenes.Sandbox));
		AddButton("Quit", () => GetTree().Quit());

		Profile? profile = Profile.Get(this);
		if (profile is not null && profile.RunsPlayed > 0)
		{
			_menu.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 12f) });
			_menu.AddChild(UiTheme.Label(
				$"Runs {profile.RunsPlayed}  ·  Victories {profile.Victories}  ·  Best {UiTheme.FormatTime(profile.BestTime)}  ·  Best level {profile.BestLevel}  ·  Gold {profile.Gold}",
				16, UiTheme.TextDim, HorizontalAlignment.Center));
		}

		Label version = UiTheme.Label("Godot 4 · C# · art made with EasyPixel", 14, UiTheme.TextDim, HorizontalAlignment.Right);
		_menuRoot.AddChild(version);
		version.SetAnchorsPreset(Control.LayoutPreset.BottomRight, true);
		version.OffsetLeft = -420f;
		version.OffsetTop = -34f;
		version.OffsetRight = -16f;

		play.CallDeferred(Control.MethodName.GrabFocus);
	}

	private Button AddButton(string text, System.Action action)
	{
		Button b = UiTheme.Button(text, action, 22, 340f);
		b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_menu.AddChild(b);
		return b;
	}

	/// <summary>Capture tool hook.</summary>
	public void OpenSettingsForCapture() => OpenSettings();

	private void OpenSettings()
	{
		_menu.Visible = false;
		SettingsPanel panel = SettingsPanel.Create(_center);
		panel.Closed += () =>
		{
			_menu.Visible = true;
			((Button)_menu.GetChild(3)).CallDeferred(Control.MethodName.GrabFocus);
		};
	}

	public override void _Process(double delta)
	{
		_t += (float)delta;
		var arena = GetNode<Arena>("Arena");
		_camera.GlobalPosition = arena.Center + new Vector2(Mathf.Sin(_t * 0.07f) * 260f, Mathf.Cos(_t * 0.05f) * 140f);
	}
}
