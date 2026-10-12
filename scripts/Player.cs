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
        if (GetNodeOrNull<Flasks>("Flasks") is null)
        {
            AddChild(new Flasks { Name = "Flasks" });
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
    /// Faces where the character is going, or where it aims when standing still.
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
        UpdateFacing(moving ? Velocity : GetGlobalMousePosition() - GlobalPosition);
        string anim = (moving ? "walk" : "idle") + _facing;
        if (_sprite.SpriteFrames?.HasAnimation(anim) != true)
        {
            anim = moving ? "walk" : "idle";
        }

        if (_sprite.Animation != anim && _sprite.SpriteFrames?.HasAnimation(anim) == true)
        {
            _sprite.Play(anim);
        }

        if (_hurtFlash > 0f)
        {
            _hurtFlash -= (float)delta;
            _sprite.Modulate = new Color(2.5f, 0.6f, 0.6f);
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

        bool wants = moving && Input.IsActionPressed("sprint") && !Exhausted && Stamina > 0f;
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

        if (!ControlsEnabled || (Health?.IsDead ?? false))
        {
            Velocity = Vector2.Zero;
            return;
        }

        // A dash owns movement for its whole duration (docs/design/dodge.md).
        if (Dodge is { IsDashing: true })
        {
            CancelPendingPickup();
            Velocity = Dodge.DashVelocity;
            MoveAndSlide();
            return;
        }

        Vector2 wasdDir = Input.GetVector("move_left", "move_right", "move_up", "move_down");
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

            Velocity = wasdDir * speed;
            MoveAndSlide();
            return;
        }

        // Path-follow triggers on either: the LMB slot still holds the built-in
        // Move sentinel and skill_slot_lmb is held; or force_move is held
        // regardless of what's bound. Both retarget to the same mouse world
        // position, so this collapses to one boolean feeding one retarget line.
        bool pathFollow = (_skillUser.SlotIsBuiltInMove(SkillSlotId.Lmb) && _lmbGate.Poll())
            || Input.IsActionPressed("force_move");
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
        Velocity = GlobalPosition.DirectionTo(nextPathPosition) * speed;
        MoveAndSlide();
    }
}
