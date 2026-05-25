using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Per-fund result returned by an agent's <c>ProcessFund</c>/<c>ProcessFundAsync</c>
/// method when processing emits universe-level warnings that the orchestrator
/// must fold back into <see cref="DataQuality.Warnings"/>. Agents whose
/// per-fund warnings live inside the <see cref="FundRecord"/> itself (e.g.
/// MetricsCalculator, SignalScorer) return a bare <see cref="FundRecord"/>
/// instead.
/// </summary>
public sealed record FundProcessingResult(
    FundRecord Fund,
    IReadOnlyList<string> Warnings);
