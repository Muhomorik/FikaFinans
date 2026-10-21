using System.Diagnostics;

using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Application.Pipeline.Progress;
using FikaFinans.Application.Pipeline.Run;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using NLog;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// Default <see cref="IStep09UniverseEnricher"/>. Reads step 8's records off every fund's
/// row and keeps one pass in fields, so it must not be shared between passes.
/// </summary>
public sealed class Step09UniverseEnricherHandler : IStep09UniverseEnricher
{
    private readonly ILogger _logger;
    private readonly NavSyncOptions _options;
    private readonly IStep09Barrier _barrier;
    private readonly IIsinProgressStore _progress;
    private readonly IStep09ConvictionConfigProvider _configProvider;
    private readonly IUniverseEnricherAgent _agent;
    private readonly IPipelineRunIdFactory _runIds;
    private readonly TimeProvider _clock;
    private readonly IStepEventPublisher _stepEvents;

    /// <summary>The pass's own id — no fund's run; empty until the pass begins.</summary>
    private PipelineRunId _runId = new(string.Empty);

    /// <summary>The week the pass runs in — what the rotation pair ids carry.</summary>
    private IsoWeek _isoWeek = IsoWeek.Empty;

    /// <summary>Every fund the pass is over, whether or not it has a record.</summary>
    private IReadOnlyList<Isin> _universe = Array.Empty<Isin>();

    /// <summary>Step 8's records for the universe; null until they load.</summary>
    private IReadOnlyList<FundRecord>? _funds;

    /// <summary>The settings this pass runs with; null until assembled.</summary>
    private UniverseEnricherConfig? _config;

    /// <summary>What the agent produced, kept for the phase that persists it.</summary>
    private DataLoaderOutput? _agentOutput;

    /// <summary>Runs from the start, so a reported duration covers every phase.</summary>
    private readonly Stopwatch _stepDuration = new();

    public Step09UniverseEnricherHandler(
        ILogger logger,
        NavSyncOptions options,
        IStep09Barrier barrier,
        IIsinProgressStore progress,
        IStep09ConvictionConfigProvider configProvider,
        IUniverseEnricherAgent agent,
        IPipelineRunIdFactory runIds,
        TimeProvider clock,
        IStepEventPublisher stepEvents)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(barrier);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(configProvider);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(runIds);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(stepEvents);

        _logger = logger;
        _options = options;
        _barrier = barrier;
        _progress = progress;
        _configProvider = configProvider;
        _agent = agent;
        _runIds = runIds;
        _clock = clock;
        _stepEvents = stepEvents;
    }

    /// <inheritdoc />
    public Task<PipelineRunId> BeginPassAsync(IReadOnlyList<Isin> universe, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(universe);

        // No trading date behind a pass, so the clock decides both: decided on step 9's
        // page, the pair ids carry the week the run happens in.
        var now = _clock.GetUtcNow();
        _isoWeek = IsoWeek.FromDate(now);
        _runId = _runIds.NewUniverseRunId(now);
        _universe = universe;

        _logger.Debug("Step 9 begin — runId={0}, week {1}, {2} fund(s) in the universe",
            _runId.Value, _isoWeek, universe.Count);

        _stepDuration.Restart();
        Report(StepEventKind.Started);

        return Task.FromResult(_runId);
    }

    /// <inheritdoc />
    public async Task LoadUniverseAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_runId.Value))
            throw new InvalidOperationException("BeginPassAsync must run before LoadUniverseAsync.");

        _logger.Debug("Step 9 load — runId={0}, {1} fund(s)", _runId.Value, _universe.Count);

        try
        {
            _funds = await _progress.ReadStep08RecordsAsync(_universe, ct).ConfigureAwait(false);

            var loaded = _funds.Select(f => f.Isin).ToHashSet();
            var leftOut = _universe.Where(i => !loaded.Contains(i)).Select(i => i.Value).ToList();

            // Info, not Debug: who the pass ran without is the first thing to look for when a
            // fund is missing from step 10.
            _logger.Info("Step 9 loaded — runId={0}, {1} of {2} fund(s) with a step 8 record, left out: [{3}]",
                _runId.Value, _funds.Count, _universe.Count, string.Join(", ", leftOut));
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown: without the records there is nothing to enrich.
            _logger.Error(ex, "Step 9 load failed — runId={0}", _runId.Value);

            Report(StepEventKind.Failed, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task AssembleAgentInputAsync(CancellationToken ct = default)
    {
        _logger.Debug("Step 9 assemble — runId={0}", _runId.Value);

        try
        {
            // The records arrive assembled; only the config is left, and the provider
            // decides what a missing one means.
            _config = await _configProvider.GetConfigAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown: running on weights the user did not choose would look like a normal
            // result and be wrong.
            _logger.Error(ex, "Step 9 assemble failed — runId={0}", _runId.Value);

            Report(StepEventKind.Failed, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<DataLoaderOutput> RunAgentAsync(CancellationToken ct = default)
    {
        // Both are set by the phases this one runs after. Reaching here without them is a
        // caller running the phases out of order, not a condition to recover from.
        if (_funds is null)
            throw new InvalidOperationException("LoadUniverseAsync must run before RunAgentAsync.");

        if (_config is null)
            throw new InvalidOperationException("AssembleAgentInputAsync must run before RunAgentAsync.");

        _logger.Debug("Step 9 compute — runId={0}, week {1}, {2} fund(s)", _runId.Value, _isoWeek, _funds.Count);

        try
        {
            _agentOutput = await _agent
                .RunInMemoryAsync(new Company(_options.CompanyFilter), _isoWeek, _runId, _funds, _config, ct)
                .ConfigureAwait(false);

            LogEnriched(_agentOutput);

            return _agentOutput;
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // A bad weights edit lands here too: the agent checks them.
            _logger.Error(ex, "Step 9 compute failed — runId={0}", _runId.Value);

            Report(StepEventKind.Failed, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task PersistAsync(CancellationToken ct = default)
    {
        if (_agentOutput is null)
            throw new InvalidOperationException("RunAgentAsync must run before PersistAsync.");

        _logger.Debug("Step 9 persist — runId={0}, {1} record(s)", _runId.Value, _agentOutput.Funds.Count);

        try
        {
            var written = await _progress.SaveStep09Async(_agentOutput.Funds, ct).ConfigureAwait(false);

            _stepDuration.Stop();

            _logger.Info(
                "Step 9 pass done — runId={0}, week {1}, universe {2}, enriched {3}, left out {4}, rows written {5}, {6} ms",
                _runId.Value, _isoWeek, _universe.Count, _agentOutput.Funds.Count,
                _universe.Count - _agentOutput.Funds.Count, written.Count, _stepDuration.ElapsedMilliseconds);

            // No emit phase follows, so the step is done here.
            Report(StepEventKind.Succeeded);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report. Rows already written keep this
            // pass's records; the next pass overwrites them.
            throw;
        }
        catch (Exception ex)
        {
            // Rethrown: the caller logs the pass as failed. Nothing to undo — the funds'
            // runs are over either way, and the next pass overwrites what landed.
            _logger.Error(ex, "Step 9 persist failed — runId={0}", _runId.Value);

            Report(StepEventKind.Failed, ex.Message);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<StepOutput?> ReadOutputAsync(CancellationToken ct = default)
    {
        _logger.Debug("Step 9 read");

        try
        {
            // The rows keep their funds' run ids, so only the universe gathers a pass back.
            var barrier = await _barrier.CheckAsync(ct).ConfigureAwait(false);

            return await _progress.ReadStep09OutputAsync(barrier.Universe, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a failure — nothing to report and nothing to undo.
            throw;
        }
        catch (Exception ex)
        {
            // A read feeds a view, so a failure costs a display rather than a run. Null lets
            // the caller show its "nothing here" state instead of surfacing a crash.
            _logger.Error(ex, "Step 9 read failed");

            return null;
        }
    }

    /// <inheritdoc />
    public async Task<DataLoaderOutput> ComputeOutputAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var isoWeek = IsoWeek.FromDate(now);
        var runId = _runIds.NewUniverseRunId(now);

        _logger.Debug("Step 9 debug compute — runId={0}, week {1}", runId.Value, isoWeek);

        // Runs whether or not the barrier has closed — a debug look at the universe as it
        // stands. The check still logs which funds are holding it open.
        var barrier = await _barrier.CheckAsync(ct).ConfigureAwait(false);

        var funds = await _progress.ReadStep08RecordsAsync(barrier.Universe, ct).ConfigureAwait(false);
        if (funds.Count == 0)
            throw new InvalidOperationException("No fund in the universe has a step 8 record.");

        var config = await _configProvider.GetConfigAsync(ct).ConfigureAwait(false);

        var output = await _agent
            .RunInMemoryAsync(new Company(_options.CompanyFilter), isoWeek, runId, funds, config, ct)
            .ConfigureAwait(false);

        LogEnriched(output);

        _logger.Info(
            "Step 9 debug compute done, not stored — runId={0}, week {1}, barrier {2}, {3} of {4} fund(s), {5} warning(s)",
            runId.Value, isoWeek, barrier.IsClosed ? "closed" : "open", output.Funds.Count,
            barrier.Universe.Count, output.DataQuality.Warnings.Count);

        return output;
    }

    /// <summary>
    /// One line per fund — what step 10 trades on — and one Warn per agent warning, which
    /// reach no row: only the records are stored.
    /// </summary>
    private void LogEnriched(DataLoaderOutput output)
    {
        foreach (var fund in output.Funds)
            _logger.Debug(
                "Step 9 enriched — isin={0}: {1}, conviction {2}, rank {3}, pair {4}",
                fund.Isin.Value, fund.Recommendation?.ToString() ?? "-", fund.ConvictionScore?.ToString() ?? "-",
                fund.UniverseRank is { } rank ? $"{rank.WithinRecommendation} of {rank.OfTotalInRecommendation}" : "-",
                fund.RotationPairId ?? "-");

        foreach (var warning in output.DataQuality.Warnings)
            _logger.Warn("Step 9 warning — runId={0}: {1}", output.RunId.Value, warning);
    }

    /// <summary>
    /// Reports this step's progress for the pass — no fund, as the runner reports step 9.
    /// Elapsed time is measured from the start, so it spans every phase.
    /// </summary>
    /// <param name="kind">Started carries no duration; there is nothing elapsed yet.</param>
    /// <param name="message">Why it failed, for a failure; otherwise null.</param>
    private void Report(StepEventKind kind, string? message = null)
        => _stepEvents.Publish(new StepEvent(
            StepId.UniverseEnricher,
            kind,
            Message: message,
            Duration: kind == StepEventKind.Started ? null : _stepDuration.Elapsed));
}
