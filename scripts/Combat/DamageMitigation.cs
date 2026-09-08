using Godot;

namespace PathToExile.Combat;

/// <summary>
/// The pure mitigation math from docs/design/stats.md -> "Mitigation". Kept as
/// static, stateless functions — rather than inlined into
/// <see cref="Health.TakeDamage"/> — so the armour formula and the
/// resistance/overcap/penetration formula are directly unit-testable without a
/// scene tree. See scripts/tests/StatsSmokeTest.cs.
/// </summary>
public static class DamageMitigation
{
    /// <summary>Genre-standard resistance cap.</summary>
    public const float ResistanceCap = 75f;

    /// <summary>Resistance can go negative (taking extra damage) but never past
    /// this, so a stack of penetration can never invert damage into healing.</summary>
    public const float ResistanceFloor = -100f;

    /// <summary>
    /// PoE's armour formula: <c>armour / (armour + 10 * physicalDamage)</c>.
    /// Strong against many small hits, weak against one large hit, and — the
    /// reason this formula was chosen over a flat-reduction or D2-style
    /// to-hit model — it stays sane without a hard cap. See docs/design/stats.md
    /// -> "Physical: Armour".
    /// </summary>
    public static float MitigatePhysical(float amount, float armour)
    {
        if (amount <= 0f || armour <= 0f)
        {
            return amount;
        }

        float reduction = armour / (armour + 10f * amount);
        return amount * (1f - reduction);
    }

    /// <summary>
    /// Resistance capped at <see cref="ResistanceCap"/> and floored at
    /// <see cref="ResistanceFloor"/>. Overcap — resistance banked above the cap —
    /// is not discarded: penetration eats it first, before it ever touches the
    /// capped, damage-reducing portion of the resistance. See
    /// docs/design/stats.md -> "Elemental: resistances".
    /// </summary>
    public static float MitigateElemental(float amount, float resistance, float penetration)
    {
        if (amount <= 0f)
        {
            return amount;
        }

        float overcap = Overcap(resistance);
        float penetrationAfterOvercap = Mathf.Max(0f, penetration - overcap);
        float cappedResistance = Mathf.Min(resistance, ResistanceCap);
        float effective = Mathf.Clamp(cappedResistance - penetrationAfterOvercap, ResistanceFloor, ResistanceCap);

        return amount * (1f - effective / 100f);
    }

    /// <summary>How much of this resistance value sits above the cap and is
    /// therefore not currently reducing damage — the number PoE's "cap your
    /// resists, then overcap them" gearing loop is built around making visible.
    /// See docs/design/stats.md -> "Overcap is tracked, not discarded."</summary>
    public static float Overcap(float resistance) => Mathf.Max(0f, resistance - ResistanceCap);
}
