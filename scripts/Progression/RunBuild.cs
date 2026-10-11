#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using PathToExile.Combat;
using PathToExile.Skills;
using PathToExile.Stats;

namespace PathToExile.Progression;

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
    public const int KeystoneEvery = 5;

    private static readonly SkillSlotId[] SkillSlots = { SkillSlotId.Slot1, SkillSlotId.Slot2, SkillSlotId.Slot3, SkillSlotId.Slot4 };

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

    public int RerollsLeft { get; set; } = 2;

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

        if (ForcedElement != SkillTags.None)
        {
            tags = (tags & ~UpgradeCatalog.Elements) | ForcedElement;
        }

        return tags;
    }

    public SkillInstance? FindSkill(string id) =>
        Skills.FirstOrDefault(s => UpgradeCatalog.SkillIdOf(s.Definition) == id);

    /// <summary>Equips a run skill in the first free slot. Returns false if full or owned.</summary>
    public bool AddSkill(string id)
    {
        if (UpgradeCatalog.FindSkill(id) is not { } entry || FindSkill(id) is not null)
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
        if (_keystones.Contains(keystone.Id))
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

    // --- offers ---------------------------------------------------------------

    private sealed class Candidate
    {
        public float Weight;
        public System.Func<RandomNumberGenerator, UpgradeOffer> Make = null!;
        public string Key = "";
    }

    /// <summary>
    /// Rolls the cards for reaching <paramref name="level"/>. Every
    /// <see cref="KeystoneEvery"/>th level offers keystones only (while any
    /// remain); otherwise a weighted mix of new skills, skill levels, supports
    /// and passives, never two cards for the same thing.
    /// </summary>
    public List<UpgradeOffer> RollOffers(int level, RandomNumberGenerator rng, int count = 3)
    {
        var candidates = level % KeystoneEvery == 0 && level > 0 ? KeystoneCandidates() : new List<Candidate>();
        if (candidates.Count == 0)
        {
            candidates = RegularCandidates();
        }

        var offers = new List<UpgradeOffer>();
        while (offers.Count < count && candidates.Count > 0)
        {
            float total = candidates.Sum(c => c.Weight);
            float roll = rng.Randf() * total;
            Candidate pick = candidates[^1];
            foreach (Candidate c in candidates)
            {
                roll -= c.Weight;
                if (roll <= 0f)
                {
                    pick = c;
                    break;
                }
            }

            offers.Add(pick.Make(rng));
            candidates.RemoveAll(c => c.Key == pick.Key);
        }

        return offers;
    }

    private List<Candidate> KeystoneCandidates() =>
        UpgradeCatalog.Keystones
            .Where(k => !_keystones.Contains(k.Id))
            .Select(k => new Candidate
            {
                Weight = 1f,
                Key = "k:" + k.Id,
                Make = _ => new UpgradeOffer
                {
                    Kind = UpgradeKind.Keystone,
                    Id = k.Id,
                    Title = k.Name,
                    Subtitle = "Keystone",
                    Description = k.Description,
                    Icon = LoadIcon("icon_keystone"),
                    Apply = () => { AddKeystone(k); Log($"Keystone: {k.Name}"); },
                },
            })
            .ToList();

    private List<Candidate> RegularCandidates()
    {
        var list = new List<Candidate>();
        List<SkillInstance> owned = Skills.ToList();

        if (owned.Count < MaxSkills)
        {
            foreach (SkillEntry entry in UpgradeCatalog.Skills)
            {
                if (FindSkill(entry.Id) is not null)
                {
                    continue;
                }

                list.Add(new Candidate
                {
                    Weight = owned.Count <= 1 ? 3f : 1.6f,
                    Key = "s:" + entry.Id,
                    Make = _ => new UpgradeOffer
                    {
                        Kind = UpgradeKind.NewSkill,
                        Id = entry.Id,
                        Title = entry.Definition.DisplayName,
                        Subtitle = "New skill",
                        Description = entry.Definition.Description,
                        Icon = entry.Definition.Icon,
                        Apply = () => { AddSkill(entry.Id); Log($"Skill: {entry.Definition.DisplayName}"); },
                    },
                });
            }
        }

        foreach (SkillInstance skill in owned)
        {
            if (skill.Level < MaxSkillLevel)
            {
                SkillInstance s = skill;
                list.Add(new Candidate
                {
                    Weight = 1.4f,
                    Key = "l:" + skill.Definition.DisplayName,
                    Make = _ => new UpgradeOffer
                    {
                        Kind = UpgradeKind.SkillLevel,
                        Id = UpgradeCatalog.SkillIdOf(s.Definition),
                        Title = $"{s.Definition.DisplayName}  Lv {s.Level + 1}",
                        Subtitle = "Skill level",
                        Description = $"+{Mathf.RoundToInt(SkillInstance.DamagePerLevel * 100)}% more damage than the previous level.",
                        Icon = s.Definition.Icon,
                        Apply = () => { s.Level++; Log($"{s.Definition.DisplayName} → Lv {s.Level}"); },
                    },
                });
            }

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
                list.Add(new Candidate
                {
                    Weight = 0.55f,
                    Key = $"g:{sup.Id}:{skill.Definition.DisplayName}",
                    Make = _ => new UpgradeOffer
                    {
                        Kind = UpgradeKind.Support,
                        Id = sup.Id,
                        Title = sup.Name,
                        Subtitle = $"Support → {s.Definition.DisplayName}",
                        Description = sup.Description,
                        Icon = LoadIcon("icon_support"),
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
                        Subtitle = p.Scales ? $"{rarity} passive" : "Notable passive",
                        Description = p.Describe(mult),
                        Icon = LoadIcon("icon_passive"),
                        Apply = () => { AddPassive(p, mult); Log($"{p.Name}: {p.Describe(mult).Replace('\n', ' ')}"); },
                    };
                },
            });
        }

        return list;
    }

    public static UpgradeRarity RollRarity(RandomNumberGenerator rng)
    {
        float r = rng.Randf();
        return r < 0.07f ? UpgradeRarity.Rare : r < 0.32f ? UpgradeRarity.Magic : UpgradeRarity.Common;
    }

    public static float RarityMultiplier(UpgradeRarity rarity) => rarity switch
    {
        UpgradeRarity.Rare => 2.2f,
        UpgradeRarity.Magic => 1.5f,
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
