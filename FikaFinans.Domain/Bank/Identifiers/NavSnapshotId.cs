namespace FikaFinans.Domain.Bank.Identifiers;

public readonly record struct NavSnapshotId(Guid Value)
{
    public static NavSnapshotId NewId() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
