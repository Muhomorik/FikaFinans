using System.Diagnostics;

using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;

using NLog;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Default <see cref="IStep03MacroAnalystEntry"/>: calls step 3's phases in order and
/// stops when the start says the signal is late.
/// </summary>
/// <remarks>
/// A twin of <see cref="Step02MetricsCalculatorEntry"/>. Takes a factory, not a handler —
/// the handler keeps one run's state in fields, so each signal needs its own.
/// </remarks>
public sealed class Step03MacroAnalystEntry : IStep03MacroAnalystEntry
{
    private readonly ILogger _logger;
    private readonly Func<IStep03MacroAnalyst> _newStep03;

    public Step03MacroAnalystEntry(ILogger logger, Func<IStep03MacroAnalyst> newStep03)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _newStep03 = newStep03 ?? throw new ArgumentNullException(nameof(newStep03));
    }

    /// <inheritdoc />
    public async Task HandleAsync(Step02DoneSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var stopwatch = Stopwatch.StartNew();

        _logger.Debug(
            "Step 3 entry — isin={0}, navDate={1:yyyy-MM-dd}, runId={2}",
            signal.Isin.Value, signal.NavDate, signal.RunId.Value);

        var step03 = _newStep03();

        try
        {
            // The start is the gate. A false here means the signal is late, so nothing
            // below it may touch the row.
            if (!await step03.BeginProcessingAsync(signal, ct).ConfigureAwait(false))
                return;

            try
            {
                // On a hit, assemble and the agent return at once — the phases stay so the
                // miss, the one that costs money, still fails in a phase that can be named.
                await step03.LoadFundAsync(signal, ct).ConfigureAwait(false);
                await step03.AssembleAgentInputAsync(signal, ct).ConfigureAwait(false);
                await step03.RunAgentAsync(signal, ct).ConfigureAwait(false);

                // Write, then emit. The reverse order can announce output that was never
                // stored.
                await step03.PersistAsync(signal, ct).ConfigureAwait(false);
                await step03.EmitDoneAsync(signal, ct).ConfigureAwait(false);

                // Step 3 is the chain tail, so its success ends the run and
                // frees the fund. Moves on once a later step consumes step 3's done-signal.
                await step03.CompleteAsync(signal, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The row is ours from the start onward, so any failure past it frees the
                // fund for a retry. Cancellation is left out: the row stays in flight
                // until it is restarted.
                await step03.FailAsync(signal, ex, ct).ConfigureAwait(false);

                throw;
            }

            stopwatch.Stop();

            _logger.Info(
                "Step 3 entry done — isin={0}, {1} ms", signal.Isin.Value, stopwatch.ElapsedMilliseconds);
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
            _logger.Error(ex, "Step 3 entry failed — isin={0}", signal.Isin.Value);

            throw;
        }
    }
}
