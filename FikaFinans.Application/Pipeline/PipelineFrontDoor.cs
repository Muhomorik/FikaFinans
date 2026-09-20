using System.Diagnostics;

using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Application.Pipeline.Steps;

using NLog;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Default <see cref="IPipelineFrontDoor"/>: calls step 1's phases in order and stops
/// when the claim says another run already holds the fund.
/// </summary>
/// <remarks>
/// Takes a factory, not a handler — the handler keeps one run's state in fields, so each
/// signal needs its own.
/// </remarks>
public sealed class PipelineFrontDoor : IPipelineFrontDoor
{
    private readonly Func<IStep01DataLoader> _newStep01;
    private readonly ILogger _logger;

    public PipelineFrontDoor(Func<IStep01DataLoader> newStep01, ILogger logger)
    {
        _newStep01 = newStep01 ?? throw new ArgumentNullException(nameof(newStep01));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task HandleAsync(NavChangeSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var stopwatch = Stopwatch.StartNew();

        _logger.Debug(
            "Front door — isin={0}, navDate={1:yyyy-MM-dd}", signal.Isin.Value, signal.NavDate);

        var step01 = _newStep01();

        try
        {
            // The claim is the gate. A false here means a run already holds this fund, so
            // nothing below it may touch the row.
            if (!await step01.BeginProcessingAsync(signal, ct).ConfigureAwait(false))
                return;

            await step01.LoadFundAsync(signal, ct).ConfigureAwait(false);
            await step01.AssembleAgentInputAsync(signal, ct).ConfigureAwait(false);
            await step01.RunAgentAsync(signal, ct).ConfigureAwait(false);

            // Write, then emit. The reverse order can tell step 2 to read output that was
            // never stored.
            await step01.PersistAsync(signal, ct).ConfigureAwait(false);
            await step01.EmitDoneAsync(signal, ct).ConfigureAwait(false);

            stopwatch.Stop();

            _logger.Info(
                "Front door done — isin={0}, {1} ms", signal.Isin.Value, stopwatch.ElapsedMilliseconds);
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
            _logger.Error(ex, "Front door failed — isin={0}", signal.Isin.Value);

            throw;
        }
    }
}
