using FikaFinans.Application.Bank;
using FikaFinans.Application.Bank.Events;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Domain.Bank.Common;
using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Infrastructure.Bank.Persistence;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace FikaFinans.Infrastructure.Bank;

/// <summary>
/// Read-only portfolio queries. Holdings come from the Positions repo
/// (chunk 5); <see cref="Domain.Bank.Funds.Fund"/> NAV history still
/// goes via direct EF until Phase 5 retires it.
/// </summary>
public class PortfolioQueryService : IPortfolioQueryService
{
    private const string PositionsPartition = "positions";
    private const string CashRowKey = "CASH";

    private readonly ILogger _logger;
    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly IAccountsRepository _accounts;
    private readonly IPositionsRepository _positions;
    private readonly ILedgerService _ledgerService;

    public PortfolioQueryService(
        ILogger logger,
        IDbContextFactory<BankDbContext> dbFactory,
        IAccountsRepository accounts,
        IPositionsRepository positions,
        ILedgerService ledgerService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _positions = positions ?? throw new ArgumentNullException(nameof(positions));
        _ledgerService = ledgerService ?? throw new ArgumentNullException(nameof(ledgerService));
    }

    public async Task<Money> GetAvailableCashAsync(CancellationToken ct = default)
    {
        var cashAccount = await _accounts.GetByCodeAsync("1000", ct);
        if (cashAccount is null)
            return Money.Zero();
        return await _ledgerService.GetAccountBalanceAsync(new AccountId(cashAccount.AccountId), ct);
    }

    public async Task<IReadOnlyList<FundPositionDto>> GetFundPositionsAsync(CancellationToken ct = default)
    {
        var rows = await _positions.QueryPartitionAsync(PositionsPartition, ct);
        var holdings = rows.Where(r => r.RowKey != CashRowKey && r.Units > 0).ToList();
        if (holdings.Count == 0) return Array.Empty<FundPositionDto>();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var allFunds = await db.Funds.AsNoTracking().Include(f => f.NavHistory).ToListAsync(ct);
        var fundsByIsin = allFunds.ToDictionary(f => f.Isin.Value, f => f);

        var positions = new List<FundPositionDto>(holdings.Count);
        foreach (var h in holdings)
        {
            if (!fundsByIsin.TryGetValue(h.Isin, out var fund))
                continue;

            var currentNav = fund.GetLatestNav();
            var currency = "SEK";
            var currentValue = new Money(h.Units * currentNav, currency);
            var costBasis = new Money(h.CostBasisKr, currency);
            var unrealizedGainLoss = currentValue - costBasis;
            var gainLossPercent = h.CostBasisKr > 0
                ? unrealizedGainLoss.Amount / h.CostBasisKr * 100
                : 0;

            positions.Add(new FundPositionDto(
                fund.Id, fund.Name, fund.Isin, h.Units,
                currentValue, costBasis, unrealizedGainLoss, gainLossPercent));
        }

        return positions;
    }

    public async Task<Money> GetTotalPortfolioValueAsync(CancellationToken ct = default)
    {
        var cash = await GetAvailableCashAsync(ct);
        var positions = await GetFundPositionsAsync(ct);
        var fundValue = positions.Aggregate(Money.Zero(), (total, p) => total + p.CurrentValue);
        return cash + fundValue;
    }
}
