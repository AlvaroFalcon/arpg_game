#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Stats;

namespace ArpgGame.Run;

public enum PickupKind
{
    Experience,
    Life,
    Gold,

    /// <summary>A Mirror Shard; <see cref="Pickup.Value"/> is its tier.</summary>
    Shard,

    /// <summary>A skill or support gem; see <see cref="Pickup.GemId"/>.</summary>
    Gem,

    /// <summary>A crafting orb; see <see cref="Pickup.CurrencyKey"/>.</summary>
    Currency,

    /// <summary>Survival Rift: the Cataclysm — kills every non-boss enemy on screen.</summary>
    Nuke,

    /// <summary>Survival Rift: the Lodestone — pulls in every experience gem and
    /// gold coin on the map, however far.</summary>
    Magnet,

    /// <summary>A Mirror Jewel (realities: bosses), socketed in the Mirror's jewel tree.</summary>
    Jewel,
}

/// <summary>Whoever runs the current activity (survival run, reality) and
/// knows what a collected pickup does.</summary>
public interface IPickupHandler
{
    void OnPickup(Pickup pickup, Player player);
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
    public string GemId = "";
    public bool GemSupport;

    /// <summary>For <see cref="PickupKind.Currency"/>: the <see cref="Items.CraftingCurrency"/> id.</summary>
    public string CurrencyKey = "";

    public const string HandlerGroup = "pickup_handler";

    private static readonly SpriteFrames? SmallFrames = Load("xp_small");
    private static readonly SpriteFrames? MediumFrames = Load("xp_medium");
    private static readonly SpriteFrames? LargeFrames = Load("xp_large");
    private static readonly SpriteFrames? LifeFrames = Load("life_orb");
    private static readonly Dictionary<string, SpriteFrames?> ShardFrames = new();

    private static SpriteFrames? ShardFramesFor(int tier)
    {
        string name = World.RealityTiers.ShardSprite(tier);
        if (!ShardFrames.TryGetValue(name, out SpriteFrames? frames))
        {
            frames = Load(name);
            ShardFrames[name] = frames;
        }

        return frames;
    }

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
        TextureFilter = TextureFilterEnum.Nearest;
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
            PickupKind.Gem => null,
            PickupKind.Currency => null,
            PickupKind.Nuke or PickupKind.Magnet or PickupKind.Jewel => null,
            PickupKind.Shard => ShardFramesFor(Mathf.RoundToInt(Value)),
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
        if (Kind is PickupKind.Shard or PickupKind.Gem)
        {
            // A light beam so rare drops are seen from across a room.
            Color beam = Kind == PickupKind.Shard
                ? World.RealityTiers.ShardColor(Mathf.RoundToInt(Value))
                : GemSupport ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.55f, 0.3f);
            DrawRect(new Rect2(-3f, -90f, 6f, 90f), beam with { A = 0.18f });
            DrawRect(new Rect2(-1f, -90f, 2f, 90f), beam with { A = 0.35f });
        }

        if (Kind is PickupKind.Nuke or PickupKind.Magnet)
        {
            DrawPowerUp(Kind == PickupKind.Nuke);
            return;
        }

        if (Kind == PickupKind.Jewel)
        {
            DrawJewel(this, Vector2.Zero, 1f, _bob, beam: true);
            return;
        }

        if (Kind == PickupKind.Currency)
        {
            Color c = Items.Crafting.Find(CurrencyKey)?.Color ?? Colors.White;
            if (Items.ItemArt.ForCurrency(CurrencyKey) is { } art)
            {
                if (Items.DropTables.OrbChance(CurrencyKey) < 0.1f)
                {
                    DrawRect(new Rect2(-2f, -70f, 4f, 70f), c with { A = 0.2f });
                }

                DrawCircle(new Vector2(0f, 6f), 7f, new Color(0f, 0f, 0f, 0.3f));
                DrawTextureRect(art, new Rect2(new Vector2(-12f, -14f), new Vector2(24f, 24f)), false);
                return;
            }

            // Rarer orbs get a beam like shards and gems do.
            if (Items.DropTables.OrbChance(CurrencyKey) < 0.1f)
            {
                DrawRect(new Rect2(-2f, -70f, 4f, 70f), c with { A = 0.2f });
            }

            DrawCircle(Vector2.Zero, 7f, new Color(0.08f, 0.06f, 0.1f));
            DrawCircle(Vector2.Zero, 5.5f, c);
            DrawArc(Vector2.Zero, 7f, 0f, Mathf.Tau, 16, c.Lightened(0.4f), 1.2f, true);
            DrawCircle(new Vector2(-2f, -2f), 1.8f, new Color(1f, 1f, 1f, 0.85f));
            return;
        }

        if (Kind == PickupKind.Gem)
        {
            Color c = GemSupport ? new Color(0.35f, 0.9f, 0.45f) : new Color(1f, 0.5f, 0.25f);
            Vector2[] d = { new(0, -9), new(7, 0), new(0, 9), new(-7, 0) };
            DrawColoredPolygon(d, c);
            DrawPolyline(new[] { d[0], d[1], d[2], d[3], d[0] }, Colors.White, 1.5f);
            DrawCircle(new Vector2(-2f, -3f), 2f, new Color(1f, 1f, 1f, 0.8f));
            return;
        }

        if (Kind != PickupKind.Gold)
        {
            return;
        }

        DrawCircle(Vector2.Zero, 6f, new Color(0.45f, 0.3f, 0.05f));
        DrawCircle(Vector2.Zero, 5f, new Color(1f, 0.8f, 0.25f));
        DrawCircle(new Vector2(-1.5f, -1.5f), 2f, new Color(1f, 0.95f, 0.7f));
    }

    /// <summary>The two Rift power-ups: a beam, a halo that pulses (see
    /// _Process) and a glyph — a burst for the Cataclysm, a horseshoe for the Lodestone.</summary>
    private void DrawPowerUp(bool nuke)
    {
        Color c = nuke ? new Color(1f, 0.32f, 0.2f) : new Color(0.45f, 0.75f, 1f);
        float pulse = 0.5f + 0.5f * Mathf.Sin(_bob * 2f);
        DrawRect(new Rect2(-3f, -96f, 6f, 96f), c with { A = 0.16f });
        DrawRect(new Rect2(-1f, -96f, 2f, 96f), c with { A = 0.4f });
        DrawCircle(new Vector2(0f, 8f), 9f, new Color(0f, 0f, 0f, 0.3f));
        DrawArc(Vector2.Zero, 15f + pulse * 3f, 0f, Mathf.Tau, 24, c with { A = 0.35f + pulse * 0.3f }, 2f, true);
        DrawCircle(Vector2.Zero, 11f, new Color(0.07f, 0.05f, 0.09f));
        DrawArc(Vector2.Zero, 11f, 0f, Mathf.Tau, 24, c, 1.5f, true);
        if (nuke)
        {
            var star = new Vector2[16];
            for (int i = 0; i < star.Length; i++)
            {
                float r = i % 2 == 0 ? 8.5f : 3.5f;
                star[i] = Vector2.Up.Rotated(Mathf.Tau * i / star.Length) * r;
            }

            DrawColoredPolygon(star, new Color(1f, 0.6f, 0.2f));
            DrawCircle(Vector2.Zero, 3f, new Color(1f, 0.95f, 0.7f));
            return;
        }

        // Horseshoe: a thick arc with silver tips.
        DrawArc(new Vector2(0f, 1f), 5.5f, 0f, Mathf.Pi, 16, new Color(0.9f, 0.2f, 0.2f), 4f, true);
        DrawRect(new Rect2(-7.5f, -5f, 4f, 6f), new Color(0.9f, 0.2f, 0.2f));
        DrawRect(new Rect2(3.5f, -5f, 4f, 6f), new Color(0.9f, 0.2f, 0.2f));
        DrawRect(new Rect2(-7.5f, -8f, 4f, 3f), new Color(0.85f, 0.9f, 0.95f));
        DrawRect(new Rect2(3.5f, -8f, 4f, 3f), new Color(0.85f, 0.9f, 0.95f));
    }

    /// <summary>A Mirror Jewel: a violet hexagonal cut stone with a prismatic glint
    /// (also drawn by the jewel tree, so it lives here as a static).</summary>
    public static void DrawJewel(CanvasItem c, Vector2 at, float scale, float phase, bool beam = false)
    {
        if (beam)
        {
            Color b = Color.FromHsv(Mathf.PosMod(phase * 0.05f, 1f), 0.45f, 1f);
            c.DrawRect(new Rect2(at + new Vector2(-3f, -100f), new Vector2(6f, 100f)), b with { A = 0.2f });
            c.DrawRect(new Rect2(at + new Vector2(-1f, -100f), new Vector2(2f, 100f)), new Color(0.9f, 0.7f, 1f, 0.45f));
            c.DrawCircle(at + new Vector2(0f, 8f * scale), 8f * scale, new Color(0f, 0f, 0f, 0.3f));
        }

        var outer = new Vector2[6];
        var inner = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            Vector2 d = Vector2.Up.Rotated(Mathf.Tau * i / 6f);
            outer[i] = at + d * 9f * scale;
            inner[i] = at + d * 5f * scale;
        }

        c.DrawColoredPolygon(outer, new Color(0.42f, 0.18f, 0.7f));
        c.DrawColoredPolygon(new[] { outer[5], outer[0], outer[1], inner[1], inner[0], inner[5] }, new Color(0.78f, 0.55f, 1f));
        c.DrawColoredPolygon(inner, new Color(0.6f, 0.35f, 0.95f));
        c.DrawPolyline(new[] { outer[0], outer[1], outer[2], outer[3], outer[4], outer[5], outer[0] }, new Color(0.95f, 0.85f, 1f), 1.2f * scale, true);
        float glint = 0.5f + 0.5f * Mathf.Sin(phase * 3f);
        c.DrawCircle(at + new Vector2(-2.5f, -3.5f) * scale, 1.6f * scale, new Color(1f, 1f, 1f, 0.5f + 0.5f * glint));
    }

    public override void _Process(double delta)
    {
        if (Kind is PickupKind.Nuke or PickupKind.Magnet or PickupKind.Jewel)
        {
            QueueRedraw();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (GetTree().GetFirstNodeInGroup(Combat.CombatQuery.PlayerGroup) is not Player player)
        {
            return;
        }

        float distance = GlobalPosition.DistanceTo(player.GlobalPosition);
        // A refill orb stays on the ground while the flasks are full (items.md ->
        // "An orb is not consumed when the flask is already full").
        if (Kind == PickupKind.Life && Items.Flasks.Of(player) is { AllFull: true })
        {
            _attracted = false;
        }

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
        if (GetTree().GetFirstNodeInGroup(HandlerGroup) is IPickupHandler handler)
        {
            handler.OnPickup(this, player);
        }

        QueueFree();
    }
}
