using System.Diagnostics;

namespace FikaFinans.Domain.Bank.Common;

[DebuggerDisplay("{Amount} {Currency}")]
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money SEK(decimal amount) => new(amount, "SEK");
    public static Money Zero(string currency = "SEK") => new(0, currency);
    public Money Negate() => new(-Amount, Currency);

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount + b.Amount, a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount - b.Amount, a.Currency);
    }

    public static Money operator *(Money m, decimal factor) => new(m.Amount * factor, m.Currency);
    public static Money operator *(decimal factor, Money m) => new(m.Amount * factor, m.Currency);

    public static bool operator >(Money a, Money b) { EnsureSameCurrency(a, b); return a.Amount > b.Amount; }
    public static bool operator <(Money a, Money b) { EnsureSameCurrency(a, b); return a.Amount < b.Amount; }
    public static bool operator >=(Money a, Money b) { EnsureSameCurrency(a, b); return a.Amount >= b.Amount; }
    public static bool operator <=(Money a, Money b) { EnsureSameCurrency(a, b); return a.Amount <= b.Amount; }

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (a.Currency != b.Currency)
            throw new InvalidOperationException($"Cannot operate on different currencies: {a.Currency} vs {b.Currency}");
    }

    public override string ToString() => $"{Amount:N2} {Currency}";
}
