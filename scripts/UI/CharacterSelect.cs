#nullable enable
using System;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;

namespace ArpgGame.UI;

/// <summary>
/// Character select and creation — docs/design/menus.md -> "Characters".
/// Lists every character (class, level, best tier cleared) with Play and
/// Delete; "New character" picks a name and one of the three classes, each
/// shown with its sprite, description and class mechanic.
/// </summary>
public partial class CharacterSelect : PanelContainer
{
    public enum PlayPurpose
    {
        Solo,

        /// <summary>Main menu → Play → With friends: the character goes on to the co-op screen.</summary>
        Coop,
    }

    public event Action? Back;

    /// <summary>With friends: a character was chosen (and is the active one).</summary>
    public event Action? CoopChosen;

    /// <summary>Set before adding: what the chosen character is for.</summary>
    public PlayPurpose Purpose { get; set; }

    /// <summary>The "hasn't played the introduction" question is showing (tests).</summary>
    public bool TutorialPromptShowing { get; private set; }

    /// <summary>The title shown (tests).</summary>
    public string TitleText { get; private set; } = "";

    private VBoxContainer _body = null!;
    private string _newClass = "arcanist";
    private LineEdit? _name;
    private int _confirmDelete = -1;

    /// <summary>Set before adding: open on "New character" (from the co-op screen).</summary>
    public bool StartInCreate { get; set; }

    private bool _creating;

    private Profile? Profile => Core.Profile.Get(this);

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(860f, 0f);
        _body = UiTheme.VBox(this, 10);
        if (StartInCreate)
        {
            ShowCreate();
        }
        else
        {
            ShowList();
        }
    }

    /// <summary>Esc goes back one step, like every other menu.</summary>
    public override void _Input(InputEvent e)
    {
        if (!IsVisibleInTree() || !e.IsActionPressed("ui_cancel"))
        {
            return;
        }

        GetViewport().SetInputAsHandled();
        if ((_creating && !StartInCreate) || TutorialPromptShowing)
        {
            ShowList();
        }
        else
        {
            Back?.Invoke();
        }
    }

    private void Clear()
    {
        TutorialPromptShowing = false;
        foreach (Node child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void ShowList()
    {
        Clear();
        _creating = false;
        StartInCreate = false;
        TitleText = Purpose == PlayPurpose.Coop ? "Choose who plays with friends" : "Choose your hero";
        _body.AddChild(UiTheme.Label(TitleText, 34, UiTheme.Gold, HorizontalAlignment.Center));
        Profile? profile = Profile;
        if (profile is null || !profile.HasCharacters)
        {
            _body.AddChild(UiTheme.Label("No characters yet.", 18, UiTheme.TextDim, HorizontalAlignment.Center));
        }
        else
        {
            // More than four rows would push the buttons off a 720p screen: scroll.
            var scroll = new ScrollContainer
            {
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                CustomMinimumSize = new Vector2(0f, Mathf.Min(profile.Characters.Count, 4) * 101f),
            };
            _body.AddChild(scroll);
            VBoxContainer list = UiTheme.VBox(scroll, 10);
            list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            for (int i = 0; i < profile.Characters.Count; i++)
            {
                int index = i;
                CharacterData c = profile.Characters[i];
                var row = new PanelContainer();
                list.AddChild(row);
                HBoxContainer h = UiTheme.HBox(row, 12);
                h.AddChild(UiTheme.Portrait(c.AscendancyDef?.Sprite ?? c.Class.Sprite, 72f));
                VBoxContainer info = UiTheme.VBox(h, 2);
                info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                info.AddChild(UiTheme.Label($"{c.Name}{(index == profile.ActiveIndex ? "  (last played)" : "")}", 20, UiTheme.Text));
                info.AddChild(UiTheme.Label($"Level {c.Level} {c.AscendancyDef?.Name ?? c.Class.Name}  ·  best tier {c.HighestTierCleared}  ·  {c.RealitiesCleared} realities  ·  {c.Deaths} deaths", 15, UiTheme.TextDim));
                Button play = UiTheme.Button("Play", () => Play(index), 18, 110f);
                h.AddChild(play);
                Button delete = UiTheme.Button(_confirmDelete == index ? "Delete — sure?" : "Delete", () => Delete(index), 15, _confirmDelete == index ? 150f : 110f);
                if (_confirmDelete == index)
                {
                    delete.AddThemeColorOverride("font_color", UiTheme.Life);
                    delete.AddThemeColorOverride("font_focus_color", UiTheme.Life);
                    delete.AddThemeColorOverride("font_hover_color", UiTheme.Life);
                }

                h.AddChild(delete);
                if (index == profile.ActiveIndex)
                {
                    UiTheme.FocusLater(play);
                    // Keep the last-played row in view when the list scrolls.
                    scroll.CallDeferred(ScrollContainer.MethodName.EnsureControlVisible, row);
                }
            }
        }

        HBoxContainer buttons = UiTheme.HBox(_body, 12);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        Button create = profile is null || !profile.HasCharacters
            ? UiTheme.PrimaryButton("New character", ShowCreate, 18, 240f)
            : UiTheme.Button("New character", ShowCreate, 18, 240f);
        buttons.AddChild(create);
        buttons.AddChild(UiTheme.Button("Back", () => Back?.Invoke(), 18, 160f));
        if (profile is null || !profile.HasCharacters)
        {
            UiTheme.FocusLater(create);
        }
    }

    /// <summary>Back, as the button or Esc (tests).</summary>
    public void PressBack() => Back?.Invoke();

    /// <summary>Plays character <paramref name="index"/> (the row's Play button; tests).</summary>
    public void Play(int index)
    {
        Profile?.Select(index);
        Chosen();
    }

    /// <summary>The active character was chosen: solo goes in; with friends goes to the co-op
    /// screen, first asking about the introduction if it is still ahead (docs/design/tutorial.md -> "With friends").</summary>
    private void Chosen()
    {
        if (Purpose != PlayPurpose.Coop)
        {
            Enter();
        }
        else if (Profile?.Active is { TutorialDone: false })
        {
            ShowTutorialPrompt();
        }
        else
        {
            CoopChosen?.Invoke();
        }
    }

    private void ShowTutorialPrompt()
    {
        CharacterData c = Profile!.Active;
        Clear();
        TutorialPromptShowing = true;
        TitleText = $"{c.Name} hasn't played the introduction";
        _body.AddChild(UiTheme.Label(TitleText, 30, UiTheme.Gold, HorizontalAlignment.Center));
        Label text = UiTheme.Label("The Threshold takes about 5 minutes and is played alone: it shows the basics, then leads home to your hideout. "
            + "Play it first and join your friends after, or skip it now — the same as \"Skip the introduction\" in its menu.", 17, UiTheme.Text, HorizontalAlignment.Center);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.CustomMinimumSize = new Vector2(760f, 0f);
        _body.AddChild(text);
        HBoxContainer buttons = UiTheme.HBox(_body, 12);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        Button first = UiTheme.Button("Play it first (solo)", Enter, 18, 260f);
        Button skip = UiTheme.PrimaryButton("Skip it and play with friends", SkipTutorial, 18, 340f);
        buttons.AddChild(first);
        buttons.AddChild(skip);
        buttons.AddChild(UiTheme.Button("Back", ShowList, 18, 140f));
        UiTheme.FocusLater(skip);
    }

    /// <summary>"Skip it and play with friends" (tests press it).</summary>
    public void SkipTutorial()
    {
        if (Profile is { } profile)
        {
            profile.Active.TutorialDone = true;
            profile.Save();
        }

        CoopChosen?.Invoke();
    }

    /// <summary>New characters start in The Threshold, the introduction
    /// (docs/design/tutorial.md); everyone else goes home.</summary>
    private void Enter()
    {
        if (Profile?.Active is { TutorialDone: false })
        {
            World.RealityLaunch.Tutorial = true;
            Scenes.Go(this, Scenes.Reality);
            return;
        }

        Scenes.Go(this, Scenes.Hideout);
    }

    private void Delete(int index)
    {
        if (_confirmDelete != index)
        {
            _confirmDelete = index;
            ShowList();
            return;
        }

        _confirmDelete = -1;
        Profile?.DeleteCharacter(index);
        ShowList();
    }

    public void ShowCreate()
    {
        Clear();
        _creating = true;
        _body.AddChild(UiTheme.Label("New character", 34, UiTheme.Gold, HorizontalAlignment.Center));
        HBoxContainer nameRow = UiTheme.HBox(_body, 10);
        nameRow.AddChild(UiTheme.Label("Name", 18, UiTheme.Text));
        _name = new LineEdit { Text = _name?.Text ?? "", PlaceholderText = "Hero", MaxLength = 20, CustomMinimumSize = new Vector2(320f, 40f) };
        _name.TextSubmitted += _ => Create();
        nameRow.AddChild(_name);
        nameRow.AddChild(UiTheme.Label("Enter creates · Esc goes back", 14, UiTheme.TextDim));

        HBoxContainer cards = UiTheme.HBox(_body, 10);
        foreach (ClassDefinition cls in ClassCatalog.Classes)
        {
            string id = cls.Id;
            bool selected = id == _newClass;
            var card = new PanelContainer { CustomMinimumSize = new Vector2(276f, 0f) };
            if (selected)
            {
                card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.16f, 0.12f, 0.22f), UiTheme.Gold, 3));
            }

            cards.AddChild(card);
            VBoxContainer v = UiTheme.VBox(card, 6);
            HBoxContainer head = UiTheme.HBox(v, 8);
            head.AddChild(UiTheme.Portrait(cls.Sprite, 96f));
            VBoxContainer titles = UiTheme.VBox(head, 0);
            titles.AddChild(UiTheme.Label(cls.Name, 24, selected ? UiTheme.Gold : UiTheme.Text));
            titles.AddChild(UiTheme.Label($"STR {cls.Strength} · DEX {cls.Dexterity} · INT {cls.Intelligence}", 13, UiTheme.TextDim));
            v.AddChild(Wrapped(cls.Description, 14, UiTheme.Text));
            v.AddChild(Wrapped(cls.Mechanic, 14, UiTheme.Ember));
            string skill = GemLoot.NameOf(cls.StartingSkill, false);
            string support = GemLoot.NameOf(cls.StartingSupport, true);
            v.AddChild(Wrapped($"Starts with {skill} + {support}.", 13, UiTheme.TextDim));
            v.AddChild(Wrapped($"Ascends into: {string.Join(", ", cls.Ascendancies)}", 13, UiTheme.TextDim));
            v.AddChild(UiTheme.Button(selected ? "Selected" : "Choose", () => { _newClass = id; ShowCreate(); }, 16, 240f));
        }

        HBoxContainer buttons = UiTheme.HBox(_body, 12);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        Button create = UiTheme.PrimaryButton("Create & play", Create, 20, 260f);
        buttons.AddChild(create);
        if (Core.GameSettings.Get(this) is { AdminMode: true })
        {
            buttons.AddChild(UiTheme.Button($"Test character (lv {CharacterData.MaxLevel})", CreateTest, 16, 240f));
        }

        buttons.AddChild(UiTheme.Button("Back", () =>
        {
            if (StartInCreate)
            {
                Back?.Invoke();
            }
            else
            {
                ShowList();
            }
        }, 18, 160f));
        UiTheme.FocusLater(_name);
    }

    private static Label Wrapped(string text, int size, Color color)
    {
        Label l = UiTheme.Label(text, size, color);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(250f, 0f);
        return l;
    }

    /// <summary>Admin mode: a max-level character with gold, shards and orbs, straight to the hideout.</summary>
    private void CreateTest()
    {
        Profile?.CreateTestCharacter(_name?.Text ?? "", _newClass);
        Chosen();
    }

    private void Create()
    {
        Profile?.CreateCharacter(_name?.Text ?? "", _newClass);
        Chosen();
    }
}
