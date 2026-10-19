#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Net;
using ArpgGame.Quests;
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

    /// <summary>The town on screen: the account's own, or the host's when visiting a friend's party.</summary>
    public HideoutData Data { get; private set; } = null!;

    /// <summary>This account's own town (storage, purchases, the guide flag). The same object as
    /// <see cref="Data"/> unless this machine is a guest (docs/design/hideout.md -> "Co-op: whose town").</summary>
    public HideoutData OwnTown { get; private set; } = null!;

    /// <summary>A guest in the host's town: nothing here is saved as this account's layout.</summary>
    public bool IsGuest { get; private set; }

    public PauseMenu Pause { get; private set; } = null!;

    /// <summary>The party hooks (presence, puppets); idle offline.</summary>
    public HideoutParty Party { get; private set; } = null!;
    public Player Player => _player;
    public HideoutEditor? Editor { get; private set; }
    public bool Editing => Editor is not null;

    private Player _player = null!;
    private NetSession? _session;
    private bool _inPortal;
    private Node2D _portal = null!;
    private bool _leaving;
    private MirrorPanel _mirror = null!;
    private AscendancyPanel _altar = null!;
    private StashPanel _stash = null!;
    private MerchantPanel _merchant = null!;
    private ArtificerPanel _artificer = null!;
    private DecorShopPanel _shop = null!;
    private ShardVendorPanel _dealer = null!;
    private TownGuidePanel _guide = null!;
    private HideoutHud _hud = null!;
    private QuestDialog _questDialog = null!;
    private string? _highlighted;
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
        OwnTown = profile?.Hideout ?? new HideoutData();
        HideoutCatalog.EnsureDefaults(OwnTown);
        _session = NetSession.Get(this);
        IsGuest = _session is { IsClient: true };
        // A guest walks the host's town (a copy: it is never written back to this account).
        Data = IsGuest ? CopyOf(_session!.HostLayout) : OwnTown;
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

        Pause = new PauseMenu
        {
            LeaveText = "Character select",
            LeaveTextNow = LeaveLabel,
            OnLeave = OnPauseLeave,
            NoPause = _session is { Active: true },
            Extras =
            {
                // Owner decision 2026-10-18: inviting lives only here, in the hideout's menu.
                new PauseMenu.Entry { Text = "Invite friends", LiveText = PartyEntryLabel, OnPress = OpenPartyOverlay },
                new PauseMenu.Entry { Text = "Edit hideout", LiveText = () => IsGuest ? null : "Edit hideout", OnPress = OpenEditor },
            },
        };
        AddChild(Pause);
        AddChild(new CharacterPanels { Player = _player, Name = "CharacterPanels" });
        _mirror = new MirrorPanel { Player = _player, Travel = Travel, Propose = ProposeRift, InParty = () => _session is { Active: true }, Name = "MirrorPanel" };
        _altar = new AscendancyPanel { Player = _player, Name = "AscendancyPanel" };
        _stash = new StashPanel { Player = _player, Name = "StashPanel" };
        _merchant = new MerchantPanel { Player = _player, Name = "MerchantPanel" };
        _artificer = new ArtificerPanel { Player = _player, Name = "ArtificerPanel" };
        // Purchases always go to this account's own storage, even in a friend's town.
        _shop = new DecorShopPanel { Player = _player, Data = OwnTown, HostName = IsGuest ? _session!.HostName : null, Name = "DecorShopPanel" };
        _guide = new TownGuidePanel { Data = Data, SeenOn = OwnTown, ShowMe = WalkTo, Name = "TownGuide" };
        _dealer = new ShardVendorPanel { Player = _player, Name = "ShardVendorPanel" };
        _questDialog = new QuestDialog { Name = "QuestDialog" };
        foreach (Node panel in new Node[] { _mirror, _altar, _stash, _merchant, _artificer, _shop, _guide, _dealer, _questDialog })
        {
            AddChild(panel);
        }

        // Closing a station's panel with its quest done opens the turn-in (quests.md -> "E prompt and station panels").
        foreach ((ModalPanel panel, string kind) in new (ModalPanel, string)[] { (_mirror, "mirror"), (_altar, "altar"), (_merchant, "merchant"), (_artificer, "forge"), (_dealer, "arehucaz") })
        {
            string station = kind;
            panel.Closed += () => CallDeferred(nameof(TryAutoTurnIn), station);
        }

        if (GetNodeOrNull<CharacterPanels>("CharacterPanels") is { } panels)
        {
            panels.Quests.Hideout = this;
            panels.Quests.Blocked = () => Editing || _leaving;
        }

        AddChild(new QuestToast { Name = "QuestToast", Anchor = ToastAnchor });

        foreach (PlacedProp prop in Data.Props.ToList())
        {
            SpawnProp(prop);
        }

        EnsureEditAction();
        _hud = new HideoutHud { Hideout = this, Name = "HideoutHud" };
        AddChild(_hud);

        Party = new HideoutParty { Hideout = this };
        AddChild(Party);
        if (_session is not null)
        {
            _session.LayoutChanged += OnHostLayoutChanged;
            _session.Launching += OnLaunching;
            _session.LocalCharacter = CaptureCharacter;
            // Read only while hosting (also when hosting starts here, from "Invite friends").
            _session.LayoutSource = () => Data;
        }

        if (!OwnTown.GuideSeen && profile is not null)
        {
            // First visit on this account: the guide explains the town. The quests open behind it
            // (queued after it, so their toast waits for the guide to close).
            CallDeferred(nameof(OpenGuide));
            CallDeferred(nameof(EnterEvent));
        }
        else
        {
            // Every visit: the quests that wait for "the next visit" unlock, and the toast and the
            // tracker (both listening by now) announce what is new.
            EnterEvent();
            int pending = profile?.Overflow.Count ?? 0;
            string line = pending > 0
                ? $"{pending} reward{(pending == 1 ? " is" : "s are")} waiting in the stash — the Pending tab"
                : IsGuest ? "Your friends are here — everything you use is your own"
                : "Your town between realities — ? opens the guide";
            _hud.ShowBanner(IsGuest ? $"{_session!.HostName}'s hideout" : "Hideout", QuestBanner(line, pending > 0));
        }
    }

    public HideoutHud Hud => _hud;

    public override void _ExitTree()
    {
        if (_session is not null)
        {
            _session.LayoutChanged -= OnHostLayoutChanged;
            _session.Launching -= OnLaunching;
            if (_session.LocalCharacter == CaptureCharacter)
            {
                _session.LocalCharacter = null;
            }

            if (_session.LayoutSource is not null)
            {
                // Late joiners while the host is away get the saved town (SaveLayout ran on the way out).
                _session.LayoutSource = null;
            }
        }
    }

    /// <summary>A toast under the menu bar (party arrivals, refusals).</summary>
    public void Toast(string text, Color colour) => _hud.ShowToast(text, colour);

    private static HideoutData CopyOf(HideoutData? source)
    {
        var copy = new HideoutData { Initialized = true, GuideSeen = true };
        if (source is not null)
        {
            copy.Props.AddRange(source.Props.Select(p => new PlacedProp { Uid = p.Uid, Kind = p.Kind, Position = p.Position }));
            foreach ((string key, string style) in source.Floors)
            {
                copy.Floors[key] = style;
            }

            foreach ((string id, int n) in source.Stock)
            {
                copy.Stock[id] = n;
            }
        }

        HideoutCatalog.EnsureDefaults(copy);
        return copy;
    }

    /// <summary>Guest: the host's town changed (an edit): rebuild what stands here.</summary>
    private void OnHostLayoutChanged()
    {
        if (IsGuest && _session?.HostLayout is { } layout && !Editing)
        {
            ReplaceLayout(CopyOf(layout));
        }
    }

    /// <summary>
    /// Shows another layout of the same town: every prop is rebuilt and the floors
    /// repainted (the walls and the canal never change). The player stays where they are.
    /// </summary>
    public void ReplaceLayout(HideoutData data)
    {
        foreach ((string uid, Node2D node) in _props)
        {
            if (node == _portal)
            {
                // The scene's own portal node is reused: only what SpawnProp added to it goes.
                if (node.GetNodeOrNull("Label") is { } label)
                {
                    node.RemoveChild(label);
                    label.QueueFree();
                }

                continue;
            }

            RemoveChild(node);
            node.QueueFree();
        }

        _props.Clear();
        _highlighted = null;
        Data = data;
        if (!IsGuest)
        {
            OwnTown = data;
            _shop.Data = data;
        }

        _guide.Data = data;
        Town.Data = data;
        Town.Repaint();
        foreach (PlacedProp prop in Data.Props.ToList())
        {
            SpawnProp(prop);
        }

        InvalidatePaths();
    }

    // --- the party's ways out ---------------------------------------------------------------

    private PartyOverlay? _overlay;

    /// <summary>The party overlay, created the first time it is needed (tests, captures).</summary>
    public PartyOverlay PartyOverlay
    {
        get
        {
            if (_overlay is null || !IsInstanceValid(_overlay))
            {
                _overlay = new PartyOverlay { BeforeHost = SaveLayout, LeaveParty = LeaveParty, Name = "PartyOverlay" };
                AddChild(_overlay);
                // Nothing pauses behind it (hosting may start while it is open): hold the player instead.
                _overlay.Closed += () => _player.ControlsEnabled = !_leaving;
            }

            return _overlay;
        }
    }

    public bool PartyOverlayOpen => _overlay is { IsOpen: true } o && IsInstanceValid(o);

    /// <summary>The pause menu's party entry: "Invite friends" alone, "Party & invite" hosting, "Party" as a guest.</summary>
    private string PartyEntryLabel() => _session switch
    {
        { IsHost: true } => "Party & invite",
        { IsClient: true } => "Party",
        _ => "Invite friends",
    };

    public void OpenPartyOverlay()
    {
        if (!Editing && !_leaving)
        {
            PartyOverlay.Open();
            _player.ControlsEnabled = false;
            _player.StopAutoWalk();
        }
    }

    /// <summary>A guest leaves the party on purpose: back to their own town, alone (D12).</summary>
    public void LeaveParty()
    {
        if (_session is { IsClient: true } guest)
        {
            Party.Detach();
            Leave(Scenes.Hideout, () => guest.Leave());
        }
    }

    private string LeaveLabel() => _session switch
    {
        { IsHost: true } => "Close the party",
        { IsClient: true } => "Leave the party",
        _ => "Character select",
    };

    private void OnPauseLeave()
    {
        if (_session is { IsHost: true } host)
        {
            // Closing the party sends every friend to the main menu (owner rule); the host goes there too.
            Leave(Scenes.MainMenu, () => host.Leave());
            return;
        }

        if (_session is { IsClient: true })
        {
            LeaveParty();
            return;
        }

        Leave(Scenes.MainMenu);
    }

    /// <summary>The Mirror's travel: solo as before (in a party the Mirror proposes instead).</summary>
    private void Travel(string scene)
    {
        if (_session is { Active: true })
        {
            Toast("In a party, rifts open for everyone: choose one at the Mirror or walk into the portal", UiTheme.Ember);
            return;
        }

        Leave(scene);
    }

    /// <summary>The player as they are now, saved into the profile (what a rift will build).</summary>
    private CharacterData? CaptureCharacter()
    {
        SaveLayout();
        return Profile.Get(this)?.Active;
    }

    /// <summary>The party is setting off and this machine goes too: save first, like any exit.</summary>
    private void OnLaunching()
    {
        if (!_leaving)
        {
            _leaving = true;
            Editor?.QueueFree();
            SaveLayout();
        }
    }

    /// <summary>
    /// Opens a rift for the party (docs/design/multiplayer.md -> "Proposals"): the
    /// host asks everyone in the hideout. Refusals come back as a toast.
    /// </summary>
    public void ProposeRift(RiftSpec spec)
    {
        if (_session is not { Active: true } s)
        {
            return;
        }

        if (s.PartyPhase != SessionPhase.Hideout)
        {
            Toast("The party is in a rift — wait for them to return", UiTheme.Ember);
            return;
        }

        if (s.OpenProposal is { } open)
        {
            Toast($"{open.ProposerName} is already opening a rift", UiTheme.Ember);
            return;
        }

        s.Propose(spec);
    }

    private void EnterEvent() => GameEvents.Raise(GameEvents.HideoutEntered);

    /// <summary>The quest dialogue (tests, captures).</summary>
    public QuestDialog QuestDialog => _questDialog;

    /// <summary>The arrival banner's second line, with the quests that wait: "1 new quest · J opens your quests".</summary>
    private string QuestBanner(string line, bool keepLine)
    {
        var fresh = new List<string>();
        int news = QuestService.Log(this).Count(q => q.Progress.Status == QuestStatus.New);
        int ready = QuestService.Log(this).Count(q => q.Progress.Status == QuestStatus.Ready);
        if (news > 0)
        {
            fresh.Add($"{news} new quest{(news == 1 ? "" : "s")}");
        }

        if (ready > 0)
        {
            fresh.Add($"{ready} quest{(ready == 1 ? "" : "s")} ready to turn in");
        }

        if (fresh.Count == 0)
        {
            return line;
        }

        string quests = string.Join(" · ", fresh) + " · J opens your quests";
        return keepLine ? $"{line} · {quests}" : quests;
    }

    /// <summary>Where the quest toast's card goes: under the quest tracker (under the character plate while it is empty).</summary>
    private Vector2 ToastAnchor() => _hud.ToastAnchor();

    public void OpenGuide()
    {
        if (!Editing && !_guide.IsOpen)
        {
            _guide.Open();
        }
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
        "arehucaz" => _dealer.Open,
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
            "arehucaz" => Station("Arehucaz", def),
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
            node.AddChild(new QuestMarker2D { Station = prop.Uid, LabelHeight = LabelHeight(def), Hideout = this });
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

    public static float LabelHeightOf(PropDef def) => LabelHeight(def);

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

        if (IsGuest)
        {
            Toast($"Only {_session!.HostName} can edit this town", UiTheme.Ember);
            return;
        }

        GetTree().Paused = false;
        Editor = new HideoutEditor { Hideout = this, Name = "HideoutEditor" };
        _player.StopAutoWalk();
        Editor.Closed += () =>
        {
            Editor = null;
            SaveLayout();
            InvalidatePaths();
            _session?.PublishLayout();
        };
        AddChild(Editor);
    }

    public void SaveLayout()
    {
        if (Profile.Get(this) is { } profile)
        {
            // A guest shows the host's town: writing it back would replace this account's own.
            if (!IsGuest)
            {
                profile.Hideout = Data;
            }

            profile.CaptureFrom(_player);
        }
    }

    // --- stations ----------------------------------------------------------------------

    /// <summary>The station the player stands next to, if any (the E prompt).</summary>
    public (PlacedProp Prop, PropDef Def)? NearestStation()
    {
        (PlacedProp, PropDef)? best = null;
        float bestD = InteractRange;
        foreach (PlacedProp p in Data.Props)
        {
            if (HideoutCatalog.Prop(p.Kind) is { Station: true } def && (UseOf(def.Id) is not null || def.Id == "portal"))
            {
                float d = _player.GlobalPosition.DistanceTo(p.Position);
                if (d < bestD)
                {
                    best = (p, def);
                    bestD = d;
                }
            }
        }

        return best;
    }

    /// <summary>The station whose E prompt is showing (its world marker hides: the prompt carries it).</summary>
    public string? HighlightedStation => _highlighted;

    /// <summary>Hides the world label of the station whose prompt is showing (the
    /// prompt carries its name), and shows the others.</summary>
    public void HighlightStation(string? uid)
    {
        if (uid == _highlighted)
        {
            return;
        }

        foreach ((string id, Node2D node) in _props)
        {
            if (node.GetNodeOrNull<Label>("Label") is { } label)
            {
                label.Visible = id != uid;
            }
        }

        _highlighted = uid;
    }

    // --- walking to a station ---------------------------------------------------------

    /// <summary>Walking into the portal (closer than this to its centre) enters the Survival Rift.</summary>
    public const float PortalRadius = 42f;

    /// <summary>A walk ends this close to a station: well inside the E prompt's range.</summary>
    public const float WalkStopRadius = InteractRange - 20f;

    private TownPaths? _paths;
    private int _pathsIn = 3;
    private string? _pendingWalk;
    private int _walkBest;
    private float _walkStuck;

    /// <summary>The precomputed station walks (null until the town has settled).</summary>
    public TownPaths? Paths => _paths;

    /// <summary>The station the player is walking to, if any.</summary>
    public string? WalkingTo { get; private set; }

    /// <summary>
    /// Walks the player to a station — the town directory, the guide's "Show me",
    /// the altar shortcut and the big map (docs/design/hideout.md -> "Walking to a
    /// station"). Starts on the next physics frame, once the fields are ready.
    /// </summary>
    public void WalkTo(string uid)
    {
        if (Data.Find(uid) is null || Editing || _leaving)
        {
            return;
        }

        _pendingWalk = uid;
    }

    /// <summary>Props moved: rebuild the walks once physics has caught up.</summary>
    public void InvalidatePaths() => _pathsIn = 2;

    private float PlayerRadius =>
        _player.GetNodeOrNull<CollisionShape2D>("CollisionShape2D")?.Shape is CircleShape2D c ? c.Radius : 12f;

    /// <summary>The grid's blocked cells are wherever the player's circle (a pixel
    /// larger) would touch something it collides with, plus the portal, which
    /// walking into would enter the Rift. One field per station.</summary>
    private void RebuildPaths()
    {
        PhysicsDirectSpaceState2D space = GetWorld2D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters2D
        {
            Shape = new CircleShape2D { Radius = PlayerRadius + 1f },
            CollisionMask = _player.CollisionMask,
            CollideWithAreas = false,
            Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() },
        };
        Vector2 portal = _portal.GlobalPosition;
        float portalBlock = PortalRadius + PlayerRadius + 6f;
        _paths = new TownPaths(HideoutTown.Bounds, at =>
        {
            if (at.DistanceTo(portal) < portalBlock)
            {
                return true;
            }

            query.Transform = new Transform2D(0f, at);
            return space.IntersectShape(query, 1).Count > 0;
        });

        foreach (PlacedProp prop in Data.Props)
        {
            if (HideoutCatalog.Prop(prop.Kind) is not { Station: true } def)
            {
                continue;
            }

            Vector2 p = prop.Position;
            _paths.AddField(prop.Uid, def.Id == "portal"
                // In front of the gate, not into it.
                ? at => at.Y > p.Y + 40f && at.DistanceTo(p) < 110f
                : at => at.DistanceTo(p) <= WalkStopRadius);
        }
    }

    private void UpdateWalks()
    {
        if (Editing)
        {
            _pendingWalk = null;
            return;
        }

        if (_pathsIn > 0 && --_pathsIn == 0)
        {
            RebuildPaths();
        }

        if (_pendingWalk is { } uid && _paths is not null && _pathsIn == 0)
        {
            _pendingWalk = null;
            BeginWalk(uid);
        }

        if (WalkingTo is not null && _player.AutoWalk is null)
        {
            WalkingTo = null;
        }
    }

    private void BeginWalk(string uid)
    {
        if (_paths is null || !_paths.HasField(uid))
        {
            return;
        }

        string name = HideoutCatalog.Prop(Data.Find(uid)?.Kind ?? "")?.Name ?? uid;
        if (_paths.StartCell(uid, _player.GlobalPosition) is not { } start)
        {
            _hud.ShowBanner("No way there", $"Something in town blocks every way to {name}");
            return;
        }

        if (_paths.DistanceAt(uid, start) == 0)
        {
            return;
        }

        WalkingTo = uid;
        _walkBest = _paths.DistanceAt(uid, start);
        _walkStuck = 0f;
        _player.StartAutoWalk(() => Steer(uid, name));
    }

    /// <summary>The point to walk toward this physics frame (null: arrived or given up).</summary>
    private Vector2? Steer(string uid, string name)
    {
        if (_paths is null || Editing || _leaving || WalkingTo != uid)
        {
            return null;
        }

        Vector2 from = _player.GlobalPosition;
        if (_paths.Waypoint(uid, from, CircleClear) is not { } next)
        {
            WalkingTo = null;
            return null;
        }

        // Safety net: no progress along the field for a second means something
        // the grid doesn't know about is in the way; stop rather than push forever.
        int d = _paths.StartCell(uid, from) is { } c ? _paths.DistanceAt(uid, c) : TownPaths.Unreachable;
        if (d < _walkBest)
        {
            _walkBest = d;
            _walkStuck = 0f;
        }
        else if ((_walkStuck += (float)GetPhysicsProcessDeltaTime()) > 1f)
        {
            _hud.ShowBanner("Blocked", $"Something is in the way to {name}");
            WalkingTo = null;
            return null;
        }

        return next;
    }

    /// <summary>Whether the player's circle can slide straight from a to b (a
    /// shape cast in the physics world: exact, unlike the grid).</summary>
    private bool CircleClear(Vector2 a, Vector2 b)
    {
        var query = new PhysicsShapeQueryParameters2D
        {
            Shape = new CircleShape2D { Radius = PlayerRadius - 1f },
            CollisionMask = _player.CollisionMask,
            CollideWithAreas = false,
            Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() },
            Transform = new Transform2D(0f, a),
            Motion = b - a,
        };
        float[] result = GetWorld2D().DirectSpaceState.CastMotion(query);
        return result.Length == 0 || result[0] >= 1f;
    }

    public override void _Process(double delta)
    {
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
            return;
        }

        if (!GetTree().Paused && Input.IsActionJustPressed(EditAction) && !PanelStack.HasOpen)
        {
            OpenEditor();
            return;
        }

        if (!GetTree().Paused && !PanelStack.HasOpen && Input.IsActionJustPressed("interact") && NearestStation() is { } near && UseOf(near.Def.Id) is not null)
        {
            Interact(near.Def.Id);
        }
    }

    // --- quests at stations -------------------------------------------------------------

    /// <summary>
    /// What E does at a station (docs/design/quests.md -> "E prompt and station panels"): a
    /// quest that is ready opens its turn-in; a new one opens its explanation first and the panel
    /// after; otherwise the panel opens at once (with the quest strip if one is running).
    /// </summary>
    public void Interact(string kind)
    {
        if (Profile.Get(this) is { } profile && QuestService.FirstAt(this, kind) is { } quest && QuestService.ContextOf(profile) is { } ctx)
        {
            QuestStatus status = QuestService.StatusOf(this, quest.Id);
            if (status == QuestStatus.Ready)
            {
                ShowTurnIn(quest, profile, ctx);
                return;
            }

            if (status == QuestStatus.New)
            {
                _questDialog.Show(quest, false, ctx, () =>
                {
                    QuestService.MarkRead(profile, quest.Id);
                    OpenStation(kind, quest.OpenTab);
                });
                return;
            }
        }

        UseOf(kind)?.Invoke();
    }

    private void ShowTurnIn(QuestDef quest, Profile profile, QuestContext ctx) =>
        _questDialog.Show(quest, true, ctx, () => Claim(quest, profile));

    /// <summary>Pays a ready quest's rewards through the player's wallet and saves.</summary>
    private void Claim(QuestDef quest, Profile profile)
    {
        var sink = new PlayerRewardSink(_player, profile);
        if (QuestService.TurnIn(profile, quest.Id, sink, out _))
        {
            profile.CaptureFrom(_player);
        }
    }

    private void OpenStation(string kind, string? tab)
    {
        UseOf(kind)?.Invoke();
        switch (tab)
        {
            case "gems":
                _merchant.ShowTab(MerchantTab.SkillGems);
                break;
            case "disenchant":
                _artificer.Show(ArtificerPanel.Tab.Disenchant);
                break;
            case "forge":
                _artificer.Show(ArtificerPanel.Tab.Forge);
                break;
            case "jewels":
                _mirror.ShowTab(true);
                break;
        }
    }

    /// <summary>A station panel closed: if its quest is done, the turn-in opens by itself (called deferred).</summary>
    private void TryAutoTurnIn(string kind)
    {
        if (_leaving || Editing || PanelStack.HasOpen || Profile.Get(this) is not { } profile
            || QuestService.FirstAt(this, kind) is not { } quest || QuestService.StatusOf(this, quest.Id) != QuestStatus.Ready
            || QuestService.ContextOf(profile) is not { } ctx)
        {
            return;
        }

        ShowTurnIn(quest, profile, ctx);
    }

    /// <summary>? (whatever the keyboard layout) or F1 opens the town guide.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } key && (key.Unicode == '?' || key.Keycode == Key.F1)
            && !GetTree().Paused && !Editing && !PanelStack.HasOpen)
        {
            OpenGuide();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        UpdateWalks();
        bool inPortal = !_leaving && !Editing && _player.GlobalPosition.DistanceTo(_portal.GlobalPosition) < PortalRadius;
        if (inPortal && !_inPortal)
        {
            if (_session is { Active: true })
            {
                Party.Popup.Confirm("Open the Survival Rift for the party?",
                    $"Everyone in the hideout is asked to join; you set off with whoever does, within {PartyRules.ProposalSeconds:0} s.",
                    "Open it", () => ProposeRift(RiftSpec.SurvivalRift()));
            }
            else
            {
                Leave(Scenes.Run);
            }
        }

        _inPortal = inPortal;
    }

    private void Leave(string scene, Action? before = null)
    {
        if (_leaving)
        {
            return;
        }

        _leaving = true;
        SaveLayout();
        before?.Invoke();
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
