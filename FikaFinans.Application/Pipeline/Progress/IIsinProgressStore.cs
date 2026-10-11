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
    Task<IsinProgressEntity?> StartStep01Async(Isin isin, DateTimeOffset navDate, CancellationToken ct = default);

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

    /// <summary>
    /// Reads back every fund record one run wrote for <paramref name="step"/> — the same
    /// column <see cref="SaveStepOutputAsync"/> fills, across every ISIN in the run.
    /// </summary>
    /// <param name="step">Decides which column is read.</param>
    /// <param name="runId">Only rows naming this run are included.</param>
    /// <returns><c>null</c> when no row carries output for that run and step.</returns>
    Task<StepOutput?> ReadStepOutputAsync(
        StepId step, PipelineRunId runId, CancellationToken ct = default);

    /// <summary>
    /// Ends a run that failed in step 1. Frees the row and records why, leaving the dedup
    /// anchor where it was so the next signal retries the fund.
    /// </summary>
    /// <param name="isin">The fund whose row is freed.</param>
    /// <param name="error">Why the step failed; stored on the row.</param>
    /// <returns>The row as it now stands — or <c>null</c> when the fund has no row.</returns>
    Task<IsinProgressEntity?> FailStep01Async(Isin isin, string error, CancellationToken ct = default);

    /// <summary>
    /// Lists the orphaned runs at step 1: rows still holding a lock at step 1 that was taken
    /// before <paramref name="startedBefore"/> — by a process that has since ended, so
    /// nothing will ever complete or fail them.
    /// </summary>
    /// <param name="startedBefore">
    /// When the current process started. Locks taken since may belong to runs still going.
    /// </param>
    /// <returns>The orphaned rows; empty when there are none.</returns>
    Task<IReadOnlyList<IsinProgressEntity>> ListOrphanedStep01Async(
        DateTimeOffset startedBefore, CancellationToken ct = default);

    /// <summary>
    /// Records that this ISIN's run has reached step 2. A check and a mark, never a clear:
    /// step 1's output is step 2's input, so unlike <see cref="StartStep01Async"/> every
    /// column survives.
    /// </summary>
    /// <param name="isin">The fund whose row is marked.</param>
    /// <param name="runId">The run the step-1 signal named; the row must still carry it.</param>
    /// <returns>
    /// The row as it now stands — or <c>null</c> when it is not this run's to continue: no
    /// row, the run already ended, or a newer run has started since. Means: do not start.
    /// </returns>
    Task<IsinProgressEntity?> StartStep02Async(Isin isin, PipelineRunId runId, CancellationToken ct = default);

    /// <summary>
    /// Ends a run that failed in step 2. Frees the row and records why, leaving the dedup
    /// anchor where it was so the next signal retries the fund.
    /// </summary>
    /// <param name="isin">The fund whose row is freed.</param>
    /// <param name="error">Why the step failed; stored on the row.</param>
    /// <returns>The row as it now stands — or <c>null</c> when the fund has no row.</returns>
    Task<IsinProgressEntity?> FailStep02Async(Isin isin, string error, CancellationToken ct = default);

    /// <summary>
    /// Lists the orphaned runs at step 2: rows still holding a lock at step 2 that was taken
    /// before <paramref name="startedBefore"/> — by a process that has since ended, so
    /// nothing will ever complete or fail them.
    /// </summary>
    /// <param name="startedBefore">
    /// When the current process started. Locks taken since may belong to runs still going.
    /// </param>
    /// <returns>The orphaned rows; empty when there are none.</returns>
    Task<IReadOnlyList<IsinProgressEntity>> ListOrphanedStep02Async(
        DateTimeOffset startedBefore, CancellationToken ct = default);

    /// <summary>
    /// Records that this ISIN's run has reached step 3. A check and a mark, never a clear:
    /// step 2's output is step 3's input, so every column survives.
    /// </summary>
    /// <param name="isin">The fund whose row is marked.</param>
    /// <param name="runId">The run the step-2 signal named; the row must still carry it.</param>
    /// <returns>
    /// The row as it now stands — or <c>null</c> when it is not this run's to continue: no
    /// row, the run already ended, or a newer run has started since. Means: do not start.
    /// </returns>
    Task<IsinProgressEntity?> StartStep03Async(Isin isin, PipelineRunId runId, CancellationToken ct = default);

    /// <summary>
    /// Ends a successful run at step 3 — step 3 is the chain tail. Frees the row and
    /// advances the durable dedup anchor to the run's trading date.
    /// </summary>
    /// <param name="isin">The fund whose row is freed.</param>
    /// <returns>The row as it now stands — or <c>null</c> when the fund has no row.</returns>
    Task<IsinProgressEntity?> CompleteStep03Async(Isin isin, CancellationToken ct = default);

    /// <summary>
    /// Ends a run that failed in step 3. Frees the row and records why, leaving the dedup
    /// anchor where it was so the next signal retries the fund.
    /// </summary>
    /// <param name="isin">The fund whose row is freed.</param>
    /// <param name="error">Why the step failed; stored on the row.</param>
    /// <returns>The row as it now stands — or <c>null</c> when the fund has no row.</returns>
    Task<IsinProgressEntity?> FailStep03Async(Isin isin, string error, CancellationToken ct = default);

    /// <summary>
    /// Lists the orphaned runs at step 3: rows still holding a lock at step 3 that was taken
    /// before <paramref name="startedBefore"/> — by a process that has since ended, so
    /// nothing will ever complete or fail them.
    /// </summary>
    /// <param name="startedBefore">
    /// When the current process started. Locks taken since may belong to runs still going.
    /// </param>
    /// <returns>The orphaned rows; empty when there are none.</returns>
    Task<IReadOnlyList<IsinProgressEntity>> ListOrphanedStep03Async(
        DateTimeOffset startedBefore, CancellationToken ct = default);
}
