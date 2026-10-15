#nullable enable
using Godot;
using ArpgGame.Combat;
using ArpgGame.Enemies;
using ArpgGame.Progression;
using ArpgGame.Stats;

namespace ArpgGame.Run;

/// <summary>
/// The bookkeeping of one run — docs/design/run.md: elapsed time, kills,
/// experience and level, and the outcome. Listens to enemy deaths (spawning
/// experience, applying on-kill effects) and pickups. Level-ups are queued and
/// handed to whoever listens to <see cref="LevelUpReady"/> (the level-up
/// screen), which pauses the run while the player chooses.
/// </summary>
[GlobalClass]
public partial class RunState : Node, IPickupHandler
{
    public const string Group = "run_state";
    public const string PickupGroup = "pickups";

    /// <summary>Above this many gems, new experience merges into existing gems
    /// instead of spawning more (keeps late-run floors readable and cheap).</summary>
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

    public int Level { get; private set; } = 1;
    public float Experience { get; private set; }
    public float Elapsed { get; private set; }
    public int Kills { get; private set; }
    public int ElitesKilled { get; private set; }
    public int BossesKilled { get; private set; }
    public long GoldCollected { get; private set; }
    public bool Ended { get; private set; }
    public bool Victory { get; private set; }
    public int OrbsCollected { get; private set; }
    public int NukesUsed { get; private set; }

    /// <summary>Level-ups earned but not yet chosen.</summary>
    public int PendingLevelUps { get; private set; }

    public Player? Player { get; set; }
    public RunBuild? Build { get; set; }

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
        if (!Ended)
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

    public float ExperienceFraction => Experience / ExperienceToNext(Level);

    public void AddExperience(float amount)
    {
        if (Ended)
        {
            return;
        }

        float gain = Player?.Stats.Get(StatId.ExperienceGain).Value ?? 1f;
        Experience += amount * gain;
        while (Experience >= ExperienceToNext(Level))
        {
            Experience -= ExperienceToNext(Level);
            Level++;
            PendingLevelUps++;
            Core.Sfx.Play(this, "levelup");
        }

        if (PendingLevelUps > 0)
        {
            EmitSignal(SignalName.LevelUpReady, Level - PendingLevelUps + 1);
        }
    }

    /// <summary>Raises the level without queueing a choice (auto-levelled
    /// preview runs apply their picks directly).</summary>
    public void AddLevelSilently() => Level++;

    /// <summary>Called by the level-up screen after a choice is applied.</summary>
    public void ConsumeLevelUp()
    {
        if (PendingLevelUps > 0)
        {
            PendingLevelUps--;
        }
    }

    public void RegisterEnemy(Enemy enemy) => enemy.Killed += OnEnemyKilled;

    private void OnEnemyKilled(Enemy enemy)
    {
        if (Ended)
        {
            return;
        }

        bool byPlayer = Player is not null && enemy.Health.LastHitSource == Player;
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
        if (byPlayer)
        {
            Characters.CharacterBuild.Of(Player)?.GrantExperience(CharacterExperienceFor(enemy));
        }
        Vector2 at = enemy.GlobalPosition;
        // Gems, life, gold, orbs and gear are rolled now and spawned a few per frame (Loot).
        Node scene = GetTree().CurrentScene;
        if (enemy.ExperienceValue > 0)
        {
            float value = enemy.ExperienceValue;
            Loot.Add(Items.LootQueue.PickupCost, () => SpawnExperience(at, value));
        }

        float roll = _rng.Randf();
        if (roll < (enemy.IsElite ? 0.5f : 0.012f))
        {
            Vector2 p = at + RandomOffset(10f);
            Loot.Add(Items.LootQueue.PickupCost, () => Pickup.Spawn(scene, PickupKind.Life, 0.25f, p));
        }
        else if (roll < (enemy.IsElite ? 1f : 0.05f))
        {
            Vector2 p = at + RandomOffset(10f);
            float gold = enemy.IsElite ? 25 : 3;
            Loot.Add(Items.LootQueue.PickupCost, () => Pickup.Spawn(scene, PickupKind.Gold, gold, p));
        }

        // Power-ups and orbs (docs/design/run.md -> "Pickups"). A Cataclysm's own
        // victims never drop another one.
        if (!_nuking)
        {
            float boost = enemy.IsBoss ? 0f : enemy.IsElite ? 40f : 1f;
            if (_rng.Randf() < NukeChance * boost && !OnFloor(PickupKind.Nuke))
            {
                Pickup.Spawn(GetTree().CurrentScene, PickupKind.Nuke, 1f, at + RandomOffset(14f));
            }
            else if (_rng.Randf() < MagnetChance * boost && !OnFloor(PickupKind.Magnet))
            {
                Pickup.Spawn(GetTree().CurrentScene, PickupKind.Magnet, 1f, at + RandomOffset(14f));
            }
        }

        int orbs = enemy.IsBoss ? 3 : _rng.Randf() < (enemy.IsElite ? 0.25f : OrbChance) ? 1 : 0;
        for (int i = 0; i < orbs; i++)
        {
            Vector2 p = at + RandomOffset(24f);
            Loot.Add(Items.LootQueue.PickupCost, () => DropOrb(p));
        }

        // Gear drops (docs/design/run.md -> "Rewards"): elites sometimes, bosses always.
        int drops = enemy.IsBoss ? 2 : enemy.IsElite && _rng.Randf() < 0.3f ? 1 : 0;
        for (int i = 0; i < drops; i++)
        {
            Vector2 p = at + RandomOffset(30f);
            Items.DropSource source = enemy.IsBoss ? Items.DropSource.RiftBoss : Items.DropSource.RiftElite;
            Loot.Add(Items.LootQueue.ItemCost, () => DropItem(p, source));
        }

        if (Player is not null && byPlayer)
        {
            float lifeOnKill = Player.Stats.Get(StatId.LifeOnKill).Value;
            if (lifeOnKill > 0f)
            {
                Player.Health?.Heal(lifeOnKill);
            }

            float manaOnKill = Player.Stats.Get(StatId.ManaOnKill).Value;
            if (manaOnKill > 0f && Player.Stats.Mana is { } mana)
            {
                mana.Current = Mathf.Min(mana.Max, mana.Current + manaOnKill);
            }

            if (Progression.BuildQuery.Any(Player, b => b.ChainReaction))
            {
                float maxLife = enemy.Stats.Life?.Max ?? 0f;
                var hit = new HitProfile
                {
                    Damage = maxLife * 0.2f,
                    Type = DamageType.Fire,
                    Source = Player,
                    Faction = Faction.Player,
                    Tags = SkillTags.Fire | SkillTags.Area,
                    KnockbackForce = 80f,
                };
                AreaBurst.SpawnOnKill(GetTree().CurrentScene, at, 70f * enemy.Scale.X, new Color(1f, 0.5f, 0.15f), hit);
            }
        }
    }

    /// <summary>Puts a rolled item on the ground through the normal drop path
    /// (label, filter, click-to-pick-up).</summary>
    public void DropItem(Vector2 at, Items.DropSource source)
    {
        int itemLevel = Mathf.Clamp(3 + Mathf.RoundToInt(Elapsed / 60f * 5f) + BossesKilled * 8, 1, 100);
        var bases = Items.ItemFactory.Bases;
        if (bases.Count == 0)
        {
            return;
        }

        Items.EquipmentItem item = Items.ItemFactory.NewBlank();
        GetTree().CurrentScene.AddChild(item);
        item.GlobalPosition = at;
        // Cascade rarity (docs/design/items.md -> "Drop tables (cascade)"), better the longer the run.
        Items.Rarity rarity = Items.DropTables.RollRarity(source, Items.DropTables.RiftMultiplier(Elapsed), _rng);
        item.Initialize(bases[_rng.RandiRange(0, bases.Count - 1)], rarity, itemLevel, Items.ItemFactory.Affixes.Entries, _rng);
    }

    public void DropOrb(Vector2 at)
    {
        Pickup orb = Pickup.Spawn(GetTree().CurrentScene, PickupKind.Currency, 1f, at);
        orb.CurrencyKey = Items.DropTables.RollOrb(_rng, Items.DropTables.RiftMultiplier(Elapsed)).Id;
        orb.QueueRedraw();
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

    /// <summary>
    /// The Cataclysm: every non-boss enemy inside the camera's view dies at once
    /// (experience and drops as normal, credited to the player); bosses on
    /// screen lose a tenth of their life. Returns how many died.
    /// </summary>
    public int Nuke(Player player)
    {
        Rect2 view = player.GetViewport().CanvasTransform.AffineInverse() * player.GetViewport().GetVisibleRect();
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
    /// to the player. Returns how many were pulled.</summary>
    public int Magnetize()
    {
        int pulled = 0;
        foreach (Node node in GetTree().GetNodesInGroup(PickupGroup))
        {
            if (node is Pickup { Kind: PickupKind.Experience or PickupKind.Gold } p)
            {
                p.Attract();
                pulled++;
            }
        }

        return pulled;
    }

    /// <summary>Persistent-character experience for a kill (docs/design/endgame.md ->
    /// "Persistent character"): grows with monster level and rarity.</summary>
    public static float CharacterExperienceFor(Enemy enemy) =>
        enemy.ExperienceValue * (2f + enemy.MonsterLevel * 0.35f) * (enemy.IsBoss ? 3f : 1f);

    private Vector2 RandomOffset(float r) => new(_rng.RandfRange(-r, r), _rng.RandfRange(-r, r));

    /// <summary>Drops experience worth <paramref name="value"/>: one gem, or a merge
    /// into an existing gem once the floor is at <see cref="GemCap"/>.</summary>
    public void SpawnExperience(Vector2 at, float value)
    {
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

        Pickup.Spawn(GetTree().CurrentScene, PickupKind.Experience, value, at + RandomOffset(6f));
    }

    public void OnPickup(Pickup pickup, Player player)
    {
        switch (pickup.Kind)
        {
            case PickupKind.Experience:
                Core.Sfx.Play(this, "pickup", -10f);
                AddExperience(pickup.Value);
                break;
            case PickupKind.Life:
                Core.Sfx.Play(this, "pickup");
                Items.Flasks.CollectOrb(player, pickup.Value);
                break;
            case PickupKind.Gold:
                Core.Sfx.Play(this, "pickup");
                GoldCollected += (long)pickup.Value;
                player.Wallet.Add(Core.Profile.GoldId, (long)pickup.Value);
                break;
            case PickupKind.Currency when Items.Crafting.Find(pickup.CurrencyKey) is { } currency:
                Core.Sfx.Play(this, "pickup", -2f);
                OrbsCollected++;
                player.Wallet.Add(currency.Id, 1);
                EmitSignal(SignalName.Toast, currency.Name, currency.Color);
                break;
            case PickupKind.Nuke:
                Core.Sfx.Play(this, "boss_warn");
                int killed = Nuke(player);
                ScreenFlash.Spawn(this, new Color(1f, 0.55f, 0.3f));
                EmitSignal(SignalName.Toast, $"Cataclysm — {killed} slain", new Color(1f, 0.45f, 0.3f));
                break;
            case PickupKind.Magnet:
                Core.Sfx.Play(this, "levelup", -6f);
                Magnetize();
                ScreenFlash.Spawn(this, new Color(0.45f, 0.75f, 1f), 0.25f);
                EmitSignal(SignalName.Toast, "Lodestone — everything comes to you", new Color(0.55f, 0.8f, 1f));
                break;
        }
    }

    public void EndRun(bool victory)
    {
        if (Ended)
        {
            return;
        }

        Ended = true;
        Victory = victory;
        EmitSignal(SignalName.RunEnded, victory);
    }
}
