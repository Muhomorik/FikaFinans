using FikaFinans.Domain.KanelBulleKapital;
using FikaFinans.Domain.Macro;

namespace FikaFinans.Application.Pipeline.Macro;

/// <summary>
/// In-process single-flight around step 3's compute: callers asking for the same key while
/// a compute is running share it instead of starting their own. Keyed by the weekly
/// summary's <c>runId</c>, so ten funds of a new week missing together make one LLM call.
/// </summary>
/// <remarks>
/// <para>
/// One process only — share one instance across every step 3 handler. Two cloud hosts can
/// still compute the same week twice; that case is open in the step 3 plan.
/// </para>
/// <para>
/// The entry is dropped once its compute ends, success or failure, so a failed compute is
/// not remembered: the next caller tries again. A caller arriving after a success finds the
/// context in the store, so the compute should re-check the store before calling the LLM.
/// </para>
/// </remarks>
public sealed class MacroContextSingleFlight
{
    private readonly Lock _gate = new();
    private readonly Dictionary<WeeklySummaryRunId, Task<MacroContext>> _inFlight = new();

    /// <summary>
    /// Runs <paramref name="compute"/> for <paramref name="key"/>, or joins the one already
    /// running for it.
    /// </summary>
    /// <param name="key">The weekly summary's <c>runId</c>.</param>
    /// <param name="compute">Started only when nothing is running for the key.</param>
    /// <param name="ct">
    /// Stops this caller's wait. The compute itself runs on the first caller's token.
    /// </param>
    public Task<MacroContext> RunAsync(
        WeeklySummaryRunId key, Func<CancellationToken, Task<MacroContext>> compute, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key.Value);
        ArgumentNullException.ThrowIfNull(compute);

        Task<MacroContext> running;

        lock (_gate)
        {
            if (!_inFlight.TryGetValue(key, out running!))
            {
                running = ComputeThenReleaseAsync(key, compute, ct);
                _inFlight[key] = running;
            }
        }

        return running.WaitAsync(ct);
    }

    private async Task<MacroContext> ComputeThenReleaseAsync(
        WeeklySummaryRunId key, Func<CancellationToken, Task<MacroContext>> compute, CancellationToken ct)
    {
        // Yields first, so the entry is always in the dictionary before the release below
        // can run — a compute that finished synchronously would otherwise leave it there.
        await Task.Yield();

        try
        {
            return await compute(ct).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _inFlight.Remove(key);
            }
        }
    }
}
