#nullable enable
using Godot;

namespace ArpgGame.View3D;

/// <summary>
/// The voxel body of one 2D sprite — docs/design/art-pipeline.md -> "Voxels".
/// Follows the sprite's owner every frame: position (feet on the ground, or
/// floating for projectiles), facing (FlipH mirrors the model), rotation
/// (projectiles yaw with their flight), scale, visibility, and the animation
/// frame (it shows the voxel mesh of whichever frame the 2D sprite is on).
/// The 2D sprite itself is made transparent but keeps running, so every
/// gameplay script that drives sprites keeps working untouched. Hit flashes
/// and tints on the sprite's Modulate become a colour overlay.
/// </summary>
public partial class VoxelActor : Node3D
{
    /// <summary>How far upright figures lean back from the camera (the camera pitch would be a full billboard facing it).</summary>
    public const float LeanDegrees = 30f;

    public AnimatedSprite2D Sprite = null!;
    public VoxelStage Stage = null!;

    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D? _overlay;
    private float _height;
    private bool _flat;

    public override void _Ready()
    {
        _mesh = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        AddChild(_mesh);
        Sprite.SelfModulate = new Color(1f, 1f, 1f, 0f);

        // Projectiles and orbs fly at chest height and turn with their heading.
        Node? owner = Sprite.GetParent();
        _flat = owner is Combat.Projectile or Combat.BossBullet;
        _height = _flat ? 0.45f : 0f;
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(Sprite) || !Sprite.IsInsideTree())
        {
            QueueFree();
            return;
        }

        // Feet: a physics body's origin (players, monsters, projectiles, dummies);
        // a free-standing sprite (props placed in a scene) is its own anchor.
        Node? parent = Sprite.GetParent();
        Node2D anchor = parent is CollisionObject2D or Combat.BossBullet ? (Node2D)parent : Sprite;
        Visible = Sprite.IsVisibleInTree();
        if (!Visible)
        {
            return;
        }

        float s = Mathf.Abs(Sprite.GlobalScale.X) / 2f;
        var scale = new Vector3(Sprite.FlipH ? -s : s, s, s);
        // Projectiles lie flat like their 2D sprite (sprite-up = screen-up = far)
        // and yaw with the heading: a 2D clockwise rotation θ is a yaw of -θ.
        // Upright figures cancel the stage's depth stretch and lean back, away from
        // the camera (most of the way to an HD-2D billboard), so faces read
        // instead of the tops of heads.
        Basis basis = _flat
            ? new Basis(Vector3.Up, -Sprite.GlobalRotation) * new Basis(Vector3.Right, -Mathf.Pi / 2f)
            : Basis.FromScale(new Vector3(1f, 1f, VoxelStage.SinPitch)) * new Basis(Vector3.Right, -Mathf.DegToRad(LeanDegrees));
        Transform = new Transform3D(basis * Basis.FromScale(scale), VoxelStage.Ground(anchor.GlobalPosition, _height));
        _mesh.Mesh = VoxelLibrary.MeshFor(Sprite);

        ApplyModulate(Sprite.Modulate * anchor.Modulate);
    }

    /// <summary>Hit flashes (bright modulate), tints (rarity, ailments) and fades.</summary>
    private void ApplyModulate(Color m)
    {
        bool plain = Mathf.IsEqualApprox(m.R, 1f) && Mathf.IsEqualApprox(m.G, 1f) && Mathf.IsEqualApprox(m.B, 1f) && Mathf.IsEqualApprox(m.A, 1f);
        if (plain)
        {
            _mesh.MaterialOverlay = null;
            _mesh.Transparency = 0f;
            return;
        }

        _overlay ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
        };

        float brightness = (m.R + m.G + m.B) / 3f;
        if (brightness > 1.05f)
        {
            // Hit flash: white over the model.
            _overlay.AlbedoColor = new Color(1f, 1f, 1f, Mathf.Clamp((brightness - 1f) * 0.9f, 0f, 0.85f));
        }
        else
        {
            _overlay.AlbedoColor = new Color(m.R, m.G, m.B, 0.28f);
        }

        _mesh.MaterialOverlay = _overlay;
        _mesh.Transparency = 1f - Mathf.Clamp(m.A, 0f, 1f);
    }
}
