#nullable enable
using System.Threading.Tasks;
using Godot;
using ArpgGame.Combat;

namespace ArpgGame.Enemies;

/// <summary>
/// The Ember Colossus — final boss, docs/design/bosses.md -> "The Ember Colossus".
///
/// Phase 1 (100–65%): Seismic Slam (+ Aftershock ring), Molten Rain, Charge, Fire Fan.
/// Phase 2 (65–30%): adds Inferno Ring — the in/out check: the boss calls
///   "GET OUT!" (inner circle burns) or "GET CLOSE!" (outer ring burns), and
///   summons adds.
/// Phase 3 (below 30%): enrage — faster cadence, adds Flame Beams (rotating
///   lanes from its body; stand between them) and periodic orb rings.
///
/// Damage is high on purpose (an Inferno Ring is a third of a fresh
/// character's life), but every hit is drawn on the floor for at least 0.9 s,
/// and every one can be dodged through with the roll's invulnerability.
/// </summary>
[GlobalClass]
public partial class EmberColossus : BossEnemy
{
    [Export]
    public SpriteFrames? OrbFrames;

    [Export]
    public PackedScene? AddScene;

    public override float[] PhaseThresholds => new[] { 0.65f, 0.3f };

    private int _lastAttack = -1;
    private bool _charging;
    private float _orbRingTimer = 4f;

    protected override string PhaseMessage(int phase) => phase == 2 ? "The Colossus roars — the arena burns" : "The Colossus is enraged!";

    protected override float ContactDamageScale() => _charging ? 2.5f : 1f;

    private float Cadence => Phase >= 3 ? 0.7f : 1f;

    protected override void Think(float delta)
    {
        base.Think(delta);
        if (Phase >= 3 && Alive)
        {
            _orbRingTimer -= delta;
            if (_orbRingTimer <= 0f)
            {
                _orbRingTimer = 5f;
                OrbRing(10, 150f);
            }
        }
    }

    protected override void Move(float delta)
    {
        DesiredVelocity = DistanceToTarget > 160f ? DirectionToTarget * Speed : Vector2.Zero;
    }

    protected override Task ChooseAttack()
    {
        int options = Phase switch { 1 => 4, 2 => 6, _ => 7 };
        int pick;
        do
        {
            pick = Rng.RandiRange(0, options - 1);
        }
        while (pick == _lastAttack);

        _lastAttack = pick;
        return pick switch
        {
            0 => SeismicSlam(),
            1 => MoltenRain(),
            2 => Charge(),
            3 => FireFan(),
            4 => InfernoRing(),
            5 => Summon(),
            _ => FlameBeams(),
        };
    }

    private async Task SeismicSlam()
    {
        Announce("Seismic Slam");
        Sprite?.Play("windup");
        float radius = 170f;
        Slam(GlobalPosition, radius, 1.1f * Cadence, 21f);
        if (!await Wait(1.1f * Cadence)) return;
        Sprite?.Play("idle");

        // Aftershock: the ring just outside the slam — punishes running only a
        // little way out. Telegraphed the moment the slam lands.
        Announce("Aftershock");
        Slam(GlobalPosition, radius + 140f, 0.9f, 16f, radius);
        if (!await Wait(1.2f)) return;
        AttackCooldown = 1.4f * Cadence;
    }

    private async Task MoltenRain()
    {
        Announce("Molten Rain");
        int count = Phase >= 2 ? 10 : 7;
        for (int i = 0; i < count; i++)
        {
            Vector2 lead = Target is Player p ? p.Velocity * 0.4f : Vector2.Zero;
            Vector2 at = ClampToArena(TargetPosition + lead + new Vector2(Rng.RandfRange(-50f, 50f), Rng.RandfRange(-50f, 50f)));
            Slam(at, 72f, 1.0f, 11f);
            if (!await Wait(0.28f * Cadence)) return;
        }

        if (!await Wait(0.9f)) return;
        AttackCooldown = 1.2f * Cadence;
    }

    private async Task Charge()
    {
        int charges = Phase >= 2 ? 2 : 1;
        for (int c = 0; c < charges; c++)
        {
            Announce("Charge");
            Sprite?.Play("windup");
            Vector2 dir = DirectionToTarget;
            float length = 620f;
            Lane(GlobalPosition, dir, length, 90f, 0.95f * Cadence, 0f).Harmless = true;
            if (!await Wait(0.95f * Cadence)) return;
            Sprite?.Play("idle");
            _charging = true;
            float speed = 1300f;
            BusyVelocity = dir * speed;
            Core.CameraShake.Request(this, 5f);
            if (!await Wait(length / speed)) { _charging = false; return; }
            _charging = false;
            BusyVelocity = Vector2.Zero;
            if (!await Wait(0.5f)) return;
        }

        AttackCooldown = 1.3f * Cadence;
    }

    private async Task FireFan()
    {
        Announce("Fire Fan");
        int volleys = Phase >= 2 ? 4 : 3;
        int count = Phase >= 2 ? 9 : 7;
        for (int v = 0; v < volleys; v++)
        {
            Sprite?.Play("windup");
            if (!await Wait(0.45f * Cadence)) return;
            Sprite?.Play("idle");
            float baseAngle = DirectionToTarget.Angle();
            float spread = Mathf.DegToRad(70f);
            float offset = v % 2 == 0 ? 0f : spread / (count - 1) / 2f;
            for (int i = 0; i < count; i++)
            {
                float a = baseAngle - spread / 2f + spread * i / (count - 1) + offset;
                Bullet(Vector2.Right.Rotated(a), 210f, 8f, OrbFrames, 2.2f);
            }

            if (!await Wait(0.5f)) return;
        }

        AttackCooldown = 1.3f * Cadence;
    }

    private async Task InfernoRing()
    {
        bool getOut = Rng.Randf() < 0.5f;
        float inner = 200f;
        float outer = 720f;
        Announce("Inferno Ring");
        Sprite?.Play("windup");
        Core.Sfx.Play(this, "boss_warn");
        if (getOut)
        {
            CallOut("GET OUT!", new Color(1f, 0.35f, 0.25f));
            Slam(GlobalPosition, inner, 2.0f, 31f);
        }
        else
        {
            CallOut("GET CLOSE!", new Color(1f, 0.75f, 0.3f));
            Slam(GlobalPosition, outer, 2.0f, 31f, inner);
        }

        if (!await Wait(2.2f)) return;
        Sprite?.Play("idle");
        AttackCooldown = 1.5f * Cadence;
    }

    private async Task Summon()
    {
        Announce("Call the Brood");
        var state = GetTree().GetFirstNodeInGroup(Run.RunState.Group) as Run.RunState;
        World.RealityController? reality = World.RealityController.Of(this);
        if (AddScene is null || (state is null && reality is null))
        {
            AttackCooldown = 0.5f;
            return;
        }

        int count = Phase >= 3 ? 4 : 3;
        var spots = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            spots[i] = ClampToArena(GlobalPosition + Vector2.Right.Rotated(Mathf.Tau * i / count + 0.4f) * 140f);
            AreaBurst.Spawn(GetTree().CurrentScene, spots[i], 26f, 0.9f, new Color(1f, 0.6f, 0.2f), new HitProfile { Faction = Faction.Enemy }, 0f, harmless: true);
        }

        if (!await Wait(0.9f)) return;
        foreach (Vector2 at in spots)
        {
            var add = AddScene.Instantiate<Enemy>();
            GetTree().CurrentScene.AddChild(add);
            add.GlobalPosition = at;
            add.ExperienceValue = 1;
            if (state is not null)
            {
                state.RegisterEnemy(add);
            }
            else
            {
                reality!.AdoptMonster(add);
            }
        }

        AttackCooldown = 1.5f;
    }

    private async Task FlameBeams()
    {
        Announce("Flame Beams");
        CallOut("STAND BETWEEN THE BEAMS", new Color(1f, 0.6f, 0.3f));
        Sprite?.Play("windup");
        int beams = 4;
        float start = Rng.Randf() * Mathf.Tau;
        for (int wave = 0; wave < 3; wave++)
        {
            float rot = start + wave * Mathf.Pi / beams;
            for (int i = 0; i < beams; i++)
            {
                Lane(GlobalPosition, Vector2.Right.Rotated(rot + Mathf.Tau * i / beams), 900f, 70f, 1.25f, 18f);
            }

            if (!await Wait(1.35f)) return;
        }

        Sprite?.Play("idle");
        AttackCooldown = 1.4f * Cadence;
    }

    private void OrbRing(int count, float speed)
    {
        float offset = Rng.Randf() * Mathf.Tau;
        for (int i = 0; i < count; i++)
        {
            Bullet(Vector2.Right.Rotated(offset + Mathf.Tau * i / count), speed, 8f, OrbFrames, 2.2f);
        }
    }
}
