using System.Diagnostics;

using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;

using NLog;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Default <see cref="IStep05MacroAlignerEntry"/>: calls step 5's phases in order and stops
/// when the start says the signal is late.
/// </summary>
/// <remarks>
/// A twin of <see cref="Step04SignalScorerEntry"/>. Takes a factory, not a handler — the
/// handler keeps one run's state in fields, so each signal needs its own.
/// </remarks>
public sealed class Step05MacroAlignerEntry : IStep05MacroAlignerEntry
{
    private readonly ILogger _logger;
    private readonly Func<IStep05MacroAligner> _newStep05;

    public Step05MacroAlignerEntry(ILogger logger, Func<IStep05MacroAligner> newStep05)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _newStep05 = newStep05 ?? throw new ArgumentNullException(nameof(newStep05));
    }

    /// <inheritdoc />
    public async Task HandleAsync(Step04DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var stopwatch = Stopwatch.StartNew();

        _logger.Debug(
            "Step 5 entry — isin={0}, navDate={1:yyyy-MM-dd}, runId={2}",
            signal.Isin.Value, signal.NavDate, signal.RunId.Value);

        var step05 = _newStep05();

        try
        {
            // The start is the gate. A false here means the signal is late, so nothing
            // below it may touch the row.
            if (!await step05.BeginProcessingAsync(signal, ct).ConfigureAwait(false))
                return;

            try
            {
                await step05.LoadFundAsync(signal, ct).ConfigureAwait(false);
                await step05.AssembleAgentInputAsync(signal, ct).ConfigureAwait(false);
                await step05.RunAgentAsync(signal, ct).ConfigureAwait(false);

                // Write, then emit. The reverse order can announce output that was never
                // stored.
                await step05.PersistAsync(signal, ct).ConfigureAwait(false);
                await step05.EmitDoneAsync(signal, ct).ConfigureAwait(false);

                // Step 5 is the chain tail, so its success ends the run and frees the fund.
                // Moves on once a later step consumes step 5's done-signal.
                await step05.CompleteAsync(signal, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The row is ours from the start onward, so any failure past it frees the
                // fund for a retry. Cancellation is left out: the row stays in flight
                // until it is restarted.
                await step05.FailAsync(signal, ex, ct).ConfigureAwait(false);

                throw;
            }

            stopwatch.Stop();

            _logger.Info(
                "Step 5 entry done — isin={0}, {1} ms", signal.Isin.Value, stopwatch.ElapsedMilliseconds);
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
            _logger.Error(ex, "Step 5 entry failed — isin={0}", signal.Isin.Value);

            throw;
        }
    }
}
