using System.Diagnostics;

using FikaFinans.Application.Pipeline.Steps;

using NLog;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Default <see cref="IStep09UniverseEnricherEntry"/>: asks the barrier, then calls step 9's
/// phases in order.
/// </summary>
/// <remarks>
/// Takes a factory, not a handler — the handler keeps one pass's state in fields, so each
/// pass needs its own. No fail phase: a pass holds no row, so a failure leaves nothing to
/// free, and the next trigger runs the pass again.
/// </remarks>
public sealed class Step09UniverseEnricherEntry : IStep09UniverseEnricherEntry
{
    private readonly ILogger _logger;
    private readonly IStep09Barrier _barrier;
    private readonly Func<IStep09UniverseEnricher> _newStep09;

    public Step09UniverseEnricherEntry(
        ILogger logger, IStep09Barrier barrier, Func<IStep09UniverseEnricher> newStep09)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _barrier = barrier ?? throw new ArgumentNullException(nameof(barrier));
        _newStep09 = newStep09 ?? throw new ArgumentNullException(nameof(newStep09));
    }

    /// <inheritdoc />
    public async Task<bool> HandleAsync(Step09Trigger trigger, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.Debug("Step 9 entry — trigger {0}", trigger);

        try
        {
            // The barrier logs which funds hold it open; this line only says what came of it.
            var barrier = await _barrier.CheckAsync(ct).ConfigureAwait(false);

            if (!barrier.IsClosed)
            {
                _logger.Info("Step 9 entry — trigger {0}, barrier open: {1} of {2} fund(s) holding, no pass",
                    trigger, barrier.Holding.Count, barrier.Universe.Count);

                return false;
            }

            // Closed over nothing — no funds configured yet. A pass would store nothing and
            // still report a step that ran.
            if (barrier.Universe.Count == 0)
            {
                _logger.Info("Step 9 entry — trigger {0}, universe empty, no pass", trigger);

                return false;
            }

            var step09 = _newStep09();

            var runId = await step09.BeginPassAsync(barrier.Universe, ct).ConfigureAwait(false);
            await step09.LoadUniverseAsync(ct).ConfigureAwait(false);
            await step09.AssembleAgentInputAsync(ct).ConfigureAwait(false);
            await step09.RunAgentAsync(ct).ConfigureAwait(false);
            await step09.PersistAsync(ct).ConfigureAwait(false);

            stopwatch.Stop();

            _logger.Info("Step 9 entry done — trigger {0}, runId={1}, {2} ms",
                trigger, runId.Value, stopwatch.ElapsedMilliseconds);

            return true;
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown so the caller owns the outcome: a queue or timer trigger needs the throw
            // to retry. An Rx subscriber has to catch it, or the stream ends here. Nothing to
            // undo: rows a failed persist already wrote are overwritten by the next pass.
            _logger.Error(ex, "Step 9 entry failed — trigger {0}", trigger);

            throw;
        }
    }
}
