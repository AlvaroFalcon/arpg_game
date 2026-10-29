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
		_menu = UiTheme.VBox(_center, 6);
		_menu.AddChild(UiTheme.Label("ARPG GAME", 72, UiTheme.Gold, HorizontalAlignment.Center));
		_menu.AddChild(UiTheme.Label("— Altered Realities —", 24, UiTheme.Ember, HorizontalAlignment.Center));
		_menu.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 40f) });
		ShowNotice();

		// Three entries, text only, a brush stroke under the selected one (docs/design/menus.md -> "Title screen").
		_playButton = AddButton("Start", OpenPlayChooser);
		_settingsButton = AddButton("Settings", OpenSettings);
		AddButton("Quit", () => GetTree().Quit());

		Profile? profile = Profile.Get(this);
		if (profile is not null && profile.HasCharacters)
		{
			Characters.CharacterData c = profile.Active;
			_menu.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 18f) });
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

		BuildTitle();

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
		DropTitle();
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
		if (_title is not null)
		{
			bool any = e is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true } or InputEventJoypadButton { Pressed: true };
			if (any)
			{
				GetViewport().SetInputAsHandled();
				SkipTitle();
			}

			return;
		}

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
		DropTitle();
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
		BrushMenuOption b = BrushMenuOption.Create(text, action);
		_menu.AddChild(b);
		return b;
	}

	/// <summary>Another screen opens straight away (tests, capture hooks): no title, no sound.</summary>
	private void DropTitle()
	{
		if (_title is not null)
		{
			_title.QueueFree();
			_title = null;
			_pressAny = null;
		}
	}

	// --- "Press any key to continue" (docs/design/menus.md -> "Title screen") -------------

	private Control? _title;
	private Label? _pressAny;

	/// <summary>The press-any-key screen is showing (tests).</summary>
	public bool TitleShowing => _title is not null;

	/// <summary>The title alone and a pulsing "Press any key to continue"; the menu waits
	/// behind it. Skipped when there is something to say or do at once (a party notice,
	/// the co-op command-line flags).</summary>
	private void BuildTitle()
	{
		bool skip = NoticeText is not null || UserArg("coop-host") is not null || UserArg("coop-join") is not null || UserArg("skip-title") is not null;
		if (skip)
		{
			UiTheme.FocusLater(_playButton);
			return;
		}

		_menu.Visible = false;
		_title = UiTheme.VBox(_center, 6);
		_title.Name = "TitleScreen";
		_title.AddChild(UiTheme.Label("ARPG GAME", 72, UiTheme.Gold, HorizontalAlignment.Center));
		_title.AddChild(UiTheme.Label("— Altered Realities —", 24, UiTheme.Ember, HorizontalAlignment.Center));
		// The prompt sits where the menu will be, so the title doesn't move when it opens.
		float menuHeight = _menu.GetCombinedMinimumSize().Y;
		_title.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 90f) });
		_pressAny = UiTheme.Label("Press any key to continue", 24, UiTheme.Text, HorizontalAlignment.Center);
		_pressAny.AddThemeConstantOverride("outline_size", 6);
		_pressAny.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.75f));
		_title.AddChild(_pressAny);
		float used = _title.GetCombinedMinimumSize().Y;
		_title.AddChild(new Control { CustomMinimumSize = new Vector2(0f, Mathf.Max(0f, menuHeight - used - 6f)) });
	}

	/// <summary>Leaves the title for the menu, Start selected.</summary>
	public void SkipTitle()
	{
		if (_title is null)
		{
			return;
		}

		_title.QueueFree();
		_title = null;
		_pressAny = null;
		_menu.Visible = true;
		Sfx.Play(this, "ui", -6f);
		UiTheme.FocusLater(_playButton);
	}

	private void OpenCharacters(bool create, CharacterSelect.PlayPurpose purpose)
	{
		DropTitle();
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
		DropTitle();
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
		if (_pressAny is not null)
		{
			_pressAny.Modulate = new Color(1f, 1f, 1f, 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(_t * 2.6f)));
		}

		var arena = GetNode<Arena>("Arena");
		_camera.GlobalPosition = arena.Center + new Vector2(Mathf.Sin(_t * 0.07f) * 260f, Mathf.Cos(_t * 0.05f) * 140f);
	}
}
