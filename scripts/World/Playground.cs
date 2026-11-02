#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Enemies;
using ArpgGame.Items;
using ArpgGame.Progression;
using ArpgGame.UI;

namespace ArpgGame.World;

/// <summary>
/// The hidden playground — docs/design/playground.md (owner's brief, 2026-11-01): a place
/// to try builds against hordes of any Mirror tier, or any boss at any tier. Entered from
/// the hideout with Ctrl+Shift+P (or the <c>--playground</c> game flag), never from a menu.
///
/// It plays a <b>sandbox copy</b> of the active character: every skill owned at level 20,
/// a mountain of gold and orbs, the same class, tree, ascendancy and gear. The profile stops
/// writing to disk on the way in (<see cref="Profile.Persist"/>) and is reloaded from disk on
/// the way out (<see cref="RestoreProfile"/>), so nothing done here survives: not the gold,
/// not a crafted item, not a tree point. Escaldón and the Artificer stand in the camp (F),
/// and F2 opens the spawner.
/// </summary>
public partial class Playground : Node
{
    public const long SandboxGold = 50_000_000;
    public const long SandboxOrbs = 999;

    /// <summary>What <see cref="Profile.Persist"/> was before the playground (restored on the way out).</summary>
    private static bool _persistBefore = true;

    public RealityController Reality = null!;

    private SkillTreePanel _escaldon = null!;
    private ArtificerPanel _artificer = null!;
    private readonly List<(Node2D Npc, string Name, ModalPanel Panel)> _npcs = new();
    private Label _prompt = null!;
    private CanvasLayer _layer = null!;
    private PanelContainer _console = null!;
    private SpinBox _tier = null!;
    private SpinBox _count = null!;
    private SpinBox _level = null!;
    private OptionButton _rarity = null!;
    private Button _spawn = null!;
    private MonsterPortrait _portrait = null!;
    private Label _cardTitle = null!;
    private Label _card = null!;
    private readonly Dictionary<string, Button> _picks = new();

    /// <summary>The picked monster or boss (null = a mixed horde).</summary>
    public string? Picked { get; private set; }
    private CheckBox _god = null!;
    private CheckBox _still = null!;
    private CheckBox _pacified = null!;
    private Label _log = null!;
    private BossEnemy? _bossAlive;
    private float _bossSince;

    /// <summary>The DPS meter (F3).</summary>
    public DpsMeter Meter { get; private set; } = null!;

    /// <summary>The console is showing (tests).</summary>
    public bool ConsoleOpen => _console.Visible;

    // --- entering and leaving -------------------------------------------------------------

    /// <summary>
    /// From the hideout: saves the real character, stops all writes, makes the sandbox copy
    /// active and loads the playground. Returns why not, or null.
    /// </summary>
    public static string? Enter(Node from, Player player)
    {
        if (Net.NetSession.Get(from) is { Active: true })
        {
            return "The playground is solo only";
        }

        if (Profile.Get(from) is not { } profile)
        {
            return "No profile";
        }

        profile.CaptureFrom(player);
        profile.Save();
        _persistBefore = profile.Persist;
        profile.Persist = false;
        CharacterData sandbox = MakeSandbox(profile.Active);
        profile.Characters.Add(sandbox);
        profile.Select(profile.Characters.Count - 1);
        foreach (CraftingCurrency orb in Crafting.All)
        {
            profile.Currency[orb.Id] = SandboxOrbs;
        }

        RealityLaunch.Reset();
        RealityLaunch.Playground = true;
        RealityLaunch.LayoutId = LayoutCatalog.Playground.Id;
        return null;
    }

    /// <summary>A copy of <paramref name="real"/> with every skill at level 20 and gold to spare.</summary>
    public static CharacterData MakeSandbox(CharacterData real)
    {
        CharacterData copy = Profile.DeserializeCharacter(Profile.SerializeCharacter(real)) ?? CharacterData.Create(real.Name, real.ClassId);
        copy.Name = real.Name + " (playground)";
        copy.TutorialDone = true;
        foreach (SkillEntry entry in UpgradeCatalog.Skills)
        {
            if (copy.Gems.FirstOrDefault(g => g.Id == entry.Id && !g.IsSupport) is { } owned)
            {
                owned.Level = GemData.MaxLevel;
            }
            else
            {
                copy.AddGem(entry.Id, false, GemData.MaxLevel);
            }
        }

        copy.Gold = SandboxGold;
        copy.GemShopSeenLevel = CharacterData.MaxLevel;
        return copy;
    }

    /// <summary>Throws the sandbox away: the profile is read back from disk and writes resume.</summary>
    public static void RestoreProfile(Node context)
    {
        if (Profile.Get(context) is not { } profile)
        {
            return;
        }

        profile.Load();
        profile.Persist = _persistBefore;
    }

    // --- the place ----------------------------------------------------------------------

    public override void _Ready()
    {
        Player player = Reality.Player;
        _escaldon = new SkillTreePanel { Player = player, Name = "SkillTreePanel" };
        _artificer = new ArtificerPanel { Player = player, Name = "ArtificerPanel" };
        Reality.AddChild(_escaldon);
        Reality.AddChild(_artificer);

        Vector2 camp = RasterLayout.World(Reality.Raster.Entrance);
        _npcs.Add((Npc("assassin", "Escaldón", camp + new Vector2(-60f, -70f)), "Escaldón", _escaldon));
        _npcs.Add((Npc("sage", "the Artificer", camp + new Vector2(60f, -70f)), "the Artificer", _artificer));

        _layer = new CanvasLayer { Layer = 6, Name = "PlaygroundUi" };
        AddChild(_layer);
        _prompt = UiTheme.Label("", 16, UiTheme.Gold, HorizontalAlignment.Center);
        _prompt.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _prompt.Position = new Vector2(-200f, -170f);
        _prompt.Size = new Vector2(400f, 30f);
        _prompt.AnchorLeft = 0.5f;
        _prompt.AnchorRight = 0.5f;
        _prompt.AnchorTop = 1f;
        _prompt.AnchorBottom = 1f;
        _layer.AddChild(_prompt);
        BuildConsole();
        // Right edge, under the quest tracker.
        Meter = new DpsMeter { Owner = player, Name = "DpsMeter", AnchorLeft = 1f, AnchorRight = 1f, OffsetLeft = -276f, OffsetRight = -16f, OffsetTop = 350f };
        _layer.AddChild(Meter);
        KeepKeyboardForTheGame(Meter);
        _console.Visible = true;
    }

    private Node2D Npc(string sprite, string name, Vector2 at)
    {
        var npc = new Node2D { Position = at, Name = name.Replace(" ", "") };
        var body = GD.Load<PackedScene>($"res://assets/sprites/player/{sprite}.tscn").Instantiate<AnimatedSprite2D>();
        body.Position = new Vector2(0f, -6f);
        npc.AddChild(body);
        body.Play("idle");
        Label tag = UiTheme.Label(name, 13, SkillTreePanel.Accent, HorizontalAlignment.Center);
        tag.Position = new Vector2(-70f, -58f);
        tag.Size = new Vector2(140f, 20f);
        npc.AddChild(tag);
        Reality.AddChild(npc);
        return npc;
    }

    private (Node2D Npc, string Name, ModalPanel Panel)? Near()
    {
        Vector2 at = Reality.Player.GlobalPosition;
        foreach (var n in _npcs)
        {
            if (n.Npc.GlobalPosition.DistanceTo(at) < 70f)
            {
                return n;
            }
        }

        return null;
    }

    public override void _Process(double delta)
    {
        var near = PanelStack.HasOpen ? null : Near();
        _prompt.Text = near is { } n ? $"F — talk to {n.Name}" : "";
        if (near is { } open && !GetTree().Paused && Input.IsActionJustPressed("interact"))
        {
            open.Panel.Open();
        }

        if (_god.ButtonPressed && Reality.Player.Health is { } h)
        {
            h.IgnoreDamage = true;
        }

        if (_bossAlive is not null)
        {
            _bossSince += (float)delta;
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F2 })
        {
            _console.Visible = !_console.Visible;
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
        {
            Meter.Visible = !Meter.Visible;
            GetViewport().SetInputAsHandled();
        }
    }

    // --- the spawner (F2) ----------------------------------------------------------------

    private void BuildConsole()
    {
        _console = new PanelContainer { Name = "Spawner", MouseFilter = Control.MouseFilterEnum.Stop };
        _console.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.06f, 0.05f, 0.09f, 0.94f), SkillTreePanel.Accent, 2, 6, 12));
        // Left edge: the right one holds the minimap, the clock and the quest tracker.
        _console.OffsetLeft = 16f;
        _console.OffsetTop = 70f;
        _layer.AddChild(_console);
        HBoxContainer columns = UiTheme.HBox(_console, 14);
        VBoxContainer box = UiTheme.VBox(columns, 6);
        box.CustomMinimumSize = new Vector2(260f, 0f);
        box.AddChild(UiTheme.Label("Playground · F2", 18, SkillTreePanel.Accent));
        box.AddChild(Dim("Nothing here is saved. Escaldón and the Artificer are in the camp (F)."));

        _tier = Spin(box, "Mirror tier", 1, RealityTiers.MaxTier, 1);
        _tier.ValueChanged += v =>
        {
            Reality.SetPlaygroundTier((int)v);
            ShowCard();
        };
        _count = Spin(box, "Monsters", 1, 80, 20);
        _count.ValueChanged += _ => ShowCard();
        _rarity = Options(box, "Rarity", new[] { "Reality mix", "Normal", "Magic", "Rare", "Unique" });
        _rarity.ItemSelected += _ => ShowCard();
        _spawn = UiTheme.PrimaryButton("Spawn", () => SpawnPicked(), 15, 260f);
        box.AddChild(_spawn);
        box.AddChild(UiTheme.Button("Training dummy (never dies)", () => SpawnDummy(), 14, 260f));
        box.AddChild(UiTheme.Button("Clear monsters", ClearMonsters, 14, 260f));

        BuildPicker(columns);
        _level = Spin(box, "Character level", 1, CharacterData.MaxLevel, CharacterBuild.Of(Reality.Player)?.Data.Level ?? 1);
        _level.ValueChanged += v => SetLevel((int)v);
        box.AddChild(UiTheme.Button("Full life, mana and cooldowns", Refill, 14, 260f));
        _still = Toggle(box, "Monsters stand still", on => Enemy.HoldStill = on);
        _pacified = Toggle(box, "Monsters don't attack", on => Enemy.Pacified = on);
        _god = new CheckBox { Text = "Can't die", FocusMode = Control.FocusModeEnum.None };
        _god.AddThemeColorOverride("font_color", UiTheme.Text);
        _god.Toggled += on =>
        {
            if (!on && Reality.Player.Health is { } h)
            {
                h.IgnoreDamage = false;
            }
        };
        box.AddChild(_god);
        _log = Dim("");
        box.AddChild(_log);
        KeepKeyboardForTheGame(_console);
        Pick(null);
    }

    // --- the monster picker: a card per monster and boss, a portrait and its numbers ------------

    private void BuildPicker(HBoxContainer columns)
    {
        VBoxContainer side = UiTheme.VBox(columns, 6);
        side.CustomMinimumSize = new Vector2(250f, 0f);
        side.AddChild(UiTheme.Label("Enemies", 15, SkillTreePanel.Accent));
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        side.AddChild(grid);
        AddPick(grid, null, "Any");
        foreach (string type in RealityController.AllMonsters.Concat(RealityController.AllBosses))
        {
            AddPick(grid, type, Pretty(type));
        }

        HBoxContainer card = UiTheme.HBox(side, 8);
        _portrait = new MonsterPortrait { CustomMinimumSize = new Vector2(84f, 84f) };
        card.AddChild(_portrait);
        _cardTitle = UiTheme.Label("", 15, UiTheme.Gold);
        _cardTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _cardTitle.CustomMinimumSize = new Vector2(150f, 0f);
        _cardTitle.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        card.AddChild(_cardTitle);
        _card = UiTheme.Label("", 12, UiTheme.Text);
        _card.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _card.CustomMinimumSize = new Vector2(246f, 0f);
        side.AddChild(_card);
    }

    private void AddPick(GridContainer grid, string? type, string name)
    {
        var b = new Button { CustomMinimumSize = new Vector2(58f, 58f), FocusMode = Control.FocusModeEnum.None, ClipContents = true };
        if (type is null)
        {
            b.Text = "Any";
        }
        else
        {
            b.AddChild(new MonsterPortrait { Type = type, MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(3f, 3f), Size = new Vector2(52f, 52f) });
        }

        UiTheme.InstantTooltip(b, type is null ? "Any: a mixed horde from every reality" : name);
        b.Pressed += () => Pick(type);
        grid.AddChild(b);
        _picks[type ?? ""] = b;
    }

    /// <summary>Picks what Spawn makes: a monster, a boss, or null for a mixed horde (tests too).</summary>
    public void Pick(string? type)
    {
        Picked = type;
        foreach ((string key, Button b) in _picks)
        {
            bool on = key == (type ?? "");
            b.AddThemeStyleboxOverride("normal", UiTheme.Box(on ? new Color(0.1f, 0.2f, 0.18f) : new Color(0.08f, 0.07f, 0.11f), on ? SkillTreePanel.Accent : UiTheme.PanelBorder, on ? 2 : 1, 4, 2));
        }

        ShowCard();
    }

    private MonsterRarity RarityPicked => _rarity.Selected switch { 2 => MonsterRarity.Magic, 3 => MonsterRarity.Rare, 4 => MonsterRarity.Unique, _ => MonsterRarity.Normal };

    private bool PickedIsBoss => Picked is { } p && RealityController.AllBosses.Contains(p);

    /// <summary>The picked enemy's numbers at the console's tier and rarity (tests read it).</summary>
    public string CardText => _card.Text;

    private void ShowCard()
    {
        if (_card is null || _spawn is null)
        {
            return;
        }

        int count = (int)_count.Value;
        _spawn.Text = Picked is null ? $"Spawn {count} mixed" : PickedIsBoss ? $"Spawn {Pretty(Picked)}" : $"Spawn {count} × {Pretty(Picked)}";
        _portrait.Type = Picked;
        if (Picked is null)
        {
            _cardTitle.Text = "A mixed horde";
            _card.Text = $"Every reality's monsters, at tier {(int)_tier.Value} (monster level {Reality.MonsterLevel}). Pick one for its numbers.";
            return;
        }

        Reality.SetPlaygroundTier((int)_tier.Value);
        RealityController.MonsterCard m = Reality.Preview(Picked, RarityPicked);
        string rarity = m.Boss ? "Boss" : m.Rarity == MonsterRarity.Normal ? (_rarity.Selected == 0 ? "Normal (reality mix)" : "Normal") : m.Rarity.ToString();
        _cardTitle.Text = $"{m.Name}\n{rarity} · level {m.Level}";
        var lines = new List<string>
        {
            m.Behaviour,
            $"Life {m.Life:N0}",
            $"Armour {m.Armour:N0}: a 100 physical hit deals {m.PhysicalTaken:0}",
            $"Resistances: fire {m.Fire:0}% · cold {m.Cold:0}% · lightning {m.Lightning:0}%",
            $"Speed {m.Speed:0} px/s",
        };
        if (m.ContactDamage > 0f)
        {
            lines.Add($"Contact hit {m.ContactDamage:N0} every {m.ContactCooldown:0.#} s");
        }

        if (m.Attack is { } attack)
        {
            lines.Add(attack);
        }

        _card.Text = string.Join("\n", lines);
    }

    /// <summary>Spawn: the picked boss, or a horde of the picked monster (or mixed).</summary>
    private void SpawnPicked()
    {
        if (PickedIsBoss)
        {
            SpawnBoss();
        }
        else
        {
            SpawnHorde();
        }
    }

    /// <summary>
    /// The panels are for the mouse: a clicked button kept keyboard focus, so Space (the dodge)
    /// pressed it again, and a number field kept the keys. Buttons, lists and checkboxes never
    /// take focus; a number field gives it back once its value is set, or when the mouse leaves
    /// the panel or clicks the world.
    /// </summary>
    private void KeepKeyboardForTheGame(Control root)
    {
        foreach (Node n in root.FindChildren("*", "", true, false))
        {
            switch (n)
            {
                case BaseButton b:
                    b.FocusMode = Control.FocusModeEnum.None;
                    break;
                case SpinBox spin:
                    LineEdit field = spin.GetLineEdit();
                    spin.ValueChanged += _ => field.ReleaseFocus();
                    field.TextSubmitted += _ => field.ReleaseFocus();
                    break;
            }
        }

        root.MouseExited += () =>
        {
            if (!root.GetGlobalRect().HasPoint(root.GetGlobalMousePosition()))
            {
                GetViewport().GuiReleaseFocus();
            }
        };
    }

    public override void _Input(InputEvent e)
    {
        // A click in the world (outside both panels) takes the focus off any field.
        if (e is InputEventMouseButton { Pressed: true } click && GetViewport().GuiGetFocusOwner() is not null
            && !_console.GetGlobalRect().HasPoint(click.Position) && !Meter.GetGlobalRect().HasPoint(click.Position))
        {
            GetViewport().GuiReleaseFocus();
        }
    }

    /// <summary>"EmberColossus" → "Ember Colossus".</summary>
    private static string Pretty(string id) => System.Text.RegularExpressions.Regex.Replace(id, "(?<=[a-z])(?=[A-Z])", " ");

    private static CheckBox Toggle(VBoxContainer box, string text, System.Action<bool> set)
    {
        var check = new CheckBox { Text = text, FocusMode = Control.FocusModeEnum.None };
        check.AddThemeColorOverride("font_color", UiTheme.Text);
        check.Toggled += on => set(on);
        box.AddChild(check);
        return check;
    }

    /// <summary>Sets the monster switches as the checkboxes would (tests).</summary>
    public void SetMonsters(bool standStill, bool pacified)
    {
        _still.ButtonPressed = standStill;
        _pacified.ButtonPressed = pacified;
    }

    public override void _ExitTree()
    {
        // Static switches: never let them leak into a real reality.
        Enemy.HoldStill = false;
        Enemy.Pacified = false;
    }

    private static Label Dim(string text)
    {
        Label l = UiTheme.Label(text, 12, UiTheme.TextDim);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(260f, 0f);
        return l;
    }

    private static SpinBox Spin(VBoxContainer box, string label, int min, int max, int value)
    {
        HBoxContainer row = UiTheme.HBox(box, 8);
        Label l = UiTheme.Label(label, 14, UiTheme.Text);
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(l);
        var spin = new SpinBox { MinValue = min, MaxValue = max, Value = value, Rounded = true, CustomMinimumSize = new Vector2(96f, 0f) };
        row.AddChild(spin);
        return spin;
    }

    private static OptionButton Options(VBoxContainer box, string label, IEnumerable<string> items)
    {
        box.AddChild(UiTheme.Label(label, 13, UiTheme.TextDim));
        var opt = new OptionButton { CustomMinimumSize = new Vector2(260f, 30f), FocusMode = Control.FocusModeEnum.None };
        foreach (string i in items)
        {
            opt.AddItem(i);
        }

        box.AddChild(opt);
        return opt;
    }

    /// <summary>Where spawns land: the arena's middle, or near the player when they are in it.</summary>
    private Vector2 SpawnPoint()
    {
        Vector2 arena = RasterLayout.World(Reality.Raster.BossCell);
        Vector2 p = Reality.Player.GlobalPosition;
        return p.DistanceTo(arena) < 420f ? p + p.DirectionTo(arena).Rotated(0.3f) * 220f + new Vector2(0f, 1f) : arena;
    }

    /// <summary>Spawns the horde the console describes (tests call it).</summary>
    public int SpawnHorde()
    {
        Reality.SetPlaygroundTier((int)_tier.Value);
        string? type = PickedIsBoss ? null : Picked;
        MonsterRarity? rarity = _rarity.Selected switch { 1 => MonsterRarity.Normal, 2 => MonsterRarity.Magic, 3 => MonsterRarity.Rare, 4 => MonsterRarity.Unique, _ => null };
        List<Enemy> list = Reality.SpawnPlaygroundHorde((int)_count.Value, type, rarity, SpawnPoint());
        _log.Text = $"{list.Count} {(type ?? "monsters")} at tier {(int)_tier.Value} (monster level {Reality.MonsterLevel})";
        return list.Count;
    }

    /// <summary>Spawns the console's boss at its tier and times the kill (tests call it).</summary>
    public BossEnemy SpawnBoss()
    {
        Reality.SetPlaygroundTier((int)_tier.Value);
        string type = PickedIsBoss ? Picked! : RealityController.AllBosses[0];
        BossEnemy boss = Reality.SpawnPlaygroundBoss(type, SpawnPoint());
        _bossAlive = boss;
        _bossSince = 0f;
        boss.Killed += _ =>
        {
            _log.Text = $"{boss.DisplayName} (tier {Reality.Tier}) died in {_bossSince:0.0} s";
            _bossAlive = null;
            Reality.EmitSignal(RealityController.SignalName.Toast, $"{boss.DisplayName} slain in {_bossSince:0.0} s", SkillTreePanel.Accent);
        };
        _log.Text = $"{boss.DisplayName} at tier {Reality.Tier}: {Mathf.RoundToInt(boss.Stats.Life?.Max ?? 0f):N0} life";
        return boss;
    }

    /// <summary>An immortal training dummy (no armour, no resistances) for the DPS meter: it
    /// stands still and heals every hit, so a rotation can be measured for as long as wanted.</summary>
    public Combat.TargetDummy SpawnDummy()
    {
        var dummy = GD.Load<PackedScene>("res://scenes/combat/TargetDummy.tscn").Instantiate<Combat.TargetDummy>();
        dummy.Immortal = true;
        Reality.AddChild(dummy);
        dummy.GlobalPosition = SpawnPoint();
        dummy.AddChild(new Combat.Ailments { Name = "Ailments" });
        _log.Text = "A training dummy: no armour, no resistances, never dies";
        return dummy;
    }

    /// <summary>Every playground monster gone (no loot, no experience).</summary>
    public void ClearMonsters()
    {
        foreach (Node n in GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup))
        {
            if (n is Enemy e && e.GetParent() == Reality)
            {
                e.QueueFree();
            }
            else if (n is Combat.TargetDummy d && d.GetParent() == Reality)
            {
                d.QueueFree();
            }
        }

        _bossAlive = null;
        _log.Text = "Cleared";
    }

    private void SetLevel(int level)
    {
        if (CharacterBuild.Of(Reality.Player) is not { } build)
        {
            return;
        }

        build.Data.Level = Mathf.Clamp(level, 1, CharacterData.MaxLevel);
        build.Data.Experience = 0f;
        build.Rebuild();
        Refill();
        _log.Text = $"Character level {build.Data.Level} (passive points to spend: {build.Data.PassivePointsFree})";
    }

    private void Refill()
    {
        Player p = Reality.Player;
        if (p.Stats.Life is { } life)
        {
            life.Current = life.Max;
        }

        if (p.Stats.Mana is { } mana)
        {
            mana.Current = mana.Max;
        }

        foreach ((Skills.SkillSlotId _, Skills.SkillInstance s) in p.SkillUser.EquippedSkills())
        {
            s.UseTimeRemaining = 0f;
            s.ChargesAvailable = s.Definition.MaxCharges;
            s.RechargeTimer = 0f;
        }
    }
}

/// <summary>A monster's sprite, animated, scaled to fit (the playground's picker and stat card).</summary>
public partial class MonsterPortrait : Control
{
    private static readonly Dictionary<string, (SpriteFrames Frames, StringName Anim)?> Cache = new();
    private string? _type;
    private float _t;

    public string? Type
    {
        get => _type;
        set
        {
            _type = value;
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    private static (SpriteFrames Frames, StringName Anim)? FramesOf(string type)
    {
        if (Cache.TryGetValue(type, out var hit))
        {
            return hit;
        }

        Node scene = GD.Load<PackedScene>($"res://scenes/enemies/{type}.tscn").Instantiate();
        (SpriteFrames, StringName)? found = null;
        if (scene.GetNodeOrNull<AnimatedSprite2D>("Sprite") is { SpriteFrames: { } frames } sprite)
        {
            StringName anim = frames.HasAnimation("idle") ? "idle" : frames.HasAnimation("walk") ? "walk" : sprite.Animation;
            found = (frames, anim);
        }

        scene.Free();
        Cache[type] = found;
        return found;
    }

    public override void _Process(double delta)
    {
        if (_type is not null && IsVisibleInTree())
        {
            _t += (float)delta;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_type is null || FramesOf(_type) is not { } f || f.Frames.GetFrameCount(f.Anim) == 0)
        {
            return;
        }

        float fps = Mathf.Max(1f, (float)f.Frames.GetAnimationSpeed(f.Anim));
        int frame = (int)(_t * fps) % f.Frames.GetFrameCount(f.Anim);
        Texture2D tex = f.Frames.GetFrameTexture(f.Anim, frame);
        Vector2 size = tex.GetSize();
        float k = Mathf.Min(Size.X / size.X, Size.Y / size.Y);
        k = k >= 1f ? Mathf.Floor(k) : k; // whole steps keep the pixels square
        Vector2 drawn = size * k;
        DrawTextureRect(tex, new Rect2((Size - drawn) / 2f, drawn), false);
    }
}
