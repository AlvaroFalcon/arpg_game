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
    public CashOutPanel CashOut { get; private set; } = null!;
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
        bool autoCashOut = RunLaunchOptions.AutoCashOut;
        RunLaunchOptions.Reset();

        Arena = GetNode<Arena>("Arena");
        State = GetNode<RunState>("RunState");
        Director = GetNode<RunDirector>("RunDirector");
        Player = GetNode<Player>("Player");

        Director.State = State;
        Director.Arena = Arena;
        Director.Spawning = false;
        Director.AutoCashOut = autoCashOut;

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
        CashOut = new CashOutPanel { State = State, Name = "CashOut" };
        AddChild(CashOut);
        Director.CashOutOffered += OnCashOutOffered;
        Director.PortalEntered += OnPortalEntered;
        // Co-op: nothing can pause, so the player is held still while the panel is up.
        CashOut.OpenChanged += open =>
        {
            if (Coop && IsInstanceValid(Player) && !State.Ended)
            {
                Player.ControlsEnabled = !open;
            }
        };
        Results = new ResultsScreen { Name = "Results", OnHideout = () => LeaveTo(Scenes.Hideout), OnMenu = () => LeaveTo(Scenes.MainMenu) };
        AddChild(Results);
        Pause = new PauseMenu
        {
            Name = "Pause",
            LeaveText = Session is null ? "Abandon run" : Session.IsHost ? "End the run for everyone" : "Leave the party",
            LeaveTextNow = LeaveLabel,
            OnLeave = OnPauseLeave,
            Summary = () => LevelUpScreen.BuildSummary(Build),
            NoPause = Coop,
        };
        AddChild(Pause);

        State.RunEnded += OnRunEnded;
        Sfx.Music(this, "music_run");
        Session?.ReportPlace(MemberPlace.Run);

        // Co-op level-ups (docs/design/multiplayer.md -> "Level-ups"): the host runs
        // rounds of picks for the whole party and pauses everything meanwhile; a
        // client's choice and row go to the host, which applies the choice.
        if (Session is { IsHost: true })
        {
            NetHost = new RiftHost { Name = "RiftHost", Run = this, SkipStartPick = startSkill is not null };
            AddChild(NetHost);
        }
        else if (Session is { IsClient: true })
        {
            NetClient = new RiftClient { Name = "RiftClient", Run = this };
            AddChild(NetClient);
            LevelUp.PartyChosen += (round, offer) => NetClient.SendPick(round, offer.Key);
            CashOut.PartyChosen += (round, leave) => NetClient.SendPick(round, leave ? "leave" : "stay");
            LevelUp.PartyOffersChanged += (round, offers) => NetClient.SendOffers(round, offers);
        }

        if (startTime > 0f)
        {
            State.SkipTo(startTime);
            Director.SyncTimers();
        }

        if (startSkill is { } skill)
        {
            // Tools and the balance bot: a preset first skill, no pick.
            Build.AddSkill(skill);
            ApplyAutoLevels(autoLevels);
            StartWhenReady();
        }
        else if (Coop)
        {
            // Co-op: the start pick is the host's first round, for everyone at once,
            // once the whole party has loaded (RiftHost.Rounds).
            StartWhenReady();
        }
        else
        {
            // docs/design/progression.md -> "The start pick": every solo run opens
            // with a skill card pick — the character's class skills if it has none,
            // otherwise a new class skill or a level for one it brought.
            ApplyAutoLevels(autoLevels);
            LevelUp.OpenStartPick(BeginRun);
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
        int slot = Session?.RunMembers.ToList().FindIndex(m => m.PeerId == Player.PeerId) ?? 0;
        Player.GlobalPosition = SpawnPoint(Mathf.Max(0, slot), Session is null ? 1 : Session.RunMembers.Count);
        Build = ConfigureRunCharacter(Player);
        Profile.Get(this)?.ApplyTo(Player);
        FinishRunCharacter(Player, Build);

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

    /// <summary>
    /// After the persistent character is on the player: its passive tree and
    /// ascendancy already apply (CharacterBuild, own modifier source, next to the
    /// run build's — docs/design/progression.md -> "The character in the Rift");
    /// its skills start at level 1 with no supports, and the run owns them from here.
    /// </summary>
    public static void FinishRunCharacter(Player player, RunBuild build)
    {
        if (player.SkillUser.SlotIsBuiltInMove(SkillSlotId.Lmb))
        {
            player.SkillUser.ClearSlot(SkillSlotId.Lmb);
        }

        if (Characters.CharacterBuild.Of(player) is { } character)
        {
            character.RunOwnsSkills = true;
        }

        build.AdoptCharacterSkills();

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
        // Only the rift's participants: whoever stayed in the hideout gets no player here.
        IReadOnlyList<LobbyMember> members = session.RunMembers;
        for (int i = 0; i < members.Count; i++)
        {
            LobbyMember member = members[i];
            if (member.PeerId == Player.PeerId)
            {
                continue;
            }

            RunBuild build = null!;
            Player p = PartySpawner.Build(this, member, puppet: session.IsClient, commanded: session.IsHost, SpawnPoint(i, members.Count),
                player => build = ConfigureRunCharacter(player));
            FinishRunCharacter(p, build);
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
    /// downed and gets back up after 10 s (sooner if an ally stands by); the run
    /// ends when everyone is down at once, with the reward reached so far.
    /// </summary>
    private void OnPlayerDied(Player player)
    {
        if (!Coop)
        {
            State.EndRun(Director.ColossusDefeated, fell: true);
            return;
        }

        player.ReviveProgress = 0f;
        if (State.PlayerOf(player) is { } rp)
        {
            rp.DownedFor = 0f;
            rp.ReviveHelp = 0f;
            NetHost?.ToastAll($"{RiftHost.NameOf(rp)} is down — back in {Net.PartyRules.RespawnSeconds:0} s, or stand next to them to revive", new Color(1f, 0.45f, 0.4f));
        }

        if (State.AllDown)
        {
            State.EndRun(Director.ColossusDefeated, fell: true);
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

    /// <summary>The pause menu's leave button: "Leave with reward" once the Colossus is down (it pays a
    /// victory then), otherwise the old wording.</summary>
    private string LeaveLabel()
    {
        if (!Director.ColossusDefeated)
        {
            return Session is null ? "Abandon run" : Session.IsHost ? "End the run for everyone" : "Leave the party";
        }

        return Session is null ? "Leave with reward" : Session.IsHost ? "End the run for everyone" : "Leave the party";
    }

    /// <summary>Solo: the Colossus fell and the director's hold is over. (Co-op: the host opens a party round.)</summary>
    private void OnCashOutOffered()
    {
        if (Coop || State.Ended)
        {
            return;
        }

        CashOut.OpenOffer(Director.TakeExitPortal, Director.BeginEndless);
    }

    /// <summary>Solo: walking into the portal asks, it never leaves by itself.</summary>
    private void OnPortalEntered() => OpenLeavePanel();

    /// <summary>The cash-out panel in leave mode, worded for solo, a client or the host.</summary>
    private void OpenLeavePanel()
    {
        if (State.Ended || CashOut.IsOpen)
        {
            return;
        }

        if (Session is null)
        {
            CashOut.OpenLeave("Leave with your reward?", "Everything below is yours the moment you leave. You can come back to the Rift any time.", "Leave with reward", Director.TakeExitPortal, pause: true);
        }
        else if (Session.IsClient)
        {
            CashOut.OpenLeave("Leave the party?", "You leave alone and the party keeps playing. You take your reward.", "Leave with reward", () =>
            {
                Session.Leave();
                NetClient?.OnHostLost("You left the party.");
            }, pause: false);
        }
        else
        {
            CashOut.OpenLeave("End the run for everyone?", "Everyone goes home with their reward.", "End the run", () => State.EndRun(true), pause: false);
        }
    }

    private void OnPauseLeave()
    {
        if (Director.ColossusDefeated && !State.Ended)
        {
            OpenLeavePanel();
            return;
        }

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
        if (Coop)
        {
            // A level-up round may have held the game: the results screen runs unpaused.
            GetTree().Paused = false;
        }

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
