using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Progress;

/// <summary>
/// Seam over the per-ISIN progress row: the in-flight lock a step takes before it reads
/// anything, and the store it writes its output to when it is done.
/// </summary>
/// <remarks>
/// Split out for the same reason as the fetch seams: a step knows it needs the fund to
/// itself and has a result to record, not that both live in one Tables row keyed by ISIN.
/// </remarks>
public interface IIsinProgressStore
{
    /// <summary>
    /// Moves this ISIN's row to in-flight and stamps the trading date the run is working
    /// on, clearing the previous run's step outputs. The durable dedup anchor survives.
    /// </summary>
    /// <param name="isin">The fund to claim.</param>
    /// <param name="navDate">Trading date of the signal that raised the run.</param>
    /// <returns>
    /// The row as it now stands, held by this run — or <c>null</c> when another run already
    /// holds it, which means: do not start.
    /// </returns>
    Task<IsinProgressEntity?> ClaimAsync(Isin isin, DateTimeOffset navDate, CancellationToken ct = default);

    /// <summary>
    /// Stores this fund's record from <paramref name="output"/> in the column
    /// <paramref name="step"/> owns, and names the run that produced it.
    /// </summary>
    /// <param name="isin">The fund whose row is written; also selects its record.</param>
    /// <param name="step">Decides which column receives the record.</param>
    /// <param name="runId">Stamped on the row — readers match outputs to a run by it.</param>
    /// <param name="output">The step's result. Records for other funds are ignored.</param>
    /// <returns>The row as it now stands, output written.</returns>
    Task<IsinProgressEntity> SaveStepOutputAsync(
        Isin isin, StepId step, PipelineRunId runId, DataLoaderOutput output, CancellationToken ct = default);
}
