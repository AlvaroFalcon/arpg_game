#nullable enable
using System.Threading.Tasks;
using Godot;
using ArpgGame.Combat;

namespace ArpgGame.Enemies;

/// <summary>
/// The Bone Herald — mid-run boss, docs/design/bosses.md -> "The Bone Herald".
/// A caster that keeps its distance and teaches the boss vocabulary the final
/// fight will test: read a spiral's gaps, step out of circles, don't stand
/// where it is about to land.
///
/// Phase 1: Bone Spiral, Grave Circles, Raise Dead, Blink.
/// Phase 2 (below 50%): every pattern gets denser and faster.
/// </summary>
[GlobalClass]
public partial class BoneHerald : BossEnemy
{
    [Export]
    public SpriteFrames? BoltFrames;

    [Export]
    public PackedScene? MinionScene;

    public override float[] PhaseThresholds => new[] { 0.5f };

    private int _lastAttack = -1;

    protected override string PhaseMessage(int phase) => "The Herald's chant grows louder";

    protected override void Move(float delta)
    {
        // Keeps a casting distance, circling slowly.
        float d = DistanceToTarget;
        Vector2 dir = DirectionToTarget;
        DesiredVelocity = d > 280f ? dir * Speed : d < 170f ? -dir * Speed : new Vector2(-dir.Y, dir.X) * Speed * 0.4f;
    }

    protected override Task ChooseAttack()
    {
        int pick;
        do
        {
            pick = Rng.RandiRange(0, 3);
        }
        while (pick == _lastAttack);

        _lastAttack = pick;
        return pick switch
        {
            0 => BoneSpiral(),
            1 => GraveCircles(),
            2 => RaiseDead(),
            _ => Blink(),
        };
    }

    private async Task BoneSpiral()
    {
        Announce("Bone Spiral");
        Sprite?.Play("cast");
        if (!await Wait(0.7f)) return;

        int arms = Phase >= 2 ? 5 : 3;
        float spin = Rng.Randf() < 0.5f ? 1f : -1f;
        float angle = Rng.Randf() * Mathf.Tau;
        int volleys = Phase >= 2 ? 16 : 12;
        for (int v = 0; v < volleys; v++)
        {
            for (int a = 0; a < arms; a++)
            {
                Bullet(Vector2.Right.Rotated(angle + Mathf.Tau * a / arms), 170f, 9f, BoltFrames, 2f, DamageType.Cold);
            }

            angle += 0.21f * spin;
            if (!await Wait(0.16f)) return;
        }

        Sprite?.Play("idle");
        AttackCooldown = Phase >= 2 ? 1.2f : 1.8f;
    }

    private async Task GraveCircles()
    {
        Announce("Grave Circles");
        Sprite?.Play("cast");
        int count = Phase >= 2 ? 6 : 4;
        for (int i = 0; i < count; i++)
        {
            Vector2 at = ClampToArena(TargetPosition + new Vector2(Rng.RandfRange(-30f, 30f), Rng.RandfRange(-30f, 30f)));
            Slam(at, 70f, 1.1f, 16f, 0f, DamageType.Cold);
            if (!await Wait(Phase >= 2 ? 0.35f : 0.5f)) return;
        }

        if (!await Wait(0.8f)) return;
        Sprite?.Play("idle");
        AttackCooldown = 1.5f;
    }

    private async Task RaiseDead()
    {
        Announce("Raise Dead");
        Sprite?.Play("cast");
        int count = Phase >= 2 ? 8 : 5;
        var marks = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            marks[i] = ClampToArena(GlobalPosition + Vector2.Right.Rotated(Mathf.Tau * i / count) * 90f);
            AreaBurst.Spawn(GetTree().CurrentScene, marks[i], 22f, 1f, new Color(0.7f, 0.4f, 1f), new HitProfile { Faction = Faction.Enemy }, 0f, harmless: true);
        }

        if (!await Wait(1f)) return;
        var state = GetTree().GetFirstNodeInGroup(Run.RunState.Group) as Run.RunState;
        World.RealityController? reality = World.RealityController.Of(this);
        if (MinionScene is not null && (state is not null || reality is not null))
        {
            foreach (Vector2 at in marks)
            {
                var minion = MinionScene.Instantiate<Enemy>();
                GetTree().CurrentScene.AddChild(minion);
                minion.GlobalPosition = at;
                minion.ExperienceValue = 0;
                if (state is not null)
                {
                    state.RegisterEnemy(minion);
                }
                else
                {
                    reality!.AdoptMonster(minion);
                }
            }
        }

        Sprite?.Play("idle");
        AttackCooldown = 2f;
    }

    private async Task Blink()
    {
        Announce("Blink");
        Vector2 destination = ClampToArena(TargetPosition + Vector2.Right.Rotated(Rng.Randf() * Mathf.Tau) * 240f, 100f);
        float novaRadius = Phase >= 2 ? 130f : 100f;
        Slam(destination, novaRadius, 1f, 13f, 0f, DamageType.Cold);
        Tween fade = CreateTween();
        fade.TweenProperty(this, "modulate:a", 0.15f, 0.4f);
        if (!await Wait(1f)) return;
        GlobalPosition = destination;
        Tween back = CreateTween();
        back.TweenProperty(this, "modulate:a", 1f, 0.2f);
        AttackCooldown = 1.2f;
    }
}
