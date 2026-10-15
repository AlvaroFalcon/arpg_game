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
    public event Action? Back;

    private VBoxContainer _body = null!;
    private string _newClass = "arcanist";
    private LineEdit? _name;
    private int _confirmDelete = -1;

    private Profile? Profile => Core.Profile.Get(this);

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(860f, 0f);
        _body = UiTheme.VBox(this, 10);
        ShowList();
    }

    private void Clear()
    {
        foreach (Node child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void ShowList()
    {
        Clear();
        _body.AddChild(UiTheme.Label("Choose your hero", 34, UiTheme.Gold, HorizontalAlignment.Center));
        Profile? profile = Profile;
        if (profile is null || !profile.HasCharacters)
        {
            _body.AddChild(UiTheme.Label("No characters yet.", 18, UiTheme.TextDim, HorizontalAlignment.Center));
        }
        else
        {
            for (int i = 0; i < profile.Characters.Count; i++)
            {
                int index = i;
                CharacterData c = profile.Characters[i];
                var row = new PanelContainer();
                _body.AddChild(row);
                HBoxContainer h = UiTheme.HBox(row, 12);
                h.AddChild(UiTheme.Portrait(c.AscendancyDef?.Sprite ?? c.Class.Sprite, 72f));
                VBoxContainer info = UiTheme.VBox(h, 2);
                info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                info.AddChild(UiTheme.Label($"{c.Name}{(index == profile.ActiveIndex ? "  (last played)" : "")}", 20, UiTheme.Text));
                info.AddChild(UiTheme.Label($"Level {c.Level} {c.AscendancyDef?.Name ?? c.Class.Name}  ·  best tier {c.HighestTierCleared}  ·  {c.RealitiesCleared} realities  ·  {c.Deaths} deaths", 15, UiTheme.TextDim));
                Button play = UiTheme.Button("Play", () => Play(index), 18, 110f);
                h.AddChild(play);
                h.AddChild(UiTheme.Button(_confirmDelete == index ? "Confirm?" : "Delete", () => Delete(index), 15, 110f));
                if (index == profile.ActiveIndex)
                {
                    play.CallDeferred(Control.MethodName.GrabFocus);
                }
            }
        }

        HBoxContainer buttons = UiTheme.HBox(_body, 12);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        Button create = UiTheme.Button("New character", ShowCreate, 18, 240f);
        buttons.AddChild(create);
        buttons.AddChild(UiTheme.Button("Back", () => Back?.Invoke(), 18, 160f));
        if (profile is null || !profile.HasCharacters)
        {
            create.CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    private void Play(int index)
    {
        Profile?.Select(index);
        Enter();
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
        _body.AddChild(UiTheme.Label("New character", 34, UiTheme.Gold, HorizontalAlignment.Center));
        HBoxContainer nameRow = UiTheme.HBox(_body, 10);
        nameRow.AddChild(UiTheme.Label("Name", 18, UiTheme.Text));
        _name = new LineEdit { Text = _name?.Text ?? "", PlaceholderText = "Hero", MaxLength = 20, CustomMinimumSize = new Vector2(320f, 40f) };
        nameRow.AddChild(_name);

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
        Button create = UiTheme.Button("Create & play", Create, 20, 260f);
        buttons.AddChild(create);
        if (Core.GameSettings.Get(this) is { AdminMode: true })
        {
            buttons.AddChild(UiTheme.Button($"Test character (lv {CharacterData.MaxLevel})", CreateTest, 16, 240f));
        }

        buttons.AddChild(UiTheme.Button("Back", ShowList, 18, 160f));
        _name.CallDeferred(Control.MethodName.GrabFocus);
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
        Enter();
    }

    private void Create()
    {
        Profile?.CreateCharacter(_name?.Text ?? "", _newClass);
        Enter();
    }
}
