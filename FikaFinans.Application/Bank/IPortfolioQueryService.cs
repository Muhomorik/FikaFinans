using FikaFinans.Application.Bank.Events;
using FikaFinans.Domain.Bank.Common;

namespace FikaFinans.Application.Bank;

public interface IPortfolioQueryService
{
    Task<Money> GetAvailableCashAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FundPositionDto>> GetFundPositionsAsync(CancellationToken ct = default);
    Task<Money> GetTotalPortfolioValueAsync(CancellationToken ct = default);
}
