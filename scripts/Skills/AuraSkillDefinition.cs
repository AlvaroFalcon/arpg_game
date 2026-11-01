#nullable enable
using System.Collections.Generic;
using Godot;
using ArpgGame.Combat;
using ArpgGame.Stats;

namespace ArpgGame.Skills;

/// <summary>The seven auras of 2026-11-01 — docs/design/skills.md -> "Auras".</summary>
public enum AuraKind
{
    /// <summary>A toggle: while on, burns enemies around the caster and drains its life.</summary>
    Immolation,

    /// <summary>+resistance to fire, cold and lightning; the active raises it a lot for a while.</summary>
    ElementalWard,

    /// <summary>More elemental damage; its tree picks fire, cold or lightning to focus on.</summary>
    ElementalStorm,

    /// <summary>Critical strike chance and multiplier.</summary>
    Precision,

    /// <summary>Physical attacks can bleed, bleeds last longer; the active stacks them faster.</summary>
    Bloodthirst,

    /// <summary>Armour; the active adds more and makes the caster immovable.</summary>
    Titan,

    /// <summary>Maximum life; the active heals and cuts the damage taken for a while.</summary>
    Vitality,
}

/// <summary>One stat an aura grants: a <see cref="StatModifier"/> recipe.</summary>
public readonly record struct AuraStat(StatId Stat, ModifierType Type, float Value, SkillTags ConditionAny = SkillTags.None);

/// <summary>
/// The per-cast aura knobs the aura's tree writes into (<see cref="CastContext.Aura"/>):
/// what it grants while it sits in a slot (<see cref="Passive"/>) and while its active
/// lasts (<see cref="Active"/>; for Immolation, while it is switched on).
/// </summary>
public sealed class AuraParams
{
    public readonly List<AuraStat> Passive = new();
    public readonly List<AuraStat> Active = new();

    /// <summary>Multiplies every passive / active value (gem level, tree "Potency" nodes).</summary>
    public float PassiveMultiplier = 1f;
    public float ActiveMultiplier = 1f;

    /// <summary>Immolation: multiplier on the life it drains per second.</summary>
    public float LifeDrainMultiplier = 1f;

    /// <summary>Vitality: fraction of maximum life the active heals at once.</summary>
    public float HealFraction;

    /// <summary>Titan: the passive alone already stops knockback (its "Immovable" notable).</summary>
    public bool PassiveUnstoppable;

    /// <summary>Elemental Storm: the one element its bonuses go to (None = all three).</summary>
    public SkillTags Focus;
}

/// <summary>
/// The Aura archetype (owner's brief, 2026-11-01): a skill with a <b>passive</b> effect
/// that holds while the gem sits in any skill slot, and an <b>active</b> one the button
/// fires — a stronger version of the passive for <see cref="ActiveDuration"/> seconds, then
/// the cooldown. Immolation is the exception: the button switches it on and off.
/// The numbers per kind live in <see cref="AuraCatalog"/>; the definition carries the
/// shared ones (duration, radius, tint), so the .tres stays editable.
/// </summary>
[GlobalClass]
public partial class AuraSkillDefinition : SkillDefinition
{
    [Export]
    public AuraKind Kind;

    /// <summary>Seconds the active lasts (unused by Immolation).</summary>
    [Export]
    public float ActiveDuration = 6f;

    /// <summary>Immolation: the burning circle's radius.</summary>
    [Export]
    public float Radius = 110f;

    /// <summary>Immolation: % of maximum life lost per second while on.</summary>
    [Export]
    public float LifeDrainPercent = 2f;

    [Export]
    public Color Tint = new(1f, 0.85f, 0.4f);

    /// <summary>Immolation is switched on and off rather than fired.</summary>
    public bool IsToggle => Kind == AuraKind.Immolation;
}

/// <summary>The base numbers of each aura (level 1, before its tree).</summary>
public static class AuraCatalog
{
    /// <summary>Each gem level past the first makes the aura's numbers 3% stronger
    /// (+57% at level 20), the auras' equivalent of the damage per level.</summary>
    public const float EffectPerLevel = 0.03f;

    private const SkillTags Elements = SkillTags.Fire | SkillTags.Cold | SkillTags.Lightning;

    /// <summary>Fills a fresh <see cref="AuraParams"/> with the kind's base passive and active.</summary>
    public static void Seed(AuraKind kind, AuraParams a)
    {
        switch (kind)
        {
            case AuraKind.Immolation:
                // Nothing passive: it is a stance you switch on.
                break;
            case AuraKind.ElementalWard:
                foreach (StatId res in new[] { StatId.FireResistance, StatId.ColdResistance, StatId.LightningResistance })
                {
                    a.Passive.Add(new AuraStat(res, ModifierType.Flat, 10f));
                    a.Active.Add(new AuraStat(res, ModifierType.Flat, 40f));
                }

                break;
            case AuraKind.ElementalStorm:
                a.Passive.Add(new AuraStat(StatId.Damage, ModifierType.Increased, 15f, Elements));
                a.Active.Add(new AuraStat(StatId.Damage, ModifierType.More, 30f, Elements));
                break;
            case AuraKind.Precision:
                a.Passive.Add(new AuraStat(StatId.CritChance, ModifierType.Flat, 0.03f));
                a.Passive.Add(new AuraStat(StatId.CritMultiplier, ModifierType.Flat, 0.2f));
                a.Active.Add(new AuraStat(StatId.CritChance, ModifierType.Flat, 0.1f));
                a.Active.Add(new AuraStat(StatId.CritMultiplier, ModifierType.Flat, 0.6f));
                break;
            case AuraKind.Bloodthirst:
                a.Passive.Add(new AuraStat(StatId.BleedChance, ModifierType.Flat, 0.15f));
                a.Passive.Add(new AuraStat(StatId.BleedDuration, ModifierType.Increased, 25f));
                a.Active.Add(new AuraStat(StatId.BleedStacksPerHit, ModifierType.Flat, 2f));
                a.Active.Add(new AuraStat(StatId.BleedChance, ModifierType.Flat, 0.2f));
                break;
            case AuraKind.Titan:
                a.Passive.Add(new AuraStat(StatId.Armour, ModifierType.Flat, 40f));
                a.Passive.Add(new AuraStat(StatId.Armour, ModifierType.Increased, 30f));
                a.Active.Add(new AuraStat(StatId.Armour, ModifierType.More, 80f));
                a.Active.Add(new AuraStat(StatId.KnockbackImmunity, ModifierType.Flat, 1f));
                break;
            case AuraKind.Vitality:
                a.Passive.Add(new AuraStat(StatId.MaxLife, ModifierType.Increased, 12f));
                a.Active.Add(new AuraStat(StatId.DamageTaken, ModifierType.More, -30f));
                a.HealFraction = 0.25f;
                break;
        }
    }

    /// <summary>Whether a stat's value scales with the aura's multipliers. Knockback
    /// immunity is a switch, not an amount.</summary>
    public static bool Scales(StatId stat) => stat != StatId.KnockbackImmunity && stat != StatId.BleedStacksPerHit;

    /// <summary>The modifier an aura stat becomes after its multiplier (and Storm's focus).</summary>
    public static StatModifier Build(AuraStat s, float multiplier, SkillTags focus, object source)
    {
        float value = Scales(s.Stat) ? s.Value * multiplier : s.Value;
        SkillTags any = s.ConditionAny;
        if (focus != SkillTags.None && any == Elements)
        {
            // Focused on one element: twice the bonus, for that element only.
            any = focus;
            value *= 2f;
        }

        return new StatModifier(s.Type, value, source, SkillTags.None, any);
    }

    private static string Num(AuraStat s, float m)
    {
        float v = Scales(s.Stat) ? s.Value * m : s.Value;
        return s.Stat switch
        {
            StatId.CritChance or StatId.BleedChance or StatId.IgniteChance or StatId.FreezeChance or StatId.ShockChance => $"+{Mathf.Round(v * 1000f) / 10f}%",
            StatId.CritMultiplier => $"+{Mathf.RoundToInt(v * 100f)}%",
            _ when s.Type == ModifierType.Flat => $"+{Mathf.Round(v * 10f) / 10f}",
            _ => (v >= 0f ? "+" : "") + $"{Mathf.RoundToInt(v)}%",
        };
    }

    /// <summary>"+10 Fire Resistance", "30% more Elemental Damage"...</summary>
    public static string Describe(AuraStat s, float multiplier, SkillTags focus)
    {
        string what = s.Stat switch
        {
            StatId.FireResistance => "fire resistance",
            StatId.ColdResistance => "cold resistance",
            StatId.LightningResistance => "lightning resistance",
            StatId.Damage => focus switch
            {
                SkillTags.Fire => "fire damage",
                SkillTags.Cold => "cold damage",
                SkillTags.Lightning => "lightning damage",
                _ => s.ConditionAny == Elements ? "elemental damage" : "damage",
            },
            StatId.CritChance => "critical strike chance",
            StatId.CritMultiplier => "critical strike multiplier",
            StatId.BleedChance => "chance for physical attacks to cause bleeding",
            StatId.BleedDuration => "bleed duration",
            StatId.BleedStacksPerHit => "bleed stacks per bleed",
            StatId.Armour => "armour",
            StatId.KnockbackImmunity => "",
            StatId.MaxLife => "maximum life",
            StatId.DamageTaken => "damage taken",
            StatId.LifeRegen => "life regeneration per second",
            StatId.MovementSpeed => "movement speed",
            StatId.AttackSpeed => "attack speed",
            StatId.CastSpeed => "cast speed",
            StatId.IgniteChance => "chance to ignite",
            StatId.FreezeChance => "chance to freeze",
            StatId.ShockChance => "chance to shock",
            _ => s.Stat.ToString(),
        };
        if (s.Stat == StatId.KnockbackImmunity)
        {
            return "Cannot be knocked back";
        }

        float v = Scales(s.Stat) ? s.Value * multiplier * (focus != SkillTags.None && s.ConditionAny == Elements ? 2f : 1f) : s.Value;
        var shown = s with { Value = v };
        string kind = s.Type switch { ModifierType.More => v >= 0f ? " more" : " less", ModifierType.Increased => " increased", _ => "" };
        string number = s.Type == ModifierType.More ? $"{Mathf.Abs(Mathf.RoundToInt(v))}%" : Num(shown, 1f);
        return $"{number}{kind} {what}";
    }
}
