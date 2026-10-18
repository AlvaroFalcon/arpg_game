#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Enemies;
using ArpgGame.Progression;
using ArpgGame.Stats;

namespace ArpgGame.Run;

/// <summary>
/// One player's share of a run (docs/design/multiplayer.md -> "Players"): their
/// level, experience, queued level-ups and what they collected. Single-player
/// runs have exactly one; a co-op party has one per member.
/// </summary>
public sealed class RunPlayer
{
    public RunPlayer(Player? player, long peerId = 0)
    {
        Player = player;
        PeerId = peerId;
    }

    /// <summary>Null only for the placeholder a bare <see cref="RunState"/> uses (tests).</summary>
    public Player? Player { get; }

    public long PeerId { get; }

    public RunBuild? Build { get; set; }

    public bool IsLocal => Player?.IsLocal ?? true;

    public int Level { get; set; } = 1;
    public float Experience { get; set; }
    public int PendingLevelUps { get; set; }

    public long GoldCollected { get; set; }
    public int OrbsCollected { get; set; }

    /// <summary>What this player's camera shows, in pixels (a remote player's is
    /// reported by their client; edge spawns and the Cataclysm use it).</summary>
    public Vector2 ViewSize { get; set; } = new(1280f, 720f);

    /// <summary>Co-op host: character experience earned but not yet sent to this player's client.</summary>
    public float PendingCharacterExperience { get; set; }

    /// <summary>Co-op host: level-ups not yet reported to this player's client.</summary>
    public int UnreportedLevelUps { get; set; }

    /// <summary>Instanced experience gems on the floor (the gem cap is per player).</summary>
    internal readonly List<Pickup> Gems = new();

    public bool Downed => Player?.Health is { IsDead: true };

    /// <summary>Co-op, host: seconds this player has been down, and an ally's revive progress (0–1).</summary>
    public float DownedFor { get; set; }
    public float ReviveHelp { get; set; }
}

/// <summary>
/// The bookkeeping of one run — docs/design/run.md: elapsed time, kills,
/// experience and level, and the outcome. Listens to enemy deaths (spawning
/// experience, applying on-kill effects) and pickups. Level-ups are queued and
/// handed to whoever listens to <see cref="LevelUpReady"/> (the level-up
/// screen), which pauses the run while the player chooses.
///
/// Co-op (docs/design/multiplayer.md): kills, time, bosses, experience and level
/// are the party's (one bar, <see cref="PartyRules.PartyExperience"/>); loot is per
/// <see cref="RunPlayer"/>. Every drop is
/// rolled for every player (instanced loot) and owned by them. The properties
/// without a player (<see cref="Level"/>, <see cref="Experience"/>...) are the
/// local player's, so single-player code reads exactly as before.
/// </summary>
[GlobalClass]
public partial class RunState : Node, IPickupHandler
{
    public const string Group = "run_state";
    public const string PickupGroup = "pickups";

    /// <summary>Above this many gems, new experience merges into existing gems
    /// instead of spawning more (keeps late-run floors readable and cheap).
    /// Co-op: per player.</summary>
    public const int GemCap = 300;

    [Signal]
    public delegate void LevelUpReadyEventHandler(int level);

    [Signal]
    public delegate void RunEndedEventHandler(bool victory);

    /// <summary>A short line for the HUD (orb picked up, power-up used).</summary>
    [Signal]
    public delegate void ToastEventHandler(string text, Color color);

    /// <summary>Drop chances of the Rift power-ups per ordinary kill (elites: ×40).
    /// About one Cataclysm every two minutes of steady killing, a Lodestone a little
    /// more often; never more than one of each on the floor.</summary>
    public const float NukeChance = 0.0009f;
    public const float MagnetChance = 0.0013f;

    /// <summary>Crafting-orb chance per ordinary kill; elites 25%, bosses drop 3.</summary>
    public const float OrbChance = 0.004f;

    // --- co-op hooks (Net.RiftHost listens; nothing listens offline) ---------------

    /// <summary>A drop was put on the floor (owner set).</summary>
    public event System.Action<Pickup>? PickupSpawned;

    /// <summary>A drop was collected: pickup, by whom, and a detail (Cataclysm kills).</summary>
    public event System.Action<Pickup, Player, int>? PickupCollected;

    /// <summary>A Lodestone or a boss pulled drops in (null = everyone's).</summary>
    public event System.Action<Player?>? Magnetized;

    /// <summary>Gear rolled for a remote player: the host sends it, then frees the node.</summary>
    public event System.Action<Player, Items.EquipmentItem, Vector2>? RemoteItemDropped;

    /// <summary>A player gained levels (any player; the local one also gets <see cref="LevelUpReady"/>).</summary>
    public event System.Action<RunPlayer, int>? Levelled;

    /// <summary>Co-op: drops are owned (instanced), the gem cap is per player.</summary>
    public bool Coop { get; set; }

    /// <summary>Co-op client: this state mirrors the host's numbers (no kills are
    /// counted here; the clock is corrected by snapshots).</summary>
    public bool Mirror { get; set; }

    /// <summary>False holds the clock (co-op host waiting for everyone to load).</summary>
    public bool ClockRunning { get; set; } = true;

    private readonly List<RunPlayer> _players = new();
    private readonly RunPlayer _placeholder = new(null);

    public IReadOnlyList<RunPlayer> Players => _players;

    /// <summary>The player on this machine (or the placeholder in a bare state).</summary>
    public RunPlayer Local
    {
        get
        {
            foreach (RunPlayer rp in _players)
            {
                if (rp.IsLocal)
                {
                    return rp;
                }
            }

            return _players.Count > 0 ? _players[0] : _placeholder;
        }
    }

    public int Level => Local.Level;
    public float Experience => Local.Experience;
    public float Elapsed { get; private set; }
    public int Kills { get; private set; }
    public int ElitesKilled { get; private set; }
    public int BossesKilled { get; private set; }
    public long GoldCollected => Local.GoldCollected;
    public bool Ended { get; private set; }
    public bool Victory { get; private set; }

    /// <summary>The run ended because the player (or, in co-op, everyone) fell, as
    /// opposed to leaving. After the Colossus a fall is still a victory.</summary>
    public bool Fell { get; private set; }

    public int OrbsCollected => Local.OrbsCollected;
    public int NukesUsed { get; private set; }

    /// <summary>Level-ups earned but not yet chosen (local player).</summary>
    public int PendingLevelUps => Local.PendingLevelUps;

    /// <summary>The local player. Setting it adds (or replaces) the local entry.</summary>
    public Player? Player
    {
        get => Local.Player;
        set
        {
            if (value is null)
            {
                return;
            }

            RunPlayer? local = _players.Find(rp => rp.IsLocal);
            if (local is not null)
            {
                _players.Remove(local);
            }

            var entry = new RunPlayer(value, value.PeerId) { Build = local?.Build ?? _placeholder.Build };
            _players.Insert(0, entry);
        }
    }

    public RunBuild? Build
    {
        get => Local.Build;
        set => Local.Build = value;
    }

    /// <summary>Adds a party member (co-op).</summary>
    public RunPlayer AddPlayer(Player player, RunBuild? build)
    {
        var entry = new RunPlayer(player, player.PeerId) { Build = build, Level = Local.Level, Experience = Local.Experience };
        _players.Add(entry);
        return entry;
    }

    public void RemovePlayer(RunPlayer player) => _players.Remove(player);

    public RunPlayer? PlayerOf(Node? node)
    {
        if (node is null)
        {
            return null;
        }

        foreach (RunPlayer rp in _players)
        {
            if (rp.Player == node)
            {
                return rp;
            }
        }

        return null;
    }

    public RunPlayer? PlayerByPeer(long peer) => _players.Find(rp => rp.PeerId == peer);

    /// <summary>True when every party member is down (co-op: the run is lost).</summary>
    public bool AllDown
    {
        get
        {
            foreach (RunPlayer rp in _players)
            {
                if (!rp.Downed)
                {
                    return false;
                }
            }

            return _players.Count > 0;
        }
    }

    /// <summary>Party size for the scaling table (Net.PartyRules).</summary>
    public int PartySize => Mathf.Max(1, _players.Count);

    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        AddToGroup(Group);
        AddToGroup(Pickup.HandlerGroup);
        _rng.Randomize();
    }

    /// <summary>Starts the clock somewhere other than zero (debug/capture runs).</summary>
    public void SkipTo(float seconds) => Elapsed = seconds;

    /// <summary>Kill drops waiting to spawn (a few per frame, see <see cref="Items.LootQueue"/>).
    /// Power-ups (Cataclysm, Lodestone) still spawn on the kill frame.</summary>
    public Items.LootQueue Loot { get; } = new();

    public override void _PhysicsProcess(double delta)
    {
        Loot.Pump();
        if (!Ended && ClockRunning)
        {
            Elapsed += (float)delta;
        }
    }

    /// <summary>docs/design/run.md -> "Experience curve": 5 at level 1, then a
    /// gentle quadratic — about twenty levels in a full run.</summary>
    public static float ExperienceToNext(int level)
    {
        int l = level - 1;
        return 5f + 3f * l + 0.55f * l * l;
    }

    /// <summary>Experience to go from <paramref name="level"/> to the next: the solo
    /// curve, times <see cref="Net.PartyRules.PartyExperience"/> for a co-op party's shared bar.</summary>
    public float ExperienceNeeded(int level) =>
        ExperienceToNext(level) * (Coop ? Net.PartyRules.PartyExperience(PartySize) : 1f);

    public float ExperienceFraction => Mathf.Clamp(Local.Experience / ExperienceNeeded(Local.Level), 0f, 1f);

    // --- co-op: one party bar (docs/design/multiplayer.md -> "Level-ups") ------------

    /// <summary>Co-op: the party's level (every <see cref="RunPlayer.Level"/> mirrors it).</summary>
    public int PartyLevel { get; private set; } = 1;

    /// <summary>Co-op: experience in the party's bar towards the next level.</summary>
    public float PartyExperience { get; private set; }

    /// <summary>Co-op: party levels gained and not yet chosen. Each one is a round of
    /// picks for everyone (RiftHost); every <see cref="RunPlayer.PendingLevelUps"/> mirrors it.</summary>
    public int PartyPendingLevelUps { get; private set; }

    public void AddExperience(float amount) => AddExperience(Local, amount);

    public void AddExperience(RunPlayer who, float amount)
    {
        if (Ended)
        {
            return;
        }

        float gain = who.Player?.Stats.Get(StatId.ExperienceGain).Value ?? 1f;
        if (Coop)
        {
            // Whoever collects a gem fills the party's bar (with their own experience gain).
            PartyExperience += amount * gain;
            CheckPartyLevel();
            return;
        }

        who.Experience += amount * gain;
        int gained = 0;
        while (who.Experience >= ExperienceNeeded(who.Level))
        {
            who.Experience -= ExperienceNeeded(who.Level);
            who.Level++;
            who.PendingLevelUps++;
            gained++;
            if (who.IsLocal)
            {
                Core.Sfx.Play(this, "levelup");
            }
        }

        if (gained > 0)
        {
            Levelled?.Invoke(who, gained);
        }

        if (who.IsLocal && who.PendingLevelUps > 0)
        {
            EmitSignal(SignalName.LevelUpReady, who.Level - who.PendingLevelUps + 1);
        }
    }

    /// <summary>
    /// Co-op host: levels the party while its bar is full and mirrors level,
    /// experience and pending picks into every player. Also called when the party
    /// shrinks (a smaller party needs less per level).
    /// </summary>
    public void CheckPartyLevel()
    {
        if (!Coop || Mirror || Ended)
        {
            return;
        }

        int gained = 0;
        while (PartyExperience >= ExperienceNeeded(PartyLevel) && gained < 100)
        {
            PartyExperience -= ExperienceNeeded(PartyLevel);
            PartyLevel++;
            gained++;
        }

        PartyPendingLevelUps += gained;
        MirrorParty();
        if (gained == 0)
        {
            return;
        }

        Core.Sfx.Play(this, "levelup");
        foreach (RunPlayer rp in _players.ToArray())
        {
            Levelled?.Invoke(rp, gained);
        }
    }

    /// <summary>Co-op host: one round of picks is over (everyone chose, or the timer did).</summary>
    public void ConsumePartyLevelUp()
    {
        if (PartyPendingLevelUps > 0)
        {
            PartyPendingLevelUps--;
        }

        MirrorParty();
    }

    private void MirrorParty()
    {
        foreach (RunPlayer rp in _players)
        {
            rp.Level = PartyLevel;
            rp.Experience = PartyExperience;
            rp.PendingLevelUps = PartyPendingLevelUps;
        }
    }

    /// <summary>Raises the level without queueing a choice (auto-levelled
    /// preview runs apply their picks directly).</summary>
    public void AddLevelSilently() => Local.Level++;

    /// <summary>Called by the level-up screen after a choice is applied.</summary>
    public void ConsumeLevelUp() => ConsumeLevelUp(Local);

    public void ConsumeLevelUp(RunPlayer who)
    {
        if (who.PendingLevelUps > 0)
        {
            who.PendingLevelUps--;
        }
    }

    // --- co-op client mirror ------------------------------------------------------

    /// <summary>Co-op client: the party's numbers from a snapshot. The clock is
    /// only pulled in when it drifts, so the HUD timer stays smooth.</summary>
    public void ApplyShared(float elapsed, int kills, int elites, int bosses)
    {
        if (Mathf.Abs(Elapsed - elapsed) > 0.25f)
        {
            Elapsed = elapsed;
        }

        Kills = kills;
        ElitesKilled = elites;
        BossesKilled = bosses;
    }

    /// <summary>Co-op client: the host says where the local player's level stands;
    /// new level-ups are queued for the cards like offline.</summary>
    public void ApplyLocalProgress(int level, float experience, int newLevelUps)
    {
        RunPlayer me = Local;
        me.Level = level;
        me.Experience = experience;
        if (newLevelUps > 0 && Coop)
        {
            // The party's level: the host opens the rounds of picks (RiftClient -> PartyRound).
            Core.Sfx.Play(this, "levelup");
        }
        else if (newLevelUps > 0)
        {
            me.PendingLevelUps += newLevelUps;
            Core.Sfx.Play(this, "levelup");
            EmitSignal(SignalName.LevelUpReady, me.Level - me.PendingLevelUps + 1);
        }
    }

    /// <summary>Co-op client: something the host credited to the local player.</summary>
    public void ApplyLocalCollected(PickupKind kind, long amount)
    {
        if (kind == PickupKind.Gold)
        {
            Local.GoldCollected += amount;
        }
        else if (kind == PickupKind.Currency)
        {
            Local.OrbsCollected++;
        }
        else if (kind == PickupKind.Nuke)
        {
            NukesUsed++;
        }
    }

    public void ShowToast(string text, Color color) => EmitSignal(SignalName.Toast, text, color);

    // --- kills and drops -------------------------------------------------------------

    public void RegisterEnemy(Enemy enemy) => enemy.Killed += OnEnemyKilled;

    private void OnEnemyKilled(Enemy enemy)
    {
        if (Ended || Mirror)
        {
            return;
        }

        RunPlayer? killer = PlayerOf(enemy.Health.LastHitSource);
        Kills++;
        if (enemy.IsElite)
        {
            ElitesKilled++;
        }

        if (enemy.IsBoss)
        {
            BossesKilled++;
        }

        Core.Sfx.Play(this, "death", -6f);
        float share = Coop ? Net.PartyRules.Share(PartySize) : 1f;
        if (killer is not null)
        {
            // Offline: the killer (the only player). Co-op: the whole party shares it.
            float characterXp = CharacterExperienceFor(enemy) * share;
            foreach (RunPlayer rp in _players)
            {
                if (!Coop && rp != killer)
                {
                    continue;
                }

                Characters.CharacterBuild.Of(rp.Player)?.GrantExperience(characterXp);
                if (!rp.IsLocal)
                {
                    rp.PendingCharacterExperience += characterXp;
                }
            }
        }

        Vector2 at = enemy.GlobalPosition;
        if (_players.Count == 0)
        {
            RollDropsFor(null, enemy, at, 1f);
        }
        else
        {
            foreach (RunPlayer rp in _players)
            {
                RollDropsFor(rp, enemy, at, share);
            }
        }

        // Power-ups (docs/design/run.md -> "Pickups"). A Cataclysm's own
        // victims never drop another one. Co-op: rolled once per kill, for the killer.
        if (!_nuking)
        {
            Player? owner = Coop ? killer?.Player ?? Local.Player : null;
            float boost = enemy.IsBoss ? 0f : enemy.IsElite ? 40f : 1f;
            if (_rng.Randf() < NukeChance * boost && !OnFloor(PickupKind.Nuke))
            {
                Spawned(Pickup.Spawn(GetTree().CurrentScene, PickupKind.Nuke, 1f, at + RandomOffset(14f)), owner);
            }
            else if (_rng.Randf() < MagnetChance * boost && !OnFloor(PickupKind.Magnet))
            {
                Spawned(Pickup.Spawn(GetTree().CurrentScene, PickupKind.Magnet, 1f, at + RandomOffset(14f)), owner);
            }
        }

        if (killer?.Player is { } player)
        {
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

            if (Progression.BuildQuery.Any(player, b => b.ChainReaction))
            {
                float maxLife = enemy.Stats.Life?.Max ?? 0f;
                var hit = new HitProfile
                {
                    Damage = maxLife * 0.2f,
                    Type = DamageType.Fire,
                    Source = player,
                    Faction = Faction.Player,
                    Tags = SkillTags.Fire | SkillTags.Area,
                    KnockbackForce = 80f,
                };
                AreaBurst.SpawnOnKill(GetTree().CurrentScene, at, 70f * enemy.Scale.X, new Color(1f, 0.5f, 0.15f), hit);
            }
        }
    }

    /// <summary>
    /// One player's drops from one kill: experience, a life orb or gold, orbs and
    /// gear. Rolled now, spawned a few per frame (<see cref="Loot"/>). Co-op: owned
    /// by that player; ordinary monsters' chances are scaled by the party share.
    /// </summary>
    private void RollDropsFor(RunPlayer? who, Enemy enemy, Vector2 at, float share)
    {
        Player? owner = Coop ? who?.Player : null;
        Node scene = GetTree().CurrentScene;
        if (enemy.ExperienceValue > 0)
        {
            float value = enemy.ExperienceValue * share;
            Loot.Add(Items.LootQueue.PickupCost, () => SpawnExperience(at, value, owner));
        }

        float roll = _rng.Randf();
        if (roll < (enemy.IsElite ? 0.5f : 0.012f * share))
        {
            Vector2 p = at + RandomOffset(10f);
            Loot.Add(Items.LootQueue.PickupCost, () => Spawned(Pickup.Spawn(scene, PickupKind.Life, 0.25f, p), owner));
        }
        else if (roll < (enemy.IsElite ? 1f : 0.05f * share))
        {
            Vector2 p = at + RandomOffset(10f);
            float gold = enemy.IsElite ? 25 : 3;
            Loot.Add(Items.LootQueue.PickupCost, () => Spawned(Pickup.Spawn(scene, PickupKind.Gold, gold, p), owner));
        }

        int orbs = enemy.IsBoss ? 3 : _rng.Randf() < (enemy.IsElite ? 0.25f : OrbChance * share) ? 1 : 0;
        for (int i = 0; i < orbs; i++)
        {
            Vector2 p = at + RandomOffset(24f);
            Loot.Add(Items.LootQueue.PickupCost, () => DropOrb(p, owner));
        }

        // Gear drops (docs/design/run.md -> "Rewards"): elites sometimes, bosses always.
        int drops = enemy.IsBoss ? 2 : enemy.IsElite && _rng.Randf() < 0.3f ? 1 : 0;
        for (int i = 0; i < drops; i++)
        {
            Vector2 p = at + RandomOffset(30f);
            Items.DropSource source = enemy.IsBoss ? Items.DropSource.RiftBoss : Items.DropSource.RiftElite;
            Loot.Add(Items.LootQueue.ItemCost, () => DropItem(p, source, owner));
        }
    }

    /// <summary>Marks a fresh drop as <paramref name="owner"/>'s and tells the co-op layer.</summary>
    private Pickup Spawned(Pickup pickup, Player? owner)
    {
        pickup.OwnerPlayer = owner;
        PickupSpawned?.Invoke(pickup);
        return pickup;
    }

    /// <summary>Puts a rolled item on the ground through the normal drop path
    /// (label, filter, click-to-pick-up). Co-op: a remote player's item is sent
    /// to their client instead (it lands in their own game, never in the host's).</summary>
    public void DropItem(Vector2 at, Items.DropSource source, Player? owner = null)
    {
        int itemLevel = Mathf.Clamp(3 + Mathf.RoundToInt(Elapsed / 60f * 5f) + BossesKilled * 8, 1, 100);
        var bases = Items.ItemFactory.Bases;
        if (bases.Count == 0)
        {
            return;
        }

        Items.EquipmentItem item = Items.ItemFactory.NewBlank();
        bool remote = owner is { IsLocal: false };
        if (!remote)
        {
            GetTree().CurrentScene.AddChild(item);
            item.GlobalPosition = at;
        }

        // Cascade rarity (docs/design/items.md -> "Drop tables (cascade)"), better the longer the run.
        Items.Rarity rarity = Items.DropTables.RollRarity(source, Items.DropTables.RiftMultiplier(Elapsed), _rng);
        item.Initialize(bases[_rng.RandiRange(0, bases.Count - 1)], rarity, itemLevel, Items.ItemFactory.Affixes.Entries, _rng);
        if (remote)
        {
            RemoteItemDropped?.Invoke(owner!, item, at);
            item.QueueFree();
        }
    }

    public void DropOrb(Vector2 at, Player? owner = null)
    {
        Pickup orb = Pickup.Spawn(GetTree().CurrentScene, PickupKind.Currency, 1f, at);
        orb.CurrencyKey = Items.DropTables.RollOrb(_rng, Items.DropTables.RiftMultiplier(Elapsed)).Id;
        orb.QueueRedraw();
        Spawned(orb, owner);
    }

    private bool OnFloor(PickupKind kind)
    {
        foreach (Node n in GetTree().GetNodesInGroup(PickupGroup))
        {
            if (n is Pickup p && p.Kind == kind && !p.IsQueuedForDeletion())
            {
                return true;
            }
        }

        return false;
    }

    private bool _nuking;

    /// <summary>What <paramref name="player"/>'s camera shows, in world space: the
    /// real camera for the local player, a rectangle of their reported screen
    /// size around a remote one.</summary>
    public Rect2 ViewOf(Player player)
    {
        if (player.IsLocal || !player.IsInsideTree())
        {
            Viewport viewport = player.GetViewport();
            return viewport.CanvasTransform.AffineInverse() * viewport.GetVisibleRect();
        }

        Vector2 size = PlayerOf(player)?.ViewSize ?? new Vector2(1280f, 720f);
        return new Rect2(player.GlobalPosition - size / 2f, size);
    }

    /// <summary>
    /// The Cataclysm: every non-boss enemy inside the camera's view dies at once
    /// (experience and drops as normal, credited to the player); bosses on
    /// screen lose a tenth of their life. Returns how many died.
    /// </summary>
    public int Nuke(Player player)
    {
        Rect2 view = ViewOf(player);
        int killed = 0;
        _nuking = true;
        foreach (Node node in GetTree().GetNodesInGroup(CombatQuery.EnemyGroup))
        {
            if (node is not Enemy enemy || enemy.Health.IsDead || !view.HasPoint(enemy.GlobalPosition))
            {
                continue;
            }

            if (enemy.IsBoss)
            {
                enemy.Health.ApplyResolvedDamage((enemy.Stats.Life?.Max ?? 0f) * 0.1f, player);
                continue;
            }

            enemy.Health.ApplyResolvedDamage(float.MaxValue / 4f, player);
            killed++;
        }

        _nuking = false;
        NukesUsed++;
        return killed;
    }

    /// <summary>The Lodestone: every experience gem and gold coin on the map flies
    /// to the player (co-op: that player's own). Returns how many were pulled.</summary>
    public int Magnetize(Player? owner = null)
    {
        int pulled = 0;
        foreach (Node node in GetTree().GetNodesInGroup(PickupGroup))
        {
            if (node is Pickup { Kind: PickupKind.Experience or PickupKind.Gold } p && (owner is null || p.OwnerPlayer == owner))
            {
                p.Attract();
                pulled++;
            }
        }

        Magnetized?.Invoke(owner);
        return pulled;
    }

    /// <summary>Everything on the floor flies in (a boss fight starts or ends).</summary>
    public void AttractAll()
    {
        foreach (Node node in GetTree().GetNodesInGroup(PickupGroup))
        {
            (node as Pickup)?.Attract();
        }

        Magnetized?.Invoke(null);
    }

    /// <summary>Persistent-character experience for a kill (docs/design/endgame.md ->
    /// "Persistent character"): grows with monster level and rarity.</summary>
    public static float CharacterExperienceFor(Enemy enemy) =>
        enemy.ExperienceValue * (2f + enemy.MonsterLevel * 0.35f) * (enemy.IsBoss ? 3f : 1f);

    private Vector2 RandomOffset(float r) => new(_rng.RandfRange(-r, r), _rng.RandfRange(-r, r));

    /// <summary>Drops experience worth <paramref name="value"/>: one gem, or a merge
    /// into an existing gem once the floor is at <see cref="GemCap"/>.</summary>
    public void SpawnExperience(Vector2 at, float value, Player? owner = null)
    {
        if (owner is not null && PlayerOf(owner) is { } who)
        {
            // Co-op: the cap counts this player's own gems.
            who.Gems.RemoveAll(g => !IsInstanceValid(g) || g.IsQueuedForDeletion());
            if (who.Gems.Count >= GemCap)
            {
                who.Gems[_rng.RandiRange(0, who.Gems.Count - 1)].Absorb(value);
                return;
            }

            Pickup gem = Spawned(Pickup.Spawn(GetTree().CurrentScene, PickupKind.Experience, value, at + RandomOffset(6f)), owner);
            who.Gems.Add(gem);
            return;
        }

        Godot.Collections.Array<Node> gems = GetTree().GetNodesInGroup(PickupGroup);
        if (gems.Count >= GemCap)
        {
            var target = gems[_rng.RandiRange(0, gems.Count - 1)] as Pickup;
            if (target is { Kind: PickupKind.Experience })
            {
                target.Absorb(value);
                return;
            }
        }

        Spawned(Pickup.Spawn(GetTree().CurrentScene, PickupKind.Experience, value, at + RandomOffset(6f)), owner);
    }

    public void OnPickup(Pickup pickup, Player player)
    {
        RunPlayer who = PlayerOf(player) ?? Local;
        bool local = player.IsLocal;
        int detail = 0;
        switch (pickup.Kind)
        {
            case PickupKind.Experience:
                if (local)
                {
                    Core.Sfx.Play(this, "pickup", -10f);
                }

                AddExperience(who, pickup.Value);
                break;
            case PickupKind.Life:
                if (local)
                {
                    Core.Sfx.Play(this, "pickup");
                }

                Items.Flasks.CollectOrb(player, pickup.Value);
                break;
            case PickupKind.Gold:
                if (local)
                {
                    Core.Sfx.Play(this, "pickup");
                }

                who.GoldCollected += (long)pickup.Value;
                player.Wallet.Add(Core.Profile.GoldId, (long)pickup.Value);
                break;
            case PickupKind.Currency when Items.Crafting.Find(pickup.CurrencyKey) is { } currency:
                who.OrbsCollected++;
                player.Wallet.Add(currency.Id, 1);
                if (local)
                {
                    Core.Sfx.Play(this, "pickup", -2f);
                    EmitSignal(SignalName.Toast, currency.Name, currency.Color);
                }

                break;
            case PickupKind.Nuke:
                int killed = Nuke(player);
                detail = killed;
                if (local)
                {
                    Core.Sfx.Play(this, "boss_warn");
                    ScreenFlash.Spawn(this, new Color(1f, 0.55f, 0.3f));
                    EmitSignal(SignalName.Toast, $"Cataclysm — {killed} slain", new Color(1f, 0.45f, 0.3f));
                }

                break;
            case PickupKind.Magnet:
                Magnetize(Coop ? player : null);
                if (local)
                {
                    Core.Sfx.Play(this, "levelup", -6f);
                    ScreenFlash.Spawn(this, new Color(0.45f, 0.75f, 1f), 0.25f);
                    EmitSignal(SignalName.Toast, "Lodestone — everything comes to you", new Color(0.55f, 0.8f, 1f));
                }

                break;
        }

        PickupCollected?.Invoke(pickup, player, detail);
    }

    public void EndRun(bool victory, bool fell = false)
    {
        if (Ended)
        {
            return;
        }

        Ended = true;
        Victory = victory;
        Fell = fell;
        EmitSignal(SignalName.RunEnded, victory);
    }
}
