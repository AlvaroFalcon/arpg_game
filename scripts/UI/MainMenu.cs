#nullable enable
using Godot;
using ArpgGame.Core;
using ArpgGame.Run;

namespace ArpgGame.UI;

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
		_menu.AddChild(UiTheme.Label("ARPG GAME", 72, UiTheme.Gold, HorizontalAlignment.Center));
		_menu.AddChild(UiTheme.Label("— Altered Realities —", 24, UiTheme.Ember, HorizontalAlignment.Center));
		_menu.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 24f) });

		Button play = AddButton("Play", OpenCharacters);
		AddButton("Settings", OpenSettings);
		AddButton("Co-op (Survival Rift)", () => OpenCoop());
		AddButton("Training Grounds (sandbox)", () => Scenes.Go(this, Scenes.Sandbox));
		AddButton("Quit", () => GetTree().Quit());

		Profile? profile = Profile.Get(this);
		if (profile is not null && profile.HasCharacters)
		{
			Characters.CharacterData c = profile.Active;
			_menu.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 12f) });
			_menu.AddChild(UiTheme.Label(
				$"{profile.Characters.Count} character{(profile.Characters.Count == 1 ? "" : "s")}  ·  last played: {c.Name}, level {c.Level} {c.AscendancyDef?.Name ?? c.Class.Name}, best tier {c.HighestTierCleared}",
				16, UiTheme.TextDim, HorizontalAlignment.Center));
		}

		Label version = UiTheme.Label("Godot 4 · C# · art made with EasyPixel", 14, UiTheme.TextDim, HorizontalAlignment.Right);
		_menuRoot.AddChild(version);
		version.SetAnchorsPreset(Control.LayoutPreset.BottomRight, true);
		version.OffsetLeft = -420f;
		version.OffsetTop = -34f;
		version.OffsetRight = -16f;

		play.CallDeferred(Control.MethodName.GrabFocus);

		// scripts/coop_local.sh: --coop-host[=port] / --coop-join=address:port open the lobby at once
		// (with --coop-code=CODE, and --coop-no-upnp for one PC).
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			string[] kv = arg.TrimStart('-').Split('=', 2);
			if (kv[0] == "coop-host")
			{
				int port = kv.Length == 2 ? kv[1].ToInt() : Net.NetTransport.DefaultPort;
				CallDeferred(MethodName.OpenCoopHost, port);
			}
			else if (kv[0] == "coop-join" && kv.Length == 2)
			{
				CallDeferred(MethodName.OpenCoopJoin, kv[1]);
			}
		}
	}

	private void OpenCoopHost(int port) => OpenCoop(port, null);

	private static int AutoStartArg()
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			string[] kv = arg.TrimStart('-').Split('=', 2);
			if (kv.Length == 2 && kv[0] == "coop-autostart")
			{
				return kv[1].ToInt();
			}
		}

		return 0;
	}

	private void OpenCoopJoin(string address) => OpenCoop(null, address);

	/// <summary>"--name=value" → value, "--name" → "", absent → null.</summary>
	private static string? UserArg(string name)
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			string[] kv = arg.TrimStart('-').Split('=', 2);
			if (kv[0] == name)
			{
				return kv.Length == 2 ? kv[1] : "";
			}
		}

		return null;
	}

	/// <summary>Co-op lobby — docs/design/multiplayer.md -> "Session and lobby".</summary>
	private void OpenCoop(int? hostPort = null, string? join = null)
	{
		_menu.Visible = false;
		var panel = new CoopPanel { AutoHostPort = hostPort, AutoJoin = join, AutoStartAt = AutoStartArg(), AutoCode = UserArg("coop-code"), NoUpnp = UserArg("coop-no-upnp") is not null };
		_center.AddChild(panel);
		panel.Back += () =>
		{
			panel.QueueFree();
			_menu.Visible = true;
			((Button)_menu.GetChild(5)).CallDeferred(Control.MethodName.GrabFocus);
		};
	}

	private Button AddButton(string text, System.Action action)
	{
		Button b = UiTheme.Button(text, action, 22, 340f);
		b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_menu.AddChild(b);
		return b;
	}

	private void OpenCharacters()
	{
		_menu.Visible = false;
		var select = new CharacterSelect();
		_center.AddChild(select);
		select.Back += () =>
		{
			select.QueueFree();
			_menu.Visible = true;
			((Button)_menu.GetChild(3)).CallDeferred(Control.MethodName.GrabFocus);
		};
	}

	/// <summary>Capture tool hook.</summary>
	public void OpenCharactersForCapture() => OpenCharacters();

	/// <summary>Capture tool hook.</summary>
	public void OpenSettingsForCapture() => OpenSettings();

	private void OpenSettings()
	{
		_menu.Visible = false;
		SettingsPanel panel = SettingsPanel.Create(_center);
		panel.Closed += () =>
		{
			_menu.Visible = true;
			((Button)_menu.GetChild(4)).CallDeferred(Control.MethodName.GrabFocus);
		};
	}

	public override void _Process(double delta)
	{
		_t += (float)delta;
		var arena = GetNode<Arena>("Arena");
		_camera.GlobalPosition = arena.Center + new Vector2(Mathf.Sin(_t * 0.07f) * 260f, Mathf.Cos(_t * 0.05f) * 140f);
	}
}
