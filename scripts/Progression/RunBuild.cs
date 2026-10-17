#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Skills;
using ArpgGame.Stats;

namespace ArpgGame.Progression;

public enum UpgradeKind
{
    NewSkill,
    SkillLevel,
    Support,
    Passive,
    Keystone,
}

/// <summary>Passive rarity: how strong this particular roll is.</summary>
public enum UpgradeRarity
{
    Common,
    Magic,
    Rare,
}

/// <summary>One card on the level-up screen.</summary>
public sealed class UpgradeOffer
{
    public UpgradeKind Kind { get; init; }
    public UpgradeRarity Rarity { get; init; }
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Description { get; init; } = "";
    public Texture2D? Icon { get; init; }
    public System.Action Apply { get; init; } = () => { };

    /// <summary>The choice as text ("s:spark", "g:support:skill", "p:passive:rarity"...),
    /// replayed with <see cref="RunBuild.ApplyKey"/> — a co-op client sends it so the
    /// host's copy of that player takes the same card (docs/design/multiplayer.md).</summary>
    public string Key { get; init; } = "";

    /// <summary>What the card is about, whatever its rarity ("p:damage", "s:spark"):
    /// a reroll never brings back one already on screen. Set by <see cref="RunBuild"/>.</summary>
    public string Identity { get; set; } = "";

    public OfferCategory Category { get; set; }

    /// <summary>For keystone cards: lesser or great.</summary>
    public KeystoneTier KeystoneTier { get; init; }
}

/// <summary>
/// The player's build for the current run — docs/design/progression.md. Owns
/// which skills, supports, passives and keystones have been taken, rolls the
/// level-up offers, and applies a chosen one. Every stat modifier it grants is
/// tagged with <see cref="Source"/>, so the whole run's build can be stripped
/// in one <see cref="ActorStats.RemoveModifiersFromSource"/> call — the same
/// mechanism gear uses — and nothing about it leaks into the persistent
/// character between runs.
///
/// A child of the player in the run scene; expects the player as its parent.
/// </summary>
[GlobalClass]
public partial class RunBuild : Node, IBuildContext
{
    public const int MaxSkills = 4;
    public const int MaxSupportsPerSkill = 3;
    public const int MaxSkillLevel = 6;

    private static readonly SkillSlotId[] SkillSlots = { SkillSlotId.Lmb, SkillSlotId.Rmb, SkillSlotId.Slot1, SkillSlotId.Slot2, SkillSlotId.Slot3, SkillSlotId.Slot4 };

    /// <summary>Source tag for every modifier this build grants.</summary>
    public object Source { get; } = new();

    public Player Player { get; private set; } = null!;
    public ActorStats Stats => Player.Stats;
    public SkillUser SkillUser => Player.SkillUser;

    private readonly Dictionary<string, int> _passiveRanks = new();
    private readonly List<string> _keystones = new();
    private readonly List<string> _history = new();

    public IReadOnlyList<string> Keystones => _keystones;
    public IReadOnlyDictionary<string, int> PassiveRanks => _passiveRanks;

    /// <summary>Human-readable log of every pick, for the results screen.</summary>
    public IReadOnlyList<string> History => _history;

    public bool ChainReaction { get; set; }
    public bool PhaseDancer { get; set; }

    /// <summary>Set by "Avatar of Fire": every skill counts as this element.</summary>
    public SkillTags ForcedElement { get; set; } = SkillTags.None;

    public int RerollsLeft { get; set; } = OfferRules.RerollsPerRun;

    public override void _Ready()
    {
        Player = GetParent<Player>();
    }

    public IEnumerable<SkillInstance> Skills =>
        SkillSlots.Select(s => SkillUser.GetSkill(s)).Where(s => s is not null)!;

    /// <summary>Whether any owned skill carries the tag, after its conversion
    /// support and any keystone-forced element are taken into account — the
    /// tags its hits will actually have.</summary>
    public bool HasTag(SkillTags tag) => Skills.Any(s => (EffectiveTags(s) & tag) != 0);

    public SkillTags EffectiveTags(SkillInstance skill)
    {
        SkillTags tags = skill.Definition.Tags;
        foreach (SupportGem gem in skill.Attached.OfType<SupportGem>())
        {
            SkillTags? to = gem.Definition.Id switch
            {
                "cold_conversion" => SkillTags.Cold,
                "fire_conversion" => SkillTags.Fire,
                "lightning_conversion" => SkillTags.Lightning,
                _ => null,
            };
            if (to is { } element)
            {
                tags = (tags & ~UpgradeCatalog.Elements) | element;
            }
        }

        // A keystone's forced element, from a card or from the character's tree.
        SkillTags forced = ForcedElement != SkillTags.None ? ForcedElement : Characters.CharacterBuild.Of(Player)?.ForcedElement ?? SkillTags.None;
        if (forced != SkillTags.None)
        {
            tags = (tags & ~UpgradeCatalog.Elements) | forced;
        }

        return tags;
    }

    public SkillInstance? FindSkill(string id) =>
        Skills.FirstOrDefault(s => UpgradeCatalog.SkillIdOf(s.Definition) == id);

    /// <summary>Equips a run skill in the first free slot. Returns false if full or owned.</summary>
    public bool AddSkill(string id)
    {
        if (UpgradeCatalog.FindSkill(id) is not { } entry || FindSkill(id) is not null || Skills.Count() >= MaxSkills)
        {
            return false;
        }

        foreach (SkillSlotId slot in SkillSlots)
        {
            if (SkillUser.GetSkill(slot) is null)
            {
                SkillUser.EquipSkill(slot, new SkillInstance(entry.Definition));
                return true;
            }
        }

        return false;
    }

    public void AddPassive(PassiveDefinition passive, float multiplier)
    {
        passive.Apply(Stats, multiplier, Source);
        _passiveRanks[passive.Id] = _passiveRanks.GetValueOrDefault(passive.Id) + 1;
        Stats.RecomputeAttributeDerivations();
    }

    public void AddKeystone(KeystoneDefinition keystone)
    {
        if (HasKeystone(keystone.Id))
        {
            return;
        }

        keystone.Apply(this);
        _keystones.Add(keystone.Id);
    }

    public bool AddSupport(SkillInstance skill, SupportDefinition support)
    {
        if (skill.Attached.Count >= MaxSupportsPerSkill || skill.Attached.OfType<SupportGem>().Any(g => g.Definition.Id == support.Id))
        {
            return false;
        }

        skill.Attached.Add(new SupportGem(support));
        return true;
    }

    // --- offers (docs/design/progression.md -> "Offer composition") -----------------

    private sealed class Candidate
    {
        public OfferCategory Category;
        public float Weight;
        public System.Func<RandomNumberGenerator, UpgradeOffer> Make = null!;

        /// <summary>What the card is about ("s:spark", "p:damage"): two cards on screen
        /// never share one, whatever rarity they rolled.</summary>
        public string Key = "";
    }

    /// <summary>The persistent character this run was started with (null in tests and the sandbox).</summary>
    public Characters.CharacterData? Character => Characters.CharacterBuild.Of(Player)?.Data;

    /// <summary>The class's Rift skill pool (empty without a character: every skill is "on class").</summary>
    public IReadOnlyList<string> ClassSkills => Character?.Class.RiftSkills ?? System.Array.Empty<string>();

    /// <summary>Keystones the character already has from its passive tree: never
    /// offered again, so nothing is counted twice.</summary>
    public IEnumerable<string> TreeKeystones =>
        Character?.Passives.Select(id => Characters.PassiveTree.Get(id)?.Keystone).Where(k => k is not null).Select(k => k!)
        ?? Enumerable.Empty<string>();

    /// <summary>True for a keystone this build has, from a card or from the tree.</summary>
    public bool HasKeystone(string id) => _keystones.Contains(id) || TreeKeystones.Contains(id);

    /// <summary>
    /// Rolls the cards for reaching <paramref name="level"/> (0 = the start pick,
    /// <see cref="RollStartOffers"/>). Keystone levels (<see cref="OfferRules.KeystoneTierAt"/>)
    /// offer keystones of that tier only, while any remain; every other level rolls a
    /// category per card by <see cref="OfferRules.CategoryWeight"/>, then an entry by
    /// its own weight, never two cards for the same thing.
    /// </summary>
    public List<UpgradeOffer> RollOffers(int level, RandomNumberGenerator rng, int count = OfferRules.CardsPerOffer) =>
        Roll(level, rng, count, new HashSet<string>());

    /// <summary>
    /// The run-start pick (docs/design/progression.md -> "Starting skills"). A
    /// character with no skills is offered skills of its class only, its starting
    /// skill always among them; one that brought skills is offered skill cards: a
    /// new skill of its class or a level for one it owns.
    /// </summary>
    public List<UpgradeOffer> RollStartOffers(RandomNumberGenerator rng, int count = OfferRules.CardsPerOffer) =>
        Roll(0, rng, count, new HashSet<string>());

    /// <summary>
    /// Per-card reroll (docs/design/progression.md -> "Rerolls"): a new card for
    /// slot <paramref name="index"/> of <paramref name="onScreen"/>, rolled with the
    /// same rules as the row, never one of the cards already showing (the replaced
    /// one included). Spends a charge only when a replacement exists; returns null
    /// (nothing spent) when there are no charges or nothing else to offer.
    /// </summary>
    public UpgradeOffer? TryReroll(int level, IReadOnlyList<UpgradeOffer> onScreen, int index, RandomNumberGenerator rng)
    {
        if (RerollsLeft <= 0 || index < 0 || index >= onScreen.Count)
        {
            return null;
        }

        var exclude = new HashSet<string>(onScreen.Select(o => o.Identity));
        List<UpgradeOffer> one = Roll(level, rng, 1, exclude, guarantee: false);
        if (one.Count == 0)
        {
            return null;
        }

        RerollsLeft--;
        return one[0];
    }

    private List<UpgradeOffer> Roll(int level, RandomNumberGenerator rng, int count, HashSet<string> exclude, bool guarantee = true)
    {
        List<Candidate> candidates;
        bool start = level <= 0;
        if (start)
        {
            candidates = StartCandidates();
        }
        else if (OfferRules.KeystoneTierAt(level) is { } tier && KeystoneCandidates(tier).Count > 0)
        {
            candidates = KeystoneCandidates(tier);
        }
        else
        {
            candidates = RegularCandidates();
        }

        candidates.RemoveAll(c => exclude.Contains(c.Key));
        var offers = new List<UpgradeOffer>();

        // A skill-less character's start pick: the class's starting skill is always there.
        if (start && guarantee && !Skills.Any() && ClassSkills.Count > 0
            && candidates.FirstOrDefault(c => c.Key == "s:" + ClassSkills[0]) is { } signature)
        {
            Take(signature);
        }

        // An ordinary level always shows a skill card while one exists.
        if (!start && guarantee && OfferRules.GuaranteeSkillCard && candidates.All(c => c.Category != OfferCategory.Keystone)
            && candidates.Where(c => c.Category == OfferCategory.Skill).ToList() is { Count: > 0 } skills)
        {
            Take(PickWeighted(skills, rng));
        }

        while (offers.Count < count && candidates.Count > 0)
        {
            Take(PickWeighted(PickCategory(candidates, rng), rng));
        }

        // The guaranteed card is not always the first one.
        for (int i = offers.Count - 1; i > 0; i--)
        {
            int j = rng.RandiRange(0, i);
            (offers[i], offers[j]) = (offers[j], offers[i]);
        }

        return offers;

        void Take(Candidate c)
        {
            UpgradeOffer offer = c.Make(rng);
            offer.Identity = c.Key;
            offer.Category = c.Category;
            offers.Add(offer);
            candidates.RemoveAll(x => x.Key == c.Key);
        }
    }

    /// <summary>The candidates of one category, chosen by <see cref="OfferRules.CategoryWeight"/>
    /// among the categories that still have something (a single-category pool is returned as is).</summary>
    private static List<Candidate> PickCategory(List<Candidate> candidates, RandomNumberGenerator rng)
    {
        List<OfferCategory> present = candidates.Select(c => c.Category).Distinct().ToList();
        if (present.Count == 1)
        {
            return candidates;
        }

        float total = present.Sum(OfferRules.CategoryWeight);
        float roll = rng.Randf() * total;
        OfferCategory chosen = present[^1];
        foreach (OfferCategory c in present)
        {
            roll -= OfferRules.CategoryWeight(c);
            if (roll <= 0f)
            {
                chosen = c;
                break;
            }
        }

        return candidates.Where(c => c.Category == chosen).ToList();
    }

    private static Candidate PickWeighted(List<Candidate> pool, RandomNumberGenerator rng)
    {
        float total = pool.Sum(c => c.Weight);
        float roll = rng.Randf() * total;
        foreach (Candidate c in pool)
        {
            roll -= c.Weight;
            if (roll <= 0f)
            {
                return c;
            }
        }

        return pool[^1];
    }

    private List<Candidate> StartCandidates()
    {
        bool skillLess = !Skills.Any();
        List<Candidate> list = SkillCandidates(classOnly: true);
        if (skillLess && list.Count < OfferRules.CardsPerOffer)
        {
            // A class pool smaller than a row: fill it from the other skills.
            list = SkillCandidates(classOnly: false);
        }

        return list;
    }

    private List<Candidate> KeystoneCandidates(KeystoneTier tier) =>
        UpgradeCatalog.Keystones
            .Where(k => k.InRift && k.Tier == tier && !HasKeystone(k.Id))
            .Select(k => new Candidate
            {
                Category = OfferCategory.Keystone,
                Weight = 1f,
                Key = "k:" + k.Id,
                Make = _ => new UpgradeOffer
                {
                    Kind = UpgradeKind.Keystone,
                    Id = k.Id,
                    Title = k.Name,
                    Subtitle = k.Tier == KeystoneTier.Great ? "Great keystone" : "Lesser keystone",
                    Description = k.Description,
                    Icon = LoadIcon("icon_keystone"),
                    Key = "k:" + k.Id,
                    KeystoneTier = k.Tier,
                    Apply = () => { AddKeystone(k); Log($"Keystone: {k.Name}"); },
                },
            })
            .ToList();

    /// <summary>New skills (while a slot is free) and levels for owned skills.</summary>
    private List<Candidate> SkillCandidates(bool classOnly)
    {
        var list = new List<Candidate>();
        List<SkillInstance> owned = Skills.ToList();
        IReadOnlyList<string> pool = ClassSkills;

        if (owned.Count < MaxSkills)
        {
            foreach (SkillEntry entry in UpgradeCatalog.Skills)
            {
                bool onClass = pool.Count == 0 || pool.Contains(entry.Id);
                if (FindSkill(entry.Id) is not null || (classOnly && !onClass))
                {
                    continue;
                }

                list.Add(new Candidate
                {
                    Category = OfferCategory.Skill,
                    Weight = (owned.Count < 2 ? OfferRules.NewSkillWeightFewSkills : OfferRules.NewSkillWeight) * (onClass ? OfferRules.ClassSkillMultiplier : 1f),
                    Key = "s:" + entry.Id,
                    Make = _ => new UpgradeOffer
                    {
                        Kind = UpgradeKind.NewSkill,
                        Id = entry.Id,
                        Title = entry.Definition.DisplayName,
                        Subtitle = onClass && pool.Count > 0 ? $"New skill · {Character?.Class.Name}" : "New skill",
                        Description = entry.Definition.Description,
                        Icon = entry.Definition.Icon,
                        Key = "s:" + entry.Id,
                        Apply = () => { AddSkill(entry.Id); Log($"Skill: {entry.Definition.DisplayName}"); },
                    },
                });
            }
        }

        foreach (SkillInstance skill in owned)
        {
            if (skill.Level >= MaxSkillLevel)
            {
                continue;
            }

            SkillInstance s = skill;
            string id = UpgradeCatalog.SkillIdOf(s.Definition);
            list.Add(new Candidate
            {
                Category = OfferCategory.Skill,
                Weight = OfferRules.SkillLevelWeight,
                Key = "l:" + id,
                Make = _ => new UpgradeOffer
                {
                    Kind = UpgradeKind.SkillLevel,
                    Id = id,
                    Title = $"{s.Definition.DisplayName}  Lv {s.Level + 1}",
                    Subtitle = $"Skill level ({s.Level + 1}/{MaxSkillLevel})",
                    Description = $"+{Mathf.RoundToInt(SkillInstance.DamagePerLevel * 100)}% more damage than the previous level.",
                    Icon = s.Definition.Icon,
                    Key = "l:" + id,
                    Apply = () => { s.Level++; Log($"{s.Definition.DisplayName} → Lv {s.Level}"); },
                },
            });
        }

        return list;
    }

    private List<Candidate> RegularCandidates()
    {
        List<Candidate> list = SkillCandidates(classOnly: false);
        foreach (SkillInstance skill in Skills)
        {
            if (skill.Attached.Count >= MaxSupportsPerSkill)
            {
                continue;
            }

            foreach (SupportDefinition support in UpgradeCatalog.Supports)
            {
                if (!support.Accepts(skill.Definition) || skill.Attached.OfType<SupportGem>().Any(g => g.Definition.Id == support.Id))
                {
                    continue;
                }

                // Only one conversion per skill: two would just fight.
                if (support.Id.EndsWith("_conversion") && skill.Attached.OfType<SupportGem>().Any(g => g.Definition.Id.EndsWith("_conversion")))
                {
                    continue;
                }

                SkillInstance s = skill;
                SupportDefinition sup = support;
                string skillId = UpgradeCatalog.SkillIdOf(s.Definition);
                list.Add(new Candidate
                {
                    Category = OfferCategory.Support,
                    Weight = OfferRules.SupportWeight,
                    Key = $"g:{sup.Id}:{skillId}",
                    Make = _ => new UpgradeOffer
                    {
                        Kind = UpgradeKind.Support,
                        Id = sup.Id,
                        Title = sup.Name,
                        Subtitle = $"Support → {s.Definition.DisplayName}",
                        Description = sup.Description,
                        Icon = LoadIcon("icon_support"),
                        Key = $"g:{sup.Id}:{skillId}",
                        Apply = () => { AddSupport(s, sup); Log($"{sup.Name} → {s.Definition.DisplayName}"); },
                    },
                });
            }
        }

        foreach (PassiveDefinition passive in UpgradeCatalog.Passives)
        {
            if (_passiveRanks.GetValueOrDefault(passive.Id) >= passive.MaxRank)
            {
                continue;
            }

            if (passive.Requires is not null && !passive.Requires(this))
            {
                continue;
            }

            PassiveDefinition p = passive;
            list.Add(new Candidate
            {
                Category = OfferCategory.PowerUp,
                Weight = passive.Weight,
                Key = "p:" + passive.Id,
                Make = rng =>
                {
                    UpgradeRarity rarity = p.Scales ? RollRarity(rng) : UpgradeRarity.Rare;
                    float mult = p.Scales ? RarityMultiplier(rarity) : 1f;
                    return new UpgradeOffer
                    {
                        Kind = UpgradeKind.Passive,
                        Rarity = rarity,
                        Id = p.Id,
                        Title = p.Name,
                        Subtitle = p.Scales ? $"{rarity} power-up" : "Notable power-up",
                        Description = p.Describe(mult),
                        Icon = LoadIcon("icon_passive"),
                        Key = $"p:{p.Id}:{(int)rarity}:{(p.Scales ? 1 : 0)}",
                        Apply = () => { AddPassive(p, mult); Log($"{p.Name}: {p.Describe(mult).Replace('\n', ' ')}"); },
                    };
                },
            });
        }

        return list;
    }

    // --- the character's own skills (docs/design/progression.md -> "Starting skills") ---

    /// <summary>
    /// The skills the persistent character brought start the run at level 1 and
    /// with no supports: the run build grows them from there, like any skill it
    /// learns. Only the live skill changes — the gems keep their level. Applied
    /// identically on every machine in co-op, so build histories stay in step.
    /// </summary>
    public void AdoptCharacterSkills()
    {
        foreach (SkillInstance skill in Skills)
        {
            skill.Level = 1;
            skill.Attached.Clear();
        }
    }

    /// <summary>
    /// Applies a card from its <see cref="UpgradeOffer.Key"/> (co-op: the host
    /// replays a client's pick on its copy of that player). Returns false for a
    /// key that no longer applies (unknown id, skill not owned).
    /// </summary>
    public bool ApplyKey(string key)
    {
        string[] k = key.Split(':');
        if (k.Length < 2)
        {
            return false;
        }

        switch (k[0])
        {
            case "s":
                bool added = AddSkill(k[1]);
                if (added)
                {
                    Log($"Skill: {UpgradeCatalog.FindSkill(k[1])?.Definition.DisplayName ?? k[1]}");
                }

                return added;
            case "l" when FindSkill(k[1]) is { } skill:
                skill.Level++;
                Log($"{skill.Definition.DisplayName} → Lv {skill.Level}");
                return true;
            case "g" when k.Length >= 3 && FindSkill(k[2]) is { } target && UpgradeCatalog.Supports.FirstOrDefault(x => x.Id == k[1]) is { } support:
                bool ok = AddSupport(target, support);
                if (ok)
                {
                    Log($"{support.Name} → {target.Definition.DisplayName}");
                }

                return ok;
            case "p" when UpgradeCatalog.Passives.FirstOrDefault(x => x.Id == k[1]) is { } passive:
                var rarity = (UpgradeRarity)(k.Length >= 3 ? k[2].ToInt() : 0);
                float mult = passive.Scales ? RarityMultiplier(rarity) : 1f;
                AddPassive(passive, mult);
                Log($"{passive.Name}: {passive.Describe(mult).Replace('\n', ' ')}");
                return true;
            case "k" when UpgradeCatalog.Keystones.FirstOrDefault(x => x.Id == k[1]) is { } keystone && !HasKeystone(keystone.Id):
                AddKeystone(keystone);
                Log($"Keystone: {keystone.Name}");
                return true;
        }

        return false;
    }

    public static UpgradeRarity RollRarity(RandomNumberGenerator rng)
    {
        float r = rng.Randf();
        return r < OfferRules.RareChance ? UpgradeRarity.Rare : r < OfferRules.RareChance + OfferRules.MagicChance ? UpgradeRarity.Magic : UpgradeRarity.Common;
    }

    public static float RarityMultiplier(UpgradeRarity rarity) => rarity switch
    {
        UpgradeRarity.Rare => OfferRules.RareMultiplier,
        UpgradeRarity.Magic => OfferRules.MagicMultiplier,
        _ => 1f,
    };

    private void Log(string entry) => _history.Add(entry);

    private static readonly Dictionary<string, Texture2D?> IconCache = new();

    public static Texture2D? LoadIcon(string name)
    {
        if (!IconCache.TryGetValue(name, out Texture2D? tex))
        {
            tex = GD.Load<Texture2D>($"res://assets/sprites/icons/{name}.png");
            IconCache[name] = tex;
        }

        return tex;
    }
}
