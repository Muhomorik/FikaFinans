namespace FikaFinans.Domain.Bank.Identifiers;

public readonly record struct JournalEntryId(Guid Value)
{
    public static JournalEntryId NewId() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
