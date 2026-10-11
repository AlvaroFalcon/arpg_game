#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using PathToExile.Combat;

namespace PathToExile.Enemies;

/// <summary>
/// Nereth, the Drowned Reflection — boss of the Drowned Mirror, docs/design/bosses.md
/// -> "Nereth".
///
/// Phase 1 (100–60%): Tidal Lanes (parallel lanes with one gap), Whirlpool
///   (a ring closes on you, then the centre bursts), Orb Spiral.
/// Phase 2 (60–30%): adds Mirror Images — two reflections appear mirrored
///   around you and each fires a cross of lanes; read all three sources.
/// Phase 3 (below 30%): adds Rising Tide — the room floods except for three
///   lit circles; be inside one (or roll through the wave) when it lands.
///
/// Cold damage throughout, so cold resistance matters for the first time.
/// </summary>
[GlobalClass]
public partial class Nereth : BossEnemy
{
    [Export]
    public SpriteFrames? OrbFrames;

    public override float[] PhaseThresholds => new[] { 0.6f, 0.3f };

    private static readonly Color Water = new(0.35f, 0.75f, 1f);
    private int _lastAttack = -1;

    private float Cadence => Phase >= 3 ? 0.75f : 1f;

    protected override string PhaseMessage(int phase) => phase == 2 ? "Nereth splits into reflections" : "The tide rises!";

    protected override void Move(float delta)
    {
        // Keeps a middle distance: close enough to pressure, far enough to read.
        DesiredVelocity = DistanceToTarget > 300f ? DirectionToTarget * Speed : DistanceToTarget < 180f ? -DirectionToTarget * Speed * 0.6f : Vector2.Zero;
    }

    protected override Task ChooseAttack()
    {
        int options = Phase switch { 1 => 3, 2 => 4, _ => 5 };
        int pick;
        do
        {
            pick = Rng.RandiRange(0, options - 1);
        }
        while (pick == _lastAttack && options > 1);

        _lastAttack = pick;
        return pick switch
        {
            0 => TidalLanes(),
            1 => Whirlpool(),
            2 => OrbSpiral(),
            3 => MirrorImages(),
            _ => RisingTide(),
        };
    }

    private async Task TidalLanes()
    {
        Announce("Tidal Lanes");
        Sprite?.Play("windup");
        int waves = Phase >= 2 ? 3 : 2;
        for (int w = 0; w < waves; w++)
        {
            bool horizontal = Rng.Randf() < 0.5f;
            Vector2 along = horizontal ? Vector2.Right : Vector2.Down;
            Vector2 across = horizontal ? Vector2.Down : Vector2.Right;
            const int lanes = 7;
            const float spacing = 96f;
            int gap = Rng.RandiRange(1, lanes - 2);
            Vector2 centre = TargetPosition;
            for (int i = 0; i < lanes; i++)
            {
                if (i == gap)
                {
                    continue;
                }

                Vector2 offset = across * (i - lanes / 2) * spacing;
                Lane(centre + offset - along * 700f, along, 1400f, 80f, 1.2f * Cadence, 17f, DamageType.Cold);
            }

            if (!await Wait(1.35f * Cadence)) return;
        }

        Sprite?.Play("idle");
        AttackCooldown = 1.3f * Cadence;
    }

    private async Task Whirlpool()
    {
        Announce("Whirlpool");
        Vector2 at = TargetPosition;
        // The ring closes in: stand still and it hits you; the safe spot is the eye...
        Slam(at, 190f, 1.0f * Cadence, 15f, 90f, DamageType.Cold);
        if (!await Wait(1.05f * Cadence)) return;
        // ...until the eye erupts a beat later. Move out after the ring lands.
        CallOut("THE EYE ERUPTS", Water);
        Slam(at, 95f, 0.8f * Cadence, 22f, 0f, DamageType.Cold);
        if (!await Wait(1.0f)) return;
        AttackCooldown = 1.2f * Cadence;
    }

    private async Task OrbSpiral()
    {
        Announce("Orb Spiral");
        Sprite?.Play("windup");
        int arms = Phase >= 2 ? 4 : 3;
        float angle = Rng.Randf() * Mathf.Tau;
        float spin = Rng.Randf() < 0.5f ? 0.32f : -0.32f;
        for (int step = 0; step < 14; step++)
        {
            for (int a = 0; a < arms; a++)
            {
                Bullet(Vector2.Right.Rotated(angle + Mathf.Tau * a / arms), 170f, 8f, OrbFrames, 2f, DamageType.Cold);
            }

            angle += spin;
            if (!await Wait(0.16f)) return;
        }

        Sprite?.Play("idle");
        AttackCooldown = 1.3f * Cadence;
    }

    private async Task MirrorImages()
    {
        Announce("Mirror Images");
        CallOut("WATCH THE REFLECTIONS", Water);
        Sprite?.Play("windup");
        Vector2 player = TargetPosition;
        Vector2 toBoss = GlobalPosition - player;
        var sources = new List<Vector2> { GlobalPosition, player - toBoss, player + new Vector2(-toBoss.Y, toBoss.X) };
        var ghosts = new List<Node2D>();
        for (int i = 1; i < sources.Count; i++)
        {
            ghosts.Add(SpawnReflection(sources[i]));
        }

        if (!await Wait(0.5f)) { FreeAll(ghosts); return; }
        foreach (Vector2 src in sources)
        {
            Vector2 dir = src.DirectionTo(player);
            Lane(src, dir, 900f, 64f, 1.25f * Cadence, 18f, DamageType.Cold);
            Lane(src, dir.Orthogonal(), 450f, 48f, 1.25f * Cadence, 12f, DamageType.Cold);
            Lane(src, -dir.Orthogonal(), 450f, 48f, 1.25f * Cadence, 12f, DamageType.Cold);
        }

        if (!await Wait(1.4f * Cadence)) { FreeAll(ghosts); return; }
        FreeAll(ghosts);
        Sprite?.Play("idle");
        AttackCooldown = 1.4f * Cadence;
    }

    private Node2D SpawnReflection(Vector2 at)
    {
        var ghost = new AnimatedSprite2D
        {
            SpriteFrames = Sprite?.SpriteFrames,
            Scale = Sprite?.Scale ?? Vector2.One * 3f,
            Modulate = new Color(0.6f, 0.9f, 1f, 0.55f),
            TextureFilter = TextureFilterEnum.Nearest,
        };
        GetTree().CurrentScene.AddChild(ghost);
        ghost.GlobalPosition = at + (Sprite?.Position ?? Vector2.Zero);
        ghost.Play("windup");
        return ghost;
    }

    private static void FreeAll(List<Node2D> nodes)
    {
        foreach (Node2D n in nodes)
        {
            if (IsInstanceValid(n))
            {
                n.QueueFree();
            }
        }
    }

    private async Task RisingTide()
    {
        Announce("Rising Tide");
        CallOut("FIND THE LIGHT!", new Color(1f, 0.9f, 0.5f));
        Core.Sfx.Play(this, "boss_warn");
        Sprite?.Play("windup");
        const float safeRadius = 70f;
        const float delay = 2.4f;
        var safe = new Vector2[3];
        for (int i = 0; i < safe.Length; i++)
        {
            safe[i] = TargetPosition + Vector2.Right.Rotated(Rng.Randf() * Mathf.Tau) * Rng.RandfRange(160f, 300f);
            AreaBurst.Spawn(GetTree().CurrentScene, safe[i], safeRadius, delay, new Color(1f, 0.9f, 0.5f), new HitProfile { Faction = Faction.Enemy }, 0f, harmless: true);
        }

        // The flood itself: a room-wide wash drawn under everything.
        AreaBurst flood = AreaBurst.Spawn(GetTree().CurrentScene, GlobalPosition, 1100f, delay, Water, new HitProfile { Faction = Faction.Enemy }, 0f, harmless: true);
        flood.ZIndex = -1;
        if (!await Wait(delay)) return;

        if (Target is { } t)
        {
            bool sheltered = false;
            foreach (Vector2 s in safe)
            {
                sheltered |= t.GlobalPosition.DistanceTo(s) <= safeRadius + 6f;
            }

            if (!sheltered)
            {
                // Lands this instant on the player: a well-timed roll still avoids it.
                AreaBurst.Spawn(GetTree().CurrentScene, t.GlobalPosition, 40f, 0.01f, Water, MakeHit(38f, DamageType.Cold, SkillTags.Area));
            }
        }

        Core.CameraShake.Request(this, 6f);
        if (!await Wait(0.6f)) return;
        Sprite?.Play("idle");
        AttackCooldown = 1.6f * Cadence;
    }
}
