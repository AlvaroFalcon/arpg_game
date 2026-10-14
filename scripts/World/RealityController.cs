#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Combat;
using ArpgGame.Core;
using ArpgGame.Enemies;
using ArpgGame.Items;
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
        // The shard's seed (docs/design/endgame.md -> "Caverns") picks the cavern and
        // shapes it; without one (tests, tools) the map is random.
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
        Theme = RealityTheme.Get(Layout.ThemeId);
        (_quantity, _rarityBonus) = RealityLaunch.Bonuses(Modifiers);
        if (!IsTutorial)
        {
            Jewels = JewelTree.Bonuses(Profile.Get(this)?.Jewels);
            _quantity += Jewels[JewelStat.Quantity];
            _rarityBonus += Jewels[JewelStat.Rarity];
        }
        PortalsLeft = IsTutorial ? TutorialPortals : RealityTiers.Portals(Tier);

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
        ConfigurePlayer();
        Player.UseCloseCamera();
        SpawnPacks();
        SpawnBoss();

        Hud = new RealityHud { Reality = this, Name = "Hud" };
        AddChild(Hud);
        Pause = new PauseMenu { LeaveText = IsTutorial ? "Skip the introduction" : "Abandon reality", OnLeave = Abandon, Summary = Summary };
        AddChild(Pause);
        AddChild(new CharacterPanels { Player = Player, Name = "CharacterPanels" });
        if (IsTutorial)
        {
            AddChild(new TutorialGuide { Reality = this, Name = "TutorialGuide" });
        }

        Enemy.SplitSpawned += OnSplitSpawned;

        Sfx.Music(this, Theme.Music);
        string mods = Modifiers.Count > 0 ? "  ·  " + string.Join(", ", Modifiers.Select(m => RealityModifier.Find(m)?.Text ?? m)) : "";
        CallDeferred(nameof(AnnounceStart), mods);
    }

    private void AnnounceStart(string mods) =>
        EmitSignal(SignalName.Announced, Layout.Name, IsTutorial ? "A short introduction — follow the objectives on the left" : StartSubtitle(mods), IsTutorial ? new Color(1f, 0.85f, 0.4f) : RealityTiers.ShardColor(Tier));

    private string StartSubtitle(string mods) => $"Tier {Tier}  ·  monster level {MonsterLevel}  ·  {PortalsLeft} portal{(PortalsLeft == 1 ? "" : "s")}{mods}";

    /// <summary>Portals in the introduction: dying there costs nothing.</summary>
    public const int TutorialPortals = 99;

    /// <summary>The introduction's monsters and guardian are this much softer.</summary>
    private float TutorialLife => IsTutorial ? 0.6f : 1f;

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
        Player.GlobalPosition = RasterLayout.World(Raster.Entrance);
        // LMB, RMB, 1-4 each fire their own loadout slot (CharacterBuild.Slots).
        Player.GetNode<PlayerSkillInput>("PlayerSkillInput").Mode = PlayerSkillInput.CastMode.Bound;
        Player.GetNodeOrNull<CanvasLayer>("DebugReadout")?.Hide();
        Profile.Get(this)?.ApplyTo(Player);

        if (Modifiers.Contains("frail"))
        {
            Player.Stats.Get(StatId.MaxLife).AddModifier(new StatModifier(ModifierType.More, -15f, ModifierSource));
        }

        if (Modifiers.Contains("exposed"))
        {
            foreach (StatId r in new[] { StatId.FireResistance, StatId.ColdResistance, StatId.LightningResistance })
            {
                Player.Stats.Get(r).AddModifier(new StatModifier(ModifierType.Flat, -20f, ModifierSource));
            }
        }

        if (Player.Stats.Life is { } life)
        {
            life.Current = life.Max;
        }

        Player.Health!.Died += OnPlayerDied;
        Player.Dodge!.Dodged += OnPlayerDodged;

        var camera = Player.GetNode<Camera2D>("Camera2D");
        camera.LimitLeft = 0;
        camera.LimitTop = 0;
        camera.LimitRight = Raster.Width * RasterLayout.CellPx;
        camera.LimitBottom = Raster.Height * RasterLayout.CellPx;
        camera.PositionSmoothingEnabled = true;
        camera.PositionSmoothingSpeed = 9f;
    }

    private void OnPlayerDodged()
    {
        if (!BuildQuery.Any(Player, b => b.PhaseDancer))
        {
            return;
        }

        var tags = SkillTags.Spell | SkillTags.Area | SkillTags.Cold;
        float damage = Stat.ComputeFormula(20f + 3f * MonsterLevel, Player.Stats.GetModifiers(new ModifierQuery(StatId.Damage, tags)));
        var hit = new HitProfile { Damage = damage, Type = DamageType.Cold, Source = Player, Faction = Faction.Player, Tags = tags, KnockbackForce = 220f };
        AreaBurst.Spawn(this, Player.GlobalPosition, 110f * Player.Stats.Get(StatId.AreaOfEffect).Value, 0f, new Color(0.45f, 0.8f, 1f), hit);
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

    private void SpawnPack(PackSpot spot, char kind, bool hordes)
    {
        var pack = new List<Enemy>();
        Vector2 centre = RasterLayout.World(spot.Cell);
        string main = Theme.Roster[_rng.RandiRange(0, Theme.Roster.Count - 1)];
        (int nMin, int nMax) = RealityTiers.NormalPackSize(Tier);
        (int eMin, int eMax) = RealityTiers.RareEscort(Tier);
        int normals = kind switch { 'm' => 0, 'r' => _rng.RandiRange(eMin, eMax), 'u' => 0, _ => IsTutorial ? 3 : _rng.RandiRange(nMin, nMax) };
        if (hordes)
        {
            normals += 2;
        }

        normals = Mathf.RoundToInt(normals * (1f + Jewels[JewelStat.Density]));

        for (int i = 0; i < normals; i++)
        {
            string type = _rng.Randf() < 0.7f ? main : Theme.Roster[_rng.RandiRange(0, Theme.Roster.Count - 1)];
            pack.Add(SpawnMonster(type, centre, MonsterRarity.Normal));
        }

        switch (kind)
        {
            case 'm':
                (int mMin, int mMax) = RealityTiers.MagicPackSize(Tier);
                for (int i = 0; i < (IsTutorial ? 2 : _rng.RandiRange(mMin, mMax)); i++)
                {
                    pack.Add(SpawnMonster(main, centre, MonsterRarity.Magic));
                }

                break;
            case 'r':
                for (int i = 0; i < (IsTutorial ? 1 : RealityTiers.RaresPerPack(Tier)); i++)
                {
                    pack.Add(SpawnMonster(main, centre, MonsterRarity.Rare));
                }

                break;
            case 'u':
                pack.Add(SpawnMonster(Theme.Roster.FirstOrDefault(r => r is "Brute" or "Thrall" or "Skeleton") ?? main, centre, MonsterRarity.Unique));
                for (int i = 0; i < 3; i++)
                {
                    pack.Add(SpawnMonster(main, centre, MonsterRarity.Magic));
                }

                break;
        }

        foreach (Enemy e in pack)
        {
            e.Pack = pack;
            e.SetMeta(RoomMeta, spot.Room);
        }
    }

    private Enemy SpawnMonster(string type, Vector2 centre, MonsterRarity rarity)
    {
        var enemy = LoadEnemy(type).Instantiate<Enemy>();
        enemy.MonsterLevel = MonsterLevel;
        enemy.Dormant = true;
        AddChild(enemy);
        Vector2 offset = new(_rng.RandfRange(-70f, 70f), _rng.RandfRange(-70f, 70f));
        Vector2 at = centre + offset;
        enemy.GlobalPosition = Raster.Walkable(Field.CellOf(at)) ? at : centre;
        float life = RealityTiers.LifeMultiplier(Tier) * TutorialLife * (Modifiers.Contains("hardened") ? 1.4f : 1f);
        float dmg = RealityTiers.DamageMultiplier(Tier) * TutorialLife * (Modifiers.Contains("searing") ? 1.25f : 1f);
        enemy.ApplyTierScaling(life, dmg);
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

        RegisterMonster(enemy);
        return enemy;
    }

    /// <summary>A monster spawned mid-fight (boss adds): scaled to the tier and counted.</summary>
    public void AdoptMonster(Enemy enemy)
    {
        enemy.MonsterLevel = MonsterLevel;
        enemy.ApplyTierScaling(RealityTiers.LifeMultiplier(Tier), RealityTiers.DamageMultiplier(Tier));
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
        float life = RealityTiers.LifeMultiplier(Tier) * Theme.BossLifeMultiplier * TutorialLife * (Modifiers.Contains("hardened") ? 1.4f : 1f);
        float dmg = RealityTiers.DamageMultiplier(Tier) * TutorialLife * (Modifiers.Contains("searing") ? 1.25f : 1f);
        boss.ApplyTierScaling(life, dmg);
        boss.Killed += OnBossKilled;
        Boss = boss;
    }

    // --- loop ------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (!Completed && !Collapsed)
        {
            Elapsed += dt;
        }

        _fieldTimer -= dt;
        if (_fieldTimer <= 0f)
        {
            _fieldTimer = 0.25f;
            Field.Update(Player.GlobalPosition);
            Reveal();
        }

        if (!BossFightActive && Boss is not null && IsInstanceValid(Boss) && !Boss.Health.IsDead && !(Player.Health?.IsDead ?? true)
            && Raster.BossRegion.Contains(Field.CellOf(Player.GlobalPosition)))
        {
            StartBossFight();
        }

    }

    public override void _Process(double delta)
    {
        // The exit portal is used deliberately (E), never by walking over it:
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

        if (near && !GetTree().Paused && Input.IsActionJustPressed("interact"))
        {
            Finish();
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

    private void StartBossFight()
    {
        BossFightActive = true;
        SetGate(true);
        Boss!.Wake();
        Sfx.Play(this, "boss_warn");
        Sfx.Music(this, "music_boss");
        EmitSignal(SignalName.Announced, Boss.DisplayName, "The way back is sealed", new Color(1f, 0.35f, 0.25f));
        EmitSignal(SignalName.BossStarted, Boss);
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
        bool byPlayer = enemy.Health.LastHitSource == Player;
        Sfx.Play(this, "death", -6f);
        if (byPlayer)
        {
            CharacterBuild.Of(Player)?.GrantExperience(RunState.CharacterExperienceFor(enemy) * (1f + Jewels[JewelStat.Experience]));
            if (Profile.Get(this) is { } profile)
            {
                profile.Active.TotalKills++;
            }

            float lifeOnKill = Player.Stats.Get(StatId.LifeOnKill).Value;
            if (lifeOnKill > 0f)
            {
                Player.Health?.Heal(lifeOnKill);
            }

            if (BuildQuery.Any(Player, b => b.ChainReaction))
            {
                var hit = new HitProfile { Damage = (enemy.Stats.Life?.Max ?? 0f) * 0.2f, Type = DamageType.Fire, Source = Player, Faction = Faction.Player, Tags = SkillTags.Fire | SkillTags.Area, KnockbackForce = 80f };
                AreaBurst.Spawn(this, enemy.GlobalPosition, 70f * enemy.Scale.X, 0f, new Color(1f, 0.5f, 0.15f), hit);
            }
        }

        DropLoot(enemy);
    }

    private void DropLoot(Enemy enemy)
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
                DropItem(at + Jitter(30f), enemy.Rarity >= MonsterRarity.Rare ? 0.35f : 0.1f);
            }
        }

        if (_rng.Randf() < gem * q)
        {
            DropGem(at + Jitter(20f));
        }

        if (_rng.Randf() < shard * q)
        {
            DropShard(at + Jitter(20f), Tier);
        }

        if (_rng.Randf() < gold * q)
        {
            Pickup.Spawn(this, PickupKind.Gold, Mathf.Round((2 + Tier) * (enemy.Rarity >= MonsterRarity.Rare ? 6 : 1) * (1f + Jewels[JewelStat.Gold])), at + Jitter(12f));
        }

        // Crafting orbs (docs/design/crafting.md -> "Where orbs come from").
        float craft = enemy.Rarity switch { MonsterRarity.Magic => 0.1f, MonsterRarity.Rare => 0.45f, MonsterRarity.Unique => 1.2f, _ => 0.035f };
        for (float roll = craft * q * (1f + Jewels[JewelStat.Orbs]); roll > 0f; roll -= 1f)
        {
            if (_rng.Randf() < roll)
            {
                DropCurrency(at + Jitter(24f));
            }
        }

        // Flask refill orbs (docs/design/items.md -> "Flasks and refill orbs").
        float orb = enemy.Rarity switch { MonsterRarity.Normal => 0.05f, MonsterRarity.Magic => 0.15f, _ => 0.6f };
        if (_rng.Randf() < orb)
        {
            Pickup.Spawn(this, PickupKind.Life, 0.2f, at + Jitter(10f));
        }
    }

    private Vector2 Jitter(float r) => new(_rng.RandfRange(-r, r), _rng.RandfRange(-r, r));

    public EquipmentItem? DropItem(Vector2 at, float quality)
    {
        IReadOnlyList<BaseItemDefinition> bases = ItemFactory.Bases;
        if (bases.Count == 0)
        {
            return null;
        }

        EquipmentItem item = ItemFactory.NewBlank();
        AddChild(item);
        item.GlobalPosition = at;
        float roll = _rng.Randf() + (quality + _rarityBonus) * 0.6f;
        Rarity rarity = roll > 1.05f ? Rarity.Rare : roll > 0.5f ? Rarity.Magic : Rarity.Common;
        item.Initialize(bases[_rng.RandiRange(0, bases.Count - 1)], rarity, Mathf.Clamp(MonsterLevel, 1, 100), ItemFactory.Affixes.Entries, _rng);
        return item;
    }

    public void DropGem(Vector2 at)
    {
        bool support = _rng.Randf() < 0.6f;
        string id = support
            ? UpgradeCatalog.Supports[_rng.RandiRange(0, UpgradeCatalog.Supports.Count - 1)].Id
            : UpgradeCatalog.Skills[_rng.RandiRange(0, UpgradeCatalog.Skills.Count - 1)].Id;
        Pickup gem = Pickup.Spawn(this, PickupKind.Gem, 1f, at);
        gem.GemId = id;
        gem.GemSupport = support;
        gem.QueueRedraw();
    }

    public void DropCurrency(Vector2 at)
    {
        Pickup orb = Pickup.Spawn(this, PickupKind.Currency, 1f, at);
        orb.CurrencyKey = Crafting.RollOrb(_rng).Id;
        orb.QueueRedraw();
    }

    private int JewelsOnFloor() => GetTree().GetNodesInGroup(RunState.PickupGroup).OfType<Pickup>().Count(p => p.Kind == PickupKind.Jewel);

    public void DropShard(Vector2 at, int tier) => Pickup.Spawn(this, PickupKind.Shard, Mathf.Clamp(tier, 1, RealityTiers.MaxTier), at);

    public void OnPickup(Pickup pickup, Player player)
    {
        CharacterData? c = Profile.Get(this)?.Active;
        switch (pickup.Kind)
        {
            case PickupKind.Life:
                Sfx.Play(this, "pickup");
                Flasks.CollectOrb(player, pickup.Value);
                break;
            case PickupKind.Gold:
                Sfx.Play(this, "pickup");
                player.Wallet.Add(Profile.GoldId, (long)pickup.Value);
                break;
            case PickupKind.Shard when c is not null:
                int tier = Mathf.RoundToInt(pickup.Value);
                c.AddShards(tier);
                Sfx.Play(this, "levelup", -4f);
                EmitSignal(SignalName.Toast, $"Mirror Shard (tier {tier})", RealityTiers.ShardColor(tier));
                break;
            case PickupKind.Jewel when Profile.Get(this) is { } account:
                account.Jewels.Found++;
                account.Save();
                Sfx.Play(this, "levelup", -2f);
                EmitSignal(SignalName.Toast, $"Mirror Jewel ({account.Jewels.Found}/{JewelTree.Sockets}) — socket it at the Mirror", new Color(0.85f, 0.6f, 1f));
                break;
            case PickupKind.Currency when Crafting.Find(pickup.CurrencyKey) is { } currency:
                Sfx.Play(this, "pickup", -2f);
                player.Wallet.Add(currency.Id, Mathf.Max(1, Mathf.RoundToInt(pickup.Value)));
                EmitSignal(SignalName.Toast, currency.Name, currency.Color);
                break;
            case PickupKind.Gem when c is not null:
                Sfx.Play(this, "levelup", -6f);
                (string text, long gold) = GemLoot.Collect(c, pickup.GemId, pickup.GemSupport);
                if (gold > 0)
                {
                    player.Wallet.Add(Profile.GoldId, gold);
                }

                EmitSignal(SignalName.Toast, text, pickup.GemSupport ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.6f, 0.35f));
                break;
        }
    }

    // --- boss, death, exit ---------------------------------------------------------

    private void OnBossKilled(Enemy boss)
    {
        Completed = true;
        BossFightActive = false;
        SetGate(false);
        Vector2 at = boss.GlobalPosition;
        int items = 3 + (_quantity > 0.3f ? 1 : 0) + Mathf.RoundToInt(Jewels[JewelStat.BossItems]);
        for (int i = 0; i < items; i++)
        {
            DropItem(at + Jitter(60f), 0.6f + Jewels[JewelStat.BossRarity]);
        }

        // A boss always drops a shard (endgame.md -> "Mirror Shards"); jewels can
        // lift its tier and add a second one.
        int shard = RealityTiers.RollBossShard(Tier, _rng);
        if (_rng.Randf() < Jewels[JewelStat.ShardUpgrade])
        {
            shard = Mathf.Min(RealityTiers.MaxTier, shard + 1);
        }

        DropShard(at + Jitter(30f), shard);
        if (_rng.Randf() < RealityTiers.BonusShardChance * (1f + _quantity) + Jewels[JewelStat.ShardChance])
        {
            DropShard(at + Jitter(30f), Tier);
        }

        // Mirror jewels (endgame.md -> "Mirror jewels").
        if (!IsTutorial && Profile.Get(this) is { } jewelProfile
            && _rng.Randf() < JewelTree.DropChance(jewelProfile.Jewels.Found + JewelsOnFloor(), Tier))
        {
            Pickup.Spawn(this, PickupKind.Jewel, 1f, at + Jitter(30f));
        }

        if (_rng.Randf() < 0.7f)
        {
            DropGem(at + Jitter(40f));
        }

        Pickup.Spawn(this, PickupKind.Gold, Mathf.Round((10 + Tier * 4) * 5 * (1f + Jewels[JewelStat.Gold] + Jewels[JewelStat.BossGold])), at + Jitter(30f));
        for (int i = 0; i < 2 + Tier / 4; i++)
        {
            DropCurrency(at + Jitter(50f));
        }

        if (Profile.Get(this) is { } profile)
        {
            CharacterData c = profile.Active;
            c.RealitiesCleared++;
            c.HighestTierCleared = Mathf.Max(c.HighestTierCleared, Tier);
            if (Tier >= AscendancyCatalog.Tier1Unlock && c.AscendancyPoints < AscendancyCatalog.FirstPoints)
            {
                c.AscendancyPoints = AscendancyCatalog.FirstPoints;
                EmitSignal(SignalName.Toast, "Ascendancy trial complete! Visit the altar in your hideout.", new Color(1f, 0.85f, 0.4f));
            }

            if (Tier >= AscendancyCatalog.Tier2Unlock && c.AscendancyPoints < AscendancyCatalog.FirstPoints + AscendancyCatalog.SecondPoints)
            {
                c.AscendancyPoints = AscendancyCatalog.FirstPoints + AscendancyCatalog.SecondPoints;
                EmitSignal(SignalName.Toast, "Second ascendancy trial complete: +2 ascendancy points.", new Color(1f, 0.85f, 0.4f));
            }
        }

        _exitPortal = new Portal { ZIndex = -1 };
        AddChild(_exitPortal);
        _exitPortal.GlobalPosition = at + new Vector2(0f, 90f);
        _exitPrompt = UiTheme.Label("[E] Return to the hideout", 16, UiTheme.Gold, HorizontalAlignment.Center);
        _exitPrompt.Size = new Vector2(260f, 22f);
        _exitPrompt.Position = new Vector2(-130f, -78f);
        _exitPrompt.Visible = false;
        _exitPortal.AddChild(_exitPrompt);
        Sfx.Play(this, "victory");
        Sfx.Music(this, "music_menu");
        EmitSignal(SignalName.Announced, IsTutorial ? "Guardian defeated" : "Reality stabilised", "Collect your loot — press E at the portal to go home", new Color(1f, 0.85f, 0.4f));
    }

    private async void OnPlayerDied()
    {
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
            BossFightActive = false;
            SetGate(false);
            if (Boss is not null && IsInstanceValid(Boss))
            {
                Boss.Dormant = true;
            }
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

        Player.GlobalPosition = RasterLayout.World(Raster.Entrance);
        Player.Health!.Revive();
        Player.Health.GrantInvulnerability(2.5f);
        if (Player.Stats.Mana is { } mana)
        {
            mana.Current = mana.Max;
        }
    }

    private void Abandon()
    {
        EmitSignal(SignalName.Announced, IsTutorial ? "Introduction skipped" : "Reality abandoned", "", new Color(0.8f, 0.8f, 0.8f));
        Leave();
    }

    private void Finish()
    {
        _leaving = true;
        Sfx.Play(this, "ui");
        Leave();
    }

    private void Leave()
    {
        _leaving = true;
        Player.Stats.RemoveModifiersFromSource(ModifierSource);
        if (IsTutorial && Profile.Get(this) is { } profile)
        {
            // Finished, skipped or collapsed: either way it is never forced again.
            profile.Active.TutorialDone = true;
        }

        Profile.Get(this)?.CaptureFrom(Player);
        Scenes.Go(this, Scenes.Hideout);
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
