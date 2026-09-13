using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Run;

/// <summary>
/// Mints the identifier for one per-ISIN pipeline run. A seam rather than a static
/// helper because the id carries randomness, which tests need to pin down.
/// </summary>
public interface IPipelineRunIdFactory
{
    /// <summary>
    /// Mints an id for the run about to start on <paramref name="isin"/> for the trading
    /// date that triggered it. Called once per successful claim — never for a signal that
    /// loses the contention race, since that starts no run.
    /// </summary>
    PipelineRunId NewRunId(Isin isin, DateTimeOffset navDate);
}
