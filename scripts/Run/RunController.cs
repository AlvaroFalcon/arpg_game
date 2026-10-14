#nullable enable
using System.Linq;
using Godot;
using ArpgGame.Core;
using ArpgGame.Progression;
using ArpgGame.Skills;
using ArpgGame.Stats;
using ArpgGame.UI;

namespace ArpgGame.Run;

/// <summary>
/// Root of the run scene — docs/design/run.md. Wires the pieces together
/// (arena, player, state, director, HUD and overlays), configures the
/// persistent character for a run (gear from <see cref="Profile"/>, a fresh
/// <see cref="RunBuild"/>, run-mode casting), runs the start-of-run skill
/// pick, and handles the end: profile capture, results, leaving.
/// </summary>
[GlobalClass]
public partial class RunController : Node2D
{
    /// <summary>Flat bonuses every run character gets: a survivor run is far
    /// denser than the sandbox, and the base 70 life / 6 mana regen were tuned
    /// for standing next to training dummies.</summary>
    public const float RunBonusLife = 60f;
    public const float RunBonusManaRegen = 8f;

    public RunState State { get; private set; } = null!;
    public RunDirector Director { get; private set; } = null!;
    public Arena Arena { get; private set; } = null!;
    public Player Player { get; private set; } = null!;
    public RunBuild Build { get; private set; } = null!;
    public RunHud Hud { get; private set; } = null!;
    public LevelUpScreen LevelUp { get; private set; } = null!;
    public PauseMenu Pause { get; private set; } = null!;
    public ResultsScreen Results { get; private set; } = null!;

    private static readonly object RunBaselineSource = new();

    public override void _Ready()
    {
        RunLaunchOptions.ReadCommandLine();
        float startTime = RunLaunchOptions.StartTime;
        string? startSkill = RunLaunchOptions.StartSkill;
        int autoLevels = RunLaunchOptions.AutoLevels;
        RunLaunchOptions.Reset();

        Arena = GetNode<Arena>("Arena");
        State = GetNode<RunState>("RunState");
        Director = GetNode<RunDirector>("RunDirector");
        Player = GetNode<Player>("Player");

        Director.State = State;
        Director.Arena = Arena;
        Director.Spawning = false;

        ConfigurePlayer();

        Hud = new RunHud { State = State, Player = Player, Build = Build, Director = Director, Name = "Hud" };
        AddChild(Hud);
        LevelUp = new LevelUpScreen { State = State, Build = Build, Name = "LevelUp" };
        AddChild(LevelUp);
        Results = new ResultsScreen { Name = "Results", OnHideout = () => Scenes.Go(this, Scenes.Hideout), OnMenu = () => Scenes.Go(this, Scenes.MainMenu) };
        AddChild(Results);
        Pause = new PauseMenu
        {
            Name = "Pause",
            LeaveText = "Abandon run",
            OnLeave = () => State.EndRun(Director.ColossusDefeated),
            Summary = () => LevelUpScreen.BuildSummary(Build),
        };
        AddChild(Pause);

        State.RunEnded += OnRunEnded;
        Sfx.Music(this, "music_run");

        if (startTime > 0f)
        {
            State.SkipTo(startTime);
            Director.SyncTimers();
        }

        if (startSkill is { } skill)
        {
            Build.AddSkill(skill);
            ApplyAutoLevels(autoLevels);
            BeginRun();
        }
        else if (Build.Skills.Any())
        {
            // The persistent character brings its own gems (docs/design/endgame.md
            // -> "Survival Rift"); the card pick only exists for a character with none.
            ApplyAutoLevels(autoLevels);
            BeginRun();
        }
        else
        {
            LevelUp.OpenStartPick(BeginRun);
        }
    }

    private void ConfigurePlayer()
    {
        Player.GlobalPosition = Arena.Center + new Vector2(0f, 180f);
        Build = new RunBuild { Name = "RunBuild" };
        Player.AddChild(Build);

        // Survival casting: every equipped skill (the character's six loadout
        // slots, equipped by CharacterBuild in ApplyTo below) fires on its own,
        // auto-aimed, and costs nothing — there is no mana in the Survival Rift
        // (docs/design/run.md -> "Controls"). No click-to-move either.
        Player.GetNode<PlayerSkillInput>("PlayerSkillInput").Mode = PlayerSkillInput.CastMode.Survival;
        Player.Stats.IgnoresResourceCosts = true;
        if (Items.Flasks.Of(Player) is { } flasks)
        {
            flasks.Mana.Enabled = false; // no mana in the Survival Rift
        }
        Player.GetNodeOrNull<CanvasLayer>("DebugReadout")?.Hide();

        Player.Stats.Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.Flat, RunBonusLife, RunBaselineSource));
        Profile.Get(this)?.ApplyTo(Player);
        if (Player.SkillUser.SlotIsBuiltInMove(SkillSlotId.Lmb))
        {
            Player.SkillUser.ClearSlot(SkillSlotId.Lmb);
        }

        if (Player.Stats.Life is { } life)
        {
            life.Current = life.Max;
        }

        if (Player.Stats.Mana is { } mana)
        {
            mana.Current = mana.Max;
        }

        State.Player = Player;
        State.Build = Build;
        // After the Colossus the run is already won; falling in the endless phase keeps that.
        Player.Health!.Died += () => State.EndRun(Director.ColossusDefeated);
        Player.Dodge!.Dodged += OnPlayerDodged;

        var camera = Player.GetNode<Camera2D>("Camera2D");
        float margin = Arena.TileWorld * 6f;
        camera.LimitLeft = (int)(Arena.GlobalPosition.X - margin);
        camera.LimitTop = (int)(Arena.GlobalPosition.Y - margin);
        camera.LimitRight = (int)(Arena.GlobalPosition.X + Arena.WidthTiles * Arena.TileWorld + margin);
        camera.LimitBottom = (int)(Arena.GlobalPosition.Y + Arena.HeightTiles * Arena.TileWorld + margin);
        camera.PositionSmoothingEnabled = true;
        camera.PositionSmoothingSpeed = 8f;
    }

    /// <summary>"Phase Dancer" keystone: dodging releases a frost nova.</summary>
    private void OnPlayerDodged()
    {
        if (!Progression.BuildQuery.Any(Player, b => b.PhaseDancer))
        {
            return;
        }

        var tags = Combat.SkillTags.Spell | Combat.SkillTags.Area | Combat.SkillTags.Cold;
        float damage = Stat.ComputeFormula(20f, Player.Stats.GetModifiers(new ModifierQuery(StatId.Damage, tags)));
        var hit = new Combat.HitProfile
        {
            Damage = damage,
            Type = Combat.DamageType.Cold,
            Source = Player,
            Faction = Combat.Faction.Player,
            Tags = tags,
            KnockbackForce = 220f,
        };
        Combat.AreaBurst.Spawn(this, Player.GlobalPosition, 110f * Player.Stats.Get(StatId.AreaOfEffect).Value, 0f, new Color(0.45f, 0.8f, 1f), hit);
    }

    private void ApplyAutoLevels(int count)
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        for (int i = 0; i < count; i++)
        {
            int level = State.Level + 1;
            var offers = Build.RollOffers(level, rng);
            if (offers.Count > 0)
            {
                offers[rng.RandiRange(0, offers.Count - 1)].Apply();
            }

            State.AddLevelSilently();
        }

        if (Player.Stats.Life is { } life)
        {
            life.Current = life.Max;
        }
    }

    private void BeginRun()
    {
        Director.Spawning = true;
        Hud.ShowBanner("Survival Rift", "Reach the Ember Colossus — and break it", UiTheme.Gold);
    }

    private async void OnRunEnded(bool victory)
    {
        Director.Spawning = false;
        Player.ControlsEnabled = false;
        Player.GetNode<PlayerSkillInput>("PlayerSkillInput").InputEnabled = false;
        if (Player.Dodge is { } dodge)
        {
            dodge.InputEnabled = false;
        }

        var rng = new RandomNumberGenerator();
        rng.Randomize();
        RunRewards rewards = RunRewards.For(State.Elapsed, State.BossesKilled, State.Kills, victory, rng);
        Profile? profile = Profile.Get(this);
        if (profile is not null)
        {
            rewards.GrantTo(Player);
            // Strip the run's build and baseline before capturing, so nothing
            // run-only is ever saved as part of the character.
            Player.Stats.RemoveModifiersFromSource(Build.Source);
            Player.Stats.RemoveModifiersFromSource(RunBaselineSource);
            profile.CaptureFrom(Player);
            profile.RecordRun(State.Elapsed, State.Level, State.Kills, victory);
        }

        if (!victory)
        {
            Engine.TimeScale = 0.35;
            await ToSignal(GetTree().CreateTimer(0.6, processAlways: true, ignoreTimeScale: true), SceneTreeTimer.SignalName.Timeout);
            Engine.TimeScale = 1.0;
        }

        Results.ShowResults(State, Build, rewards);
    }

    public override void _ExitTree()
    {
        Engine.TimeScale = 1.0;
    }
}
