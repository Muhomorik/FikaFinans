using FikaFinans.Domain.Portfolio;

namespace FikaFinans.Application.Bank;

/// <summary>
/// Translates a Step 10 <see cref="TradesOutput"/> into bank-sim trading
/// orders via <see cref="ITradingService"/>. The submit loop used to live in
/// <c>Step10PortfolioConstructorViewModel</c>; lifting it here lets the same
/// logic be reused by the eventual Step 10 timer-triggered Function
/// (see <see href="../../../Docs/backend-nav-sync-plan.md">backend-nav-sync-plan.md</see>
/// §"Step 10 — Daily Portfolio Trades") without dragging WPF along.
/// </summary>
public interface ISendToBankService
{
    Task<SendToBankResult> SubmitAsync(TradesOutput trades, CancellationToken ct = default);
}
