using System.Diagnostics;

using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;

using NLog;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Default <see cref="IStep08RecommenderEntry"/>: calls step 8's phases in order and stops
/// when the start says the signal is late.
/// </summary>
/// <remarks>
/// A twin of <see cref="Step07ThesisValidatorEntry"/>. Takes a factory, not a handler — the
/// handler keeps one run's state in fields, so each signal needs its own.
/// </remarks>
public sealed class Step08RecommenderEntry : IStep08RecommenderEntry
{
    private readonly ILogger _logger;
    private readonly Func<IStep08Recommender> _newStep08;

    public Step08RecommenderEntry(ILogger logger, Func<IStep08Recommender> newStep08)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _newStep08 = newStep08 ?? throw new ArgumentNullException(nameof(newStep08));
    }

    /// <inheritdoc />
    public async Task HandleAsync(Step07DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var stopwatch = Stopwatch.StartNew();

        _logger.Debug(
            "Step 8 entry — isin={0}, navDate={1:yyyy-MM-dd}, runId={2}",
            signal.Isin.Value, signal.NavDate, signal.RunId.Value);

        var step08 = _newStep08();

        try
        {
            // The start is the gate. A false here means the signal is late, so nothing
            // below it may touch the row.
            if (!await step08.BeginProcessingAsync(signal, ct).ConfigureAwait(false))
                return;

            try
            {
                await step08.LoadFundAsync(signal, ct).ConfigureAwait(false);
                await step08.AssembleAgentInputAsync(signal, ct).ConfigureAwait(false);
                await step08.RunAgentAsync(signal, ct).ConfigureAwait(false);

                await step08.PersistAsync(signal, ct).ConfigureAwait(false);

                // Step 8 is the chain tail, so its success ends the run and frees the fund —
                // before the emit, unlike steps 2–7: step 9's barrier reads the rows, and the
                // last fund must already read finished when its signal arrives.
                await step08.CompleteAsync(signal, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The row is ours from the start until the complete, so any failure in
                // between frees the fund for a retry. Cancellation is left out: the row
                // stays in flight until it is restarted.
                await step08.FailAsync(signal, ex, ct).ConfigureAwait(false);

                throw;
            }

            // Outside the fail: the row is complete and no longer this run's, so failing it
            // could stamp an error on a finished fund — or on a newer run's. A lost signal
            // costs only the early step 9 check; the daily timer runs it anyway.
            await step08.EmitDoneAsync(signal, ct).ConfigureAwait(false);

            stopwatch.Stop();

            _logger.Info(
                "Step 8 entry done — isin={0}, {1} ms", signal.Isin.Value, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown so the caller owns the outcome: a queue trigger needs the throw to
            // retry the message. An Rx subscriber has to catch it, or the stream ends here.
            _logger.Error(ex, "Step 8 entry failed — isin={0}", signal.Isin.Value);

            throw;
        }
    }
}
