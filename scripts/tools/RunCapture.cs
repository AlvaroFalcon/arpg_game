#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PathToExile.Enemies;
using PathToExile.Run;

namespace PathToExile.Tools;

/// <summary>
/// Plays scripted runs in a real (windowed) session and saves screenshots for
/// the devlog / PR — run with scripts/capture_run_screenshots.sh. It moves
/// itself under the scene-tree root so it survives scene changes, then
/// launches the run several times with different <see cref="RunLaunchOptions"/>
/// (early swarm, mid-run build, each boss, phase mechanics). The player is
/// made immune to damage and steered in slow circles so the shots show a
/// living fight rather than a corpse; level-up screens are auto-picked (one is
/// photographed).
/// </summary>
public partial class RunCapture : Node
{
    private string _outDir = "res://docs/devlog/screenshots";
    private bool _autoPick = true;
    private bool _steer = true;
    private float _steerAngle;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--out="))
            {
                _outDir = arg[6..];
            }
        }

        ProcessMode = ProcessModeEnum.Always;
        // Aim at enemies: there is no mouse in a scripted run.
        Core.GameSettings.Get(this)?.SetAutoAim(true);
        CallDeferred(nameof(Detach));
    }

    private void Detach()
    {
        Node root = GetTree().Root;
        GetParent().RemoveChild(this);
        root.AddChild(this);
        _ = Script();
    }

    private async Task Script()
    {
        await Shot(Scenes(main: true), "menu-main.png", 1.0f);
        if (GetTree().CurrentScene is UI.MainMenu menu)
        {
            menu.OpenSettingsForCapture();
            await Wait(0.4f);
            await Snap("menu-settings.png");
        }

        await Shot(PathToExile.Core.Scenes.Hideout, "hideout.png", 1.2f);

        // Start-of-run skill pick.
        RunLaunchOptions.Reset();
        _autoPick = false;
        await Launch();
        await Wait(1.0f);
        await Snap("run-skill-pick.png");
        PickFirstCard();
        _autoPick = true;

        // Early run.
        await Wait(14f);
        await Snap("run-early.png");

        // Mid run with a built-up character.
        RunLaunchOptions.Reset();
        RunLaunchOptions.StartTime = 150f;
        RunLaunchOptions.StartSkill = "spark";
        RunLaunchOptions.AutoLevels = 12;
        await Launch();
        await Wait(16f);
        await Snap("run-midgame.png");

        // A level-up screen.
        _autoPick = false;
        if (Run() is { } r1)
        {
            r1.State.AddExperience(RunState.ExperienceToNext(r1.State.Level));
        }

        await Wait(0.6f);
        await Snap("run-levelup.png");
        PickFirstCard();
        _autoPick = true;

        // Pause menu.
        await Wait(0.5f);
        Run()?.Pause.Open();
        await Wait(0.3f);
        await Snap("run-pause.png");
        Run()?.Pause.Close();

        // The Bone Herald.
        RunLaunchOptions.Reset();
        RunLaunchOptions.StartTime = 176f;
        RunLaunchOptions.StartSkill = "fireball";
        RunLaunchOptions.AutoLevels = 10;
        await Launch();
        await Wait(9f);
        await Snap("boss-herald.png");
        await WaitForAttack("Grave Circles", 45f);
        await Wait(0.7f);
        await Snap("boss-herald-circles.png");

        // The Ember Colossus, each phase.
        RunLaunchOptions.Reset();
        RunLaunchOptions.StartTime = 417f;
        RunLaunchOptions.StartSkill = "meteor";
        RunLaunchOptions.AutoLevels = 18;
        await Launch();
        await Wait(6f);
        await Snap("boss-colossus.png");
        await WaitForAttack("Seismic Slam", 25f);
        await Wait(0.6f);
        await Snap("boss-colossus-slam.png");
        SetBossLife(0.6f, immune: true);
        await WaitForAttack("Inferno Ring", 40f);
        await Wait(1.0f);
        await Snap("boss-colossus-inferno.png");
        SetBossLife(0.25f, immune: true);
        await WaitForAttack("Flame Beams", 40f);
        await Wait(0.8f);
        await Snap("boss-colossus-beams.png");

        // Results.
        if (Run()?.Director.ActiveBoss is { } finalBoss)
        {
            finalBoss.Health.IgnoreDamage = false;
        }

        Run()?.State.EndRun(true);
        await Wait(1.5f);
        await Snap("run-results.png");

        GD.Print("[CAPTURE] done");
        GetTree().Quit();
    }

    private static string Scenes(bool main) => PathToExile.Core.Scenes.MainMenu;

    private async Task Shot(string scene, string file, float wait)
    {
        GetTree().ChangeSceneToFile(scene);
        await Wait(wait);
        await Snap(file);
    }

    private async Task Launch()
    {
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile(PathToExile.Core.Scenes.Run);
        await Wait(0.3f);
        if (Run() is { } run)
        {
            run.Player.Health!.IgnoreDamage = true;
        }
    }

    private RunController? Run() => GetTree().CurrentScene as RunController;

    public override void _PhysicsProcess(double delta)
    {
        if (Run() is not { } run || run.State.Ended)
        {
            return;
        }

        if (_autoPick && run.LevelUp.IsOpen)
        {
            PickFirstCard();
        }

        if (_steer && !GetTree().Paused)
        {
            // Slow circles around the arena centre: keeps the camera moving
            // through the fight without needing input.
            _steerAngle += (float)delta * 0.5f;
            Vector2 centre = run.Director.ActiveBoss?.GlobalPosition ?? run.Arena.Center;
            Vector2 goal = centre + Vector2.Right.Rotated(_steerAngle) * 230f;
            Vector2 dir = run.Player.GlobalPosition.DirectionTo(goal);
            run.Player.Velocity = dir * run.Player.Stats.MovementSpeed.Value;
            run.Player.MoveAndSlide();
        }
    }

    private void PickFirstCard() => Run()?.LevelUp.ChooseIndex(0);

    private void SetBossLife(float fraction, bool immune = false)
    {
        if (Run()?.Director.ActiveBoss is { Stats.Life: { } life } boss)
        {
            life.Current = life.Max * fraction;
            boss.Health.IgnoreDamage = immune;
        }
    }

    private async Task WaitForAttack(string attack, float timeout)
    {
        float waited = 0f;
        while (waited < timeout)
        {
            if (Run()?.Director.ActiveBoss is BossEnemy boss && boss.CurrentAttack == attack)
            {
                return;
            }

            await Wait(0.1f);
            waited += 0.1f;
        }

        GD.Print($"[CAPTURE] timed out waiting for {attack}");
    }

    private async Task Wait(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds, processAlways: true), SceneTreeTimer.SignalName.Timeout);
    }

    private async Task Snap(string file)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        string path = _outDir.StartsWith("res://") || _outDir.StartsWith("user://")
            ? ProjectSettings.GlobalizePath($"{_outDir}/{file}")
            : $"{_outDir}/{file}";
        Error err = image.SavePng(path);
        GD.Print(err == Error.Ok ? $"[CAPTURE] saved {path}" : $"[CAPTURE] FAILED {path}: {err}");
    }
}
