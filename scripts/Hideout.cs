#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Stats;
using ArpgGame.UI;
using ArpgGame.World;

namespace ArpgGame;

/// <summary>
/// The hideout town — docs/design/hideout.md. A small walled city: the Mirror in
/// the central plaza with Odran's stall and Ysolde's furnishings, the Artificer's
/// forge in the smithy, the stash in the vault, the Altar of Ascension in the
/// temple, training dummies in the yard and the Survival Rift at the north gate.
/// Everything that stands in it is a prop the player can rearrange (H opens the
/// editor), and the floors can be repaved with tiles bought from Ysolde. The
/// arrangement belongs to the account (<see cref="Profile.Hideout"/>).
/// </summary>
public partial class Hideout : Node2D
{
    private const float InteractRange = 80f;
    public const string EditAction = "edit_hideout";

    public HideoutTown Town { get; private set; } = null!;
    public HideoutData Data { get; private set; } = null!;
    public Player Player => _player;
    public HideoutEditor? Editor { get; private set; }
    public bool Editing => Editor is not null;

    private Player _player = null!;
    private Node2D _portal = null!;
    private bool _leaving;
    private MirrorPanel _mirror = null!;
    private AscendancyPanel _altar = null!;
    private StashPanel _stash = null!;
    private MerchantPanel _merchant = null!;
    private ArtificerPanel _artificer = null!;
    private DecorShopPanel _shop = null!;
    private Label _prompt = null!;
    private Label _status = null!;
    private ProgressBar _xp = null!;
    private readonly Dictionary<string, Node2D> _props = new();
    private readonly HashSet<TileMap> _dirtyMaps = new();
    private float _rebuildIn;

    /// <summary>Every prop node by its uid (stations and decorations).</summary>
    public IReadOnlyDictionary<string, Node2D> Props => _props;

    public override void _Ready()
    {
        GetTree().Paused = false;
        RenderingServer.SetDefaultClearColor(new Color(0.3f, 0.3f, 0.3f));
        _player = GetNode<Player>("Player");
        _portal = GetNode<Node2D>("Portal");

        Profile? profile = Profile.Get(this);
        Data = profile?.Hideout ?? new HideoutData();
        HideoutCatalog.EnsureDefaults(Data);
        Town = new HideoutTown { Data = Data };
        AddChild(Town);
        MoveChild(Town, 0);
        Town.MapChanged += map =>
        {
            _dirtyMaps.Add(map);
            _rebuildIn = 0.25f;
        };

        profile?.ApplyTo(_player);
        _player.GlobalPosition = HideoutTown.World(HideoutTown.SpawnCell);
        _player.UseCloseCamera();
        _player.GetNodeOrNull<CanvasLayer>("DebugReadout")?.Hide();
        _player.GetNode<PlayerSkillInput>("PlayerSkillInput").Mode = PlayerSkillInput.CastMode.Bound;
        Sfx.Music(this, "music_menu");

        AddChild(new PauseMenu
        {
            LeaveText = "Character select",
            OnLeave = () => Leave(Scenes.MainMenu),
            ExtraText = "Edit hideout",
            OnExtra = OpenEditor,
        });
        AddChild(new CharacterPanels { Player = _player, Name = "CharacterPanels" });
        _mirror = new MirrorPanel { Player = _player, Travel = Leave, Name = "MirrorPanel" };
        _altar = new AscendancyPanel { Player = _player, Name = "AscendancyPanel" };
        _stash = new StashPanel { Player = _player, Name = "StashPanel" };
        _merchant = new MerchantPanel { Player = _player, Name = "MerchantPanel" };
        _artificer = new ArtificerPanel { Player = _player, Name = "ArtificerPanel" };
        _shop = new DecorShopPanel { Player = _player, Data = Data, Name = "DecorShopPanel" };
        foreach (Node panel in new Node[] { _mirror, _altar, _stash, _merchant, _artificer, _shop })
        {
            AddChild(panel);
        }

        foreach (PlacedProp prop in Data.Props.ToList())
        {
            SpawnProp(prop);
        }

        EnsureEditAction();
        BuildHud();
    }

    private static void EnsureEditAction()
    {
        if (InputMap.HasAction(EditAction))
        {
            return;
        }

        InputMap.AddAction(EditAction);
        InputMap.ActionAddEvent(EditAction, new InputEventKey { PhysicalKeycode = Key.H });
    }

    // --- props -----------------------------------------------------------------------

    /// <summary>What using a station does (null for decorations).</summary>
    private Action? UseOf(string kind) => kind switch
    {
        "mirror" => _mirror.Open,
        "merchant" => _merchant.Open,
        "decorator" => _shop.Open,
        "forge" => _artificer.Open,
        "stash" => _stash.Open,
        "altar" => _altar.Open,
        _ => null,
    };

    /// <summary>Builds the node for a placed prop and puts it where the record says.</summary>
    public Node2D SpawnProp(PlacedProp prop)
    {
        PropDef def = HideoutCatalog.Prop(prop.Kind) ?? HideoutCatalog.Props[0];
        Node2D node = def.Id switch
        {
            "portal" => _portal,
            "altar" => new Altar { Name = "Altar" },
            "training_dummy" => Dummy(),
            "merchant" => Station("Merchant", def, ("paladin", new Vector2(0f, 14f))),
            "forge" => Station("Artificer", def, ("sage", new Vector2(-50f, 10f))),
            "decorator" => Decorator(),
            "mirror" => Station("Mirror", def),
            "stash" => Station("Stash", def),
            _ => Station($"Prop_{prop.Uid}", def),
        };
        if (node.GetParent() is null)
        {
            AddChild(node);
        }

        node.GlobalPosition = prop.Position;
        node.SetMeta("prop_uid", prop.Uid);
        if (def.Station && def.Id != "portal")
        {
            AddLabel(node, def.Name, LabelHeight(def));
        }
        else if (def.Id == "portal")
        {
            AddLabel(node, def.Name, 70f);
        }

        if (def.Radius > 0f && def.Id is not ("training_dummy" or "portal"))
        {
            var body = new StaticBody2D { CollisionLayer = Combat.CollisionLayers.World, CollisionMask = 0, Name = "Body" };
            body.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = def.Radius } });
            node.AddChild(body);
        }

        _props[prop.Uid] = node;
        return node;
    }

    private static float LabelHeight(PropDef def) => def.Id switch
    {
        "altar" => 64f,
        "merchant" => 86f,
        "decorator" => 64f,
        _ => SpriteHeight(def) * def.Scale + 16f,
    };

    private static float SpriteHeight(PropDef def) =>
        def.Sprite.Length > 0 && GD.Load<SpriteFrames>($"res://assets/sprites/{def.Sprite}.tres") is { } frames
            ? frames.GetFrameTexture("idle", 0)?.GetHeight() ?? 24f
            : 24f;

    /// <summary>A sprite standing on the node's origin (drawn above it in 2D; the
    /// voxel view stands its model on the same point).</summary>
    private static AnimatedSprite2D Sprite(string path, float scale)
    {
        var sprite = GD.Load<PackedScene>($"res://assets/sprites/{path}.tscn").Instantiate<AnimatedSprite2D>();
        sprite.Scale = Vector2.One * scale;
        float h = sprite.SpriteFrames?.GetFrameTexture("idle", 0)?.GetHeight() ?? 0f;
        sprite.Offset = new Vector2(0f, -h / 2f);
        sprite.Play("idle");
        return sprite;
    }

    private Node2D Station(string name, PropDef def, params (string Sprite, Vector2 At)[] npcs)
    {
        var node = new Node2D { Name = name, YSortEnabled = true };
        if (def.Sprite.Length > 0)
        {
            node.AddChild(Sprite(def.Sprite, def.Scale));
        }

        foreach ((string sprite, Vector2 at) in npcs)
        {
            node.AddChild(Npc(sprite, at));
        }

        return node;
    }

    private Node2D Decorator()
    {
        var node = new Node2D { Name = "Decorator", YSortEnabled = true };
        node.AddChild(Npc("ascendant", new Vector2(0f, 8f)));
        AnimatedSprite2D crate = Sprite("props/crate", 2f);
        crate.Position = new Vector2(-34f, 0f);
        node.AddChild(crate);
        AnimatedSprite2D plant = Sprite("props/potted_plant", 2f);
        plant.Position = new Vector2(32f, 2f);
        node.AddChild(plant);
        AnimatedSprite2D banner = Sprite("props/banner_blue", 2f);
        banner.Position = new Vector2(-22f, -14f);
        node.AddChild(banner);
        return node;
    }

    /// <summary>A standing townsperson from a class sprite.</summary>
    private static Node2D Npc(string sprite, Vector2 at)
    {
        var npc = new Node2D { Position = at };
        var body = GD.Load<PackedScene>($"res://assets/sprites/player/{sprite}.tscn").Instantiate<AnimatedSprite2D>();
        body.Position = new Vector2(0f, -6f);
        npc.AddChild(body);
        body.Play("idle");
        return npc;
    }

    private static Node2D Dummy()
    {
        var dummy = GD.Load<PackedScene>("res://scenes/combat/TargetDummy.tscn").Instantiate<Node2D>();
        dummy.GetNode<ActorStats>("Stats").Bases = GD.Load<StatBlock>("res://resources/stats/TrainingDummy.tres");
        AnimatedSprite2D sprite = GD.Load<PackedScene>("res://assets/sprites/props/training_dummy.tscn").Instantiate<AnimatedSprite2D>();
        sprite.Name = "Sprite";
        sprite.Position = new Vector2(0f, -12f);
        sprite.Scale = new Vector2(2.5f, 2.5f);
        dummy.AddChild(sprite);
        return dummy;
    }

    private static void AddLabel(Node2D node, string text, float above)
    {
        Label l = UiTheme.Label(text, 14, new Color(0.85f, 0.8f, 1f), HorizontalAlignment.Center);
        l.Name = "Label";
        l.Size = new Vector2(220f, 20f);
        l.Position = new Vector2(-110f, -above - 10f);
        l.ZIndex = 5;
        l.MouseFilter = Control.MouseFilterEnum.Ignore;
        node.AddChild(l);
    }

    /// <summary>Moves a prop and records where it now stands.</summary>
    public void MoveProp(string uid, Vector2 to)
    {
        if (Data.Find(uid) is not { } prop || !_props.TryGetValue(uid, out Node2D? node))
        {
            return;
        }

        prop.Position = to;
        node.GlobalPosition = to;
    }

    /// <summary>Takes a decoration down into storage (stations can't be stored).</summary>
    public bool StoreProp(string uid)
    {
        if (Data.Find(uid) is not { } prop || HideoutCatalog.Prop(prop.Kind) is not { Station: false } def)
        {
            return false;
        }

        Data.Props.Remove(prop);
        Data.AddStock(def.Id, 1);
        if (_props.Remove(uid, out Node2D? node))
        {
            node.QueueFree();
        }

        return true;
    }

    /// <summary>Places a stored decoration at <paramref name="at"/>.</summary>
    public string? PlaceFromStock(string kind, Vector2 at)
    {
        if (Data.StockOf(kind) <= 0 || HideoutCatalog.Prop(kind) is not { Station: false })
        {
            return null;
        }

        Data.AddStock(kind, -1);
        var prop = new PlacedProp { Uid = HideoutCatalog.NewUid(Data), Kind = kind, Position = at };
        Data.Props.Add(prop);
        SpawnProp(prop);
        return prop.Uid;
    }

    /// <summary>The prop under a world point, nearest first (edit mode picking).</summary>
    public string? PropAt(Vector2 world)
    {
        string? best = null;
        float bestD = float.MaxValue;
        foreach (PlacedProp p in Data.Props)
        {
            float pick = HideoutCatalog.Prop(p.Kind)?.PickRadius ?? 24f;
            // Props stand on their origin and rise above it: pick around their middle.
            float d = (p.Position + new Vector2(0f, -pick * 0.6f)).DistanceTo(world);
            if (d < pick && d < bestD)
            {
                best = p.Uid;
                bestD = d;
            }
        }

        return best;
    }

    // --- edit mode -------------------------------------------------------------------

    public void OpenEditor()
    {
        if (Editing || _leaving)
        {
            return;
        }

        GetTree().Paused = false;
        Editor = new HideoutEditor { Hideout = this, Name = "HideoutEditor" };
        Editor.Closed += () =>
        {
            Editor = null;
            SaveLayout();
        };
        AddChild(Editor);
    }

    public void SaveLayout()
    {
        if (Profile.Get(this) is { } profile)
        {
            profile.Hideout = Data;
            profile.CaptureFrom(_player);
        }
    }

    // --- HUD and frame ---------------------------------------------------------------

    private void BuildHud()
    {
        var layer = new CanvasLayer { Layer = 4 };
        AddChild(layer);
        Control root = UiTheme.FullRect(layer);

        _xp = UiTheme.Bar(UiTheme.Experience, 8f);
        root.AddChild(_xp);
        _xp.SetAnchorsPreset(Control.LayoutPreset.TopWide, true);
        _xp.OffsetBottom = 8f;
        _status = UiTheme.Label("", 18, UiTheme.Gold);
        root.AddChild(_status);
        _status.Position = new Vector2(16f, 16f);

        var hintBack = new ColorRect { Color = new Color(0.03f, 0.02f, 0.05f, 0.75f), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(hintBack);
        hintBack.SetAnchorsPreset(Control.LayoutPreset.BottomWide, true);
        hintBack.OffsetTop = -72f;
        Label hint = UiTheme.Label(
            "HIDEOUT — E to use: Mirror · Odran (trade) · Ysolde (decor) · forge (smithy) · stash (vault) · altar (temple) · north gate: Survival Rift\nI inventory/crafting · C character · K skills · P passive tree · H edit hideout · Shift sprint · Space dodge · Q/R flasks · Esc menu",
            17, UiTheme.Text, HorizontalAlignment.Center);
        root.AddChild(hint);
        hint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom, true);
        hint.OffsetLeft = -620f;
        hint.OffsetRight = 620f;
        hint.OffsetTop = -64f;

        _prompt = UiTheme.Label("", 22, UiTheme.Gold, HorizontalAlignment.Center);
        root.AddChild(_prompt);
        _prompt.SetAnchorsPreset(Control.LayoutPreset.CenterBottom, true);
        _prompt.OffsetLeft = -300f;
        _prompt.OffsetRight = 300f;
        _prompt.OffsetTop = -150f;
    }

    public override void _Process(double delta)
    {
        CharacterBuild? build = CharacterBuild.Of(_player);
        if (build is not null)
        {
            CharacterData c = build.Data;
            _xp.Value = build.ExperienceFraction;
            string points = c.PassivePointsFree > 0 ? $"  ·  {c.PassivePointsFree} passive point{(c.PassivePointsFree == 1 ? "" : "s")} (P)" : "";
            string asc = c.AscendancyPointsFree > 0 ? $"  ·  {c.AscendancyPointsFree} ascendancy point{(c.AscendancyPointsFree == 1 ? "" : "s")}" : "";
            _status.Text = $"{c.Name} — level {c.Level} {c.AscendancyDef?.Name ?? c.Class.Name}  ·  gold {_player.Wallet.GetBalance(Profile.GoldId)}  ·  best tier {c.HighestTierCleared}{points}{asc}";
        }

        // Repaved floors: rebuild their voxel terrain once painting pauses.
        if (_dirtyMaps.Count > 0 && (_rebuildIn -= (float)delta) <= 0f)
        {
            if (View3D.VoxelStage.Of(this) is { } stage)
            {
                foreach (TileMap map in _dirtyMaps)
                {
                    stage.RebuildTerrain(map);
                }
            }

            _dirtyMaps.Clear();
        }

        if (Editing)
        {
            _prompt.Text = "";
            return;
        }

        if (!GetTree().Paused && Input.IsActionJustPressed(EditAction) && !PanelStack.HasOpen)
        {
            OpenEditor();
            return;
        }

        (string Name, Action Use)? near = Nearest();
        _prompt.Text = near is { } n && !GetTree().Paused ? $"[E] {n.Name}" : "";
        if (near is { } target && !GetTree().Paused && Input.IsActionJustPressed("interact"))
        {
            target.Use();
        }
    }

    private (string Name, Action Use)? Nearest()
    {
        foreach (PlacedProp p in Data.Props)
        {
            if (HideoutCatalog.Prop(p.Kind) is { Station: true } def && UseOf(def.Id) is { } use
                && _player.GlobalPosition.DistanceTo(p.Position) < InteractRange)
            {
                return (def.Name, use);
            }
        }

        return null;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_leaving && !Editing && _player.GlobalPosition.DistanceTo(_portal.GlobalPosition) < 42f)
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
        SaveLayout();
        Scenes.Go(this, scene);
    }

    private sealed partial class Altar : Node2D
    {
        private float _t;

        public override void _Process(double delta)
        {
            _t += (float)delta;
            QueueRedraw();
        }

        public override void _Draw()
        {
            bool ready = Profile.Get(this)?.Active is { AscendancyPointsFree: > 0 } || Profile.Get(this)?.Active is { AscendancyPoints: > 0, Ascendancy: null };
            Color glow = ready ? new Color(1f, 0.8f, 0.35f) : new Color(0.55f, 0.45f, 0.8f);
            float pulse = 0.6f + 0.4f * Mathf.Sin(_t * (ready ? 4f : 1.5f));
            DrawSetTransform(Vector2.Zero, 0f, new Vector2(1f, 0.55f));
            DrawCircle(Vector2.Zero, 40f, new Color(0.12f, 0.1f, 0.16f));
            DrawArc(Vector2.Zero, 38f, 0f, Mathf.Tau, 40, glow with { A = 0.5f + 0.5f * pulse }, 3f);
            DrawArc(Vector2.Zero, 28f, _t * 0.5f, _t * 0.5f + Mathf.Tau * 0.8f, 32, glow with { A = 0.7f }, 2f);
            for (int i = 0; i < 6; i++)
            {
                float a = _t * 0.3f + i * Mathf.Tau / 6f;
                DrawCircle(Vector2.FromAngle(a) * 33f, 3f, glow);
            }

            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            DrawRect(new Rect2(-8f, -34f, 16f, 30f), new Color(0.3f, 0.26f, 0.36f));
            DrawRect(new Rect2(-12f, -38f, 24f, 6f), new Color(0.42f, 0.36f, 0.5f));
            DrawCircle(new Vector2(0f, -44f), 6f + pulse * 2f, glow with { A = 0.9f });
        }
    }
}
