using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Application.Pipeline.Progress;

/// <summary>
/// Claim seam for the per-ISIN in-flight lock. Step 1 takes the lock before it reads
/// anything; the later steps carry it until the run releases it.
/// </summary>
/// <remarks>
/// Split out for the same reason as the fetch seams: the step knows it needs the fund
/// to itself, not that the answer lives in a single Tables partition keyed by ISIN.
/// </remarks>
public interface IIsinProgressClaim
{
    /// <summary>
    /// Moves this ISIN's row to in-flight and stamps the trading date the run is working
    /// on, clearing the previous run's step outputs. The durable dedup anchor survives.
    /// </summary>
    /// <param name="isin">The fund to claim.</param>
    /// <param name="navDate">Trading date of the signal that raised the run.</param>
    /// <returns><c>false</c> when a run already holds the row, which means: do not start.</returns>
    Task<bool> TryClaimAsync(Isin isin, DateTimeOffset navDate, CancellationToken ct = default);
}
