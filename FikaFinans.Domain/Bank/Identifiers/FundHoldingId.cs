namespace FikaFinans.Domain.Bank.Identifiers;

public readonly record struct FundHoldingId(Guid Value)
{
    public static FundHoldingId NewId() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
