using System.Diagnostics;
using FikaFinans.Domain.Bank.Identifiers;

namespace FikaFinans.Domain.Bank.Funds;

[DebuggerDisplay("NAV {NavPerUnit:N2} on {Date:yyyy-MM-dd}")]
public class NavSnapshot
{
    public NavSnapshotId Id { get; private init; }
    public FundId FundId { get; private init; }
    public DateTimeOffset Date { get; private init; }
    public decimal NavPerUnit { get; private init; }

    private NavSnapshot() { }

    internal static NavSnapshot Create(FundId fundId, DateTimeOffset date, decimal navPerUnit)
    {
        return new NavSnapshot
        {
            Id = NavSnapshotId.NewId(),
            FundId = fundId,
            Date = date,
            NavPerUnit = navPerUnit
        };
    }

    /// <summary>
    /// Reconstruct a NavSnapshot from persisted storage. Skips validation;
    /// callers must guarantee the input is already valid.
    /// </summary>
    public static NavSnapshot Rehydrate(NavSnapshotId id, FundId fundId, DateTimeOffset date, decimal navPerUnit)
    {
        return new NavSnapshot
        {
            Id = id,
            FundId = fundId,
            Date = date,
            NavPerUnit = navPerUnit
        };
    }
}
