#nullable enable
using Godot;
using ArpgGame.Combat;
using ArpgGame.Items;
using ArpgGame.Skills;
using ArpgGame.Stats;

namespace ArpgGame;

/// <summary>
/// Top-down player controller. Supports two input modes that share one velocity:
/// - Direct (WASD): immediate directional movement, always takes priority.
/// - PathFollow (hold a move input): retargets a NavigationAgent2D to the mouse
///   position every physics frame while held, then coasts the existing path to
///   completion after release (so a quick click-and-release still works).
/// See docs/design/movement.md for the full rationale.
///
/// Path-follow's trigger changed with the combat pass (docs/design/skills.md ->
/// "The right-click conflict — resolved"): it is no longer a hardcoded
/// `move_to` action. LMB is now a loadout slot that *defaults* to the built-in
/// Move occupant, and a separate `force_move` action is the escape hatch once a
/// real skill is bound over LMB. This class asks <see cref="SkillUser"/> what's
/// in the LMB slot rather than knowing loadout internals itself — see
/// <see cref="PlayerSkillInput"/>'s doc comment for the matching split on the
/// casting side.
/// </summary>
public partial class Player : CharacterBody2D
{
    private NavigationAgent2D _navigationAgent = null!;
    private ActorStats _stats = null!;
    private SkillUser _skillUser = null!;

    /// <summary>
    /// docs/design/items.md -&gt; "UI must consume its own input": the fix for
    /// "dragging an item in the inventory also issues a move order" lives here,
    /// not as an "is a panel open?" check bolted onto <see cref="_PhysicsProcess"/>
    /// — see <see cref="MouseActionGate"/>'s class doc for why a raw
    /// <c>Input.IsActionPressed</c> poll cannot tell a world click from a
    /// GUI-consumed one, and why the fix belongs at the input-handling layer
    /// instead.
    /// </summary>
    private readonly MouseActionGate _lmbGate = new("skill_slot_lmb");

    /// <summary>docs/design/items.md -> "Currency": <c>Player.Wallet</c>. A sibling
    /// component node, exposed here the same way <see cref="ActorStats"/>/
    /// <see cref="SkillUser"/> are, so pickup code (<see cref="Items.CurrencyDrop"/>)
    /// can reach it off the player it hit.</summary>
    public Wallet Wallet { get; private set; } = null!;

    /// <summary>
    /// Another sibling component, same convention as <see cref="Wallet"/>.
    /// Exposed publicly so <see cref="Items.EquipmentItem.OnPickup"/> can reach
    /// the player's slots the same way <see cref="Items.CurrencyDrop.OnPickup"/>
    /// reaches <see cref="Wallet"/>.
    /// </summary>
    public Items.Equipment Equipment { get; private set; } = null!;

    /// <summary>
    /// Milestone 2c: the grid inventory (docs/design/items.md -> "Grid
    /// inventory"). Same sibling-component convention as
    /// <see cref="Wallet"/>/<see cref="Equipment"/>; exposed publicly so
    /// <see cref="Items.EquipmentItem.OnPickup"/> can route a pickup into it.
    /// </summary>
    public Items.Inventory Inventory { get; private set; } = null!;

    /// <summary>docs/design/dodge.md. Optional so older test scenes that build
    /// a bare player still work.</summary>
    public Dodge? Dodge { get; private set; }

    public Health? Health { get; private set; }

    public ActorStats Stats => _stats;

    public SkillUser SkillUser => _skillUser;

    private AnimatedSprite2D? _sprite;
    private float _hurtFlash;

    [Export]
    public PackedScene? DamageNumberScene;

    /// <summary>False freezes all player-driven movement (level-up screen,
    /// death, results). Physics still runs so knock-on systems stay sane.</summary>
    public bool ControlsEnabled { get; set; } = true;

    /// <summary>
    /// Camera zoom for the endgame scenes (hideout, realities): the hi-res
    /// characters read better a bit closer. The Survival Rift keeps 1:1 so its
    /// hordes stay in view.
    /// </summary>
    public const float CloseZoom = 1.5f;

    // --- co-op roles (docs/design/multiplayer.md -> "Players") ---------------------

    /// <summary>
    /// True for the player this machine controls (camera, HUD, inventory, input).
    /// Single-player: always. Co-op: false for the other party members, which are
    /// either driven by network <see cref="Command"/>s (on the host) or are
    /// <see cref="Puppet"/>s placed by snapshots (on a client). Set before _Ready.
    /// </summary>
    public bool IsLocal { get; set; } = true;

    /// <summary>Co-op client: another party member, moved only by snapshots.</summary>
    public bool Puppet { get; set; }

    /// <summary>Network peer that controls this player (0 offline, 1 the host).</summary>
    public long PeerId { get; set; }

    /// <summary>Display name over a remote player's head.</summary>
    public string NetName { get; set; } = "";

    /// <summary>When set, movement, sprint, dodge, flasks and aim read this instead
    /// of the keyboard and mouse (a remote player on the host, or a test).</summary>
    public Net.PlayerCommand? Command { get; set; }

    /// <summary>Where the player aims: the mouse locally, the sent aim point remotely.</summary>
    public Vector2 AimPoint => Command is { } c ? c.Aim : GetGlobalMousePosition();

    /// <summary>The movement direction actually used on the last physics step
    /// (WASD or a click-to-move path), scaled 0..1. A co-op client sends this to
    /// the host, so pathing to an item moves the host's copy too.</summary>
    public Vector2 LastMoveDir { get; private set; }

    /// <summary>Co-op: choosing a level-up card — cannot act or be hurt, and
    /// monsters look for someone else (docs/design/multiplayer.md -> "Level-ups").</summary>
    public bool Warded
    {
        get => _warded;
        set
        {
            _warded = value;
            if (Health is not null)
            {
                Health.Warded = value;
            }
        }
    }

    private bool _warded;

    /// <summary>Co-op: 0..1 progress of an ally reviving this downed player (drawn as a ring).</summary>
    public float ReviveProgress { get; set; }

    /// <summary>Co-op client: shows a hit the host resolved (number, flash, sound).</summary>
    public void ShowHurt(float amount) => OnDamaged(amount);

    public void UseCloseCamera()
    {
        if (GetNodeOrNull<Camera2D>("Camera2D") is { } camera)
        {
            camera.Zoom = new Vector2(CloseZoom, CloseZoom);
        }
    }

    public override void _Ready()
    {
        AddToGroup(CombatQuery.PlayerGroup);
        if (IsLocal)
        {
            AddToGroup(CombatQuery.LocalPlayerGroup);
        }
        else
        {
            ConfigureRemote();
        }

        if (GetNodeOrNull<Flasks>("Flasks") is null)
        {
            AddChild(new Flasks { Name = "Flasks" });
        }

        if (GetNodeOrNull<UI.OverheadBars>("OverheadBars") is null)
        {
            AddChild(new UI.OverheadBars { Name = "OverheadBars" });
        }

        Dodge = GetNodeOrNull<Dodge>("Dodge");
        Health = GetNodeOrNull<Health>("Health");
        _sprite = GetNodeOrNull<AnimatedSprite2D>("Sprite");
        if (Health is not null)
        {
            Health.Damaged += OnDamaged;
        }

        _navigationAgent = GetNode<NavigationAgent2D>("NavigationAgent2D");
        _stats = GetNode<ActorStats>("Stats");
        _skillUser = GetNode<SkillUser>("SkillUser");
        Wallet = GetNode<Wallet>("Wallet");
        Equipment = GetNode<Items.Equipment>("Equipment");
        Inventory = GetNode<Items.Inventory>("Inventory");
    }

    /// <summary>
    /// Only the rising edge of <c>skill_slot_lmb</c> is fed here (see
    /// <see cref="MouseActionGate"/>) — a press the GUI already consumed
    /// (e.g. starting a drag over the inventory grid, which sets
    /// <c>MouseFilter.Stop</c>) never reaches <c>_UnhandledInput</c> at all, so
    /// <see cref="_lmbGate"/> never latches on for it.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event) => _lmbGate.HandleUnhandledInput(@event);

    /// <summary>
    /// Another party member: no camera of its own, no inventory window, no debug
    /// readout, no reading this machine's mouse — and a name over its head.
    /// </summary>
    private void ConfigureRemote()
    {
        SetProcessUnhandledInput(false);
        if (GetNodeOrNull<Camera2D>("Camera2D") is { } camera)
        {
            camera.Enabled = false;
        }

        foreach (string child in new[] { "InventoryUI", "DebugReadout" })
        {
            if (GetNodeOrNull(child) is { } node)
            {
                RemoveChild(node);
                node.QueueFree();
            }
        }

        if (GetNodeOrNull<PlayerSkillInput>("PlayerSkillInput") is { } skills)
        {
            skills.SetProcessUnhandledInput(false);
        }

        var tag = new Label
        {
            Name = "NameTag",
            Text = NetName,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(-80f, -66f),
            Size = new Vector2(160f, 20f),
            ZIndex = 5,
        };
        tag.AddThemeFontSizeOverride("font_size", 13);
        tag.AddThemeColorOverride("font_color", new Color(0.75f, 0.9f, 1f));
        tag.AddThemeColorOverride("font_outline_color", Colors.Black);
        tag.AddThemeConstantOverride("outline_size", 4);
        AddChild(tag);
    }

    public override void _Draw()
    {
        // Co-op: a downed player shows how far an ally's revive has come.
        if (Health is { IsDead: true } && !IsLocalOffline)
        {
            DrawArc(new Vector2(0f, -4f), 22f, 0f, Mathf.Tau, 32, new Color(0f, 0f, 0f, 0.5f), 4f, true);
            if (ReviveProgress > 0f)
            {
                DrawArc(new Vector2(0f, -4f), 22f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * ReviveProgress, 32, new Color(0.45f, 1f, 0.55f), 4f, true);
            }
        }
    }

    /// <summary>True outside co-op (nothing to draw for a downed player there).</summary>
    private bool IsLocalOffline => IsLocal && PeerId == 0;

    /// <summary>
    /// The item a click asked for while it was out of reach. Set by
    /// <see cref="RequestPickup"/>, honoured once the walk gets close enough, and
    /// dropped the moment the player steers anywhere else.
    /// </summary>
    private Items.EquipmentItem? _pendingPickup;

    /// <summary>
    /// docs/design/items.md -> "Pickup has a reach": clicking an item you are too
    /// far from walks you to it and picks it up on arrival, rather than doing
    /// nothing. The intent lives on the player rather than on the item because the
    /// player is what owns movement, and because only one such intent can be
    /// outstanding — a second click replaces the first.
    /// </summary>
    public void RequestPickup(Items.EquipmentItem item)
    {
        _pendingPickup = item;
        _navigationAgent.TargetPosition = item.GlobalPosition;
    }

    /// <summary>Abandons a queued pickup. Any deliberate movement counts: having
    /// walked somewhere else and then being yanked back to an item you have
    /// forgotten about is worse than the click having done nothing.</summary>
    private void CancelPendingPickup() => _pendingPickup = null;

    /// <summary>Test seam for the cancel rule — the production callers are input
    /// paths, which a headless test cannot drive.</summary>
    public void CancelPendingPickupForTest() => CancelPendingPickup();

    /// <summary>Test seam: whether a queued pickup is outstanding.</summary>
    public bool HasPendingPickupForTest => _pendingPickup is not null;

    /// <summary>
    /// Completes a queued pickup as soon as the walk brings the item into reach —
    /// on proximity rather than on "navigation finished", since the path ends at
    /// the item's centre and reach is satisfied well before that.
    /// </summary>
    private void TryCompletePendingPickup()
    {
        if (_pendingPickup is not { } item)
        {
            return;
        }

        // The item can vanish mid-walk: someone else's pickup, a despawn, or the
        // scene changing under us.
        if (!IsInstanceValid(item) || !item.IsInsideTree())
        {
            CancelPendingPickup();
            return;
        }

        if (!item.IsWithinPickupReach(this))
        {
            return;
        }

        item.OnPickup(this);
        CancelPendingPickup();

        // Stop where we are rather than continuing to the item's exact centre.
        _navigationAgent.TargetPosition = GlobalPosition;
    }

    /// <summary>Range in which F looks for an item to walk to when none is in reach.</summary>
    public const float PickupKeyRange = 360f;

    private const float PickupKeyRepeat = 0.12f;
    private float _pickupKeyCooldown;

    /// <summary>
    /// docs/design/items.md -> "F picks up": the nearest item on the ground is
    /// picked up as if clicked — in reach it goes straight to the bag, otherwise
    /// the character walks to it (within <see cref="PickupKeyRange"/>). Holding F
    /// keeps going, one item every <see cref="PickupKeyRepeat"/> s, so a pile
    /// empties without mashing. Items the bag can't take are skipped while others
    /// fit; when nothing fits, F tries the nearest anyway so the "inventory full"
    /// message explains why nothing happened.
    /// </summary>
    private void PollPickupKey(float dt)
    {
        _pickupKeyCooldown -= dt;
        if (!IsLocal || Command is not null || _pickupKeyCooldown > 0f || !Input.IsActionPressed("pickup"))
        {
            return;
        }

        if (NearestGroundItem(mustFit: true) is { } item)
        {
            _pickupKeyCooldown = PickupKeyRepeat;
            item.PickUpOrWalkTo(this);
        }
        else if (NearestGroundItem(mustFit: false) is { } full)
        {
            _pickupKeyCooldown = 0.6f;
            full.PickUpOrWalkTo(this);
        }
    }

    /// <summary>The closest visible item on the ground (that fits the bag, if
    /// <paramref name="mustFit"/>) within <see cref="PickupKeyRange"/>, or null.</summary>
    public Items.EquipmentItem? NearestGroundItem(bool mustFit)
    {
        Items.EquipmentItem? best = null;
        float bestDistance = PickupKeyRange;
        foreach (Node node in GetTree().GetNodesInGroup(Items.EquipmentItem.GroundGroup))
        {
            if (node is not Items.EquipmentItem item || !item.IsVisibleInTree() || (mustFit && !Inventory.TryFindFirstFit(item, out _)))
            {
                continue;
            }

            float d = item.GlobalPosition.DistanceTo(GlobalPosition);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = item;
            }
        }

        return best;
    }

    private void OnDamaged(float amount)
    {
        if (amount < 0.5f)
        {
            return;
        }

        _hurtFlash = 0.15f;
        Core.Sfx.Play(this, "hurt");
        Core.CameraShake.Request(this, 3f);
        if (DamageNumberScene is not null && (Core.GameSettings.Get(this)?.DamageNumbers ?? true))
        {
            var number = DamageNumberScene.Instantiate<UI.DamageNumber>();
            GetTree().CurrentScene.AddChild(number);
            number.GlobalPosition = GlobalPosition + new Vector2(0f, -28f);
            number.Display(amount, new Color(1f, 0.3f, 0.3f));
        }
    }

    /// <summary>"" (front, facing down), "_up" (back) or "_side" (profile, mirrored
    /// for left): the animation suffix of the current facing — docs/design/art-pipeline.md
    /// -> "Four directions".</summary>
    private string _facing = "";

    /// <summary>
    /// Faces where the character aims (the mouse, or the command's aim point).
    /// Sprites without directional views just flip left/right. A little
    /// hysteresis keeps diagonals from flickering between the side and the
    /// front/back views.
    /// </summary>
    private void UpdateFacing(Vector2 toward)
    {
        if (_sprite is null || toward.LengthSquared() < 16f)
        {
            return;
        }

        float ax = Mathf.Abs(toward.X);
        float ay = Mathf.Abs(toward.Y);
        bool side = _facing == "_side" ? ax * 1.3f >= ay : ax > ay * 1.3f;
        bool directional = _sprite.SpriteFrames?.HasAnimation("idle_side") == true;
        if (!directional || side)
        {
            _facing = directional ? "_side" : "";
            _sprite.FlipH = toward.X < 0f;
        }
        else
        {
            _facing = toward.Y < 0f ? "_up" : "";
            _sprite.FlipH = false;
        }
    }

    public override void _Process(double delta)
    {
        if (_sprite is null)
        {
            return;
        }

        bool moving = Velocity.LengthSquared() > 25f;
        // Faces the cursor while walking too (owner feedback); a dash faces where it
        // goes, and a co-op puppet (no aim replicated) faces where it moves.
        bool facesMotion = Puppet || (Dodge?.IsDashing ?? false);
        UpdateFacing(facesMotion ? (moving ? Velocity : Vector2.Zero) : AimPoint - GlobalPosition);
        string anim = (moving ? "walk" : "idle") + _facing;
        if (_sprite.SpriteFrames?.HasAnimation(anim) != true)
        {
            anim = moving ? "walk" : "idle";
        }

        if (_sprite.Animation != anim && _sprite.SpriteFrames?.HasAnimation(anim) == true)
        {
            _sprite.Play(anim);
        }

        if (!IsLocalOffline)
        {
            QueueRedraw();
        }

        if (Health is { IsDead: true } && !IsLocalOffline)
        {
            // Co-op: downed, waiting for a revive.
            _sprite.Modulate = new Color(0.45f, 0.45f, 0.55f, 0.6f);
        }
        else if (_hurtFlash > 0f)
        {
            _hurtFlash -= (float)delta;
            _sprite.Modulate = new Color(2.5f, 0.6f, 0.6f);
        }
        else if (Warded)
        {
            _sprite.Modulate = new Color(0.8f, 0.9f, 1.4f, 0.75f);
        }
        else if (Health is { InvulnerableTime: > 0f } && !(Dodge?.IsDashing ?? false))
        {
            _sprite.Modulate = new Color(1f, 1f, 1f, Mathf.Sin((float)Time.GetTicksMsec() * 0.05f) > 0 ? 1f : 0.4f);
        }
        else
        {
            _sprite.Modulate = Colors.White;
        }
    }

    // --- Sprint (docs/design/movement.md -> "Sprint") -------------------------------

    public const float SprintMultiplier = 1.55f;
    private const float SprintDrainPerSecond = 30f;
    private const float StaminaRegenDelay = 0.7f;

    /// <summary>Current stamina (0..<see cref="MaxStamina"/>).</summary>
    public float Stamina { get; private set; } = -1f;

    public float MaxStamina => _stats.Get(StatId.MaxStamina).Value;

    public bool IsSprinting { get; private set; }

    /// <summary>After running dry, sprint stays locked until stamina is back to 30 %.</summary>
    public bool Exhausted { get; private set; }

    private float _staminaRegenWait;

    /// <summary>Holds Shift to sprint while moving; returns the speed factor.</summary>
    private float UpdateSprint(float dt, bool moving)
    {
        float max = MaxStamina;
        if (Stamina < 0f)
        {
            Stamina = max;
        }

        bool wants = moving && (Command?.Sprint ?? Input.IsActionPressed("sprint")) && !Exhausted && Stamina > 0f;
        IsSprinting = wants;
        if (wants)
        {
            Stamina = Mathf.Max(0f, Stamina - SprintDrainPerSecond * dt);
            _staminaRegenWait = StaminaRegenDelay;
            if (Stamina <= 0f)
            {
                Exhausted = true;
            }

            return SprintMultiplier;
        }

        _staminaRegenWait -= dt;
        if (_staminaRegenWait <= 0f)
        {
            Stamina = Mathf.Min(max, Stamina + _stats.Get(StatId.StaminaRegen).Value * dt);
        }

        if (Exhausted && Stamina >= max * 0.3f)
        {
            Exhausted = false;
        }

        return 1f;
    }

    public override void _PhysicsProcess(double delta)
    {
        float speed = _stats.MovementSpeed.Value;
        LastMoveDir = Vector2.Zero;

        // A co-op puppet is placed by snapshots (Net.RiftClient), never simulated.
        if (Puppet)
        {
            return;
        }

        if (!ControlsEnabled || Warded || (Health?.IsDead ?? false))
        {
            Velocity = Vector2.Zero;
            return;
        }

        PollPickupKey((float)delta);

        // A dash owns movement for its whole duration (docs/design/dodge.md).
        if (Dodge is { IsDashing: true })
        {
            CancelPendingPickup();
            Velocity = Dodge.DashVelocity;
            MoveAndSlide();
            return;
        }

        Vector2 wasdDir = Command is { } command
            ? command.Move.LimitLength(1f)
            : Input.GetVector("move_left", "move_right", "move_up", "move_down");
        speed *= UpdateSprint((float)delta, wasdDir != Vector2.Zero || !_navigationAgent.IsNavigationFinished());
        if (wasdDir != Vector2.Zero)
        {
            // WASD cancels a pending click-to-move destination outright instead of
            // pausing it — otherwise releasing WASD would resume walking to a target
            // the player has already steered away from. Godot 4.1's NavigationAgent2D
            // has no explicit cancel, so retargeting to the current position is what
            // marks the path finished.
            if (!_navigationAgent.IsNavigationFinished())
            {
                _navigationAgent.TargetPosition = GlobalPosition;
            }

            CancelPendingPickup();

            LastMoveDir = wasdDir;
            Velocity = wasdDir * speed;
            MoveAndSlide();
            return;
        }

        // Path-follow triggers on either: the LMB slot still holds the built-in
        // Move sentinel and skill_slot_lmb is held; or force_move is held
        // regardless of what's bound. Both retarget to the same mouse world
        // position, so this collapses to one boolean feeding one retarget line.
        bool pathFollow = Command is null
            && ((_skillUser.SlotIsBuiltInMove(SkillSlotId.Lmb) && _lmbGate.Poll()) || Input.IsActionPressed("force_move"));
        if (pathFollow)
        {
            CancelPendingPickup();
            _navigationAgent.TargetPosition = GetGlobalMousePosition();
        }

        // Before the navigation guard below: reaching an item is a question of
        // proximity, not of pathing, so a queued pickup on something already within
        // reach must resolve even on a frame where the navigation map is not ready.
        TryCompletePendingPickup();

        // Navigation map may not be active yet (e.g. first physics frame(s) after
        // the scene loads, before the NavigationRegion2D registers with the
        // server) — nothing to path against yet, just wait.
        // Note: NavigationServer2D.MapGetIterationId() (the more precise "has this
        // map finished syncing" check) isn't available in Godot 4.1.1's C# API —
        // it was added in a later 4.x release. MapIsActive() is the closest
        // equivalent guard on this version; see docs/design/movement.md.
        if (!NavigationServer2D.MapIsActive(_navigationAgent.GetNavigationMap()))
        {
            return;
        }

        if (_navigationAgent.IsNavigationFinished())
        {
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        Vector2 nextPathPosition = _navigationAgent.GetNextPathPosition();
        LastMoveDir = GlobalPosition.DirectionTo(nextPathPosition);
        Velocity = LastMoveDir * speed;
        MoveAndSlide();
    }
}
