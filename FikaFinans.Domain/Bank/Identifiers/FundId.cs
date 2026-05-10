namespace FikaFinans.Domain.Bank.Identifiers;

public readonly record struct FundId(Guid Value)
{
    public static FundId NewId() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
