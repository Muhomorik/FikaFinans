using System.Diagnostics;

using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;

using NLog;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Default <see cref="IStep06CatalystTaggerEntry"/>: calls step 6's phases in order and stops
/// when the start says the signal is late.
/// </summary>
/// <remarks>
/// A twin of <see cref="Step05MacroAlignerEntry"/>. Takes a factory, not a handler — the
/// handler keeps one run's state in fields, so each signal needs its own.
/// </remarks>
public sealed class Step06CatalystTaggerEntry : IStep06CatalystTaggerEntry
{
    private readonly ILogger _logger;
    private readonly Func<IStep06CatalystTagger> _newStep06;

    public Step06CatalystTaggerEntry(ILogger logger, Func<IStep06CatalystTagger> newStep06)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _newStep06 = newStep06 ?? throw new ArgumentNullException(nameof(newStep06));
    }

    /// <inheritdoc />
    public async Task HandleAsync(Step05DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var stopwatch = Stopwatch.StartNew();

        _logger.Debug(
            "Step 6 entry — isin={0}, navDate={1:yyyy-MM-dd}, runId={2}",
            signal.Isin.Value, signal.NavDate, signal.RunId.Value);

        var step06 = _newStep06();

        try
        {
            // The start is the gate. A false here means the signal is late, so nothing
            // below it may touch the row.
            if (!await step06.BeginProcessingAsync(signal, ct).ConfigureAwait(false))
                return;

            try
            {
                await step06.LoadFundAsync(signal, ct).ConfigureAwait(false);
                await step06.AssembleAgentInputAsync(signal, ct).ConfigureAwait(false);
                await step06.RunAgentAsync(signal, ct).ConfigureAwait(false);

                // Write, then emit. The reverse order can announce output that was never
                // stored. No complete: step 7 consumes the done-signal and is the chain
                // tail, so the run — and the fund's row — stays in flight for it.
                await step06.PersistAsync(signal, ct).ConfigureAwait(false);
                await step06.EmitDoneAsync(signal, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The row is ours from the start onward, so any failure past it frees the
                // fund for a retry. Cancellation is left out: the row stays in flight
                // until it is restarted.
                await step06.FailAsync(signal, ex, ct).ConfigureAwait(false);

                throw;
            }

            stopwatch.Stop();

            _logger.Info(
                "Step 6 entry done — isin={0}, {1} ms", signal.Isin.Value, stopwatch.ElapsedMilliseconds);
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
            _logger.Error(ex, "Step 6 entry failed — isin={0}", signal.Isin.Value);

            throw;
        }
    }
}
