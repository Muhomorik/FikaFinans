namespace FikaFinans.Domain.Bank.Identifiers;

public readonly record struct TradingOrderId(Guid Value)
{
    public static TradingOrderId NewId() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
