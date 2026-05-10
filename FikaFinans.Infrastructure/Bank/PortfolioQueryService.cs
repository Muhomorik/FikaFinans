using FikaFinans.Application.Bank;
using FikaFinans.Application.Bank.Events;
using FikaFinans.Domain.Bank.Common;
using FikaFinans.Infrastructure.Bank.Persistence;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace FikaFinans.Infrastructure.Bank;

public class PortfolioQueryService : IPortfolioQueryService
{
    private readonly ILogger _logger;
    private readonly BankDbContext _db;
    private readonly ILedgerService _ledgerService;

    public PortfolioQueryService(ILogger logger, BankDbContext db, ILedgerService ledgerService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _ledgerService = ledgerService ?? throw new ArgumentNullException(nameof(ledgerService));
    }

    public async Task<Money> GetAvailableCashAsync(CancellationToken ct = default)
    {
        var cashAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1000", ct);
        if (cashAccount == null)
            return Money.Zero();
        return await _ledgerService.GetAccountBalanceAsync(cashAccount.Id, ct);
    }

    public async Task<IReadOnlyList<FundPositionDto>> GetFundPositionsAsync(CancellationToken ct = default)
    {
        var holdings = await _db.FundHoldings.Where(h => h.Units > 0).ToListAsync(ct);
        var positions = new List<FundPositionDto>();

        foreach (var holding in holdings)
        {
            var fund = await _db.Funds
                .Include(f => f.NavHistory)
                .FirstOrDefaultAsync(f => f.Id == holding.FundId, ct);
            if (fund == null) continue;

            var currentNav = fund.GetLatestNav();
            var currentValue = holding.GetCurrentValue(currentNav);
            var costBasis = new Money(holding.TotalCostBasis, holding.Currency);
            var unrealizedGainLoss = holding.GetUnrealizedGainLoss(currentNav);
            var gainLossPercent = holding.TotalCostBasis > 0
                ? unrealizedGainLoss.Amount / holding.TotalCostBasis * 100
                : 0;

            positions.Add(new FundPositionDto(
                holding.FundId, fund.Name, fund.Isin, holding.Units,
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
