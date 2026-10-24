#nullable enable
using System.Linq;
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

	// Kept to give focus back (by reference: the menu's children shift when a notice shows).
	private Button _playButton = null!;
	private Button _settingsButton = null!;

	/// <summary>Play → Solo / With friends (null while closed).</summary>
	private VBoxContainer? _chooser;
	private Button? _soloButton;
	private Button? _friendsButton;

	/// <summary>The Play chooser is open (tests).</summary>
	public bool ChooserOpen => _chooser is not null && IsInstanceValid(_chooser);

	/// <summary>The character list or co-op screen open now (tests).</summary>
	public CharacterSelect? OpenCharacterSelect => _center.GetChildren().OfType<CharacterSelect>().LastOrDefault(c => !c.IsQueuedForDeletion());
	public CoopPanel? OpenCoopPanel => _center.GetChildren().OfType<CoopPanel>().LastOrDefault(c => !c.IsQueuedForDeletion());

	public override void _Ready()
	{
		GetTree().Paused = false;
		_camera = GetNode<Camera2D>("Camera2D");
		Sfx.Music(this, "music_menu");

		var layer = new CanvasLayer { Layer = 5 };
		AddChild(layer);
		_menuRoot = UiTheme.FullRect(layer);
		var shade = new ColorRect { Color = new Color(0.03f, 0.02f, 0.05f, 0.55f), MouseFilter = Control.MouseFilterEnum.Ignore };
		_menuRoot.AddChild(shade);
		shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		_center = UiTheme.Center(_menuRoot);
		_menu = UiTheme.VBox(_center, 14);
		_menu.AddChild(UiTheme.Label("ARPG GAME", 72, UiTheme.Gold, HorizontalAlignment.Center));
		_menu.AddChild(UiTheme.Label("— Altered Realities —", 24, UiTheme.Ember, HorizontalAlignment.Center));
		_menu.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 24f) });
		ShowNotice();

		Button play = AddButton("Play", OpenPlayChooser);
		_playButton = play;
		_settingsButton = AddButton("Settings", OpenSettings);
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

		UiTheme.FocusLater(play);

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

	/// <summary>The notice card shown above the buttons (tests); null when none.</summary>
	public string? NoticeText { get; private set; }

	/// <summary>A party that ended out in the world says why, once (docs/design/menus.md -> "Main menu").</summary>
	private void ShowNotice()
	{
		if (Net.NetSession.Get(this) is not { MenuNotice: { Length: > 0 } notice } session)
		{
			return;
		}

		session.MenuNotice = null;
		NoticeText = notice;
		var card = new PanelContainer { Name = "Notice", SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
		card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.1f, 0.08f, 0.14f, 0.95f), UiTheme.Gold, 2, 8, 14));
		_menu.AddChild(card);
		HBoxContainer row = UiTheme.HBox(card, 14);
		VBoxContainer text = UiTheme.VBox(row, 2);
		text.AddChild(UiTheme.Label("The party ended", 20, UiTheme.Gold));
		Label why = UiTheme.Label(notice, 16, UiTheme.Text);
		why.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		why.CustomMinimumSize = new Vector2(420f, 0f);
		text.AddChild(why);
		Button ok = UiTheme.Button("OK", () =>
		{
			card.QueueFree();
			NoticeText = null;
		}, 16, 80f);
		ok.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		row.AddChild(ok);
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

	// --- Play: Solo / With friends (docs/design/menus.md -> "Main menu") -------------------

	/// <summary>Play opens a two-way choice in place of the menu.</summary>
	public void OpenPlayChooser()
	{
		CloseChooser();
		_menu.Visible = false;
		_chooser = UiTheme.VBox(_center, 12);
		_chooser.Name = "PlayChooser";
		_chooser.AddChild(UiTheme.Label("How do you want to play?", 34, UiTheme.Gold, HorizontalAlignment.Center));
		_soloButton = ChoiceButton("Solo", "Your hideout, your rifts, at your pace.", PressSolo);
		_friendsButton = ChoiceButton("With friends", $"Host a party or join one — up to {Net.PartyRules.MaxPlayers} players, each with their own character and loot.", PressWithFriends);
		_chooser.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 8f) });
		Button back = UiTheme.Button("Back", BackToMenu, 18, 160f);
		back.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_chooser.AddChild(back);
		UiTheme.FocusLater(_soloButton);
	}

	private Button ChoiceButton(string text, string blurb, System.Action press)
	{
		Button b = UiTheme.Button(text, press, 24, 400f);
		b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_chooser!.AddChild(b);
		Label l = UiTheme.Label(blurb, 15, UiTheme.TextDim, HorizontalAlignment.Center);
		l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		l.CustomMinimumSize = new Vector2(440f, 0f);
		_chooser.AddChild(l);
		return b;
	}

	public void PressSolo() => OpenCharacters(false, CharacterSelect.PlayPurpose.Solo);

	public void PressWithFriends() => OpenCharacters(false, CharacterSelect.PlayPurpose.Coop);

	private void CloseChooser()
	{
		if (_chooser is not null && IsInstanceValid(_chooser))
		{
			_chooser.QueueFree();
		}

		_chooser = null;
	}

	/// <summary>Esc / Back in the chooser: the title menu again, Play focused.</summary>
	public void BackToMenu()
	{
		CloseChooser();
		_menu.Visible = true;
		UiTheme.FocusLater(_playButton);
	}

	/// <summary>Back from a screen opened from the chooser: the chooser, its button focused.</summary>
	private void BackToChooser(CharacterSelect.PlayPurpose purpose)
	{
		OpenPlayChooser();
		UiTheme.FocusLater((purpose == CharacterSelect.PlayPurpose.Coop ? _friendsButton : _soloButton)!);
	}

	public override void _Input(InputEvent e)
	{
		if (ChooserOpen && _chooser!.IsVisibleInTree() && e.IsActionPressed("ui_cancel"))
		{
			GetViewport().SetInputAsHandled();
			BackToMenu();
		}
	}

	/// <summary>Co-op lobby — docs/design/multiplayer.md -> "Session and lobby". From the
	/// chooser (With friends), Back returns there; from the command line, to the menu.</summary>
	private void OpenCoop(int? hostPort = null, string? join = null, bool fromChooser = false)
	{
		CloseChooser();
		_menu.Visible = false;
		var panel = new CoopPanel { AutoHostPort = hostPort, AutoJoin = join, AutoStartAt = AutoStartArg(), AutoCode = UserArg("coop-code"), NoUpnp = UserArg("coop-no-upnp") is not null, OfferChange = true };
		_center.AddChild(panel);
		panel.Back += () =>
		{
			panel.QueueFree();
			if (fromChooser)
			{
				BackToChooser(CharacterSelect.PlayPurpose.Coop);
			}
			else
			{
				BackToMenu();
			}
		};
		panel.CreateCharacter += () =>
		{
			panel.QueueFree();
			OpenCharacters(create: true, CharacterSelect.PlayPurpose.Coop);
		};
		panel.ChangeCharacter += () =>
		{
			panel.QueueFree();
			OpenCharacters(false, CharacterSelect.PlayPurpose.Coop);
		};
	}

	private Button AddButton(string text, System.Action action)
	{
		Button b = UiTheme.Button(text, action, 22, 340f);
		b.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		_menu.AddChild(b);
		return b;
	}

	private void OpenCharacters(bool create, CharacterSelect.PlayPurpose purpose)
	{
		CloseChooser();
		_menu.Visible = false;
		var select = new CharacterSelect { StartInCreate = create, Purpose = purpose };
		_center.AddChild(select);
		select.Back += () =>
		{
			select.QueueFree();
			BackToChooser(purpose);
		};
		// With friends: the chosen character goes on to the co-op screen.
		select.CoopChosen += () =>
		{
			select.QueueFree();
			OpenCoop(fromChooser: true);
		};
	}

	/// <summary>Capture tool hook.</summary>
	public void OpenCharactersForCapture() => OpenCharacters(false, CharacterSelect.PlayPurpose.Solo);

	/// <summary>Capture tool hook.</summary>
	public void OpenSettingsForCapture() => OpenSettings();

	/// <summary>Capture tool hook: opens the co-op screen and returns it.</summary>
	public CoopPanel OpenCoopForCapture()
	{
		OpenCoop();
		return _center.GetChildren().OfType<CoopPanel>().Last();
	}

	/// <summary>Capture tool hook: a fresh co-op screen in place of the open one,
	/// optionally driving another session (a second player in the same process).</summary>
	public CoopPanel ReopenCoopForCapture(Net.NetSession? session = null)
	{
		foreach (CoopPanel old in _center.GetChildren().OfType<CoopPanel>())
		{
			_center.RemoveChild(old);
			old.QueueFree();
		}

		if (session is null)
		{
			return OpenCoopForCapture();
		}

		_menu.Visible = false;
		var panel = new CoopPanel { Session = session };
		_center.AddChild(panel);
		return panel;
	}

	private void OpenSettings()
	{
		_menu.Visible = false;
		SettingsPanel panel = SettingsPanel.Create(_center);
		panel.Closed += () =>
		{
			_menu.Visible = true;
			UiTheme.FocusLater(_settingsButton);
		};
	}

	public override void _Process(double delta)
	{
		_t += (float)delta;
		var arena = GetNode<Arena>("Arena");
		_camera.GlobalPosition = arena.Center + new Vector2(Mathf.Sin(_t * 0.07f) * 260f, Mathf.Cos(_t * 0.05f) * 140f);
	}
}
