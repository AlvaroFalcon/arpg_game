#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Enemies;

namespace ArpgGame.View3D;

/// <summary>
/// The HD-2D voxel view of one scene — docs/design/art-pipeline.md -> "Voxels".
///
/// The game still simulates in 2D; this node renders it in 3D underneath the
/// 2D canvas. An orthographic camera pitched down by <see cref="PitchDegrees"/>
/// follows the active Camera2D, and the 3D world is stretched in depth by
/// 1/sin(pitch) so the ground plane lands on screen exactly where the 2D world
/// would have been drawn: telegraphs, labels, damage numbers and every other 2D
/// overlay keep lining up with the voxel world without changing a line of
/// gameplay code. Things with height (walls, characters) rise up the screen
/// the way 2D sprites already did.
///
/// What it does: builds voxel terrain from the scene's TileMaps (floor slabs,
/// glowing liquids with lights, tall walls that dither away in front of the
/// player), mirrors every AnimatedSprite2D that has a voxel model with a
/// <see cref="VoxelActor"/>, lights the scene (key light with shadows, a
/// light following the player, lights over lava/water), and bursts monsters
/// into voxel debris when they die.
/// </summary>
public partial class VoxelStage : Node3D
{
    /// <summary>World pixels per metre: a 16 px tile drawn at scale 2 is one metre.</summary>
    public const float PixelsPerMeter = 32f;

    public const float PitchDegrees = 40f;

    public static readonly float SinPitch = Mathf.Sin(Mathf.DegToRad(PitchDegrees));

    /// <summary>The (depth-stretched) node every 3D object of the scene lives under.</summary>
    public Node3D World { get; private set; } = null!;

    public Camera3D Camera { get; private set; } = null!;

    private DirectionalLight3D _sun = null!;
    private OmniLight3D _playerLight = null!;
    private WorldEnvironment _environment = null!;
    private readonly List<ShaderMaterial> _wallMaterials = new();

    /// <summary>Wall blocks by whole-metre ground cell (for chips on heavy hits) and their colour.</summary>
    private readonly Dictionary<Vector2I, Vector3> _walls = new();
    private Color _wallColor = new(0.45f, 0.38f, 0.35f);
    private Node2D? _player;

    /// <summary>The theme's look; realities set it from their theme.</summary>
    public Color AmbientColor = new(0.75f, 0.7f, 0.72f);

    public Color SunColor = new(1f, 0.9f, 0.8f);

    public Color HazardLightColor = new(1f, 0.45f, 0.15f);

    /// <summary>Maps a 2D world position (and a height in metres) into <see cref="World"/>.</summary>
    public static Vector3 Ground(Vector2 world2D, float height = 0f) =>
        new(world2D.X / PixelsPerMeter, height, world2D.Y / PixelsPerMeter);

    public static VoxelStage? Of(Node context) =>
        context.GetTree().CurrentScene?.GetNodeOrNull<VoxelStage>("VoxelStage");

    public override void _Ready()
    {
        Name = "VoxelStage";
        World = new Node3D { Name = "World", Scale = new Vector3(1f, 1f, 1f / SinPitch) };
        AddChild(World);

        Camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Far = 300f,
            Near = 0.05f,
            RotationDegrees = new Vector3(-PitchDegrees, 0f, 0f),
            Current = true,
        };
        AddChild(Camera);

        _sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-58f, 28f, 0f),
            LightEnergy = 1.25f,
            LightColor = SunColor,
            ShadowEnabled = true,
            ShadowBias = 0.03f,
            DirectionalShadowMaxDistance = 60f,
        };
        AddChild(_sun);

        _playerLight = new OmniLight3D { OmniRange = 6f, LightEnergy = 1.1f, LightColor = new Color(1f, 0.82f, 0.6f), ShadowEnabled = false };
        World.AddChild(_playerLight);

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = AmbientColor,
            AmbientLightEnergy = 1.1f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1.15f,
            GlowEnabled = true,
            GlowIntensity = 0.7f,
            GlowBloom = 0.08f,
            GlowHdrThreshold = 0.9f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.12f,
            AdjustmentContrast = 1.05f,
        };
        _environment = new WorldEnvironment { Environment = env };

        // Tilt-shift: the top (far) and bottom (near) of the screen blur, the
        // classic HD-2D diorama look. Forward+ only; ignored by Compatibility.
        _environment.CameraAttributes = new CameraAttributesPractical
        {
            DofBlurFarEnabled = true,
            DofBlurFarDistance = 46f,
            DofBlurFarTransition = 12f,
            DofBlurNearEnabled = true,
            DofBlurNearDistance = 32f,
            DofBlurNearTransition = 6f,
            DofBlurAmount = 0.06f,
        };
        AddChild(_environment);

        Enemy.AnyKilled += OnEnemyKilled;
    }

    public override void _ExitTree()
    {
        Enemy.AnyKilled -= OnEnemyKilled;
    }

    /// <summary>Applies a theme's colours (realities).</summary>
    public void SetMood(Color ambient, Color sun, Color hazard)
    {
        AmbientColor = ambient;
        SunColor = sun;
        HazardLightColor = hazard;
        if (_environment?.Environment is { } env)
        {
            env.AmbientLightColor = ambient;
        }

        if (_sun is not null)
        {
            _sun.LightColor = sun;
        }
    }

    public override void _Process(double delta)
    {
        SyncCamera();
        if (_player is null || !IsInstanceValid(_player))
        {
            _player = GetTree().GetFirstNodeInGroup(CombatQuery.PlayerGroup) as Node2D;
        }

        if (_player is not null)
        {
            Vector3 local = Ground(_player.GlobalPosition, 1.4f);
            _playerLight.Position = local;
            Vector3 feet = World.ToGlobal(Ground(_player.GlobalPosition));
            foreach (ShaderMaterial m in _wallMaterials)
            {
                m.SetShaderParameter("player_pos", feet);
            }
        }
    }

    /// <summary>Puts the orthographic camera where the Camera2D is looking, at the same zoom.</summary>
    private void SyncCamera()
    {
        Viewport viewport = GetViewport();
        Vector2 size = viewport.GetVisibleRect().Size;
        Vector2 center = size / 2f;
        float zoom = 1f;
        if (viewport.GetCamera2D() is { } cam)
        {
            center = cam.GetScreenCenterPosition();
            zoom = cam.Zoom.Y;
        }

        Camera.Size = size.Y / (zoom * PixelsPerMeter);
        Vector3 target = World.ToGlobal(Ground(center));
        const float distance = 40f;
        float pitch = Mathf.DegToRad(PitchDegrees);
        Camera.GlobalPosition = target + new Vector3(0f, Mathf.Sin(pitch), Mathf.Cos(pitch)) * distance;
    }

    // --- terrain -----------------------------------------------------------------------

    /// <summary>Builds voxel terrain for a TileMap and hides the 2D one.</summary>
    public void BuildTerrain(TileMap map)
    {
        if (VoxelLibrary.TerrainFor(map.TileSet) is not { } pieces)
        {
            return;
        }

        var byPiece = new Dictionary<string, List<Transform3D>>();
        var hazards = new List<Vector3>();
        foreach (Vector2I cell in map.GetUsedCells(0))
        {
            Vector2I atlas = map.GetCellAtlasCoords(0, cell);
            string piece = atlas.Y == 0 ? $"floor{Mathf.Clamp(atlas.X, 0, 3)}" : atlas.X switch
            {
                0 => "hazard0",
                1 => "hazard1",
                2 => "wall",
                _ => "rune",
            };

            Vector2 world2D = map.ToGlobal(map.MapToLocal(cell));
            float tileMeters = map.TileSet.TileSize.X * map.GlobalScale.X / PixelsPerMeter;
            var basis = Basis.Identity.Scaled(Vector3.One * tileMeters);
            Vector3 at = Ground(world2D);
            if (!byPiece.TryGetValue(piece, out List<Transform3D>? list))
            {
                byPiece[piece] = list = new List<Transform3D>();
            }

            list.Add(new Transform3D(basis, at));
            if (piece == "wall")
            {
                _walls[new Vector2I(Mathf.FloorToInt(at.X), Mathf.FloorToInt(at.Z))] = at;
            }

            if (piece.StartsWith("hazard"))
            {
                hazards.Add(at);
            }
        }

        foreach ((string piece, List<Transform3D> transforms) in byPiece)
        {
            if (!pieces.TryGetValue(piece, out Mesh? mesh))
            {
                continue;
            }

            if (piece == "wall")
            {
                if (mesh.GetSurfaceCount() > 0 && mesh.SurfaceGetMaterial(0) is BaseMaterial3D wm)
                {
                    _wallColor = wm.AlbedoColor;
                }

                mesh = SeeThroughWall(mesh);
            }

            var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = transforms.Count };
            for (int i = 0; i < transforms.Count; i++)
            {
                multimesh.SetInstanceTransform(i, transforms[i]);
            }

            World.AddChild(new MultiMeshInstance3D
            {
                Multimesh = multimesh,
                Name = $"Terrain_{piece}",
                CastShadow = piece == "wall" ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        AddHazardLights(hazards);
        map.Visible = false;
    }

    /// <summary>Copies the wall mesh with the dithering wall shader on every colour surface.</summary>
    private Mesh SeeThroughWall(Mesh source)
    {
        if (source is not ArrayMesh array)
        {
            return source;
        }

        var copy = (ArrayMesh)array.Duplicate();
        var shader = GD.Load<Shader>("res://resources/shaders/voxel_wall.gdshader");
        for (int s = 0; s < copy.GetSurfaceCount(); s++)
        {
            Color albedo = copy.SurfaceGetMaterial(s) is BaseMaterial3D bm ? bm.AlbedoColor : Colors.Gray;
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("albedo", albedo);
            copy.SurfaceSetMaterial(s, material);
            _wallMaterials.Add(material);
        }

        return copy;
    }

    /// <summary>A light over every few liquid tiles (spaced out, capped) so lava and water glow on their surroundings.</summary>
    private void AddHazardLights(List<Vector3> hazards)
    {
        var placed = new List<Vector3>();
        foreach (Vector3 h in hazards)
        {
            if (placed.Count >= 40)
            {
                break;
            }

            bool near = false;
            foreach (Vector3 p in placed)
            {
                if (p.DistanceSquaredTo(h) < 16f)
                {
                    near = true;
                    break;
                }
            }

            if (near)
            {
                continue;
            }

            placed.Add(h);
            World.AddChild(new OmniLight3D
            {
                Position = h + new Vector3(0f, 0.6f, 0f),
                OmniRange = 3.5f,
                LightEnergy = 1.3f,
                LightColor = HazardLightColor,
            });
        }
    }

    // --- actors ------------------------------------------------------------------------

    /// <summary>Mirrors a 2D sprite with a voxel actor, if it has a voxel model.</summary>
    public void Track(AnimatedSprite2D sprite)
    {
        if (VoxelLibrary.ModelPathFor(sprite.SpriteFrames) is not { } path || VoxelLibrary.Load(path) is null)
        {
            return;
        }

        if (sprite.HasMeta("voxel_actor"))
        {
            return;
        }

        var actor = new VoxelActor { Sprite = sprite, Stage = this };
        sprite.SetMeta("voxel_actor", true);
        World.AddChild(actor);
    }

    // --- effects -----------------------------------------------------------------------

    private void OnEnemyKilled(Enemy enemy)
    {
        if (!IsInstanceValid(enemy) || enemy.Sprite is not { } sprite || VoxelLibrary.MeshFor(sprite) is not { } mesh)
        {
            return;
        }

        float scale = sprite.GlobalScale.X / 2f;
        VoxelDebris.Burst(World, mesh, Ground(enemy.GlobalPosition), scale, enemy.IsBoss ? 220 : 70);
    }

    /// <summary>Ground debris where a big hit lands (slams, meteors, explosions).</summary>
    public void Impact(Vector2 at2D, float radiusPx, Color tint)
    {
        int count = Mathf.Clamp(Mathf.RoundToInt(radiusPx / 4f), 8, 60);
        Vector3 at = Ground(at2D);
        float radius = radiusPx / PixelsPerMeter;
        VoxelDebris.Puff(World, at, radius, tint, count);

        // Walls caught in the blast lose a few bricks.
        int chipped = 0;
        int r = Mathf.CeilToInt(radius) + 1;
        Vector2I c = new(Mathf.FloorToInt(at.X), Mathf.FloorToInt(at.Z));
        for (int dx = -r; dx <= r && chipped < 4; dx++)
        {
            for (int dz = -r; dz <= r && chipped < 4; dz++)
            {
                if (_walls.TryGetValue(c + new Vector2I(dx, dz), out Vector3 wall) && wall.DistanceTo(at) <= radius + 0.7f)
                {
                    VoxelDebris.Puff(World, wall + new Vector3(0f, 0.9f, 0f), 0.4f, _wallColor, 10);
                    chipped++;
                }
            }
        }
    }
}
