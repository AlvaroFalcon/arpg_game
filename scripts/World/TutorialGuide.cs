#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Core;
using ArpgGame.Enemies;
using ArpgGame.Items;
using ArpgGame.Skills;
using ArpgGame.UI;

namespace ArpgGame.World;

/// <summary>
/// Walks a new character through The Threshold, the introduction reality —
/// docs/design/tutorial.md. One objective at a time in a panel on the left;
/// each step finishes when the player has actually done the thing (moved,
/// dodged, killed the pack, picked up and equipped the item...), never on a
/// timer, and the guide hands out what a lesson needs (an item to equip, a gem
/// to look at) when the step starts. Polls state every frame instead of
/// wiring signals into half the codebase: the checks are cheap and it lives
/// for five minutes.
/// </summary>
public partial class TutorialGuide : CanvasLayer
{
    public RealityController Reality = null!;

    private sealed class Step
    {
        public string Title = "";
        public string Body = "";
        public Func<bool> Done = () => true;
        public Action? Enter;
    }

    private readonly List<Step> _steps = new();
    private int _index = -1;
    private Label _counter = null!;
    private Label _title = null!;
    private RichTextLabel _body = null!;
    private PanelContainer _panel = null!;
    private float _pulse;

    // Baselines captured when a step starts.
    private Vector2 _start;
    private bool _dodged;
    private int _killsAtStart;
    private EquipmentItem? _gift;

    public override void _Ready()
    {
        Layer = 4;
        BuildPanel();
        Reality.Player.Dodge!.Dodged += () => _dodged = true;
        DefineSteps();
        CallDeferred(nameof(Advance));
    }

    private Player Player => Reality.Player;

    /// <summary>Current step (0-based) and its title; past the end once finished.</summary>
    public int StepIndex => _index;
    public int StepCount => _steps.Count;
    public string StepTitle => _index >= 0 && _index < _steps.Count ? _steps[_index].Title : "";

    private void DefineSteps()
    {
        string lmb = SkillName(SkillSlotId.Lmb) ?? "your skill";
        _steps.Add(new Step
        {
            Title = "Find your feet",
            Body = $"Move with {Key("W")}{Key("A")}{Key("S")}{Key("D")}, or hold {Key("Ctrl")} and click to walk to the cursor.\nHead east, out of the camp.",
            Enter = () => _start = Player.GlobalPosition,
            Done = () => Player.GlobalPosition.DistanceTo(_start) > 420f,
        });
        _steps.Add(new Step
        {
            Title = "Sprint and dodge",
            Body = $"Hold {Key("Shift")} to sprint (it drains the yellow stamina bar).\nPress {Key("Space")} to dodge-roll: you can't be hit mid-roll.",
            Enter = () => _dodged = false,
            Done = () => _dodged,
        });
        _steps.Add(new Step
        {
            Title = "First blood",
            Body = $"Monsters ahead in the yard. Aim with the mouse and press {Key("LMB")} to use {lmb}.\nSkills are bound to {Key("LMB")} {Key("RMB")} {Key("1")}-{Key("4")}; mana refills over time.",
            Enter = () => _killsAtStart = Reality.Kills,
            Done = () => Reality.Kills >= _killsAtStart + 3 || RoomCleared("yard"),
        });
        _steps.Add(new Step
        {
            Title = "Loot",
            Body = $"Something dropped! Hover it to read it, then click it to pick it up.\n{Key("Z")} toggles item names on the ground.",
            Enter = () => _gift = Reality.DropItem(Player.GlobalPosition + new Vector2(70f, 0f), 0.6f),
            Done = () => _gift is null || !IsInstanceValid(_gift) || Bag()?.Placements.ContainsKey(_gift) == true || IsWorn(_gift),
        });
        _steps.Add(new Step
        {
            Title = "Gear up",
            Body = $"Open your inventory with {Key("I")} and right-click the new item to equip it.\nIts tooltip compares it with what you wear: green goes up, red goes down.",
            Done = () => _gift is null || !IsInstanceValid(_gift) || IsWorn(_gift),
        });
        _steps.Add(new Step
        {
            Title = "Grow stronger",
            Body = $"Kills give experience. Each level grants a passive point: spend it on the tree with {Key("P")}.\n{Key("K")} shows your skill gems, {Key("C")} your character sheet. Clear the cache to the east.",
            Enter = () => Reality.DropGem(Player.GlobalPosition + new Vector2(-60f, 30f)),
            Done = () => RoomCleared("cache"),
        });
        _steps.Add(new Step
        {
            Title = "The guardian",
            Body = "Something old guards the seal beyond the gate. The gate closes behind you once the fight starts.\nWatch the ground: red markings show where an attack will land, so dodge out of them.",
            Done = () => Reality.Completed,
        });
        _steps.Add(new Step
        {
            Title = "Home",
            Body = $"Collect the loot, then press {Key("E")} at the portal.\nYour hideout holds the Mirror of Realities: it opens new realities from the Mirror Shards you find. Harder shards, better loot.",
            Done = () => false,
        });
    }

    private void Advance()
    {
        if (_index >= 0)
        {
            Sfx.Play(this, "levelup", -10f);
        }

        _index++;
        if (_index >= _steps.Count)
        {
            _panel.Visible = false;
            return;
        }

        Step step = _steps[_index];
        step.Enter?.Invoke();
        _counter.Text = $"INTRODUCTION  ·  {_index + 1} / {_steps.Count}";
        _title.Text = step.Title;
        _body.Text = step.Body;
        _pulse = 1f;
    }

    public override void _Process(double delta)
    {
        if (_index < 0 || _index >= _steps.Count)
        {
            return;
        }

        _pulse = Mathf.Max(0f, _pulse - (float)delta * 1.5f);
        _title.Modulate = UiTheme.Gold.Lerp(Colors.White, _pulse);
        if (!(Player.Health?.IsDead ?? true) && _steps[_index].Done())
        {
            Advance();
        }
    }

    // --- queries -------------------------------------------------------------------

    /// <summary>Every monster whose pack spawned in that room is dead.</summary>
    private bool RoomCleared(string room) =>
        !Reality.GetChildren().OfType<Enemy>().Any(e => !e.Health.IsDead && e.HasMeta(RealityController.RoomMeta) && (string)e.GetMeta(RealityController.RoomMeta) == room);

    private Inventory? Bag() => Player.GetNodeOrNull<Inventory>("Inventory");

    private bool IsWorn(EquipmentItem item)
    {
        Equipment? equipment = Player.GetNodeOrNull<Equipment>("Equipment");
        return equipment is not null && Enum.GetValues<EquipmentSlot>().Any(s => equipment.GetEquipped(s) == item);
    }

    private string? SkillName(SkillSlotId slot) => Player.SkillUser.GetSkill(slot)?.Definition.DisplayName;

    private static string Key(string key) => $"[bgcolor=#2e2842][color=#ffd27a] {key} [/color][/bgcolor] ";

    // --- UI ------------------------------------------------------------------------

    private void BuildPanel()
    {
        _panel = new PanelContainer
        {
            Position = new Vector2(16f, 92f),
            CustomMinimumSize = new Vector2(380f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _panel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.07f, 0.06f, 0.1f, 0.88f), new Color(1f, 0.82f, 0.45f, 0.7f), 2, 6, 14));
        AddChild(_panel);
        VBoxContainer v = UiTheme.VBox(_panel, 6);
        v.MouseFilter = Control.MouseFilterEnum.Ignore;
        _counter = UiTheme.Label("", 12, UiTheme.TextDim);
        _title = UiTheme.Label("", 22, UiTheme.Gold);
        _body = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            CustomMinimumSize = new Vector2(352f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _body.AddThemeFontSizeOverride("normal_font_size", 15);
        _body.AddThemeColorOverride("default_color", UiTheme.Text);
        v.AddChild(_counter);
        v.AddChild(_title);
        v.AddChild(_body);
        v.AddChild(UiTheme.Label("Esc → skip the introduction", 11, UiTheme.TextDim));
    }
}
