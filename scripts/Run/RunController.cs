#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Core;
using ArpgGame.Net;
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
///
/// Co-op (docs/design/multiplayer.md): when a <see cref="NetSession"/> is
/// active, the other party members get player nodes of their own (built from
/// the characters their machines sent), the host adds a <see cref="RiftHost"/>
/// and runs the game, a client adds a <see cref="RiftClient"/> and shows it.
/// <see cref="Player"/> is always this machine's player.
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

    /// <summary>The co-op session this run belongs to (null offline).</summary>
    public NetSession? Session { get; private set; }

    public bool Coop => Session is { Active: true };

    /// <summary>Set on the co-op host.</summary>
    public RiftHost? NetHost { get; private set; }

    /// <summary>Set on a co-op client.</summary>
    public RiftClient? NetClient { get; private set; }

    private static readonly object RunBaselineSource = new();
    private float _downedFor;

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

        Session = NetSession.Get(this);
        if (Session is not { Active: true, InRun: true })
        {
            Session = null;
        }

        if (Session is not null)
        {
            Player.PeerId = Session.LocalPeerId;
            Player.NetName = Session.Member(Player.PeerId)?.Name ?? "";
            State.Coop = true;
        }

        ConfigurePlayer();
        if (Session is not null)
        {
            SpawnParty(Session);
        }

        Hud = new RunHud { State = State, Player = Player, Build = Build, Director = Director, Name = "Hud" };
        AddChild(Hud);
        LevelUp = new LevelUpScreen { State = State, Build = Build, Name = "LevelUp", Coop = Coop };
        AddChild(LevelUp);
        Results = new ResultsScreen { Name = "Results", OnHideout = () => LeaveTo(Scenes.Hideout), OnMenu = () => LeaveTo(Scenes.MainMenu) };
        AddChild(Results);
        Pause = new PauseMenu
        {
            Name = "Pause",
            LeaveText = Session is null ? "Abandon run" : Session.IsHost ? "End the run for everyone" : "Leave the party",
            OnLeave = OnPauseLeave,
            Summary = () => LevelUpScreen.BuildSummary(Build),
            NoPause = Coop,
        };
        AddChild(Pause);

        State.RunEnded += OnRunEnded;
        Sfx.Music(this, "music_run");

        if (Coop)
        {
            // Never paused: whoever is choosing a card is warded (docs/design/multiplayer.md).
            LevelUp.OpenChanged += open => Player.Warded = open && !State.Ended;
        }

        if (Session is { IsHost: true })
        {
            NetHost = new RiftHost { Name = "RiftHost", Run = this };
            AddChild(NetHost);
        }
        else if (Session is { IsClient: true })
        {
            NetClient = new RiftClient { Name = "RiftClient", Run = this };
            AddChild(NetClient);
            LevelUp.Picked += offer => NetClient.SendPick(offer.Key);
        }

        if (startTime > 0f)
        {
            State.SkipTo(startTime);
            Director.SyncTimers();
        }

        if (startSkill is { } skill)
        {
            Build.AddSkill(skill);
            ApplyAutoLevels(autoLevels);
            StartWhenReady();
        }
        else if (Build.Skills.Any())
        {
            // The persistent character brings its own gems (docs/design/endgame.md
            // -> "Survival Rift"); the card pick only exists for a character with none.
            ApplyAutoLevels(autoLevels);
            StartWhenReady();
        }
        else
        {
            if (Coop)
            {
                StartWhenReady();
            }

            LevelUp.OpenStartPick(Coop ? () => { } : BeginRun);
        }
    }

    /// <summary>Offline: begin now. Co-op host: once everyone has loaded. Client: the host decides.</summary>
    private void StartWhenReady()
    {
        if (NetHost is not null)
        {
            NetHost.BeginWhenReady(BeginRun);
        }
        else if (NetClient is null)
        {
            BeginRun();
        }
    }

    private void ConfigurePlayer()
    {
        int slot = Session?.Members.FindIndex(m => m.PeerId == Player.PeerId) ?? 0;
        Player.GlobalPosition = SpawnPoint(Mathf.Max(0, slot), Session?.Members.Count ?? 1);
        Build = ConfigureRunCharacter(Player);
        Profile.Get(this)?.ApplyTo(Player);
        FinishRunCharacter(Player);

        if (NetSessionIsClient)
        {
            // The host casts for everyone; this machine only shows it.
            Player.GetNode<PlayerSkillInput>("PlayerSkillInput").InputEnabled = false;
            MuteLocalEffects(Player);
        }

        State.Player = Player;
        State.Build = Build;
        HookDeath(Player);
        Player.Dodge!.Dodged += () => OnPlayerDodged(Player);

        var camera = Player.GetNode<Camera2D>("Camera2D");
        float margin = Arena.TileWorld * 6f;
        camera.LimitLeft = (int)(Arena.GlobalPosition.X - margin);
        camera.LimitTop = (int)(Arena.GlobalPosition.Y - margin);
        camera.LimitRight = (int)(Arena.GlobalPosition.X + Arena.WidthTiles * Arena.TileWorld + margin);
        camera.LimitBottom = (int)(Arena.GlobalPosition.Y + Arena.HeightTiles * Arena.TileWorld + margin);
        camera.PositionSmoothingEnabled = true;
        camera.PositionSmoothingSpeed = 8f;
    }

    private bool NetSessionIsClient => Session is { IsClient: true };

    /// <summary>Where party member <paramref name="slot"/> of <paramref name="count"/>
    /// starts: side by side below the centre (the same on every machine).</summary>
    private Vector2 SpawnPoint(int slot, int count) =>
        Arena.Center + new Vector2(0f, 180f) + new Vector2((slot - (count - 1) / 2f) * 70f, 0f);

    /// <summary>Survival casting and the run baseline: every equipped skill (the
    /// character's six loadout slots) fires on its own, auto-aimed, and costs
    /// nothing — there is no mana in the Survival Rift (docs/design/run.md ->
    /// "Controls"). No click-to-move either.</summary>
    private RunBuild ConfigureRunCharacter(Player player)
    {
        var build = new RunBuild { Name = "RunBuild" };
        player.AddChild(build);
        player.GetNode<PlayerSkillInput>("PlayerSkillInput").Mode = PlayerSkillInput.CastMode.Survival;
        player.Stats.IgnoresResourceCosts = true;
        if (Items.Flasks.Of(player) is { } flasks)
        {
            flasks.Mana.Enabled = false; // no mana in the Survival Rift
        }

        player.GetNodeOrNull<CanvasLayer>("DebugReadout")?.Hide();
        player.Stats.Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.Flat, RunBonusLife, RunBaselineSource));
        return build;
    }

    private static void FinishRunCharacter(Player player)
    {
        if (player.SkillUser.SlotIsBuiltInMove(SkillSlotId.Lmb))
        {
            player.SkillUser.ClearSlot(SkillSlotId.Lmb);
        }

        if (player.Stats.Life is { } life)
        {
            life.Current = life.Max;
        }

        if (player.Stats.Mana is { } mana)
        {
            mana.Current = mana.Max;
        }
    }

    /// <summary>
    /// Co-op: a player node for every other party member, dressed from the
    /// character their machine sent. On the host they are driven by their
    /// inputs (<see cref="Player.Command"/>); on a client they are puppets
    /// placed by snapshots.
    /// </summary>
    private void SpawnParty(NetSession session)
    {
        var scene = GD.Load<PackedScene>("res://scenes/Player.tscn");
        for (int i = 0; i < session.Members.Count; i++)
        {
            LobbyMember member = session.Members[i];
            if (member.PeerId == Player.PeerId)
            {
                continue;
            }

            // A character that can't be read still joins, with a fresh build (friends-only:
            // characters are trusted, but malformed data must not crash the host).
            Characters.CharacterData? character = null;
            try
            {
                character = Profile.DeserializeCharacter(member.Character);
            }
            catch (System.Exception e)
            {
                GD.PushWarning($"Co-op: could not read {member.Name}'s character: {e.Message}");
            }
            var p = scene.Instantiate<Player>();
            p.Name = $"Player_{member.PeerId}";
            p.IsLocal = false;
            p.PeerId = member.PeerId;
            p.NetName = member.Name;
            p.Puppet = session.IsClient;
            if (session.IsHost)
            {
                p.Command = new PlayerCommand();
            }

            AddChild(p);
            p.GlobalPosition = SpawnPoint(i, session.Members.Count);
            RunBuild build = ConfigureRunCharacter(p);
            if (character is not null)
            {
                Profile.ApplyCharacterTo(p, character);
            }

            FinishRunCharacter(p);
            if (session.IsClient)
            {
                p.GetNode<PlayerSkillInput>("PlayerSkillInput").InputEnabled = false;
                p.Dodge!.InputEnabled = false;
                MuteLocalEffects(p);
            }
            else
            {
                HookDeath(p);
                p.Dodge!.Dodged += () => OnPlayerDodged(p);
            }

            RunPlayer rp = State.AddPlayer(p, build);
            rp.ViewSize = member.ViewSize;
        }
    }

    /// <summary>Co-op client: ascendancy behaviours that spawn effects on their own
    /// timers stay off here — the host runs them and its events replay them.</summary>
    private static void MuteLocalEffects(Player player)
    {
        if (Characters.CharacterBuild.Of(player)?.Ascendancy is { } ascendancy)
        {
            ascendancy.ProcessMode = ProcessModeEnum.Disabled;
        }
    }

    private void HookDeath(Player player)
    {
        if (NetSessionIsClient)
        {
            return; // the host decides who falls
        }

        player.Health!.Died += () => OnPlayerDied(player);
    }

    /// <summary>
    /// Offline: falling ends the run (after the Colossus it still counts as a
    /// victory). Co-op (docs/design/multiplayer.md -> "Deaths"): the player is
    /// downed until an ally revives them; the run is lost when everyone is down.
    /// </summary>
    private void OnPlayerDied(Player player)
    {
        if (!Coop)
        {
            State.EndRun(Director.ColossusDefeated);
            return;
        }

        player.ReviveProgress = 0f;
        if (State.PlayerOf(player) is { } rp)
        {
            NetHost?.ToastAll($"{RiftHost.NameOf(rp)} is down — stand next to them to revive", new Color(1f, 0.45f, 0.4f));
        }

        if (State.AllDown)
        {
            State.EndRun(Director.ColossusDefeated);
        }
    }

    /// <summary>"Phase Dancer" keystone: dodging releases a frost nova.</summary>
    private void OnPlayerDodged(Player player)
    {
        if (NetSessionIsClient || !Progression.BuildQuery.Any(player, b => b.PhaseDancer))
        {
            return;
        }

        var tags = Combat.SkillTags.Spell | Combat.SkillTags.Area | Combat.SkillTags.Cold;
        float damage = Stat.ComputeFormula(20f, player.Stats.GetModifiers(new ModifierQuery(StatId.Damage, tags)));
        var hit = new Combat.HitProfile
        {
            Damage = damage,
            Type = Combat.DamageType.Cold,
            Source = player,
            Faction = Combat.Faction.Player,
            Tags = tags,
            KnockbackForce = 220f,
        };
        Combat.AreaBurst.Spawn(this, player.GlobalPosition, 110f * player.Stats.Get(StatId.AreaOfEffect).Value, 0f, new Color(0.45f, 0.8f, 1f), hit);
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
        Director.Spawning = !NetSessionIsClient;
        Hud.ShowBanner("Survival Rift", Coop ? $"A party of {State.PartySize} — reach the Ember Colossus and break it" : "Reach the Ember Colossus — and break it", UiTheme.Gold);
    }

    public override void _Process(double delta)
    {
        if (!Coop || State.Ended)
        {
            return;
        }

        // Downed in co-op: after a moment the camera follows a standing ally.
        var camera = Player.GetNodeOrNull<Camera2D>("Camera2D");
        if (camera is null)
        {
            return;
        }

        if (Player.Health is { IsDead: true })
        {
            _downedFor += (float)delta;
            Player? ally = _downedFor > 1.5f ? State.Players.Select(rp => rp.Player).FirstOrDefault(p => p is not null && p != Player && p.Health is { IsDead: false }) : null;
            if (ally is not null)
            {
                camera.TopLevel = true;
                camera.GlobalPosition = ally.GlobalPosition;
            }
        }
        else if (_downedFor > 0f)
        {
            _downedFor = 0f;
            camera.TopLevel = false;
            camera.Position = Vector2.Zero;
        }
    }

    private void OnPauseLeave()
    {
        if (Session is { IsClient: true })
        {
            // Leaving the party: this player's run ends here, with their rewards so far.
            Session.Leave();
            NetClient?.OnHostLost("You left the party.");
            return;
        }

        State.EndRun(Director.ColossusDefeated);
    }

    private void LeaveTo(string scene)
    {
        // A session lasts one run: leaving the results leaves the party.
        Session?.Leave();
        Scenes.Go(this, scene);
    }

    private async void OnRunEnded(bool victory)
    {
        Director.Spawning = false;
        Player.ControlsEnabled = false;
        Player.Warded = false;
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

        if (!victory && !Coop)
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
