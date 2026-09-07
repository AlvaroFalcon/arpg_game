#nullable enable
using System.Collections.Generic;
using Godot;
using PathToExile.Combat;
using PathToExile.Skills;

namespace PathToExile.Tools;

/// <summary>
/// Drives scripted Fireball casts in a real (windowed) run and saves devlog
/// screenshots to docs/devlog/screenshots/. Same rationale as
/// <see cref="MovementCapture"/>: a static frame of a projectile mid-flight says
/// nothing about what the capability does, so this draws each projectile's full
/// flight path as a trail and marks every dummy that took a hit. The geometry is
/// the explanation.
///
/// Run with:
///   ./scripts/capture_skill_screenshots.sh
/// Must run windowed: --headless uses the dummy renderer and captures an empty image.
///
/// Produces three images:
///   skills-pierce.png — Pierce = 3, one projectile through four dummies.
///   skills-fork.png   — Forks = 1, one projectile becoming two on hit.
///   skills-spread.png — Count = 5 / SpreadDegrees = 45, one press, five projectiles.
/// </summary>
public partial class SkillCapture : Node2D
{
    private static readonly Vector2 PlayerPos = new(120f, 320f);

    /// <summary>Trail colours, assigned in order of first sighting. The first
    /// projectile seen in the fork phase is the parent; the next two are its
    /// children, which is what makes the Y readable.</summary>
    private static readonly Color[] TrailColours =
    {
        new(1f, 0.55f, 0.15f),   // parent — orange
        new(1f, 0.85f, 0.30f),   // children — yellow
        new(1f, 0.85f, 0.30f),
        new(0.95f, 0.75f, 0.25f),
        new(0.95f, 0.75f, 0.25f),
        new(0.95f, 0.75f, 0.25f),
    };

    [Export]
    public PackedScene DummyScene = null!;

    private Node2D _player = null!;
    private SkillUser _skillUser = null!;

    private readonly Dictionary<ulong, Line2D> _trails = new();
    private readonly List<Node> _phaseNodes = new();
    private int _trailsSeen;

    private int _phase;
    private int _frame;
    private bool _busy;

    public override void _Ready()
    {
        _player = GetNode<Node2D>("Player");
        _skillUser = _player.GetNode<SkillUser>("SkillUser");

        // The player carries a follow-camera; disable it so the shot frames the
        // whole play area rather than tracking the character.
        _player.GetNode<Camera2D>("Camera2D").Enabled = false;
        _player.GlobalPosition = PlayerPos;

        // The player's life/mana debug readout is a CanvasLayer pinned to the
        // top-left, exactly where these captions go. Useful while playing, noise
        // in a documentation shot.
        _player.GetNode<CanvasLayer>("DebugReadout").Visible = false;

        StartPierce();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_busy)
        {
            return;
        }

        _frame++;
        TrackTrails();

        switch (_phase)
        {
            case 1 when _frame > 210:
                Capture("res://docs/devlog/screenshots/skills-pierce.png", StartFork);
                break;
            case 2 when _frame > 150:
                Capture("res://docs/devlog/screenshots/skills-fork.png", StartSpread);
                break;
            case 3 when _frame > 160:
                Capture("res://docs/devlog/screenshots/skills-spread.png", () => GetTree().Quit());
                break;
        }
    }

    /// <summary>
    /// Appends every live projectile's position to its own <see cref="Line2D"/>.
    /// Polling the scene rather than having Projectile emit a position signal keeps
    /// this capture tool entirely outside the gameplay code — nothing in
    /// scripts/Combat or scripts/Skills knows this exists.
    /// </summary>
    private void TrackTrails()
    {
        foreach (Node child in GetTree().CurrentScene.GetChildren())
        {
            if (child is not Projectile projectile || !IsInstanceValid(projectile))
            {
                continue;
            }

            ulong id = projectile.GetInstanceId();
            if (!_trails.TryGetValue(id, out Line2D? trail))
            {
                trail = new Line2D
                {
                    Width = 3f,
                    DefaultColor = TrailColours[Mathf.Min(_trailsSeen, TrailColours.Length - 1)],
                    ZIndex = -1,
                };
                _trailsSeen++;
                AddChild(trail);
                _trails[id] = trail;
                _phaseNodes.Add(trail);
            }

            trail.AddPoint(projectile.GlobalPosition);
        }
    }

    private void ResetPhase()
    {
        foreach (Node node in _phaseNodes)
        {
            node.QueueFree();
        }

        _phaseNodes.Clear();
        _trails.Clear();
        _trailsSeen = 0;
        _frame = 0;

        foreach (Node child in GetTree().CurrentScene.GetChildren())
        {
            if (child is Projectile projectile)
            {
                projectile.QueueFree();
            }
        }
    }

    /// <summary>
    /// Spawns a dummy and wires its Damaged signal to a visible marker, so the
    /// finished screenshot shows *which* dummies were hit rather than leaving the
    /// reader to infer it from a trail passing nearby.
    /// </summary>
    private void SpawnDummy(Vector2 position)
    {
        var dummy = DummyScene.Instantiate<Node2D>();
        dummy.Position = position;
        AddChild(dummy);
        _phaseNodes.Add(dummy);

        var hitMarker = new Label
        {
            Position = position + new Vector2(-14f, -46f),
            Text = string.Empty,
        };
        hitMarker.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.35f));
        AddChild(hitMarker);
        _phaseNodes.Add(hitMarker);

        var health = dummy.GetNode<Health>("Health");
        int hits = 0;
        health.Damaged += _ =>
        {
            hits++;
            hitMarker.Text = $"HIT{(hits > 1 ? $" x{hits}" : string.Empty)}";

            // Modulate, not the Visual polygon's Color: TargetDummy runs its own
            // 0.1s white hit-flash that restores the polygon's base colour when it
            // expires, so writing Color here is silently undone a few frames later
            // — which is exactly what the first version of this tool did.
            // Modulate is a separate property TargetDummy never touches.
            dummy.Modulate = new Color(1f, 0.5f, 0.45f);
        };
    }

    private Label AddCaption(Vector2 position, string text, float size = 16f)
    {
        var label = new Label { Position = position, Text = text };
        label.AddThemeFontSizeOverride("font_size", (int)size);
        label.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.95f));
        AddChild(label);
        _phaseNodes.Add(label);
        return label;
    }

    private void EquipAndFire(int count, float spreadDegrees, int pierce, int forks)
    {
        _skillUser.EquipSkill(SkillSlotId.Slot1, new SkillInstance(new ProjectileSkillDefinition
        {
            DisplayName = "Fireball",
            BaseDamage = 1f,   // low on purpose: dummies must survive to stay in frame
            BaseUseTime = 5f,  // one cast only, no repeat
            Tags = SkillTags.Fire | SkillTags.Projectile | SkillTags.Spell,
            Speed = 260f,      // slower than the real 600 so a trail has visible length
            Range = 1400f,
            SpawnOffset = 20f,
            Count = count,
            SpreadDegrees = spreadDegrees,
            Pierce = pierce,
            Forks = forks,
        }));

        _skillUser.TryCast(SkillSlotId.Slot1, Vector2.Right);
    }

    private void StartPierce()
    {
        _phase = 1;
        ResetPhase();

        AddCaption(new Vector2(40f, 40f), "Pierce = 3   ·   Count = 1   ·   Forks = 0", 22f);
        AddCaption(new Vector2(40f, 72f),
            "One projectile, one straight line, four dummies hit.\n" +
            "Pierce spends a charge per target and keeps going.");

        foreach (float x in new[] { 400f, 560f, 720f, 880f })
        {
            SpawnDummy(new Vector2(x, 320f));
        }

        AddCaption(new Vector2(960f, 300f), "pierce spent\nafter the 4th");
        EquipAndFire(count: 1, spreadDegrees: 0f, pierce: 3, forks: 0);
        _busy = false;
    }

    private void StartFork()
    {
        _phase = 2;
        ResetPhase();

        AddCaption(new Vector2(40f, 40f), "Forks = 1   ·   Count = 1   ·   Pierce = 0", 22f);
        AddCaption(new Vector2(40f, 72f),
            "One projectile becomes two on hit, opening a 60 degree V.\n" +
            "Children inherit the decremented counter, so they never fork again.");

        SpawnDummy(new Vector2(420f, 320f));
        SpawnDummy(new Vector2(640f, 200f));
        SpawnDummy(new Vector2(640f, 440f));

        AddCaption(new Vector2(300f, 360f), "1. hit here");
        AddCaption(new Vector2(690f, 190f), "2. fork child");
        AddCaption(new Vector2(690f, 430f), "2. fork child");

        EquipAndFire(count: 1, spreadDegrees: 0f, pierce: 0, forks: 1);
        _busy = false;
    }

    private void StartSpread()
    {
        _phase = 3;
        ResetPhase();

        AddCaption(new Vector2(40f, 40f), "Count = 5   ·   SpreadDegrees = 45", 22f);
        AddCaption(new Vector2(40f, 72f),
            "One button press, five projectiles fanned across a 45 degree cone.\n" +
            "The outermost two sit exactly on the cone edges.");

        // An arc of dummies at a fixed radius, so the fan's spacing is legible
        // against something rather than against empty floor.
        for (int i = -2; i <= 2; i++)
        {
            float angle = Mathf.DegToRad(i * 11.25f);
            Vector2 offset = Vector2.Right.Rotated(angle) * 520f;
            SpawnDummy(PlayerPos + offset);
        }

        EquipAndFire(count: 5, spreadDegrees: 45f, pierce: 0, forks: 0);
        _busy = false;
    }

    private async void Capture(string resPath, System.Action onDone)
    {
        _busy = true;

        // Let a frame finish drawing, otherwise the viewport texture is empty.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        Image image = GetViewport().GetTexture().GetImage();
        string absolutePath = ProjectSettings.GlobalizePath(resPath);
        Error err = image.SavePng(absolutePath);
        GD.Print(err == Error.Ok
            ? $"[CAPTURE] Saved {absolutePath}"
            : $"[CAPTURE] FAILED to save {absolutePath}: {err}");

        onDone();
    }
}
