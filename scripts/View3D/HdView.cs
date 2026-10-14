#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;

namespace ArpgGame.View3D;

/// <summary>
/// Autoload that gives every scene its voxel view — docs/design/art-pipeline.md
/// -> "Voxels". When a scene becomes current it adds a <see cref="VoxelStage"/>,
/// turns the scene's TileMaps into voxel terrain and mirrors its sprites; from
/// then on new sprites (spawned monsters, projectiles, the class sprite) get a
/// voxel actor as they appear, and heavy telegraphed hits kick up floor cubes.
///
/// Off in headless runs (tests, the autoplay bot) and when the player turns
/// "Voxel graphics" off in the settings: the game is then the classic 2D one.
/// </summary>
public partial class HdView : Node
{
    private Node? _scene;
    private VoxelStage? _stage;
    private readonly List<(AnimatedSprite2D Sprite, float Until)> _pending = new();
    private float _clock;

    public static bool Enabled(Node context) =>
        DisplayServer.GetName() != "headless" && (Core.GameSettings.Get(context)?.VoxelGraphics ?? true);

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GetTree().NodeAdded += OnNodeAdded;
    }

    public override void _Process(double delta)
    {
        _clock += (float)delta;
        Node? scene = GetTree().CurrentScene;
        if (scene != _scene)
        {
            _scene = scene;
            _stage = null;
            _pending.Clear();
            if (scene is not null && Enabled(this))
            {
                CallDeferred(nameof(Attach), scene);
            }
        }

        // Sprites whose SpriteFrames are assigned after they enter the tree
        // (pickups, the class sprite) are retried for a moment.
        if (_stage is not null && IsInstanceValid(_stage))
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                (AnimatedSprite2D sprite, float until) = _pending[i];
                if (!IsInstanceValid(sprite) || _clock > until)
                {
                    _pending.RemoveAt(i);
                    continue;
                }

                if (sprite.SpriteFrames is not null && VoxelLibrary.ModelPathFor(sprite.SpriteFrames) is { } path && VoxelLibrary.Load(path) is not null)
                {
                    _stage.Track(sprite);
                    _pending.RemoveAt(i);
                }
            }
        }
    }

    private void Attach(Node scene)
    {
        if (!IsInstanceValid(scene) || scene != GetTree().CurrentScene)
        {
            return;
        }

        _stage = new VoxelStage();
        if (scene is World.RealityController reality)
        {
            Color ambient = reality.Theme.Ambient;
            if (reality.Layout.Cave is not null)
            {
                // Caverns: a darker ambient so the cave's own glows carry the light.
                _stage.Caverns = true;
                _stage.GlowColor = reality.Theme.Id switch
                {
                    "crypt" => new Color(0.5f, 1f, 0.75f),
                    "drowned" => new Color(0.45f, 0.8f, 1f),
                    _ => new Color(1f, 0.5f, 0.18f),
                };
                ambient = ambient.Darkened(0.2f);
            }

            _stage.SetMood(
                new Color(ambient.R * 0.75f, ambient.G * 0.75f, ambient.B * 0.8f),
                ambient.Lerp(Colors.White, 0.3f),
                HazardColor(reality.Theme.Id),
                reality.Theme.Id switch { "drowned" => LiquidStyle.Water, "crypt" => LiquidStyle.Ooze, _ => LiquidStyle.Lava });
        }

        else if (scene is Hideout)
        {
            // A sunny town; the canal glows a soft blue.
            _stage.SetMood(new Color(0.78f, 0.76f, 0.72f), new Color(1f, 0.93f, 0.82f), new Color(0.4f, 0.7f, 1f), LiquidStyle.Water);
        }

        scene.AddChild(_stage);
        Walk(scene);
    }

    private static Color HazardColor(string themeId) => themeId switch
    {
        "crypt" => new Color(0.45f, 1f, 0.35f),
        "drowned" => new Color(0.35f, 0.65f, 1f),
        _ => new Color(1f, 0.45f, 0.15f),
    };

    private void Walk(Node node)
    {
        switch (node)
        {
            case TileMap map:
                _stage!.BuildTerrain(map);
                break;
            case AnimatedSprite2D sprite:
                _stage!.Track(sprite);
                break;
        }

        foreach (Node child in node.GetChildren())
        {
            Walk(child);
        }
    }

    private void OnNodeAdded(Node node)
    {
        if (_stage is null || !IsInstanceValid(_stage))
        {
            return;
        }

        switch (node)
        {
            case AnimatedSprite2D sprite:
                _pending.Add((sprite, _clock + 1.5f));
                break;
            case AreaBurst burst:
                burst.Impacted += _ => OnImpact(burst);
                break;
        }
    }

    private void OnImpact(AreaBurst burst)
    {
        if (_stage is null || !IsInstanceValid(_stage) || !IsInstanceValid(burst) || burst.Harmless || burst.Radius < 55f)
        {
            return;
        }

        Color floor = burst.Hit.Faction == Faction.Player ? burst.Tint : new Color(0.55f, 0.4f, 0.35f);
        _stage.Impact(burst.GlobalPosition, burst.Radius, floor);
    }
}
