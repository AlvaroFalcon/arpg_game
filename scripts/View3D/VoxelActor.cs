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
    private Node2D _anchor = null!;
    private Player? _player;

    // The mesh is resolved only when the sprite's frames, animation or frame change
    // (its signals), never polled: reading Sprite.Animation allocates a StringName, and
    // the old per-frame lookup built strings (docs/design/performance.md -> "Frame budget pass", I3).
    // A frame change only re-indexes the current animation's meshes; the animation (a
    // new StringName on every read) is read again only when it or the frames change.
    private bool _meshDirty = true;
    private bool _animDirty = true;
    private SpriteFrames? _frames;
    private StringName? _anim;
    private Mesh?[]? _animMeshes;
    private int _frame;

    // The overlay state last written, so a plain or unchanged tint costs no engine call.
    private bool _plain;
    private bool _overlayOn;
    private float _transparency = -1f;
    private Color _overlayColor = new(-1f, -1f, -1f, -1f);

    public override void _Ready()
    {
        _mesh = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        AddChild(_mesh);
        Sprite.SelfModulate = new Color(1f, 1f, 1f, 0f);

        // Projectiles and orbs fly at chest height and turn with their heading.
        // Feet: a physics body's origin (players, monsters, projectiles, dummies);
        // a free-standing sprite (props placed in a scene) is its own anchor.
        Node? owner = Sprite.GetParent();
        _flat = owner is Combat.Projectile or Combat.BossBullet;
        _height = _flat ? 0.45f : 0f;
        _anchor = owner is CollisionObject2D or Combat.BossBullet ? (Node2D)owner : Sprite;
        _player = owner as Player;
        Sprite.SpriteFramesChanged += MarkAnimDirty;
        Sprite.AnimationChanged += MarkAnimDirty;
        Sprite.FrameChanged += MarkMeshDirty;
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(Sprite))
        {
            Sprite.SpriteFramesChanged -= MarkAnimDirty;
            Sprite.AnimationChanged -= MarkAnimDirty;
            Sprite.FrameChanged -= MarkMeshDirty;
        }
    }

    private void MarkMeshDirty() => _meshDirty = true;

    private void MarkAnimDirty() => _animDirty = _meshDirty = true;

    /// <summary>The mesh shown now (tests).</summary>
    public Mesh? CurrentMesh => _mesh.Mesh;

    public override void _Process(double delta)
    {
        long t0 = Tools.Perf.BeginAlloc(out long b0);
        Follow();
        Tools.Perf.EndAlloc(Tools.Perf.K.VoxActor, t0, b0);
    }

    private void Follow()
    {
        if (!IsInstanceValid(Sprite) || !Sprite.IsInsideTree() || !IsInstanceValid(_anchor))
        {
            QueueFree();
            return;
        }

        Visible = Sprite.IsVisibleInTree();
        if (!Visible)
        {
            return;
        }

        if (_meshDirty)
        {
            long tm = Tools.Perf.Begin();
            _meshDirty = false;
            if (_animDirty)
            {
                _animDirty = false;
                _frames = Sprite.SpriteFrames;
                _anim = Sprite.Animation;
                _animMeshes = VoxelLibrary.MeshesFor(_frames, _anim);
            }

            _frame = Sprite.Frame;
            _mesh.Mesh = _animMeshes is { Length: > 0 } meshes ? meshes[Mathf.Clamp(_frame, 0, meshes.Length - 1)] : null;
            Tools.Perf.End(Tools.Perf.K.VoxMeshFor, tm);
        }

        float s = Mathf.Abs(Sprite.GlobalScale.X) / 2f;
        var scale = new Vector3(Sprite.FlipH ? -s : s, s, s);
        // Projectiles lie flat like their 2D sprite (sprite-up = screen-up = far)
        // and yaw with the heading: a 2D clockwise rotation θ is a yaw of -θ.
        // Upright figures cancel the stage's depth stretch and lean back, away from
        // the camera (most of the way to an HD-2D billboard), so faces read
        // instead of the tops of heads.
        // A downed co-op player lies on the ground on its side (docs/design/multiplayer.md -> "Deaths").
        bool lying = _player is { ShowsDowned: true };
        Basis basis = _flat || lying
            ? new Basis(Vector3.Up, -Sprite.GlobalRotation) * new Basis(Vector3.Right, -Mathf.Pi / 2f)
            : Basis.FromScale(new Vector3(1f, 1f, VoxelStage.SinPitch)) * new Basis(Vector3.Right, -Mathf.DegToRad(LeanDegrees));
        Vector2 feet = _anchor.GlobalPosition;
        if (lying && _anim is not null && _frames?.GetFrameTexture(_anim, _frame) is { } frame)
        {
            // The model stands on its feet (bottom-centre origin): lying, the body would
            // stretch out beside the revive ring. Centre it on the player instead.
            feet -= Vector2.Up.Rotated(Sprite.GlobalRotation) * frame.GetHeight() * Mathf.Abs(Sprite.GlobalScale.Y) * 0.4f;
        }

        Transform = new Transform3D(basis * Basis.FromScale(scale), VoxelStage.Ground(feet, lying ? 0.1f : _height));
        ApplyModulate(Sprite.Modulate * _anchor.Modulate);
    }

    /// <summary>Hit flashes (bright modulate), tints (rarity, ailments) and fades.</summary>
    private void ApplyModulate(Color m)
    {
        bool plain = Mathf.IsEqualApprox(m.R, 1f) && Mathf.IsEqualApprox(m.G, 1f) && Mathf.IsEqualApprox(m.B, 1f) && Mathf.IsEqualApprox(m.A, 1f);
        if (plain)
        {
            if (!_plain)
            {
                _plain = true;
                _overlayOn = false;
                _transparency = 0f;
                _mesh.MaterialOverlay = null;
                _mesh.Transparency = 0f;
            }

            return;
        }

        _plain = false;
        _overlay ??= new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
        };

        float brightness = (m.R + m.G + m.B) / 3f;
        // Hit flash: white over the model; otherwise the tint.
        Color albedo = brightness > 1.05f
            ? new Color(1f, 1f, 1f, Mathf.Clamp((brightness - 1f) * 0.9f, 0f, 0.85f))
            : new Color(m.R, m.G, m.B, 0.28f);
        if (albedo != _overlayColor)
        {
            _overlayColor = albedo;
            _overlay.AlbedoColor = albedo;
        }

        if (!_overlayOn)
        {
            _overlayOn = true;
            _mesh.MaterialOverlay = _overlay;
        }

        float transparency = 1f - Mathf.Clamp(m.A, 0f, 1f);
        if (transparency != _transparency)
        {
            _transparency = transparency;
            _mesh.Transparency = transparency;
        }
    }
}
