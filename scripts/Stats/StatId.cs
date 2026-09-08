namespace PathToExile.Stats;

/// <summary>
/// Identifies which named <see cref="Stat"/> on an <see cref="ActorStats"/> a
/// <see cref="ModifierQuery"/> (or <see cref="ActorStats.Get"/>) is asking about.
/// Grew from five entries to the full eighteen in docs/design/stats.md's second
/// pass — see "The registry": named C# properties (<c>stats.AttackSpeed</c>)
/// can't be passed around as values without a parallel key like this, and
/// eighteen stats is exactly the number that forces a keyed
/// <see cref="ActorStats"/> registry rather than one property per stat.
///
/// Deliberately does *not* include Life/Mana *current* amounts: those stay
/// <see cref="ResourcePool"/>s (a plain mutable <c>Current</c> plus a Max/Regen
/// pair backed by <see cref="MaxLife"/>/<see cref="LifeRegen"/> and
/// <see cref="MaxMana"/>/<see cref="ManaRegen"/>), not bare modifier-stack
/// <see cref="Stat"/>s — see docs/design/stats.md -> "Resource pools become
/// stat-backed".
///
/// <b>Values are explicit and must never be reused or renumbered.</b> A
/// <see cref="StatBlock"/> <c>.tres</c> serialises this enum as its integer value
/// (<c>Id = 5</c>, not <c>Id = "MaxLife"</c>), so with implicit ordinals, inserting
/// or deleting an entry silently rebinds every authored StatBlock to a different
/// stat — a dummy's "105 maximum life" becomes "105 life regeneration", with no
/// error and no crash, just numbers landing on the wrong stat. Pinning the values
/// makes a deletion leave a permanent hole instead, which is harmless. Add new
/// stats with the next unused number; when removing one, leave its number retired
/// rather than filling the gap.
/// </summary>
public enum StatId
{
    MovementSpeed = 0,
    Damage = 1,
    AttackSpeed = 2,
    CastSpeed = 3,
    CooldownRecovery = 4,
    MaxLife = 5,
    LifeRegen = 6,
    MaxMana = 7,
    ManaRegen = 8,
    Armour = 9,
    FireResistance = 10,
    ColdResistance = 11,
    LightningResistance = 12,
    FirePenetration = 13,
    ColdPenetration = 14,
    LightningPenetration = 15,
    CritChance = 16,
    CritMultiplier = 17,

    // Third pass (docs/design/stats.md -> "Attributes and derived bonuses",
    // docs/design/affixes.md -> "Attributes"): three real attributes plus two
    // more modifier-carrier stats an affix pool needed to stop rolling
    // near-duplicates. Appended, never inserted — see this enum's own doc
    // comment on why values are pinned.
    Strength = 18,
    Dexterity = 19,
    Intelligence = 20,
    ManaCostMultiplier = 21,
    ThornsDamage = 22,
}
