#nullable enable
using System.Collections.Generic;
using Godot;

namespace PathToExile.Items;

/// <summary>
/// docs/design/items.md -> "Currency": <c>Player.Wallet : Dictionary&lt;CurrencyId,
/// long&gt;</c>. A separate component node rather than a field on
/// <see cref="Player"/> itself, consistent with how <see cref="Stats.ActorStats"/>
/// and <see cref="Skills.SkillUser"/> are sibling nodes rather than growing
/// <see cref="Player"/> into a god object.
/// </summary>
[GlobalClass]
public partial class Wallet : Node
{
    /// <summary>Nothing consumes this yet (no currency UI exists in milestone 1),
    /// but it costs nothing to emit and it's the seam a future HUD reads instead
    /// of polling. String rather than <see cref="CurrencyId"/> in the signal
    /// signature because Godot signals marshal through Variant, which a custom
    /// struct doesn't participate in.</summary>
    [Signal]
    public delegate void BalanceChangedEventHandler(string currencyId, long newBalance);

    private readonly Dictionary<CurrencyId, long> _balances = new();

    public long GetBalance(CurrencyId id) => _balances.TryGetValue(id, out long value) ? value : 0L;

    /// <summary>
    /// The one and only way currency enters the wallet. Called from
    /// <see cref="CurrencyDrop.OnPickup"/> on walk-over — never from label or
    /// filter code, per docs/design/items.md -> "Filters control display, never
    /// behaviour".
    /// </summary>
    public void Add(CurrencyId id, long amount)
    {
        long newBalance = GetBalance(id) + amount;
        _balances[id] = newBalance;
        EmitSignal(SignalName.BalanceChanged, id.Value, newBalance);
    }

    /// <summary>Spends <paramref name="amount"/> if the balance covers it.</summary>
    public bool TrySpend(CurrencyId id, long amount)
    {
        if (amount < 0 || GetBalance(id) < amount)
        {
            return false;
        }

        Add(id, -amount);
        return true;
    }
}
