using System.Diagnostics;

using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;

using NLog;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Default <see cref="IStep07ThesisValidatorEntry"/>: calls step 7's phases in order and stops
/// when the start says the signal is late.
/// </summary>
/// <remarks>
/// A twin of <see cref="Step06CatalystTaggerEntry"/>. Takes a factory, not a handler — the
/// handler keeps one run's state in fields, so each signal needs its own.
/// </remarks>
public sealed class Step07ThesisValidatorEntry : IStep07ThesisValidatorEntry
{
    private readonly ILogger _logger;
    private readonly Func<IStep07ThesisValidator> _newStep07;

    public Step07ThesisValidatorEntry(ILogger logger, Func<IStep07ThesisValidator> newStep07)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _newStep07 = newStep07 ?? throw new ArgumentNullException(nameof(newStep07));
    }

    /// <inheritdoc />
    public async Task HandleAsync(Step06DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var stopwatch = Stopwatch.StartNew();

        _logger.Debug(
            "Step 7 entry — isin={0}, navDate={1:yyyy-MM-dd}, runId={2}",
            signal.Isin.Value, signal.NavDate, signal.RunId.Value);

        var step07 = _newStep07();

        try
        {
            // The start is the gate. A false here means the signal is late, so nothing
            // below it may touch the row.
            if (!await step07.BeginProcessingAsync(signal, ct).ConfigureAwait(false))
                return;

            try
            {
                await step07.LoadFundAsync(signal, ct).ConfigureAwait(false);
                await step07.AssembleAgentInputAsync(signal, ct).ConfigureAwait(false);
                await step07.RunAgentAsync(signal, ct).ConfigureAwait(false);

                // Write, then emit. The reverse order can announce output that was never
                // stored. No complete: step 8 consumes the done-signal and is the chain
                // tail, so the run — and the fund's row — stays in flight for it.
                await step07.PersistAsync(signal, ct).ConfigureAwait(false);
                await step07.EmitDoneAsync(signal, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The row is ours from the start onward, so any failure past it frees the
                // fund for a retry. Cancellation is left out: the row stays in flight
                // until it is restarted.
                await step07.FailAsync(signal, ex, ct).ConfigureAwait(false);

                throw;
            }

            stopwatch.Stop();

            _logger.Info(
                "Step 7 entry done — isin={0}, {1} ms", signal.Isin.Value, stopwatch.ElapsedMilliseconds);
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
            _logger.Error(ex, "Step 7 entry failed — isin={0}", signal.Isin.Value);

            throw;
        }
    }
}
