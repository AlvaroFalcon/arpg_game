#nullable enable
using System.Collections.Generic;
using Godot;

namespace ArpgGame.Combat;

/// <summary>
/// Runtime instance of an <see cref="Skills.OrbitSkillDefinition"/> cast: a set
/// of objects circling an anchor (the caster) that damage whatever they touch.
/// Follows the anchor rather than being its child so it can outlive a dead
/// caster for a frame without crashing, and so it does not inherit the
/// caster's sprite flip.
/// </summary>
[GlobalClass]
public partial class OrbitBlades : Node2D
{
    public Node2D? Anchor;
    public int Count = 2;
    public float Radius = 70f;
    public float Duration = 4f;
    public float AngularSpeed = 3.5f;
    public float HitRadius = 14f;
    public float HitCooldown = 0.5f;
    public HitProfile Hit = new();
    public SpriteFrames? Frames;

    private float _elapsed;
    private float _angle;
    private readonly List<Node2D> _blades = new();
    private readonly Dictionary<ulong, float> _lastHit = new();

    public override void _Ready()
    {
        for (int i = 0; i < Count; i++)
        {
            Node2D blade;
            if (Frames is not null)
            {
                var sprite = new AnimatedSprite2D { SpriteFrames = Frames, Scale = new Vector2(2f, 2f), TextureFilter = TextureFilterEnum.Nearest };
                sprite.Play();
                blade = sprite;
            }
            else
            {
                blade = new Polygon2D { Color = new Color(0.8f, 0.85f, 0.95f), Polygon = new[] { new Vector2(10, 0), new Vector2(0, 4), new Vector2(-10, 0), new Vector2(0, -4) } };
            }

            AddChild(blade);
            _blades.Add(blade);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _elapsed += dt;
        if (_elapsed >= Duration || Anchor is null || !IsInstanceValid(Anchor))
        {
            QueueFree();
            return;
        }

        GlobalPosition = Anchor.GlobalPosition;
        _angle += AngularSpeed * dt;

        // Fade in/out at the ends so blades don't pop.
        float fade = Mathf.Min(1f, Mathf.Min(_elapsed / 0.2f, (Duration - _elapsed) / 0.3f));
        Modulate = new Color(1f, 1f, 1f, fade);

        float now = _elapsed;
        for (int i = 0; i < _blades.Count; i++)
        {
            float a = _angle + Mathf.Tau * i / _blades.Count;
            Vector2 offset = Vector2.Right.Rotated(a) * Radius;
            Node2D blade = _blades[i];
            blade.Position = offset;
            blade.Rotation = a * 3f;

            foreach (Health health in CombatQuery.HostilesInRadius(GetTree(), GlobalPosition + offset, HitRadius, Hit.Faction))
            {
                ulong id = health.GetInstanceId();
                if (_lastHit.TryGetValue(id, out float last) && now - last < HitCooldown)
                {
                    continue;
                }

                _lastHit[id] = now;
                health.TakeDamage(Hit.BuildPayload(offset.Normalized()));
            }
        }
    }
}
