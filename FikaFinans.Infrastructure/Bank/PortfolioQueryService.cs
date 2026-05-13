using FikaFinans.Application.Bank;
using FikaFinans.Application.Bank.Events;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Domain.Bank.Common;
using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Identifiers;
using NLog;

namespace FikaFinans.Infrastructure.Bank;

/// <summary>
/// Read-only portfolio queries. All reads flow through Tables-shaped
/// repos — holdings via <see cref="IPositionsRepository"/>, fund metadata
/// and NAV history via <see cref="IFundsRepository"/>. No direct EF.
/// </summary>
public class PortfolioQueryService : IPortfolioQueryService
{
    private const string PositionsPartition = "positions";
    private const string CashRowKey = "CASH";

    private readonly ILogger _logger;
    private readonly IAccountsRepository _accounts;
    private readonly IPositionsRepository _positions;
    private readonly IFundsRepository _funds;
    private readonly ILedgerService _ledgerService;

    public PortfolioQueryService(
        ILogger logger,
        IAccountsRepository accounts,
        IPositionsRepository positions,
        IFundsRepository funds,
        ILedgerService ledgerService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _positions = positions ?? throw new ArgumentNullException(nameof(positions));
        _funds = funds ?? throw new ArgumentNullException(nameof(funds));
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

        var allFunds = await _funds.QueryPartitionAsync("funds", ct);
        var fundsByIsin = allFunds.ToDictionary(f => f.Isin, StringComparer.Ordinal);

        var positions = new List<FundPositionDto>(holdings.Count);
        foreach (var h in holdings)
        {
            if (!fundsByIsin.TryGetValue(h.Isin, out var fund))
                continue;

            var currentNav = await _funds.GetLatestNavByFundIdAsync(new FundId(fund.FundId), ct) ?? 0m;
            var currency = "SEK";
            var currentValue = new Money(h.Units * currentNav, currency);
            var costBasis = new Money(h.CostBasisKr, currency);
            var unrealizedGainLoss = currentValue - costBasis;
            var gainLossPercent = h.CostBasisKr > 0
                ? unrealizedGainLoss.Amount / h.CostBasisKr * 100
                : 0;

            positions.Add(new FundPositionDto(
                new FundId(fund.FundId), fund.Name, new Isin(fund.Isin), h.Units,
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
