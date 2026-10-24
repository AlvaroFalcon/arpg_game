#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Enemies;

namespace ArpgGame.View3D;

/// <summary>What fills a level's liquid tiles (VoxelStage.LiquidLook).</summary>
public enum LiquidStyle
{
    Lava,
    Water,
    Ooze,
}

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

    /// <summary>What fills the liquid tiles (the surface shader's look).</summary>
    public LiquidStyle Liquid = LiquidStyle.Lava;

    /// <summary>
    /// Generated caverns (docs/design/endgame.md -> "Caverns"): rock walls rise to
    /// uneven heights and glowing clusters (embers, ghost-lights, crystals in
    /// <see cref="GlowColor"/>) dot the cave walls with flickering light.
    /// </summary>
    public bool Caverns;

    public Color GlowColor = new(1f, 0.5f, 0.2f);

    /// <summary>Lights that flicker: liquid glows and cave glows.</summary>
    private readonly List<(OmniLight3D Light, float Energy, float Phase, float Speed, float Depth)> _flicker = new();
    private float _time;

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
    public void SetMood(Color ambient, Color sun, Color hazard, LiquidStyle liquid = LiquidStyle.Lava)
    {
        Liquid = liquid;
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
        long t0 = Tools.Perf.Begin();
        Follow(delta);
        Tools.Perf.End(Tools.Perf.K.VoxStage, t0);
    }

    private void Follow(double delta)
    {
        SyncCamera();
        _time += (float)delta;
        foreach ((OmniLight3D light, float energy, float phase, float speed, float depth) in _flicker)
        {
            if (IsInstanceValid(light))
            {
                float wobble = Mathf.Sin(_time * speed + phase) * 0.6f + Mathf.Sin(_time * speed * 2.7f + phase * 1.9f) * 0.4f;
                light.LightEnergy = energy * (1f + depth * wobble);
            }
        }

        if (_player is null || !IsInstanceValid(_player))
        {
            _player = CombatQuery.LocalPlayer(GetTree());
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

        var created = new List<Node>();
        _terrain[map.GetInstanceId()] = created;

        var byPiece = new Dictionary<string, List<Transform3D>>();
        var hazards = new List<Vector3>();
        var liquidCells = new Dictionary<Vector2I, Transform3D>();
        var floorCells = new Dictionary<Vector2I, Vector3>();
        var wallCells = new HashSet<Vector2I>();
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

            Transform3D placed = new(basis, at);
            if (piece == "wall" && Caverns)
            {
                // Cave rock is one mesh (CaveWalls, built below), not a block per
                // cell; the cell gets a floor under it for the chamfered corners.
                string under = $"floor{Mathf.Abs(cell.X * 7 + cell.Y * 13) % 2}";
                if (!byPiece.TryGetValue(under, out List<Transform3D>? unders))
                {
                    byPiece[under] = unders = new List<Transform3D>();
                }

                unders.Add(placed);
            }
            else
            {
                list.Add(placed);
            }

            if (piece.StartsWith("floor"))
            {
                floorCells[cell] = at;
            }

            if (piece == "wall")
            {
                wallCells.Add(cell);
                _walls[new Vector2I(Mathf.FloorToInt(at.X), Mathf.FloorToInt(at.Z))] = at;
            }

            if (piece.StartsWith("hazard"))
            {
                hazards.Add(at);
                liquidCells[cell] = new Transform3D(basis, at);
                // The liquid surface rounds its shore and cuts the land side away,
                // so the bed under it is ordinary floor (seen at the rounded edges).
                list.RemoveAt(list.Count - 1);
                string bed = $"floor{Mathf.Abs(cell.X * 7 + cell.Y * 13) % 2}";
                if (!byPiece.TryGetValue(bed, out List<Transform3D>? beds))
                {
                    byPiece[bed] = beds = new List<Transform3D>();
                }

                beds.Add(new Transform3D(basis, at));
            }
        }

        if (Caverns && wallCells.Count > 0 && pieces.TryGetValue("wall", out Mesh? rockSource))
        {
            created.Add(AddCaveRock(map, wallCells, rockSource));
        }

        foreach ((string piece, List<Transform3D> transforms) in byPiece)
        {
            if (transforms.Count == 0 || !pieces.TryGetValue(piece, out Mesh? mesh))
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

            var instance = new MultiMeshInstance3D
            {
                Multimesh = multimesh,
                Name = $"Terrain_{piece}",
                CastShadow = piece == "wall" ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            };
            World.AddChild(instance);
            created.Add(instance);
        }

        created.AddRange(AddHazardLights(hazards));
        if (Caverns)
        {
            created.AddRange(AddCaveGlows(floorCells, wallCells, map.TileSet.TileSize.X * map.GlobalScale.X / PixelsPerMeter));
        }

        if (liquidCells.Count > 0)
        {
            Vector3 GlobalOf(Vector2I c) => World.ToGlobal(Ground(map.ToGlobal(map.MapToLocal(c))));
            created.Add(AddLiquidSurface(liquidCells, floorCells, GlobalOf));
        }

        map.Visible = false;
    }

    /// <summary>
    /// The animated liquid surface (resources/shaders/voxel_liquid.gdshader). The
    /// shoreline does not follow the tiles: the liquid tiles are written into a mask
    /// (one texel per tile), blurred, and the shader cuts the surface where the
    /// blurred mask crosses one half — so the map's square steps become a smooth,
    /// rounded bank. One subdivided quad per liquid tile and per floor tile around
    /// it (inside corners fill out over the floor a little); land is cut away and the
    /// floor drawn beneath shows through.
    /// </summary>
    private MultiMeshInstance3D AddLiquidSurface(Dictionary<Vector2I, Transform3D> cells, Dictionary<Vector2I, Vector3> floors, System.Func<Vector2I, Vector3> globalOf)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://resources/shaders/voxel_liquid.gdshader") };
        foreach ((string name, Variant value) in LiquidLook(Liquid))
        {
            material.SetShaderParameter(name, value);
        }

        // The mask: liquid tiles = 1, a margin of three tiles, then a 5-tap Gaussian
        // blur in each direction (about one tile of rounding).
        const int margin = 3;
        int minX = cells.Keys.Min(c => c.X) - margin;
        int minY = cells.Keys.Min(c => c.Y) - margin;
        int w = cells.Keys.Max(c => c.X) - minX + 1 + margin;
        int h = cells.Keys.Max(c => c.Y) - minY + 1 + margin;
        var mask = new float[w, h];
        foreach (Vector2I c in cells.Keys)
        {
            mask[c.X - minX, c.Y - minY] = 1f;
        }

        float[] k = { 1f / 16f, 4f / 16f, 6f / 16f, 4f / 16f, 1f / 16f };
        var tmp = new float[w, h];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                float sum = 0f;
                for (int i = -2; i <= 2; i++)
                {
                    sum += k[i + 2] * mask[Mathf.Clamp(x + i, 0, w - 1), y];
                }

                tmp[x, y] = sum;
            }
        }

        Image image = Image.Create(w, h, false, Image.Format.Rf);
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                float sum = 0f;
                for (int i = -2; i <= 2; i++)
                {
                    sum += k[i + 2] * tmp[x, Mathf.Clamp(y + i, 0, h - 1)];
                }

                image.SetPixel(x, y, new Color(sum, 0f, 0f));
            }
        }

        // Where texel (0,0)'s centre is in the shader's world space, and one tile's step.
        Vector3 g0 = globalOf(new Vector2I(minX, minY));
        Vector3 gx = globalOf(new Vector2I(minX + 1, minY));
        Vector3 gz = globalOf(new Vector2I(minX, minY + 1));
        material.SetShaderParameter("shore_mask", ImageTexture.CreateFromImage(image));
        material.SetShaderParameter("mask_origin", new Vector2(g0.X, g0.Z));
        material.SetShaderParameter("mask_step", new Vector2(gx.X - g0.X, gz.Z - g0.Z));
        material.SetShaderParameter("mask_size", new Vector2(w, h));

        // The surface floats just over the floor (two voxels, 2/16 of a tile), high
        // enough that wave troughs never dip under it.
        var quad = new PlaneMesh { Size = Vector2.One, SubdivideWidth = 3, SubdivideDepth = 3, CenterOffset = new Vector3(0f, 2.7f / 16f, 0f), Material = material };
        Basis basis = cells.Values.First().Basis;
        var placed = new List<Transform3D>(cells.Values);
        foreach ((Vector2I c, Vector3 at) in floors)
        {
            bool beside = false;
            for (int dx = -1; dx <= 1 && !beside; dx++)
            {
                for (int dy = -1; dy <= 1 && !beside; dy++)
                {
                    beside = cells.ContainsKey(c + new Vector2I(dx, dy));
                }
            }

            if (beside)
            {
                placed.Add(new Transform3D(basis, at));
            }
        }

        var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = quad, InstanceCount = placed.Count };
        for (int i = 0; i < placed.Count; i++)
        {
            multimesh.SetInstanceTransform(i, placed[i]);
        }

        var instance = new MultiMeshInstance3D { Multimesh = multimesh, Name = "LiquidSurface", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        World.AddChild(instance);
        return instance;
    }

    /// <summary>Shader uniforms per liquid: clear moving water, slow glowing lava, sickly ooze.</summary>
    public static (string, Variant)[] LiquidLook(LiquidStyle style) => style switch
    {
        LiquidStyle.Water => new (string, Variant)[]
        {
            ("deep_color", new Color(0.03f, 0.14f, 0.28f)), ("shallow_color", new Color(0.1f, 0.45f, 0.6f)), ("foam_color", new Color(0.85f, 0.95f, 1f)),
            ("emission_strength", 0.08f), ("flow_speed", 1f), ("wave_height", 0.035f), ("roughness_value", 0.05f), ("caustics", 0.45f), ("style", 0),
        },
        LiquidStyle.Ooze => new (string, Variant)[]
        {
            ("deep_color", new Color(0.06f, 0.2f, 0.05f)), ("shallow_color", new Color(0.3f, 0.8f, 0.2f)), ("foam_color", new Color(0.75f, 1f, 0.5f)),
            ("emission_strength", 0.45f), ("flow_speed", 0.45f), ("wave_height", 0.05f), ("roughness_value", 0.25f), ("caustics", 0.3f), ("style", 2),
        },
        _ => new (string, Variant)[]
        {
            ("deep_color", new Color(0.16f, 0.04f, 0.03f)), ("shallow_color", new Color(1f, 0.36f, 0.06f)), ("foam_color", new Color(1f, 0.85f, 0.45f)),
            ("crack_color", new Color(1f, 0.85f, 0.38f)),
            ("emission_strength", 1.5f), ("flow_speed", 1f), ("wave_height", 0.03f), ("roughness_value", 0.8f), ("caustics", 0.6f), ("style", 1),
        },
    };

    private readonly Dictionary<ulong, List<Node>> _terrain = new();

    /// <summary>Rebuilds one TileMap's terrain after its cells changed (the hideout
    /// floor being repaved — docs/design/hideout.md).</summary>
    public void RebuildTerrain(TileMap map)
    {
        if (_terrain.Remove(map.GetInstanceId(), out List<Node>? old))
        {
            foreach (Node n in old)
            {
                if (((n as MultiMeshInstance3D)?.Multimesh?.Mesh ?? (n as MeshInstance3D)?.Mesh) is ArrayMesh mesh)
                {
                    for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                    {
                        if (mesh.SurfaceGetMaterial(s) is ShaderMaterial m)
                        {
                            _wallMaterials.Remove(m);
                        }
                    }
                }

                n.QueueFree();
            }
        }

        BuildTerrain(map);
    }

    /// <summary>
    /// A cavern's walls as one rock mesh (<see cref="CaveWalls"/>, docs/design/art-pipeline.md
    /// -> "Cave walls"): heights from a continuous noise field around the tile wall's
    /// height, coloured by voxel_rock.gdshader from the tileset wall's own palette
    /// (its darkest, middle and lightest colours), so each theme keeps its stone.
    /// </summary>
    private MeshInstance3D AddCaveRock(TileMap map, HashSet<Vector2I> wallCells, Mesh wallMesh)
    {
        Vector3 origin = Ground(map.ToGlobal(map.MapToLocal(Vector2I.Zero)));
        Vector3 ex = Ground(map.ToGlobal(map.MapToLocal(Vector2I.Right))) - origin;
        Vector3 ey = Ground(map.ToGlobal(map.MapToLocal(Vector2I.Down))) - origin;
        float tileMeters = map.TileSet.TileSize.X * map.GlobalScale.X / PixelsPerMeter;
        float baseHeight = Mathf.Max(0.5f, wallMesh.GetAabb().End.Y) * tileMeters;
        var swell = new FastNoiseLite { Seed = 7, Frequency = 0.12f };
        var grain = new FastNoiseLite { Seed = 11, Frequency = 0.6f };
        float Height(Vector2 p) =>
            baseHeight * (0.75f + 0.55f * (swell.GetNoise2D(p.X, p.Y) * 0.5f + 0.5f) + 0.1f * grain.GetNoise2D(p.X, p.Y));

        // Every cell of the map's rectangle that is not open ground is rock — the
        // empty cells the rasteriser leaves deep inside the rock too, which used to
        // show as black pits — so the cave reads as carved out of one mass.
        var open = new HashSet<Vector2I>(map.GetUsedCells(0));
        open.ExceptWith(wallCells);
        Rect2I bounds = map.GetUsedRect();
        var rock = new HashSet<Vector2I>(wallCells);
        for (int x = bounds.Position.X; x < bounds.End.X; x++)
        {
            for (int y = bounds.Position.Y; y < bounds.End.Y; y++)
            {
                var c = new Vector2I(x, y);
                if (!open.Contains(c))
                {
                    rock.Add(c);
                }
            }
        }

        ArrayMesh mesh = CaveWalls.Build(rock, p => origin + ex * p.X + ey * p.Y, Height);

        var palette = new List<Color>();
        for (int s = 0; s < wallMesh.GetSurfaceCount(); s++)
        {
            if (wallMesh.SurfaceGetMaterial(s) is BaseMaterial3D bm)
            {
                palette.Add(bm.AlbedoColor);
            }
        }

        palette.Sort((a, b) => a.Luminance.CompareTo(b.Luminance));
        Color mid = palette.Count > 0 ? palette[palette.Count / 2] : _wallColor;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://resources/shaders/voxel_rock.gdshader") };
        material.SetShaderParameter("albedo", mid);
        material.SetShaderParameter("albedo_dark", palette.Count > 0 ? palette[0] : mid.Darkened(0.4f));
        material.SetShaderParameter("albedo_light", palette.Count > 0 ? palette[^1] : mid.Lightened(0.25f));
        mesh.SurfaceSetMaterial(0, material);
        _wallMaterials.Add(material);
        _wallColor = mid;
        var instance = new MeshInstance3D { Mesh = mesh, Name = "Terrain_rock", CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        World.AddChild(instance);
        return instance;
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

    /// <summary>
    /// Glowing clusters along cave walls: a few small emissive shards leaning on
    /// the rock with a soft flickering light, at least seven metres apart, at
    /// most thirty — embers in the Ashen Depths, ghost-lights in the Bone Warrens,
    /// crystals in the Drowned Grotto (the colour comes from <see cref="GlowColor"/>).
    /// </summary>
    private List<Node> AddCaveGlows(Dictionary<Vector2I, Vector3> floors, HashSet<Vector2I> walls, float tileMeters)
    {
        var created = new List<Node>();
        var rng = new RandomNumberGenerator { Seed = (ulong)(floors.Count * 7919 + walls.Count) };
        var material = new StandardMaterial3D
        {
            AlbedoColor = GlowColor.Darkened(0.2f),
            EmissionEnabled = true,
            Emission = GlowColor,
            EmissionEnergyMultiplier = 2.6f,
            Roughness = 0.35f,
        };
        var shard = new PrismMesh { Size = new Vector3(0.16f, 0.55f, 0.16f), Material = material };
        var placed = new List<Vector3>();
        foreach ((Vector2I cell, Vector3 at) in floors.OrderBy(_ => rng.Randi()))
        {
            if (placed.Count >= 30)
            {
                break;
            }

            Vector2I? toWall = null;
            foreach (Vector2I d in new[] { Vector2I.Up, Vector2I.Left, Vector2I.Right, Vector2I.Down })
            {
                if (walls.Contains(cell + d))
                {
                    toWall = d;
                    break;
                }
            }

            if (toWall is not { } dir || placed.Any(p => p.DistanceSquaredTo(at) < 49f))
            {
                continue;
            }

            placed.Add(at);
            var cluster = new Node3D { Name = "CaveGlow", Position = at + new Vector3(dir.X, 0f, dir.Y) * 0.3f * tileMeters + new Vector3(0f, 0.12f * tileMeters, 0f) };
            int count = rng.RandiRange(2, 4);
            for (int i = 0; i < count; i++)
            {
                float scale = rng.RandfRange(0.55f, 1.15f);
                cluster.AddChild(new MeshInstance3D
                {
                    Mesh = shard,
                    Position = new Vector3(rng.RandfRange(-0.18f, 0.18f), 0.25f * scale, rng.RandfRange(-0.18f, 0.18f)),
                    Scale = Vector3.One * scale,
                    RotationDegrees = new Vector3(rng.RandfRange(-25f, 25f), rng.RandfRange(0f, 360f), rng.RandfRange(-25f, 25f)),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }

            var light = new OmniLight3D { Position = new Vector3(0f, 0.5f, 0f), OmniRange = 3.2f, LightEnergy = 1f, LightColor = GlowColor };
            cluster.AddChild(light);
            _flicker.Add((light, light.LightEnergy, rng.RandfRange(0f, Mathf.Tau), rng.RandfRange(1.2f, 2.4f), 0.25f));
            World.AddChild(cluster);
            created.Add(cluster);
        }

        return created;
    }

    /// <summary>A light over every few liquid tiles (spaced out, capped) so lava and water glow on their surroundings.</summary>
    private List<Node> AddHazardLights(List<Vector3> hazards)
    {
        var lights = new List<Node>();
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
            var light = new OmniLight3D
            {
                Position = h + new Vector3(0f, 0.6f, 0f),
                OmniRange = Liquid == LiquidStyle.Lava ? 4.2f : 3.5f,
                LightEnergy = Liquid == LiquidStyle.Lava ? 1.6f : 1.3f,
                LightColor = HazardLightColor,
            };
            World.AddChild(light);
            lights.Add(light);
            // Lava flickers hard and fast, ooze pulses slowly, water barely shimmers.
            (float speed, float depth) = Liquid switch { LiquidStyle.Lava => (2.6f, 0.35f), LiquidStyle.Ooze => (0.9f, 0.25f), _ => (1.2f, 0.1f) };
            _flicker.Add((light, light.LightEnergy, placed.Count * 1.7f, speed, depth));
        }

        return lights;
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
        // Bosses explode; ordinary monsters crumble — a few cubes that barely
        // leave the ground, so packs dying don't bury the screen in debris.
        if (enemy.IsBoss)
        {
            VoxelDebris.Burst(World, mesh, Ground(enemy.GlobalPosition), scale, 220);
            return;
        }

        // Every crumble of this frame goes into one MultiMesh (docs/design/performance.md
        // -> "Mass deaths"): a pack dying at once is one node and one draw, not sixty.
        if (_crumbles.Count == 0)
        {
            CallDeferred(nameof(FlushCrumbles));
        }

        _crumbles.Add((mesh, Ground(enemy.GlobalPosition), scale));
    }

    private readonly List<(Mesh Model, Vector3 At, float Scale)> _crumbles = new();

    private void FlushCrumbles()
    {
        VoxelDebris.BurstMany(World, _crumbles, 16, 0.35f);
        _crumbles.Clear();
    }

    /// <summary>Cosmetic budget for <see cref="Impact"/>: floor-cube puffs started this frame.</summary>
    private ulong _impactFrame;
    private readonly List<Vector3> _impactsThisFrame = new();
    private const int MaxImpactsPerFrame = 6;

    /// <summary>Ground debris where a big hit lands (slams, meteors, explosions).</summary>
    public void Impact(Vector2 at2D, float radiusPx, Color tint)
    {
        int count = Mathf.Clamp(Mathf.RoundToInt(radiusPx / 4f), 8, 60);
        Vector3 at = Ground(at2D);
        float radius = radiusPx / PixelsPerMeter;

        // Many blasts landing together (a volatile pack) would each kick up their own
        // floor and wall cubes in one frame. Cosmetic only: past a few per frame, or
        // on top of one already kicked up this frame, the extra puffs are skipped.
        ulong frame = Engine.GetProcessFrames();
        if (frame != _impactFrame)
        {
            _impactFrame = frame;
            _impactsThisFrame.Clear();
        }

        if (_impactsThisFrame.Count >= MaxImpactsPerFrame)
        {
            return;
        }

        foreach (Vector3 other in _impactsThisFrame)
        {
            if (other.DistanceSquaredTo(at) < radius * radius * 0.25f)
            {
                return;
            }
        }

        _impactsThisFrame.Add(at);
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
