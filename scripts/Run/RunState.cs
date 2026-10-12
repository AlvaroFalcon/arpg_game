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

    public int Level { get; private set; } = 1;
    public float Experience { get; private set; }
    public float Elapsed { get; private set; }
    public int Kills { get; private set; }
    public int ElitesKilled { get; private set; }
    public int BossesKilled { get; private set; }
    public long GoldCollected { get; private set; }
    public bool Ended { get; private set; }
    public bool Victory { get; private set; }

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

    public override void _PhysicsProcess(double delta)
    {
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
        if (enemy.ExperienceValue > 0)
        {
            SpawnExperience(at, enemy.ExperienceValue);
        }

        float roll = _rng.Randf();
        if (roll < (enemy.IsElite ? 0.5f : 0.012f))
        {
            Pickup.Spawn(GetTree().CurrentScene, PickupKind.Life, 0.25f, at + RandomOffset(10f));
        }
        else if (roll < (enemy.IsElite ? 1f : 0.05f))
        {
            Pickup.Spawn(GetTree().CurrentScene, PickupKind.Gold, enemy.IsElite ? 25 : 3, at + RandomOffset(10f));
        }

        // Gear drops (docs/design/run.md -> "Rewards"): elites sometimes, bosses always.
        int drops = enemy.IsBoss ? 2 : enemy.IsElite && _rng.Randf() < 0.3f ? 1 : 0;
        for (int i = 0; i < drops; i++)
        {
            DropItem(at + RandomOffset(30f), enemy.IsBoss ? 0.6f : 0.2f);
        }

        if (Player is not null && byPlayer)
        {
            float lifeOnKill = Player.Stats.Get(StatId.LifeOnKill).Value;
            if (lifeOnKill > 0f)
            {
                Player.Health?.Heal(lifeOnKill);
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
                AreaBurst.Spawn(GetTree().CurrentScene, at, 70f * enemy.Scale.X, 0f, new Color(1f, 0.5f, 0.15f), hit);
            }
        }
    }

    /// <summary>Puts a rolled item on the ground through the normal drop path
    /// (label, filter, click-to-pick-up).</summary>
    public void DropItem(Vector2 at, float quality)
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
        float roll = _rng.Randf() + quality * 0.6f;
        Items.Rarity rarity = roll > 1.05f ? Items.Rarity.Rare : roll > 0.5f ? Items.Rarity.Magic : Items.Rarity.Common;
        item.Initialize(bases[_rng.RandiRange(0, bases.Count - 1)], rarity, itemLevel, Items.ItemFactory.Affixes.Entries, _rng);
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
                player.Health?.Heal((player.Stats.Life?.Max ?? 0f) * pickup.Value);
                break;
            case PickupKind.Gold:
                Core.Sfx.Play(this, "pickup");
                GoldCollected += (long)pickup.Value;
                player.Wallet.Add(Core.Profile.GoldId, (long)pickup.Value);
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
