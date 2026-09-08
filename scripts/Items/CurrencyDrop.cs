#nullable enable
using Godot;

namespace PathToExile.Items;

/// <summary>
/// The currency <see cref="IDroppable"/>. Per docs/design/items.md -> "Drop
/// pipeline": currency's on-ground representation is "label + sprite" and its
/// on-pickup behaviour is "increment wallet" — it does not persist, and it must
/// never be forced through a grid-inventory abstraction it doesn't need (that
/// abstraction doesn't even exist yet; see the milestone-1 scope note in
/// docs/design/items.md -> "Build order").
///
/// <see cref="Currency"/>/<see cref="Amount"/> are [Export] fields rather than
/// constructor arguments, mirroring <c>SkillUser.DefaultRmbSkill</c>'s "convenience
/// Inspector wiring" idiom: a test/demo scene can author a gold pile entirely in
/// the `.tscn` with no bootstrap script. <see cref="Initialize"/> exists
/// separately for a future runtime spawner (a drop table resolving against a
/// <c>DropContext</c>, per the design doc) to configure a freshly-instantiated
/// drop before anything else observes it.
/// </summary>
[GlobalClass]
public partial class CurrencyDrop : Area2D, IDroppable
{
    [Export]
    public CurrencyDefinition? Currency;

    [Export]
    public long Amount;

    /// <summary>Optional explicit wiring to a <see cref="GroundLabelPool"/>. If
    /// unset, falls back to looking for a sibling node named "GroundLabelPool"
    /// under the current scene root — mirroring <c>DebugReadout.StatsPath</c>'s
    /// optional-wiring convention. Either way, a missing pool is not an error: a
    /// drop with no pool nearby just doesn't render a label, which is exactly what
    /// milestone-1 test scenes that don't need labels want.</summary>
    [Export]
    public NodePath GroundLabelPoolPath = new();

    public ItemDescriptor Descriptor { get; private set; } = new() { ItemClass = ItemClass.Currency };

    private GroundLabelPool? _labelPool;
    private int? _labelHandle;

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;

        // Inspector-authored drops (Currency/Amount set directly in the .tscn)
        // are already final at this point — this is the one and only descriptor
        // computation for them, matching "immutable after drop". Code-driven
        // drops (via Initialize, below) are null here and get their real,
        // final descriptor moments later, still before the end of this physics
        // frame — before anything outside this node could have observed the
        // placeholder one.
        RebuildDescriptor();
        RegisterLabel();
    }

    /// <summary>
    /// Runtime configuration path for a future spawner (docs/design/items.md ->
    /// "Drop tables are separate again"). Callers must call this exactly once,
    /// immediately after instancing and before the drop is visible to anything
    /// else — see "The descriptor is immutable after drop".
    /// </summary>
    public void Initialize(CurrencyDefinition currency, long amount)
    {
        Currency = currency;
        Amount = amount;
        RebuildDescriptor();
        RegisterLabel();
    }

    private void RebuildDescriptor()
    {
        if (Currency is null)
        {
            Descriptor = new ItemDescriptor { ItemClass = ItemClass.Currency };
            return;
        }

        // Every other ItemDescriptor field (Rarity aside, which defaults to the
        // meaningless-for-currency Common) is left at its type default —
        // BaseTypeId, Width/Height, StackSize, AffixCount, etc. genuinely don't
        // apply to a currency pile. See ItemDescriptor's own field-by-field doc
        // comments for why each one stays unset here.
        Descriptor = new ItemDescriptor
        {
            ItemClass = ItemClass.Currency,
            DisplayName = string.Format(Currency.StackFormat, Amount),
            CurrencyId = Currency.Id,
            Amount = Amount,
        };
    }

    private void RegisterLabel()
    {
        ReleaseLabel();

        _labelPool = GetNodeOrNull<GroundLabelPool>(GroundLabelPoolPath)
            ?? GetTree().CurrentScene.GetNodeOrNull<GroundLabelPool>("GroundLabelPool");
        if (_labelPool is null || Currency is null)
        {
            return;
        }

        // The display-only consumer of Descriptor. Note what this method does
        // NOT do: it never touches Currency.AutoPickup, never calls OnPickup, and
        // has no way to prevent pickup even if Style.Hidden is true. That
        // separation is the whole point — see OnPickup's doc comment below.
        DisplayStyle style = FilterChain.Default.Evaluate(Descriptor);
        _labelHandle = _labelPool.Acquire(() => GlobalPosition, Descriptor.DisplayName, style);
    }

    private void ReleaseLabel()
    {
        if (_labelHandle is { } handle)
        {
            _labelPool?.Release(handle);
            _labelHandle = null;
        }
    }

    private void OnBodyEntered(Node2D body)
    {
        if (body is Player player)
        {
            OnPickup(player);
        }
    }

    /// <summary>
    /// docs/design/items.md -> "Filters control display, never behaviour": "A
    /// hidden gold pile is still auto-collected on walk-over." This method is the
    /// structural guarantee, not a comment to remember: it takes no
    /// <see cref="DisplayStyle"/>, no <see cref="FilterChain"/>, and has no field
    /// or reference through which either could reach it — the only inputs are
    /// <see cref="Currency"/> and <see cref="Amount"/>, both set before either
    /// this method or <see cref="RegisterLabel"/> ever runs, and neither method
    /// calls the other. There is no code path left by which hiding a label could
    /// also suppress collection.
    /// </summary>
    public void OnPickup(Player player)
    {
        if (Currency is null || !Currency.AutoPickup)
        {
            // AutoPickup = false: no manual-pickup path exists yet in milestone 1
            // (docs/design/items.md's per-currency AutoPickup note), so the pile
            // is left exactly where it is rather than silently vanishing.
            return;
        }

        player.Wallet.Add(Currency.Id, Amount);
        ReleaseLabel();
        QueueFree();
    }

    public override void _ExitTree() => ReleaseLabel();
}
