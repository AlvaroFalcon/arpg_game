#nullable enable
using System;

namespace ArpgGame.Items;

/// <summary>
/// Typed key for <see cref="Wallet"/>'s balance dictionary and
/// <see cref="ItemDescriptor.CurrencyId"/> — docs/design/items.md -> "Currency":
/// <c>Player.Wallet : Dictionary&lt;CurrencyId, long&gt;</c>.
///
/// A thin wrapper over the string authored on <see cref="CurrencyDefinition.Id"/>,
/// not a C# enum. An enum would mean every new currency (a future crafting
/// currency, per the design doc) needs a recompile of this type, which directly
/// contradicts "built generically from the start." Implicitly convertible from
/// <see cref="string"/> so call sites can pass a <see cref="CurrencyDefinition"/>'s
/// raw <c>Id</c> field wherever a <see cref="CurrencyId"/> is expected, with no
/// explicit construction ceremony.
/// </summary>
public readonly struct CurrencyId : IEquatable<CurrencyId>
{
    public string Value { get; }

    public CurrencyId(string value)
    {
        Value = value;
    }

    public bool Equals(CurrencyId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is CurrencyId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value;

    public static implicit operator CurrencyId(string value) => new(value);

    public static bool operator ==(CurrencyId left, CurrencyId right) => left.Equals(right);

    public static bool operator !=(CurrencyId left, CurrencyId right) => !left.Equals(right);
}
