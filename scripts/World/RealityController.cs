#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Combat;
using ArpgGame.Core;
using ArpgGame.Enemies;
using ArpgGame.Items;
using ArpgGame.Net;
using ArpgGame.Progression;
using ArpgGame.Run;
using ArpgGame.Skills;
using ArpgGame.Stats;
using ArpgGame.UI;

namespace ArpgGame.World;

/// <summary>
/// One Altered Reality — docs/design/endgame.md. Builds the layout for the
/// shard's tier, populates it with dormant packs (normal, magic, rare, the odd
/// unique-rare) and the theme's boss behind a gate, and runs the attempt:
/// experience and drops on kill, portals (lives) on death, the gate closing
/// when the boss fight starts, shards and loot when the boss dies, and the
/// exit portal home.
///
/// Co-op (docs/design/endgame.md -> "Co-op realities"): when the party session
/// runs this reality, every participant builds the same cavern from the host's
/// seed; only the host spawns and simulates monsters, deals damage and rolls
/// loot (every participant gets their own drops and the kill's experience), and
/// the party shares one portal pool. A <see cref="RealityHost"/> or
/// <see cref="RealityClient"/> carries it over the network. Rewards are applied
/// on each player's own machine, never into the host's profile.
/// </summary>
[GlobalClass]
public partial class RealityController : Node2D, IPickupHandler
{
    public const string Group = "reality";

    /// <summary>Metadata key on pack monsters: the layout room (or corridor) they spawned in.</summary>
    public const string RoomMeta = "reality_room";

    [Signal]
    public delegate void AnnouncedEventHandler(string title, string subtitle, Color color);

    [Signal]
    public delegate void ToastEventHandler(string text, Color color);

    [Signal]
    public delegate void BossStartedEventHandler(Node2D boss);

    public int Tier { get; private set; } = 1;
    public LayoutDef Layout { get; private set; } = null!;
    public RealityTheme Theme { get; private set; } = null!;
    public RasterLayout Raster { get; private set; } = null!;

    /// <summary>The map seed this reality was built from (null: random).</summary>
    public ulong? Seed { get; private set; }
    public Player Player { get; private set; } = null!;
    public FlowField Field { get; private set; } = null!;
    public List<string> Modifiers { get; private set; } = new();
    public int PortalsLeft { get; private set; }
    public int Kills { get; private set; }
    public int MonstersTotal { get; private set; }
    public float Elapsed { get; private set; }
    public BossEnemy? Boss { get; private set; }
    public bool BossFightActive { get; private set; }
    public bool Completed { get; private set; }
    public bool Collapsed { get; private set; }

    /// <summary>The introduction reality (docs/design/tutorial.md): fixed packs,
    /// softer monsters, unlimited portals and a <see cref="TutorialGuide"/>.</summary>
    public bool IsTutorial { get; private set; }
    public Node2D? ExitPortal => _exitPortal;
    public HashSet<Vector2I> Explored { get; } = new();
    public RealityHud Hud { get; private set; } = null!;
    public PauseMenu Pause { get; private set; } = null!;

    // --- co-op (docs/design/endgame.md -> "Co-op realities") -------------------------

    /// <summary>The party session running this reality (null solo).</summary>
    public NetSession? Session { get; private set; }

    public bool Coop => Session is not null;
    public bool IsHostSide => Session is { IsHost: true };
    public bool IsClientSide => Session is { IsClient: true };

    /// <summary>Every participant (the local player first; solo: just the player).</summary>
    public RealityParty Party { get; private set; } = null!;

    public RealityHost? NetHost { get; private set; }
    public RealityClient? NetClient { get; private set; }

    /// <summary>Co-op: the revive prompt and E (docs/design/multiplayer.md -> "Deaths and revives"); null solo.</summary>
    public ReviveControl? Revive { get; private set; }

    /// <summary>Participants when the reality started (the pool and the scaling use it).</summary>
    public int PartySize { get; private set; } = 1;

    private readonly RandomNumberGenerator _rng = new();
    private StaticBody2D _gate = null!;
    private GateBars _gateBars = null!;
    private Node2D? _exitPortal;
    private Label? _exitPrompt;
    private float _fieldTimer;
    private float _quantity;
    private float _rarityBonus;

    /// <summary>The Mirror's socketed jewels (account-wide; none in the tutorial).</summary>
    public JewelBonuses Jewels { get; private set; } = JewelBonuses.None;
    private bool _leaving;
    private static readonly object ModifierSource = new();

    public static RealityController? Of(Node context) => context.GetTree().GetFirstNodeInGroup(Group) as RealityController;

    public int MonsterLevel => RealityTiers.MonsterLevel(Tier);

    public override void _Ready()
    {
        AddToGroup(Group);
        AddToGroup(Pickup.HandlerGroup);
        _rng.Randomize();

        IsTutorial = RealityLaunch.Tutorial;
        Tier = IsTutorial ? 1 : Mathf.Clamp(RealityLaunch.Tier, 1, RealityTiers.MaxTier);
        // A new cavern on every entry (docs/design/endgame.md -> "Caverns"); tests and
        // tools can pin a seed to rebuild the same one.
        Seed = IsTutorial ? null : RealityLaunch.Seed;
        var layoutRng = new RandomNumberGenerator();
        if (Seed is { } seed)
        {
            layoutRng.Seed = seed;
        }
        else
        {
            layoutRng.Randomize();
        }

        Layout = IsTutorial
            ? LayoutCatalog.Tutorial
            : (RealityLaunch.LayoutId is { } id ? LayoutCatalog.Find(id) : null)
                ?? (Seed is { } s ? RealityLaunch.LayoutFor(s) : LayoutCatalog.Layouts[_rng.RandiRange(0, LayoutCatalog.Layouts.Count - 1)]);
        Modifiers = IsTutorial ? new List<string>() : new List<string>(RealityLaunch.Modifiers);
        RealityLaunch.Reset();
        Session = NetSession.Get(this);
        if (IsTutorial || Session is not { Active: true, InRun: true } || Session.CurrentRun?.Spec.Kind != RiftKind.Reality)
        {
            Session = null;
        }

        Party = new RealityParty((text, color) => EmitSignal(SignalName.Toast, text, color));
        PartySize = Session is null ? 1 : Mathf.Max(1, Session.RunMembers.Count);
        Theme = RealityTheme.Get(Layout.ThemeId);
        (_quantity, _rarityBonus) = RealityLaunch.Bonuses(Modifiers);
        if (!IsTutorial && !IsClientSide)
        {
            // Co-op: the host's jewels shape the whole map (its packs, its drop rolls).
            Jewels = JewelTree.Bonuses(Profile.Get(this)?.Jewels);
            _quantity += Jewels[JewelStat.Quantity];
            _rarityBonus += Jewels[JewelStat.Rarity];
        }

        if (Coop)
        {
            _quantity += PartyRules.ItemQuantity(PartySize);
            _rarityBonus += PartyRules.ItemRarity(PartySize);
        }

        PortalsLeft = IsTutorial ? TutorialPortals : Coop ? PartyRules.RealityPortals(Tier, PartySize) : RealityTiers.Portals(Tier);

        RenderingServer.SetDefaultClearColor(Theme.Void);
        AddChild(new CanvasModulate { Color = Theme.Ambient });

        var layoutRoot = new Node2D { Name = "Layout" };
        AddChild(layoutRoot);
        MoveChild(layoutRoot, 0);
        Raster = LayoutBuilder.Rasterize(Layout, layoutRng);
        _gate = LayoutBuilder.BuildScene(layoutRoot, Raster, Theme, layoutRng);
        _gateBars = new GateBars { Cells = Raster.Gate, ZIndex = 2 };
        AddChild(_gateBars);
        Field = new FlowField(Raster.WalkableGrid(), RasterLayout.CellPx, Vector2.Zero);
        FlowField.Current = Field;

        Player = GetNode<Player>("Player");
        if (Session is not null)
        {
            Player.PeerId = Session.LocalPeerId;
            Player.NetName = Session.Member(Player.PeerId)?.Name ?? "";
        }

        ConfigurePlayer();
        Player.UseCloseCamera();
        Party.Add(Player);
        if (Session is not null)
        {
            SpawnParty(Session);
        }

        // The host's replication goes in before the monsters, so it sees every one appear.
        if (IsHostSide)
        {
            NetHost = new RealityHost { Name = "RealityHost", Reality = this };
            AddChild(NetHost);
        }

        if (!IsClientSide)
        {
            SpawnPacks();
            SpawnBoss();
        }

        Hud = new RealityHud { Reality = this, Name = "Hud" };
        AddChild(Hud);
        Pause = new PauseMenu
        {
            LeaveText = IsTutorial ? "Skip the introduction" : !Coop ? "Abandon reality" : IsHostSide ? "Close the reality for everyone" : "Leave the reality",
            OnLeave = Abandon,
            Summary = Summary,
            NoPause = Coop,
        };
        AddChild(Pause);
        AddChild(new CharacterPanels { Player = Player, Name = "CharacterPanels" });
        AddChild(new QuestToast { Name = "QuestToast", Anchor = Hud.QuestToastAnchor });
        if (IsTutorial)
        {
            AddChild(new TutorialGuide { Reality = this, Name = "TutorialGuide" });
        }

        Enemy.SplitSpawned += OnSplitSpawned;
        if (IsClientSide)
        {
            NetClient = new RealityClient { Name = "RealityClient", Reality = this };
            AddChild(NetClient);
        }

        if (Coop)
        {
            Revive = new ReviveControl { Name = "ReviveControl", Local = Player };
            AddChild(Revive);
        }

        Session?.ReportPlace(MemberPlace.Run);

        Sfx.Music(this, Theme.Music);
        string mods = Modifiers.Count > 0 ? "  ·  " + string.Join(", ", Modifiers.Select(m => RealityModifier.Find(m)?.Text ?? m)) : "";
        CallDeferred(nameof(AnnounceStart), mods);
    }

    private void AnnounceStart(string mods) =>
        EmitSignal(SignalName.Announced, Layout.Name, IsTutorial ? "A short introduction — follow the objectives on the left" : StartSubtitle(mods), IsTutorial ? new Color(1f, 0.85f, 0.4f) : RealityTiers.ShardColor(Tier));

    private string StartSubtitle(string mods) => $"Tier {Tier}  ·  monster level {MonsterLevel}  ·  {PortalsLeft} portal{(PortalsLeft == 1 ? "" : "s")}{(Coop ? $" (shared, party of {PartySize})" : "")}{mods}";

    /// <summary>Portals in the introduction: dying there costs nothing.</summary>
    public const int TutorialPortals = 99;

    /// <summary>The introduction's monsters and guardian are this much softer.</summary>
    private float TutorialLife => IsTutorial ? 0.6f : 1f;

    /// <summary>A normal monster's life multiplier — docs/design/endgame.md ->
    /// "Monster scaling" (the introduction keeps authored life, softened: its
    /// hero is level 1 with no gear, far below the reference character).</summary>
    private float TierLife => (IsTutorial ? TutorialLife : MonsterScaling.LifeMultiplier(MonsterLevel)) * (Coop ? PartyRules.MonsterLife(PartySize) : 1f);

    /// <summary>Resistances and armour by monster level plus the theme's affinity
    /// (none in the introduction).</summary>
    private void ApplyDefences(Enemy enemy)
    {
        if (!IsTutorial)
        {
            MonsterScaling.ApplyDefences(enemy, MonsterLevel, Theme.Affinity);
        }
    }

    /// <summary>Tops a magic/rare/unique monster's rarity life (shared with the
    /// Survival Rift) up to the reality's time-to-kill ratio.</summary>
    private void ApplyRarityLife(Enemy enemy)
    {
        if (!IsTutorial && enemy.Rarity != MonsterRarity.Normal)
        {
            enemy.ApplyTierScaling(MonsterScaling.RarityLife(enemy.Rarity) / MonsterRarities.LifeMultiplier(enemy.Rarity), 1f);
        }
    }

    public override void _ExitTree()
    {
        Enemy.SplitSpawned -= OnSplitSpawned;
        if (FlowField.Current == Field)
        {
            FlowField.Current = null;
        }

        RenderingServer.SetDefaultClearColor(new Color(0.3f, 0.3f, 0.3f));
    }

    private string Summary()
    {
        CharacterData c = Profile.Get(this)?.Active ?? new CharacterData();
        return $"{Layout.Name} — tier {Tier}\n{c.Name}, level {c.Level} {c.AscendancyDef?.Name ?? c.Class.Name}\n{Kills} / {MonstersTotal} monsters slain  ·  {PortalsLeft} portals left";
    }

    // --- setup ---------------------------------------------------------------------

    private void ConfigurePlayer()
    {
        Player.GlobalPosition = SpawnSpot(Session?.RunMembers.ToList().FindIndex(m => m.PeerId == Player.PeerId) ?? 0);
        // LMB, RMB, 1-4 each fire their own loadout slot (CharacterBuild.Slots).
        Player.GetNode<PlayerSkillInput>("PlayerSkillInput").Mode = PlayerSkillInput.CastMode.Bound;
        Player.GetNodeOrNull<CanvasLayer>("DebugReadout")?.Hide();
        Profile.Get(this)?.ApplyTo(Player);
        ApplyRealityModifiers(Player);
        if (IsClientSide)
        {
            // The host casts for everyone (from this player's buttons); this machine only shows it.
            Player.GetNode<PlayerSkillInput>("PlayerSkillInput").InputEnabled = false;
            MuteLocalEffects(Player);
        }
        else
        {
            Player p = Player;
            Player.Health!.Died += () => OnPlayerDied(p);
            Player.Dodge!.Dodged += () => OnPlayerDodged(p);
        }

        var camera = Player.GetNode<Camera2D>("Camera2D");
        camera.LimitLeft = 0;
        camera.LimitTop = 0;
        camera.LimitRight = Raster.Width * RasterLayout.CellPx;
        camera.LimitBottom = Raster.Height * RasterLayout.CellPx;
        camera.PositionSmoothingEnabled = true;
        camera.PositionSmoothingSpeed = 9f;
    }

    /// <summary>The shard's modifiers on a player, then full life (every participant on the host).</summary>
    private void ApplyRealityModifiers(Player player)
    {
        if (Modifiers.Contains("frail"))
        {
            player.Stats.Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.More, -15f, ModifierSource));
        }

        if (Modifiers.Contains("exposed"))
        {
            foreach (StatId r in new[] { StatId.FireResistance, StatId.ColdResistance, StatId.LightningResistance })
            {
                player.Stats.Get(r).AddModifier(new StatModifier(ModifierType.Flat, -20f, ModifierSource));
            }
        }

        if (player.Stats.Life is { } life)
        {
            life.Current = life.Max;
        }
    }

    /// <summary>Where participant <paramref name="slot"/> starts (and comes back): around the entrance.</summary>
    private Vector2 SpawnSpot(int slot)
    {
        Vector2 entrance = RasterLayout.World(Raster.Entrance);
        if (slot <= 0)
        {
            return entrance;
        }

        Vector2 at = entrance + Vector2.Right.Rotated(slot * Mathf.Tau / 4f) * 40f;
        return Raster.Walkable(Field.CellOf(at)) ? at : entrance;
    }

    /// <summary>
    /// Co-op: a player node for every other participant, dressed from the character
    /// their machine sent. On the host it is driven by that player's inputs (its
    /// skills cast from their buttons); on a client it is a puppet placed by snapshots.
    /// </summary>
    private void SpawnParty(NetSession session)
    {
        List<LobbyMember> members = session.RunMembers.ToList();
        for (int i = 0; i < members.Count; i++)
        {
            LobbyMember member = members[i];
            if (member.PeerId == Player.PeerId)
            {
                continue;
            }

            Player p = PartySpawner.Build(this, member, puppet: session.IsClient, commanded: session.IsHost, SpawnSpot(i), player =>
            {
                player.GetNode<PlayerSkillInput>("PlayerSkillInput").Mode = PlayerSkillInput.CastMode.Bound;
                player.GetNodeOrNull<CanvasLayer>("DebugReadout")?.Hide();
            });
            ApplyRealityModifiers(p);
            if (session.IsClient)
            {
                p.GetNode<PlayerSkillInput>("PlayerSkillInput").InputEnabled = false;
                p.Dodge!.InputEnabled = false;
                MuteLocalEffects(p);
            }
            else
            {
                p.Health!.Died += () => OnPlayerDied(p);
                p.Dodge!.Dodged += () => OnPlayerDodged(p);
            }

            Party.Add(p);
        }
    }

    /// <summary>Co-op client: ascendancy behaviours with their own timers stay off (the host runs them).</summary>
    private static void MuteLocalEffects(Player player)
    {
        if (CharacterBuild.Of(player)?.Ascendancy is { } ascendancy)
        {
            ascendancy.ProcessMode = ProcessModeEnum.Disabled;
        }
    }

    private void OnPlayerDodged(Player player)
    {
        if (!BuildQuery.Any(player, b => b.PhaseDancer))
        {
            return;
        }

        var tags = SkillTags.Spell | SkillTags.Area | SkillTags.Cold;
        float damage = Stat.ComputeFormula(20f + 3f * MonsterLevel, player.Stats.GetModifiers(new ModifierQuery(StatId.Damage, tags)));
        var hit = new HitProfile { Damage = damage, Type = DamageType.Cold, Source = player, Faction = Faction.Player, Tags = tags, KnockbackForce = 220f };
        AreaBurst.Spawn(this, player.GlobalPosition, 110f * player.Stats.Get(StatId.AreaOfEffect).Value, 0f, new Color(0.45f, 0.8f, 1f), hit);
    }

    private PackedScene LoadEnemy(string name) => GD.Load<PackedScene>($"res://scenes/enemies/{name}.tscn");

    private void SpawnPacks()
    {
        bool hordes = Modifiers.Contains("hordes");
        bool elite = Modifiers.Contains("elite");
        foreach (PackSpot spot in Raster.Packs)
        {
            if (IsTutorial)
            {
                // Every marker spawns, small and predictable.
                SpawnPack(spot, spot.Kind, false);
                continue;
            }

            float chance = spot.Kind switch
            {
                'm' => RealityTiers.MagicPackChance(Tier) + (elite ? 0.25f : 0f) + Jewels[JewelStat.PackRarity],
                'r' => RealityTiers.RarePackChance(Tier) + (elite ? 0.25f : 0f) + Jewels[JewelStat.PackRarity],
                'u' => RealityTiers.UniqueChance(Tier),
                _ => RealityTiers.NormalPackChance(Tier) + (hordes ? 0.2f : 0f),
            };
            if (_rng.Randf() > chance)
            {
                // A failed magic/rare roll still leaves a normal pack.
                if (spot.Kind is 'm' or 'r')
                {
                    SpawnPack(spot, 'p', hordes);
                }

                continue;
            }

            SpawnPack(spot, spot.Kind, hordes);
            // Higher tiers double up on grey packs (density).
            if (spot.Kind == 'p' && _rng.Randf() < RealityTiers.ExtraPackChance(Tier) + Jewels[JewelStat.Density])
            {
                SpawnPack(spot, 'p', hordes);
            }
        }
    }

    /// <summary>
    /// One pack — docs/design/endgame.md -> "Spawning". The spot's budget (tier ×
    /// heat × zone × modifiers) buys its monsters by cost; the kind decides the
    /// leader. The spot's territory picks the main species (most of the pack).
    /// The tutorial keeps its small fixed packs.
    /// </summary>
    private void SpawnPack(PackSpot spot, char kind, bool hordes)
    {
        var pack = new List<Enemy>();
        Vector2 centre = RasterLayout.World(spot.Cell);
        string main = spot.Territory >= 0
            ? Theme.Roster[spot.Territory % Theme.Roster.Count]
            : Theme.Roster[_rng.RandiRange(0, Theme.Roster.Count - 1)];
        float budget = RealityTiers.PackBudget(Tier) * RealityTiers.BudgetScale(spot.Heat, spot.Zone)
            * (hordes ? 1.3f : 1f) * (1f + Jewels[JewelStat.Density]) * _rng.RandfRange(0.85f, 1.15f)
            * (Coop ? PartyRules.RealityPackSize(PartySize) : 1f);
        int normals = kind switch
        {
            'm' or 'u' => 0,
            'r' => IsTutorial ? 2 : Mathf.RoundToInt(budget * RealityTiers.RareEscortShare / RealityTiers.NormalCost),
            _ => IsTutorial ? 3 : Mathf.Max(1, Mathf.RoundToInt(budget / RealityTiers.NormalCost)),
        };

        int index = 0;
        float turn = _rng.Randf() * Mathf.Tau;
        Enemy Add(string type, MonsterRarity rarity)
        {
            Enemy e = SpawnMonster(type, centre, rarity, index++, turn);
            pack.Add(e);
            return e;
        }

        switch (kind)
        {
            case 'm':
                int magics = IsTutorial ? 2 : Mathf.Clamp(Mathf.RoundToInt(budget / RealityTiers.MagicCost), 2, 9);
                for (int i = 0; i < magics; i++)
                {
                    Add(main, MonsterRarity.Magic);
                }

                break;
            case 'r':
                for (int i = 0; i < (IsTutorial ? 1 : RealityTiers.RaresPerPack(Tier)); i++)
                {
                    Add(main, MonsterRarity.Rare);
                }

                break;
            case 'u':
                Add(Theme.Roster.FirstOrDefault(r => r is "Brute" or "Thrall" or "Skeleton") ?? main, MonsterRarity.Unique);
                for (int i = 0; i < 3; i++)
                {
                    Add(main, MonsterRarity.Magic);
                }

                break;
        }

        for (int i = 0; i < normals; i++)
        {
            Add(_rng.Randf() < 0.7f ? main : Theme.Roster[_rng.RandiRange(0, Theme.Roster.Count - 1)], MonsterRarity.Normal);
        }

        foreach (Enemy e in pack)
        {
            e.Pack = pack;
            e.SetMeta(RoomMeta, spot.Room);
        }
    }

    /// <summary>A pack member. Members sit on a golden-angle spiral around the
    /// centre with a little jitter: evenly spread (blue noise), leader in the middle,
    /// never stacked and never in a grid.</summary>
    private Enemy SpawnMonster(string type, Vector2 centre, MonsterRarity rarity, int index, float turn)
    {
        var enemy = LoadEnemy(type).Instantiate<Enemy>();
        enemy.MonsterLevel = MonsterLevel;
        enemy.Dormant = true;
        AddChild(enemy);
        Vector2 offset = Vector2.Right.Rotated(turn + index * 2.39996f) * 24f * Mathf.Sqrt(index + 0.35f)
            + new Vector2(_rng.RandfRange(-8f, 8f), _rng.RandfRange(-8f, 8f));
        Vector2 at = centre + offset;
        enemy.GlobalPosition = Raster.Walkable(Field.CellOf(at)) ? at : centre;
        float life = TierLife * (Modifiers.Contains("hardened") ? 1.4f : 1f);
        float dmg = RealityTiers.DamageMultiplier(Tier) * TutorialLife * (Modifiers.Contains("searing") ? 1.25f : 1f);
        enemy.ApplyTierScaling(life, dmg);
        ApplyDefences(enemy);
        if (Modifiers.Contains("swift"))
        {
            enemy.Stats.MovementSpeed.AddModifier(new StatModifier(ModifierType.More, 20f, ModifierSource));
        }

        if (rarity != MonsterRarity.Normal)
        {
            enemy.ApplyRarity(rarity);
        }
        else if (Modifiers.Contains("volatile") && _rng.Randf() < 0.25f)
        {
            enemy.ApplyRarity(MonsterRarity.Magic, new[] { MonsterRarities.Mods.First(m => m.Id == "volatile") });
        }

        ApplyRarityLife(enemy);

        RegisterMonster(enemy);
        return enemy;
    }

    /// <summary>A monster spawned mid-fight (boss adds): scaled to the tier and counted.</summary>
    public void AdoptMonster(Enemy enemy)
    {
        enemy.MonsterLevel = MonsterLevel;
        enemy.ApplyTierScaling(TierLife, RealityTiers.DamageMultiplier(Tier) * TutorialLife);
        ApplyDefences(enemy);
        ApplyRarityLife(enemy);
        RegisterMonster(enemy);
    }

    private void RegisterMonster(Enemy enemy)
    {
        MonstersTotal++;
        enemy.Killed += OnMonsterKilled;
    }

    private void OnSplitSpawned(Enemy child)
    {
        if (IsInstanceValid(child))
        {
            RegisterMonster(child);
        }
    }

    private void SpawnBoss()
    {
        var boss = LoadEnemy(Theme.Boss).Instantiate<BossEnemy>();
        boss.MonsterLevel = MonsterLevel;
        boss.Dormant = true;
        boss.AggroRadius = 0f;
        AddChild(boss);
        boss.GlobalPosition = RasterLayout.World(Raster.BossCell);
        // Bosses last MonsterScaling.TtkBoss against the reference character; the
        // introduction's guardian keeps its authored life (softened).
        float life = (IsTutorial ? Theme.BossLifeMultiplier * TutorialLife : MonsterScaling.BossLifeMultiplier(MonsterLevel, boss.BaseLife * Theme.BossLifeMultiplier) * Theme.BossLifeMultiplier)
            * (Modifiers.Contains("hardened") ? 1.4f : 1f) * (Coop ? PartyRules.BossLife(PartySize) : 1f);
        // Its hits are aimed at the reference character's life (MonsterScaling.BossHitShare).
        float dmg = (IsTutorial ? TutorialLife : MonsterScaling.BossDamageMultiplier(MonsterLevel)) * (Modifiers.Contains("searing") ? 1.25f : 1f);
        boss.ApplyTierScaling(life, dmg);
        ApplyDefences(boss);
        boss.Killed += OnBossKilled;
        Boss = boss;
        NetHost?.AdoptBossNow(boss);
    }

    // --- loop ------------------------------------------------------------------------

    /// <summary>Monster drops waiting to spawn (a few per frame, see <see cref="LootQueue"/>).</summary>
    public LootQueue Loot { get; } = new();

    public override void _PhysicsProcess(double delta)
    {
        Loot.Pump();
        float dt = (float)delta;
        if (!Completed && !Collapsed)
        {
            Elapsed += dt;
        }

        _fieldTimer -= dt;
        if (_fieldTimer <= 0f)
        {
            _fieldTimer = 0.25f;
            if (Coop)
            {
                // Monsters walk toward whichever living player is nearest along the walls.
                _targets.Clear();
                foreach (RunPlayer rp in Party.Players)
                {
                    if (rp.Player is { } p && IsInstanceValid(p) && !rp.Downed)
                    {
                        _targets.Add(p.GlobalPosition);
                    }
                }

                if (_targets.Count > 0)
                {
                    Field.Update(_targets);
                }
            }
            else
            {
                Field.Update(Player.GlobalPosition);
            }

            Reveal();
        }

        if (IsClientSide || BossFightActive || Boss is null || !IsInstanceValid(Boss) || Boss.Health.IsDead)
        {
            return;
        }

        if (!Coop)
        {
            if (!(Player.Health?.IsDead ?? true) && Raster.BossRegion.Contains(Field.CellOf(Player.GlobalPosition)))
            {
                StartBossFight(null);
            }

            return;
        }

        foreach (RunPlayer rp in Party.Players)
        {
            if (rp.Player is { } p && IsInstanceValid(p) && !rp.Downed && Raster.BossRegion.Contains(Field.CellOf(p.GlobalPosition)))
            {
                StartBossFight(rp);
                break;
            }
        }
    }

    private readonly List<Vector2> _targets = new();

    public override void _Process(double delta)
    {
        // The exit portal is used deliberately (F), never by walking over it:
        // loot tends to land right on top of it.
        if (_exitPortal is null || _leaving)
        {
            return;
        }

        bool near = Player.GlobalPosition.DistanceTo(_exitPortal.GlobalPosition) < 70f;
        if (_exitPrompt is not null)
        {
            _exitPrompt.Visible = near;
        }

        // F next to a downed ally revives them (ReviveControl) rather than leaving.
        if (near && !GetTree().Paused && Input.IsActionJustPressed("interact") && Revive is not { WantsInteract: true })
        {
            UseExitPortal();
        }
    }

    private void Reveal()
    {
        Vector2I c = Field.CellOf(Player.GlobalPosition);
        for (int dx = -7; dx <= 7; dx++)
        {
            for (int dy = -5; dy <= 5; dy++)
            {
                Explored.Add(new Vector2I(c.X + dx, c.Y + dy));
            }
        }
    }

    /// <summary>
    /// The fight starts (<paramref name="starter"/>: who walked in; null solo). Co-op
    /// (docs/design/endgame.md -> "Co-op realities"): every other living participant
    /// is pulled in just inside the gate before it closes, so nobody is shut out.
    /// </summary>
    private void StartBossFight(RunPlayer? starter)
    {
        BossFightActive = true;
        if (starter is not null)
        {
            int slot = 0;
            foreach (RunPlayer rp in Party.Players)
            {
                if (rp == starter || rp.Player is not { } p || !IsInstanceValid(p) || rp.Downed || Raster.BossRegion.Contains(Field.CellOf(p.GlobalPosition)))
                {
                    continue;
                }

                MovePlayer(rp, ArenaSpot(slot++));
            }
        }

        SetGate(true);
        Boss!.Wake();
        Sfx.Play(this, "boss_warn");
        Sfx.Music(this, "music_boss");
        string subtitle = starter is null ? "The way back is sealed" : $"{CoopHost.NameOf(starter)} started the fight · the way back is sealed";
        AnnounceAll(Boss.DisplayName, subtitle, new Color(1f, 0.35f, 0.25f));
        NetHost?.Gate(true);
        EmitSignal(SignalName.BossStarted, Boss);
    }

    /// <summary>The host moves a participant (respawn, pull-in) and tells their machine.</summary>
    private void MovePlayer(RunPlayer rp, Vector2 at)
    {
        if (rp.Player is not { } p)
        {
            return;
        }

        p.GlobalPosition = at;
        p.Velocity = Vector2.Zero;
        if (!rp.IsLocal)
        {
            NetHost?.Teleport(rp.PeerId, at);
        }
    }

    private Vector2? _arenaSpot;

    /// <summary>A walkable spot just inside the boss gate (spread by <paramref name="slot"/>).</summary>
    public Vector2 ArenaSpot(int slot = 0)
    {
        if (_arenaSpot is null)
        {
            Vector2 gate = Vector2.Zero;
            foreach (Vector2I c in Raster.Gate)
            {
                gate += RasterLayout.World(c);
            }

            gate = Raster.Gate.Count > 0 ? gate / Raster.Gate.Count : RasterLayout.World(Raster.BossCell);
            var gateCells = new HashSet<Vector2I>(Raster.Gate);
            Vector2 best = RasterLayout.World(Raster.BossCell);
            float bestScore = float.MaxValue;
            foreach (Vector2I c in Raster.BossRegion)
            {
                Vector2 at = RasterLayout.World(c);
                float d = at.DistanceTo(gate);
                // Inside the gate, not on it: at least a cell and a half in.
                if (!Raster.Walkable(c) || gateCells.Contains(c) || d < RasterLayout.CellPx * 1.5f || d >= bestScore)
                {
                    continue;
                }

                bestScore = d;
                best = at;
            }

            _arenaSpot = best;
        }

        Vector2 spot = _arenaSpot.Value + Vector2.Right.Rotated(slot * 2.1f) * (slot == 0 ? 0f : 26f);
        return Raster.Walkable(Field.CellOf(spot)) ? spot : _arenaSpot.Value;
    }

    /// <summary>A banner on this screen and, co-op host, on every participant's.</summary>
    private void AnnounceAll(string title, string subtitle, Color color)
    {
        EmitSignal(SignalName.Announced, title, subtitle, color);
        NetHost?.BannerAll(title, subtitle, color);
    }

    private void SetGate(bool closed)
    {
        foreach (Node n in _gate.GetChildren())
        {
            if (n is CollisionShape2D shape)
            {
                shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, !closed);
            }
        }

        foreach (Vector2I c in Raster.Gate)
        {
            Field.SetWalkable(c, !closed);
        }

        _gateBars.Closed = closed;
        _gateBars.QueueRedraw();
    }

    // --- kills, drops, pickups -----------------------------------------------------

    private void OnMonsterKilled(Enemy enemy)
    {
        Kills++;
        Sfx.Play(this, "death", -6f);
        if (!Coop)
        {
            if (enemy.Health.LastHitSource == Player)
            {
                RewardKill(enemy, Party.Players[0]);
            }

            DropLoot(enemy, null);
            return;
        }

        // Co-op: the killer gets the on-kill effects; every participant the full
        // character experience and their own drops (docs/design/endgame.md -> "Co-op realities").
        if (Party.PlayerOf(enemy.Health.LastHitSource) is { } killer)
        {
            RewardKill(enemy, killer);
        }

        foreach (RunPlayer rp in Party.Players)
        {
            DropLoot(enemy, rp.IsLocal ? null : rp.Player);
        }
    }

    /// <summary>A player's kill: the party's character experience, then that player's on-kill effects.</summary>
    private void RewardKill(Enemy enemy, RunPlayer killer)
    {
        float xp = RunState.CharacterExperienceFor(enemy) * (1f + Jewels[JewelStat.Experience]);
        foreach (RunPlayer rp in Party.Players)
        {
            if (rp.IsLocal)
            {
                CharacterBuild.Of(rp.Player!)?.GrantExperience(xp);
            }
            else
            {
                rp.PendingCharacterExperience += xp; // RealityHost sends it to that player's machine
            }
        }

        Player player = killer.Player!;
        if (killer.IsLocal && Profile.Get(this) is { } profile)
        {
            profile.Active.TotalKills++;
        }

        float lifeOnKill = player.Stats.Get(StatId.LifeOnKill).Value;
        if (lifeOnKill > 0f)
        {
            player.Health?.Heal(lifeOnKill);
        }

        float manaOnKill = player.Stats.Get(StatId.ManaOnKill).Value;
        if (manaOnKill > 0f && player.Stats.Mana is { } mana)
        {
            mana.Current = Mathf.Min(mana.Max, mana.Current + manaOnKill);
        }

        if (BuildQuery.Any(player, b => b.ChainReaction))
        {
            var hit = new HitProfile { Damage = (enemy.Stats.Life?.Max ?? 0f) * 0.2f, Type = DamageType.Fire, Source = player, Faction = Faction.Player, Tags = SkillTags.Fire | SkillTags.Area, KnockbackForce = 80f };
            AreaBurst.SpawnOnKill(this, enemy.GlobalPosition, 70f * enemy.Scale.X, new Color(1f, 0.5f, 0.15f), hit);
        }
    }

    /// <summary>One loot roll of a kill, for <paramref name="owner"/> (null = this machine's player).</summary>
    private void DropLoot(Enemy enemy, Player? owner)
    {
        Vector2 at = enemy.GlobalPosition;
        float q = 1f + _quantity;
        (float item, float gem, float shard, float gold) = enemy.Rarity switch
        {
            MonsterRarity.Magic => (0.08f, 0.012f, 0f, 0.15f),
            MonsterRarity.Rare => (0.45f, 0.06f, 0.03f, 1f),
            MonsterRarity.Unique => (2.0f, 0.35f, 0.15f, 1f),
            _ => (0.03f, 0.003f, 0f, 0.06f),
        };

        for (float roll = item * q; roll > 0f; roll -= 1f)
        {
            if (_rng.Randf() < roll)
            {
                Vector2 p = at + Jitter(30f);
                DropSource source = enemy.Rarity switch
                {
                    MonsterRarity.Magic => DropSource.MagicMonster,
                    MonsterRarity.Rare => DropSource.RareMonster,
                    MonsterRarity.Unique => DropSource.UniqueMonster,
                    _ => DropSource.NormalMonster,
                };
                Loot.Add(LootQueue.ItemCost, () => DropItem(p, source, 0f, owner));
            }
        }

        // Everything below is rolled now and spawned a few per frame (Loot).
        if (_rng.Randf() < gem * q)
        {
            Vector2 p = at + Jitter(20f);
            Loot.Add(LootQueue.PickupCost, () => DropGem(p, owner));
        }

        if (_rng.Randf() < shard * q)
        {
            Vector2 p = at + Jitter(20f);
            int tier = Tier;
            Loot.Add(LootQueue.PickupCost, () => DropShard(p, tier, owner));
        }

        if (_rng.Randf() < gold * q)
        {
            float amount = Mathf.Round((2 + Tier) * (enemy.Rarity >= MonsterRarity.Rare ? 6 : 1) * (1f + Jewels[JewelStat.Gold]));
            Vector2 p = at + Jitter(12f);
            Loot.Add(LootQueue.PickupCost, () => Party.Spawned(Pickup.Spawn(this, PickupKind.Gold, amount, p), owner));
        }

        // Crafting orbs (docs/design/crafting.md -> "Where orbs come from").
        float craft = enemy.Rarity switch { MonsterRarity.Magic => 0.1f, MonsterRarity.Rare => 0.45f, MonsterRarity.Unique => 1.2f, _ => 0.035f };
        for (float roll = craft * q * (1f + Jewels[JewelStat.Orbs]); roll > 0f; roll -= 1f)
        {
            if (_rng.Randf() < roll)
            {
                Vector2 p = at + Jitter(24f);
                Loot.Add(LootQueue.PickupCost, () => DropCurrency(p, owner));
            }
        }

        // Flask refill orbs (docs/design/items.md -> "Flasks and refill orbs").
        float orb = enemy.Rarity switch { MonsterRarity.Normal => 0.05f, MonsterRarity.Magic => 0.15f, _ => 0.6f };
        if (_rng.Randf() < orb)
        {
            Vector2 p = at + Jitter(10f);
            Loot.Add(LootQueue.PickupCost, () => Party.Spawned(Pickup.Spawn(this, PickupKind.Life, 0.2f, p), owner));
        }
    }

    private Vector2 Jitter(float r) => new(_rng.RandfRange(-r, r), _rng.RandfRange(-r, r));

    /// <summary>Drops an item whose rarity comes from the source's cascade
    /// (docs/design/items.md -> "Drop tables (cascade)"), every chance scaled by the
    /// reality's increased rarity (shard modifiers, jewels, <paramref name="extraRarity"/>)
    /// and its tier.</summary>
    /// <remarks>Co-op: an item for a remote <paramref name="owner"/> is rolled here, sent to
    /// their machine and freed (it never lands in the host's world); null is returned then.</remarks>
    public EquipmentItem? DropItem(Vector2 at, DropSource source, float extraRarity = 0f, Player? owner = null)
    {
        IReadOnlyList<BaseItemDefinition> bases = ItemFactory.Bases;
        if (bases.Count == 0)
        {
            return null;
        }

        EquipmentItem item = ItemFactory.NewBlank();
        bool remote = owner is { IsLocal: false };
        if (!remote)
        {
            AddChild(item);
            item.GlobalPosition = at;
        }

        Rarity rarity = DropTables.RollRarity(source, DropTables.RealityMultiplier(_rarityBonus + extraRarity, Tier), _rng);
        item.Initialize(bases[_rng.RandiRange(0, bases.Count - 1)], rarity, Mathf.Clamp(MonsterLevel, 1, 100), ItemFactory.Affixes.Entries, _rng);
        if (remote)
        {
            Party.ItemForRemote(owner!, item, at);
            item.QueueFree();
            return null;
        }

        return item;
    }

    public void DropGem(Vector2 at, Player? owner = null)
    {
        bool support = Features.SupportGems && _rng.Randf() < 0.6f;
        string id = support
            ? UpgradeCatalog.Supports[_rng.RandiRange(0, UpgradeCatalog.Supports.Count - 1)].Id
            : UpgradeCatalog.Skills[_rng.RandiRange(0, UpgradeCatalog.Skills.Count - 1)].Id;
        Pickup gem = Pickup.Spawn(this, PickupKind.Gem, 1f, at);
        gem.GemId = id;
        gem.GemSupport = support;
        gem.QueueRedraw();
        Party.Spawned(gem, owner);
    }

    public void DropCurrency(Vector2 at, Player? owner = null)
    {
        Pickup orb = Pickup.Spawn(this, PickupKind.Currency, 1f, at);
        orb.CurrencyKey = DropTables.RollOrb(_rng, DropTables.RealityMultiplier(_rarityBonus, Tier)).Id;
        orb.QueueRedraw();
        Party.Spawned(orb, owner);
    }

    private int JewelsOnFloor() => GetTree().GetNodesInGroup(RunState.PickupGroup).OfType<Pickup>().Count(p => p.Kind == PickupKind.Jewel);

    public void DropShard(Vector2 at, int tier, Player? owner = null) =>
        Party.Spawned(Pickup.Spawn(this, PickupKind.Shard, Mathf.Clamp(tier, 1, RealityTiers.MaxTier), at), owner);

    public void OnPickup(Pickup pickup, Player player)
    {
        if (!player.IsLocal)
        {
            // Co-op host: somebody else's drop. Only what the simulation needs happens
            // here (their flask, their copy's wallet); the reward itself goes to their
            // machine (RealityHost → RealityClient → ApplyReward), never into this profile.
            if (pickup.Kind == PickupKind.Life)
            {
                Flasks.CollectOrb(player, pickup.Value);
            }

            Party.Collected(pickup, player, pickup.Kind == PickupKind.Gem && pickup.GemSupport ? 1 : 0);
            return;
        }

        ApplyReward(player, pickup.Kind, pickup.Value, pickup.Kind == PickupKind.Gem ? pickup.GemId : pickup.CurrencyKey, pickup.GemSupport, fromHost: false);
    }

    /// <summary>
    /// What a collected drop gives this machine's player: the wallet, the character and
    /// the account (shards, jewels, gems). Solo and the host call it from
    /// <see cref="OnPickup"/>; a co-op client when the host says one of its drops was
    /// collected (<paramref name="fromHost"/>: the flask charges come with the snapshots).
    /// </summary>
    public void ApplyReward(Player player, PickupKind kind, float value, string key, bool support, bool fromHost)
    {
        CharacterData? c = Profile.Get(this)?.Active;
        switch (kind)
        {
            case PickupKind.Life:
                Sfx.Play(this, "pickup");
                if (!fromHost)
                {
                    Flasks.CollectOrb(player, value);
                }

                break;
            case PickupKind.Gold:
                Sfx.Play(this, "pickup");
                player.Wallet.Add(Profile.GoldId, (long)value);
                UI.LootFeed.Gold(this, (long)value);
                break;
            case PickupKind.Shard when c is not null:
                int tier = Mathf.Clamp(Mathf.RoundToInt(value), 1, RealityTiers.MaxTier);
                c.AddShards(tier);
                Sfx.Play(this, "levelup", -4f);
                EmitSignal(SignalName.Toast, $"Mirror Shard (tier {tier})", RealityTiers.ShardColor(tier));
                GameEvents.Raise(GameEvents.ShardFound, tier);
                break;
            case PickupKind.Jewel when Profile.Get(this) is { } account:
                // Co-op: the host rolls jewels from its own progress; never past the tree's sockets.
                account.Jewels.Found = Mathf.Min(account.Jewels.Found + 1, Mathf.Max(account.Jewels.Found, JewelTree.Sockets));
                account.Save();
                Sfx.Play(this, "levelup", -2f);
                EmitSignal(SignalName.Toast, $"Mirror Jewel ({account.Jewels.Found}/{JewelTree.Sockets}) — socket it at the Mirror", new Color(0.85f, 0.6f, 1f));
                GameEvents.Raise(GameEvents.JewelFound, account.Jewels.Found);
                break;
            case PickupKind.Currency when Crafting.Find(key) is { } currency:
                Sfx.Play(this, "pickup", -2f);
                int orbs = Mathf.Clamp(Mathf.RoundToInt(value), 1, 1000);
                player.Wallet.Add(currency.Id, orbs);
                UI.LootFeed.Currency(this, currency.Id, orbs);
                break;
            case PickupKind.Gem when c is not null && (!fromHost || UpgradeCatalog.Skills.Any(g => g.Id == key) || UpgradeCatalog.Supports.Any(g => g.Id == key)):
                Sfx.Play(this, "levelup", -6f);
                (string text, long gold) = GemLoot.Collect(c, key, support);
                if (gold > 0)
                {
                    player.Wallet.Add(Profile.GoldId, gold);
                }

                EmitSignal(SignalName.Toast, text, support ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.6f, 0.35f));
                break;
        }
    }

    // --- boss, death, exit ---------------------------------------------------------

    private void OnBossKilled(Enemy boss)
    {
        Completed = true;
        BossFightActive = false;
        SetGate(false);
        NetHost?.Gate(false);
        Vector2 at = boss.GlobalPosition;
        if (!Coop)
        {
            BossLoot(at, null);
        }
        else
        {
            // A full set for every participant, the guaranteed shard included.
            foreach (RunPlayer rp in Party.Players)
            {
                BossLoot(at, rp.IsLocal ? null : rp.Player);
            }
        }

        if (Profile.Get(this) is { } profile)
        {
            ApplyClear(profile.Active);
        }

        NetHost?.Cleared(Tier);
        BuildExitPortal(at + new Vector2(0f, 90f));
        NetHost?.Exit(_exitPortal!.GlobalPosition);
        Sfx.Play(this, "victory");
        Sfx.Music(this, "music_menu");
        AnnounceAll(IsTutorial ? "Guardian defeated" : "Reality stabilised", "Collect your loot — press F at the portal to go home", new Color(1f, 0.85f, 0.4f));
    }

    /// <summary>The boss's drops for <paramref name="owner"/> (null = this machine's player).</summary>
    private void BossLoot(Vector2 at, Player? owner)
    {
        int items = 3 + (_quantity > 0.3f ? 1 : 0) + Mathf.RoundToInt(Jewels[JewelStat.BossItems]);
        for (int i = 0; i < items; i++)
        {
            DropItem(at + Jitter(60f), DropSource.Boss, Jewels[JewelStat.BossRarity], owner);
        }

        // A boss always drops a shard (endgame.md -> "Mirror Shards"); jewels can
        // lift its tier and add a second one.
        int shard = RealityTiers.RollBossShard(Tier, _rng);
        if (_rng.Randf() < Jewels[JewelStat.ShardUpgrade])
        {
            shard = Mathf.Min(RealityTiers.MaxTier, shard + 1);
        }

        DropShard(at + Jitter(30f), shard, owner);
        if (_rng.Randf() < RealityTiers.BonusShardChance * (1f + _quantity) + Jewels[JewelStat.ShardChance])
        {
            DropShard(at + Jitter(30f), Tier, owner);
        }

        // Mirror jewels (endgame.md -> "Mirror jewels"). Co-op: rolled from the host's progress.
        if (!IsTutorial && Profile.Get(this) is { } jewelProfile
            && _rng.Randf() < JewelTree.DropChance(jewelProfile.Jewels.Found + JewelsOnFloor(), Tier))
        {
            Party.Spawned(Pickup.Spawn(this, PickupKind.Jewel, 1f, at + Jitter(30f)), owner);
        }

        if (_rng.Randf() < 0.7f)
        {
            DropGem(at + Jitter(40f), owner);
        }

        Party.Spawned(Pickup.Spawn(this, PickupKind.Gold, Mathf.Round((10 + Tier * 4) * 5 * (1f + Jewels[JewelStat.Gold] + Jewels[JewelStat.BossGold])), at + Jitter(30f)), owner);
        for (int i = 0; i < 2 + Tier / 4; i++)
        {
            DropCurrency(at + Jitter(50f), owner);
        }
    }

    /// <summary>The clear on this machine's character: count, best tier, ascendancy
    /// points and the quests they open (co-op: each participant on its own machine).</summary>
    private void ApplyClear(CharacterData c)
    {
        c.RealitiesCleared++;
        c.HighestTierCleared = Mathf.Max(c.HighestTierCleared, Tier);
        if (Tier >= AscendancyCatalog.Tier1Unlock && c.AscendancyPoints < AscendancyCatalog.FirstPoints)
        {
            // The quest toast ("New quest: Choose an ascendancy") announces it.
            c.AscendancyPoints = AscendancyCatalog.FirstPoints;
        }

        if (Tier >= AscendancyCatalog.Tier2Unlock && c.AscendancyPoints < AscendancyCatalog.FirstPoints + AscendancyCatalog.SecondPoints)
        {
            c.AscendancyPoints = AscendancyCatalog.FirstPoints + AscendancyCatalog.SecondPoints;
            EmitSignal(SignalName.Toast, "Second ascendancy trial complete: +2 ascendancy points.", new Color(1f, 0.85f, 0.4f));
        }

        if (!IsTutorial)
        {
            // After the tier record and the ascendancy points: unlocks the quests they open.
            GameEvents.Raise(GameEvents.BossKilled, Tier);
        }
    }

    private void BuildExitPortal(Vector2 at)
    {
        if (_exitPortal is not null)
        {
            return;
        }

        _exitPortal = new Portal { ZIndex = -1 };
        AddChild(_exitPortal);
        _exitPortal.GlobalPosition = at;
        _exitPrompt = UiTheme.Label("[F] Return to the hideout", 16, UiTheme.Gold, HorizontalAlignment.Center);
        _exitPrompt.Size = new Vector2(260f, 22f);
        _exitPrompt.Position = new Vector2(-130f, -78f);
        _exitPrompt.Visible = false;
        _exitPortal.AddChild(_exitPrompt);
    }

    private async void OnPlayerDied(Player who)
    {
        if (Coop)
        {
            OnPartyMemberDied(who);
            return;
        }

        if (Profile.Get(this) is { } profile)
        {
            profile.Active.Deaths++;
        }

        PortalsLeft--;
        Engine.TimeScale = 0.35;
        await ToSignal(GetTree().CreateTimer(0.6, processAlways: true, ignoreTimeScale: true), SceneTreeTimer.SignalName.Timeout);
        Engine.TimeScale = 1.0;

        if (BossFightActive)
        {
            ResetBossFight();
        }

        if (PortalsLeft <= 0)
        {
            Collapsed = true;
            Sfx.Play(this, "defeat");
            EmitSignal(SignalName.Announced, "The reality collapses", "The shard is lost", new Color(1f, 0.3f, 0.3f));
            await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
            Leave();
            return;
        }

        EmitSignal(SignalName.Announced, "You have fallen", $"{PortalsLeft} portal{(PortalsLeft == 1 ? "" : "s")} left", new Color(1f, 0.4f, 0.35f));
        await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        if (!IsInstanceValid(this))
        {
            return;
        }

        Respawn(Party.Players[0], RasterLayout.World(Raster.Entrance));
    }

    private void ResetBossFight()
    {
        BossFightActive = false;
        SetGate(false);
        NetHost?.Gate(false);
        if (Boss is not null && IsInstanceValid(Boss))
        {
            Boss.Dormant = true;
        }
    }

    /// <summary>Test hook: a shard of this tier and an item at <paramref name="owner"/>'s feet, theirs.</summary>
    public void ForceDropForTest(Player owner)
    {
        Player? who = owner.IsLocal ? null : owner;
        DropShard(owner.GlobalPosition + new Vector2(6f, 0f), Tier, who);
        DropItem(owner.GlobalPosition + new Vector2(-30f, 0f), DropSource.RareMonster, 0f, who);
    }

    /// <summary>Test hook: the shared pool set outright (a collapse on the next death).</summary>
    public void SetPortalsForTest(int portals) => PortalsLeft = portals;

    /// <summary>
    /// Co-op host (docs/design/endgame.md -> "Co-op realities", multiplayer.md -> "Deaths and
    /// revives"): one shared pool of portals. Every fall costs
    /// <see cref="PartyRules.RealityPortalsPerFall"/>, and the death that empties the pool
    /// collapses the reality for everyone (the solo rule). Otherwise the player goes down where
    /// they fell: back up alone after 10 s, or sooner with an ally's 5 s revive (CoopHost.Revives).
    /// The boss fight resets once nobody standing is left in it. When the whole party is down
    /// at once, everyone is back at the entrance <see cref="PartyRules.RealityWipeSeconds"/>
    /// later (<see cref="Wipe"/>: the solo death rule, for the party). After the clear a fall
    /// costs nothing and nobody is brought back: they get up as usual.
    /// </summary>
    private void OnPartyMemberDied(Player who)
    {
        if (Collapsed || Party.Ended || Party.PlayerOf(who) is not { } rp)
        {
            return;
        }

        if (who.IsLocal && Profile.Get(this) is { } profile)
        {
            profile.Active.Deaths++;
        }

        string name = CoopHost.NameOf(rp);
        if (Completed)
        {
            NetHost?.ToastAll($"{name} is down — back in {PartyRules.RespawnSeconds:0} s, or hold E next to them to revive", new Color(1f, 0.45f, 0.4f));
            return;
        }

        PortalsLeft -= PartyRules.RealityPortalsPerFall;
        bool fighting = BossFightActive && Party.Players.Any(o => o != rp && !o.Downed && o.Player is { } op && Raster.BossRegion.Contains(Field.CellOf(op.GlobalPosition)));
        if (BossFightActive && !fighting)
        {
            ResetBossFight();
        }

        if (PortalsLeft <= 0)
        {
            Collapse();
            return;
        }

        string left = $"{PortalsLeft} portal{(PortalsLeft == 1 ? "" : "s")} left for the party";
        bool wiped = Party.AllDown;
        string subtitle = wiped ? $"The whole party is down — back at the entrance · {left}" : $"Back up in {PartyRules.RespawnSeconds:0} s, or an ally revives you · {left}";
        if (who.IsLocal)
        {
            EmitSignal(SignalName.Announced, "You have fallen", subtitle, new Color(1f, 0.4f, 0.35f));
        }
        else
        {
            NetHost?.BannerTo(rp.PeerId, "You have fallen", subtitle, new Color(1f, 0.4f, 0.35f));
        }

        NetHost?.ToastAll(wiped ? $"{name} has fallen — the whole party is down · {left}" : $"{name} is down — {left}; hold E next to them to revive", new Color(1f, 0.45f, 0.4f));
        if (wiped)
        {
            Wipe();
        }
    }

    /// <summary>Co-op host: the whole party is down and about to be brought back (revives are held meanwhile).</summary>
    public bool WipePending { get; private set; }

    /// <summary>Wipes seen (tests).</summary>
    public int Wipes { get; private set; }

    /// <summary>
    /// Co-op host: nobody is left standing (each fall already paid its portal), so nobody can
    /// revive anyone: after <see cref="PartyRules.RealityWipeSeconds"/> everyone is back at the
    /// entrance with full life and mana, exactly as a solo player is after a death.
    /// </summary>
    private async void Wipe()
    {
        if (WipePending)
        {
            return;
        }

        WipePending = true;
        Wipes++;
        await ToSignal(GetTree().CreateTimer(PartyRules.RealityWipeSeconds), SceneTreeTimer.SignalName.Timeout);
        WipePending = false;
        if (!IsInstanceValid(this) || Collapsed || Party.Ended)
        {
            return;
        }

        List<RunPlayer> party = Party.Players.ToList();
        for (int i = 0; i < party.Count; i++)
        {
            if (party[i].Downed)
            {
                Respawn(party[i], SpawnSpot(i));
            }
        }
    }

    private void Respawn(RunPlayer rp, Vector2 at)
    {
        if (rp.Player is not { } p)
        {
            return;
        }

        MovePlayer(rp, at);
        p.Health!.Revive();
        p.Health.GrantInvulnerability(2.5f);
        if (p.Stats.Mana is { } mana)
        {
            mana.Current = mana.Max;
        }
    }

    /// <summary>Co-op host: the pool is empty: the reality collapses for everyone.</summary>
    private async void Collapse()
    {
        Collapsed = true;
        Party.Ended = true;
        Sfx.Play(this, "defeat");
        AnnounceAll("The reality collapses", "The party's portals are spent — the shard is lost", new Color(1f, 0.3f, 0.3f));
        NetHost?.End(RealityProtocol.EndCollapsed);
        await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
        if (IsInstanceValid(this))
        {
            Leave();
        }
    }

    /// <summary>Co-op host: a participant went home or dropped. If everyone left is down, that is a wipe.</summary>
    public void OnParticipantLeft()
    {
        if (BossFightActive && !Party.Players.Any(o => !o.Downed))
        {
            ResetBossFight();
        }

        if (!Completed && !Collapsed && !Party.Ended && Party.AllDown)
        {
            Wipe();
        }
    }

    private void Abandon()
    {
        EmitSignal(SignalName.Announced, IsTutorial ? "Introduction skipped" : !Coop ? "Reality abandoned" : IsHostSide ? "Reality closed" : "You left the reality", "", new Color(0.8f, 0.8f, 0.8f));
        Leave();
    }

    private ulong _confirmCloseUntil;

    /// <summary>E at the exit portal. The co-op host with friends still inside asks first
    /// (a second E within a few seconds closes the reality for everyone).</summary>
    private void UseExitPortal()
    {
        if (IsHostSide && Party.Players.Count > 1 && Time.GetTicksMsec() > _confirmCloseUntil)
        {
            _confirmCloseUntil = Time.GetTicksMsec() + 4000;
            EmitSignal(SignalName.Announced, "Your friends are still inside", "Press F again to close the reality for everyone", new Color(1f, 0.75f, 0.4f));
            return;
        }

        Finish();
    }

    private void Finish()
    {
        _leaving = true;
        Sfx.Play(this, "ui");
        Leave();
    }

    private void Leave()
    {
        if (_left)
        {
            return;
        }

        _left = true;
        _leaving = true;
        Player.Stats.RemoveModifiersFromSource(ModifierSource);
        if (IsTutorial && Profile.Get(this) is { } profile)
        {
            // Finished, skipped or collapsed: either way it is never forced again.
            profile.Active.TutorialDone = true;
        }

        Profile.Get(this)?.CaptureFrom(Player);
        if (Session is { Active: true } party)
        {
            // Co-op (docs/design/endgame.md -> "Co-op realities"): home to the party's hideout.
            // The host closes the reality for everyone still inside; a client leaves alone
            // (the host drops its player) and keeps everything it collected.
            if (party.IsHost && !Party.Ended)
            {
                Party.Ended = true;
                NetHost?.End(RealityProtocol.EndHostClosed);
            }

            party.ReturnToHideout();
            Scenes.Go(this, Scenes.Hideout);
            return;
        }

        // The host left a co-op reality: guests go to the main menu, which says why.
        Scenes.Go(this, Session is { Active: false, MenuNotice: not null } ? Scenes.MainMenu : Scenes.Hideout);
    }

    private bool _left;

    // --- co-op client: what the host says --------------------------------------------

    /// <summary>The reality's numbers from a snapshot.</summary>
    public void ApplyShared(int kills, int total, int portals, bool bossFight)
    {
        Kills = kills;
        MonstersTotal = total;
        PortalsLeft = portals;
        if (bossFight != BossFightActive)
        {
            OnRemoteGate(bossFight);
        }
    }

    /// <summary>The replica the host says is the boss: the HUD's bar and the gate.</summary>
    public void AdoptBossReplica(Enemy boss)
    {
        if (boss is BossEnemy b)
        {
            Boss = b;
            if (BossFightActive)
            {
                EmitSignal(SignalName.BossStarted, b);
            }
        }
    }

    public void OnRemoteGate(bool closed)
    {
        if (!IsClientSide || closed == BossFightActive)
        {
            return;
        }

        BossFightActive = closed;
        SetGate(closed);
        if (closed)
        {
            Sfx.Play(this, "boss_warn");
            Sfx.Music(this, "music_boss");
            if (Boss is not null && IsInstanceValid(Boss))
            {
                EmitSignal(SignalName.BossStarted, Boss);
            }
        }
    }

    public void OnRemoteCleared(int tier)
    {
        if (!IsClientSide || Completed)
        {
            return;
        }

        Completed = true;
        BossFightActive = false;
        SetGate(false);
        Sfx.Play(this, "victory");
        Sfx.Music(this, "music_menu");
        if (Profile.Get(this) is { } profile)
        {
            ApplyClear(profile.Active);
        }
    }

    public void OnRemoteExit(Vector2 at)
    {
        if (IsClientSide)
        {
            BuildExitPortal(at);
        }
    }

    /// <summary>The reality is over for everyone (collapsed, or the host closed it): home in 3 s.</summary>
    public async void OnRemoteEnd(byte reason)
    {
        if (!IsClientSide || Party.Ended)
        {
            return;
        }

        Party.Ended = true;
        if (reason == RealityProtocol.EndCollapsed)
        {
            Collapsed = true;
            Sfx.Play(this, "defeat");
        }
        else
        {
            EmitSignal(SignalName.Announced, $"{Session?.HostName ?? "The host"} closed the reality", "Back to the hideout — you keep everything you collected", new Color(1f, 0.75f, 0.4f));
        }

        await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
        if (IsInstanceValid(this))
        {
            Leave();
        }
    }

    /// <summary>The host is gone: this player keeps what it collected, then the main menu says why.</summary>
    public async void OnHostLost(string reason)
    {
        if (Party.Ended)
        {
            return;
        }

        Party.Ended = true;
        EmitSignal(SignalName.Announced, "The host left", reason, new Color(1f, 0.6f, 0.4f));
        await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
        if (IsInstanceValid(this))
        {
            Leave();
        }
    }

    /// <summary>Bars drawn across the boss gate while it is closed.</summary>
    private sealed partial class GateBars : Node2D
    {
        public List<Vector2I> Cells = new();
        public bool Closed;

        public override void _Draw()
        {
            if (!Closed)
            {
                return;
            }

            foreach (Vector2I c in Cells)
            {
                Vector2 p = RasterLayout.World(c) - Vector2.One * RasterLayout.CellPx / 2f;
                DrawRect(new Rect2(p, Vector2.One * RasterLayout.CellPx), new Color(0.6f, 0.15f, 0.1f, 0.35f));
                for (int i = 0; i < 4; i++)
                {
                    float x = p.X + 8f + i * 16f;
                    DrawLine(new Vector2(x, p.Y), new Vector2(x, p.Y + RasterLayout.CellPx), new Color(1f, 0.45f, 0.2f, 0.9f), 3f);
                }
            }
        }
    }
}
