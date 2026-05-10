namespace FikaFinans.Domain.Bank.Identifiers;

public readonly record struct AccountId(Guid Value)
{
    public static AccountId NewId() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
