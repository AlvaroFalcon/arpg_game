#nullable enable
using Godot;
using PathToExile.Stats;

namespace PathToExile.Run;

public enum PickupKind
{
    Experience,
    Life,
    Gold,
}

/// <summary>
/// Something on the floor the player collects by walking near it — docs/design/run.md
/// -> "Pickups". Within the player's <see cref="StatId.PickupRadius"/> it is
/// pulled in (accelerating, so a big radius feels like a vacuum rather than a
/// slow drift) and collected on contact. Experience gems change sprite with
/// value; gold and life orbs are rarer drops.
///
/// Deliberately not an Area2D: a few hundred gems doing one distance check each
/// is cheaper and simpler than a few hundred physics bodies.
/// </summary>
public partial class Pickup : Node2D
{
    private const float CollectDistance = 18f;

    public PickupKind Kind;
    public float Value;

    private static readonly SpriteFrames? SmallFrames = Load("xp_small");
    private static readonly SpriteFrames? MediumFrames = Load("xp_medium");
    private static readonly SpriteFrames? LargeFrames = Load("xp_large");
    private static readonly SpriteFrames? LifeFrames = Load("life_orb");

    private static SpriteFrames? Load(string name) =>
        ResourceLoader.Exists($"res://assets/sprites/pickups/{name}.tres")
            ? GD.Load<SpriteFrames>($"res://assets/sprites/pickups/{name}.tres")
            : null;

    private AnimatedSprite2D _sprite = null!;
    private bool _attracted;
    private float _pullSpeed;
    private float _bob = GD.Randf() * Mathf.Tau;

    public static Pickup Spawn(Node parent, PickupKind kind, float value, Vector2 position)
    {
        var pickup = new Pickup { Kind = kind, Value = value, ZIndex = -1 };
        parent.AddChild(pickup);
        pickup.GlobalPosition = position;
        return pickup;
    }

    public override void _Ready()
    {
        AddToGroup(RunState.PickupGroup);
        _sprite = new AnimatedSprite2D { Scale = new Vector2(2f, 2f), TextureFilter = TextureFilterEnum.Nearest };
        AddChild(_sprite);
        RefreshVisual();
    }

    /// <summary>Merges another gem's value into this one (gem cap, see RunState).</summary>
    public void Absorb(float value)
    {
        Value += value;
        RefreshVisual();
    }

    private void RefreshVisual()
    {
        SpriteFrames? frames = Kind switch
        {
            PickupKind.Life => LifeFrames,
            PickupKind.Gold => null,
            _ => Value >= 20f ? LargeFrames : Value >= 4f ? MediumFrames : SmallFrames,
        };
        _sprite.SpriteFrames = frames;
        _sprite.Visible = frames is not null;
        if (frames is not null)
        {
            _sprite.Play();
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Kind != PickupKind.Gold)
        {
            return;
        }

        DrawCircle(Vector2.Zero, 6f, new Color(0.45f, 0.3f, 0.05f));
        DrawCircle(Vector2.Zero, 5f, new Color(1f, 0.8f, 0.25f));
        DrawCircle(new Vector2(-1.5f, -1.5f), 2f, new Color(1f, 0.95f, 0.7f));
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (GetTree().GetFirstNodeInGroup(Combat.CombatQuery.PlayerGroup) is not Player player)
        {
            return;
        }

        float distance = GlobalPosition.DistanceTo(player.GlobalPosition);
        if (!_attracted)
        {
            _bob += dt * 3f;
            _sprite.Position = new Vector2(0f, Mathf.Sin(_bob) * 2f);
            if (distance <= player.Stats.Get(StatId.PickupRadius).Value)
            {
                _attracted = true;
                _pullSpeed = 120f;
            }

            return;
        }

        _pullSpeed += 1400f * dt;
        GlobalPosition = GlobalPosition.MoveToward(player.GlobalPosition, _pullSpeed * dt);
        if (GlobalPosition.DistanceTo(player.GlobalPosition) <= CollectDistance)
        {
            Collect(player);
        }
    }

    /// <summary>Pulls this pickup in regardless of radius (vacuum effects, run end).</summary>
    public void Attract() => _attracted = true;

    private void Collect(Player player)
    {
        if (GetTree().GetFirstNodeInGroup(RunState.Group) is RunState state)
        {
            state.OnPickup(this, player);
        }

        QueueFree();
    }
}
