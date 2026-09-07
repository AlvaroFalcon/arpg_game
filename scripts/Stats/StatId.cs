namespace PathToExile.Stats;

/// <summary>
/// Identifies which named <see cref="Stat"/> on an <see cref="ActorStats"/> a
/// <see cref="ModifierQuery"/> is asking about. Named C# properties
/// (<c>stats.AttackSpeed</c>) can't be passed around as values without a parallel
/// key like this — see docs/design/stats.md's note on the "halved, not eliminated"
/// pressure toward a keyed stat registry. Deliberately does *not* include Life/Mana:
/// those are <see cref="ResourcePool"/>s (current/max/regen), not modifier-stack
/// Stats, in the first slice.
/// </summary>
public enum StatId
{
    MovementSpeed,
    Damage,
    AttackSpeed,
    CastSpeed,
    CooldownRecovery,
}
