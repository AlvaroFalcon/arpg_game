#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Progression;
using ArpgGame.Skills;
using ArpgGame.Stats;

namespace ArpgGame.Characters;

/// <summary>
/// Applies a persistent <see cref="CharacterData"/> to the live player —
/// docs/design/classes.md -> "How it plugs into what exists". A child of the
/// player, attached by <see cref="Core.Profile.ApplyTo"/>. It owns everything
/// the character contributes that is not gear: class attributes and bonuses,
/// passive tree, ascendancy, the gem loadout in slots 1-4, the class resource
/// and the class sprite. Every stat modifier carries one <see cref="Source"/>,
/// so <see cref="Rebuild"/> (after a passive or gem change) is "remove by
/// source, re-apply" — the same mechanism gear and the run build use.
/// </summary>
[GlobalClass]
public partial class CharacterBuild : Node, IBuildContext
{
    /// <summary>Loadout index → input slot: LMB, RMB, 1, 2, 3, 4.</summary>
    public static readonly SkillSlotId[] Slots = { SkillSlotId.Lmb, SkillSlotId.Rmb, SkillSlotId.Slot1, SkillSlotId.Slot2 };

    [Signal]
    public delegate void LevelledUpEventHandler(int level);

    [Signal]
    public delegate void GemLevelledEventHandler(string gemName, int level);

    public CharacterData Data { get; private set; } = new();
    public Player Player { get; private set; } = null!;
    public ActorStats Stats => Player.Stats;
    public SkillUser SkillUser => Player.SkillUser;
    public object Source { get; } = new();
    public bool ChainReaction { get; set; }
    public bool PhaseDancer { get; set; }
    public SkillTags ForcedElement { get; set; } = SkillTags.None;

    public ClassResource? Resource { get; private set; }

    /// <summary>Ascendancy behaviours that need runtime hooks (see <see cref="Ascendancy"/>).</summary>
    public AscendancyEffects Ascendancy { get; private set; } = null!;

    /// <summary>
    /// Survival Rift (docs/design/progression.md -> "Starting skills"): the run
    /// build owns the live skills once the run has started — a character level-up
    /// mid-run rebuilds stats, tree and ascendancy but never re-equips the loadout
    /// (which would wipe the run's skills, levels and supports), and gem experience
    /// still goes to the gems without touching the live skill's level.
    /// </summary>
    public bool RunOwnsSkills { get; set; }

    private readonly List<CastModifier> _myGlobalModifiers = new();
    private readonly Dictionary<SkillSlotId, GemData> _slotGems = new();
    private bool _built;

    public static CharacterBuild Attach(Player player, CharacterData data)
    {
        if (player.GetNodeOrNull<CharacterBuild>("CharacterBuild") is { } old)
        {
            old.Strip();
            player.RemoveChild(old);
            old.QueueFree();
        }

        var build = new CharacterBuild { Name = "CharacterBuild", Data = data };
        player.AddChild(build);
        return build;
    }

    public static CharacterBuild? Of(Node? player) => player?.GetNodeOrNull<CharacterBuild>("CharacterBuild");

    public override void _Ready()
    {
        Player = GetParent<Player>();
        Ascendancy = new AscendancyEffects { Name = "Ascendancy" };
        AddChild(Ascendancy);
        Resource = new ClassResource { Name = "ClassResource", Kind = Data.Class.Resource };
        AddChild(Resource);
        Rebuild();
    }

    /// <summary>Strips and re-applies everything (after allocating a passive or
    /// changing gems). Life and mana keep their absolute values (clamped).</summary>
    public void Rebuild()
    {
        if (Player is null)
        {
            return;
        }

        Strip();
        ClassDefinition cls = Data.Class;
        Stats.Strength.BaseValue = cls.Strength;
        Stats.Dexterity.BaseValue = cls.Dexterity;
        Stats.Intelligence.BaseValue = cls.Intelligence;
        AddMod(StatId.MaxLife, ModifierType.Flat, cls.BonusLife + (Data.Level - 1) * 4f);
        AddMod(StatId.MaxMana, ModifierType.Flat, cls.BonusMana + Mathf.Floor((Data.Level - 1) * 1.5f));
        // Mana regeneration grows with the character (docs/design/skills.md -> "Mana and
        // cooldowns"): a level-1 caster has to ration mana, a seasoned one less so.
        AddMod(StatId.ManaRegen, ModifierType.Flat, (Data.Level - 1) * 0.15f);
        if (cls.BonusDodgeCharges != 0)
        {
            AddMod(StatId.DodgeCharges, ModifierType.Flat, cls.BonusDodgeCharges);
        }

        int before = SkillUser.GlobalModifiers.Count;
        PassiveTree.Apply(Data, this);
        Ascendancy.Apply(this);
        _myGlobalModifiers.AddRange(SkillUser.GlobalModifiers.Skip(before));

        Stats.RecomputeAttributeDerivations();
        Stats.Life?.ClampToMax();
        Stats.Mana?.ClampToMax();
        if (!RunOwnsSkills)
        {
            EquipLoadout();
        }

        ApplySprite();
        _built = true;
    }

    private void AddMod(StatId stat, ModifierType type, float value) =>
        Stats.Get(stat).AddModifier(new StatModifier(type, value, Source));

    private void Strip()
    {
        if (Player is null || !_built)
        {
            return;
        }

        Stats.RemoveModifiersFromSource(Source);
        foreach (CastModifier m in _myGlobalModifiers)
        {
            SkillUser.GlobalModifiers.Remove(m);
        }

        _myGlobalModifiers.Clear();
        ChainReaction = false;
        PhaseDancer = false;
        ForcedElement = SkillTags.None;
        Ascendancy.Strip(this);
    }

    /// <summary>Puts the loadout's skill gems (with their supports) into LMB, RMB, Q and E.</summary>
    public void EquipLoadout()
    {
        _slotGems.Clear();
        for (int i = 0; i < Slots.Length; i++)
        {
            LoadoutSlot? slot = i < Data.Loadout.Length ? Data.Loadout[i] : null;
            if (slot is null || Data.FindGem(slot.SkillUid) is not { IsSupport: false } gem || UpgradeCatalog.FindSkill(gem.Id) is not { } entry)
            {
                // An empty LMB keeps click-to-move; any other empty slot is just empty.
                if (Slots[i] == SkillSlotId.Lmb)
                {
                    SkillUser.RestoreMove(SkillSlotId.Lmb);
                }
                else
                {
                    SkillUser.ClearSlot(Slots[i]);
                }

                continue;
            }

            var instance = new SkillInstance(entry.Definition) { Level = gem.Level };
            foreach (string uid in slot.SupportUids.Take(Data.SupportSockets))
            {
                if (Data.FindGem(uid) is { IsSupport: true } sup
                    && UpgradeCatalog.Supports.FirstOrDefault(s => s.Id == sup.Id) is { } def
                    && def.Accepts(entry.Definition))
                {
                    instance.Attached.Add(new SupportGem(def));
                }
            }

            instance.TreeModifiers.AddRange(SkillTrees.ModifiersFor(gem.Id, gem.Tree));
            SkillUser.EquipSkill(Slots[i], instance);
            _slotGems[Slots[i]] = gem;
        }
    }

    private void ApplySprite()
    {
        string sprite = Data.AscendancyDef?.Sprite ?? Data.Class.Sprite;
        string path = $"res://assets/sprites/player/{sprite}.tres";
        if (Player.GetNodeOrNull<AnimatedSprite2D>("Sprite") is { } node && ResourceLoader.Exists(path))
        {
            node.SpriteFrames = GD.Load<SpriteFrames>(path);
            node.Play("idle");
        }
    }

    /// <summary>
    /// Character experience for a kill (docs/design/endgame.md -> "Persistent
    /// character"). Skill gems no longer learn from kills (2026-10-30): their levels
    /// are bought with gold at Escaldón (docs/design/skills.md -> "Skill upgrade trees").
    /// </summary>
    public void GrantExperience(float amount)
    {
        float gain = amount * Mathf.Max(0f, Stats.Get(StatId.ExperienceGain).Value);
        int levels = Data.AddExperience(gain);

        if (levels > 0)
        {
            float life = Stats.Life?.Current ?? 0f;
            Rebuild();
            if (Stats.Life is { } pool && !Stats.OwnerDead)
            {
                pool.Current = Mathf.Max(life, pool.Max);
            }

            EmitSignal(SignalName.LevelledUp, Data.Level);
            Core.Sfx.Play(this, "levelup");
        }
    }

    public float ExperienceFraction => Data.Level >= CharacterData.MaxLevel ? 1f : Data.Experience / CharacterData.ExperienceToNext(Data.Level);
}
