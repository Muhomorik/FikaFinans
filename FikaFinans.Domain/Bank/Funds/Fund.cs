using System.Diagnostics;
using FikaFinans.Domain.Bank.Identifiers;
using FluentResults;
using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Domain.Bank.Funds;

[DebuggerDisplay("{Name} ({Isin}) - NAV: {GetLatestNav()}")]
public class Fund
{
    public FundId Id { get; private init; }
    public string Name { get; private init; } = string.Empty;
    public Isin Isin { get; private init; }
    public string Currency { get; private init; } = "SEK";

    private readonly List<NavSnapshot> _navHistory = new();
    public IReadOnlyList<NavSnapshot> NavHistory => _navHistory.AsReadOnly();

    private Fund() { }

    public static Fund Create(string name, string isin, string currency = "SEK")
    {
        return new Fund
        {
            Id = FundId.NewId(),
            Name = name,
            Isin = isin,
            Currency = currency
        };
    }

    public Result RecordNav(DateTimeOffset date, decimal navPerUnit)
    {
        if (navPerUnit <= 0)
            return Result.Fail("NAV per unit must be positive.");

        var lastNav = _navHistory.MaxBy(n => n.Date);
        if (lastNav != null && date <= lastNav.Date)
            return Result.Fail("NAV date must be after the last recorded NAV.");

        _navHistory.Add(NavSnapshot.Create(Id, date, navPerUnit));
        return Result.Ok();
    }

    public decimal GetLatestNav() => _navHistory.MaxBy(n => n.Date)?.NavPerUnit ?? 0;

    public decimal GetNavAt(DateTimeOffset date)
    {
        var snapshot = _navHistory.Where(n => n.Date <= date).MaxBy(n => n.Date);
        return snapshot?.NavPerUnit ?? 0;
    }

    /// <summary>
    /// Reconstruct a Fund from persisted storage. Skips the invariants
    /// enforced by <see cref="Create"/> and <see cref="RecordNav"/> —
    /// callers must guarantee the input is already valid.
    /// </summary>
    public static Fund Rehydrate(FundId id, string name, Isin isin, string currency, IEnumerable<NavSnapshot> navHistory)
    {
        var fund = new Fund
        {
            Id = id,
            Name = name,
            Isin = isin,
            Currency = currency
        };
        foreach (var nav in navHistory.OrderBy(n => n.Date))
            fund._navHistory.Add(nav);
        return fund;
    }
}
