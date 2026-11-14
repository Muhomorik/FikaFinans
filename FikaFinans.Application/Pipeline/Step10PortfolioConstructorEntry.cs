using System.Diagnostics;

using FikaFinans.Application.Pipeline.Steps;

using NLog;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Default <see cref="IStep10PortfolioConstructorEntry"/>: calls step 10's phases in order.
/// </summary>
/// <remarks>
/// Takes a factory, not a handler — the handler keeps one run's state in fields, so each run
/// needs its own. No fail phase: a run holds no row, so a failure leaves nothing to free, and
/// the next trigger runs again and overwrites.
/// </remarks>
public sealed class Step10PortfolioConstructorEntry : IStep10PortfolioConstructorEntry
{
    private readonly ILogger _logger;
    private readonly Func<IStep10PortfolioConstructor> _newStep10;

    public Step10PortfolioConstructorEntry(ILogger logger, Func<IStep10PortfolioConstructor> newStep10)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _newStep10 = newStep10 ?? throw new ArgumentNullException(nameof(newStep10));
    }

    /// <inheritdoc />
    public async Task<bool> HandleAsync(Step10Trigger trigger, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.Debug("Step 10 entry — trigger {0}", trigger);

        try
        {
            var step10 = _newStep10();

            var runId = await step10.BeginRunAsync(ct).ConfigureAwait(false);

            // The handler has closed the run already; trading on nothing would only store
            // nothing and hand the bank an empty day.
            if (await step10.LoadUniverseAsync(ct).ConfigureAwait(false) == 0)
            {
                _logger.Info("Step 10 entry — trigger {0}, runId={1}, no step 9 record, no trades",
                    trigger, runId.Value);

                return false;
            }

            await step10.AssembleAgentInputAsync(ct).ConfigureAwait(false);
            await step10.RunAgentAsync(ct).ConfigureAwait(false);
            await step10.PersistAsync(ct).ConfigureAwait(false);

            stopwatch.Stop();

            _logger.Info("Step 10 entry done — trigger {0}, runId={1}, {2} ms",
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
            // undo: slices a failed persist already wrote are overwritten by the next run.
            _logger.Error(ex, "Step 10 entry failed — trigger {0}", trigger);

            throw;
        }
    }
}
