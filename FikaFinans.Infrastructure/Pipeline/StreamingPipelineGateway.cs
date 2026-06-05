using System.Text.Json;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Json;

namespace FikaFinans.Infrastructure.Pipeline;

/// <summary>
/// JSON / disk implementation of <see cref="IStreamingPipelineGateway"/>.
/// Reads and writes the same files the universe-wide agents read and write,
/// so a streaming run leaves identical on-disk artifacts behind. Also
/// fronts the per-ISIN progress repository: the runner calls the
/// claim/write/release methods at each phase boundary and this class
/// serializes per-fund slices into the inline step JSON columns.
/// </summary>
public sealed class StreamingPipelineGateway : IStreamingPipelineGateway
{
    /// <summary>Partition key for every <c>IsinProgress</c> row.</summary>
    private const string IsinProgressPartition = "isin-progress";

    /// <summary>
    /// Chunk size used when batch-upserting per-ISIN rows. Matches the Tables
    /// batch cap (<see cref="FikaFinans.Application.Storage.Bank.IPositionsRepository"/>
    /// + friends) so the SQLite path stays drop-in compatible with the
    /// eventual Tables binding.
    /// </summary>
    private const int IsinProgressBatchSize = 100;

    private readonly IPathsService _paths;
    private readonly IIsinProgressRepository _isinProgress;
    private readonly StreamingPipelineOptions _options;

    public StreamingPipelineGateway(
        IPathsService paths,
        IIsinProgressRepository isinProgress,
        StreamingPipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(isinProgress);
        ArgumentNullException.ThrowIfNull(options);
        _paths = paths;
        _isinProgress = isinProgress;
        _options = options;
    }

    public async Task<DataLoaderOutput> LoadUniverseFromIsinProgressAsync(
        DataLoaderOutput universeTemplate,
        StepId perFundSource,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(universeTemplate);

        // Column selection — only Step08Json (Recommender) and Step09Json
        // (UniverseEnricher) are addressable today; other steps' per-ISIN
        // columns aren't legal upstream sources for the universe-wide
        // re-assembly path.
        Func<IsinProgressEntity, string?> columnSelector = perFundSource.Value switch
        {
            8 => row => row.Step08Json,
            9 => row => row.Step09Json,
            _ => throw new ArgumentOutOfRangeException(nameof(perFundSource), perFundSource,
                "LoadUniverseFromIsinProgressAsync only supports Step 8 or Step 9 as the per-fund source."),
        };

        var rows = await _isinProgress.QueryPartitionAsync(IsinProgressPartition, ct).ConfigureAwait(false);
        var byIsin = rows.ToDictionary(r => r.Isin, r => r);

        // Preserve template ordering — the streaming runner's
        // BuildUniverse already keeps Step 1's order; we mirror that
        // so downstream agents see the same fund sequence whether the
        // upstream came from disk JSON or SQLite columns.
        var assembled = new List<FundRecord>(universeTemplate.Funds.Count);
        foreach (var templateFund in universeTemplate.Funds)
        {
            if (!byIsin.TryGetValue(templateFund.Isin.Value, out var row))
                continue; // fund missing from SQLite (failed-fund case) — drop it

            var json = columnSelector(row);
            if (string.IsNullOrEmpty(json))
                continue; // column not populated for this fund (failed earlier in the chain)

            var fund = JsonSerializer.Deserialize<FundRecord>(json, JsonOptions.Default)
                ?? throw new InvalidDataException(
                    $"Failed to deserialize {perFundSource} column for ISIN {templateFund.Isin.Value}.");
            assembled.Add(fund);
        }

        return new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = universeTemplate.IsoWeek,
            Family          = universeTemplate.Family,
            RunId           = universeTemplate.RunId,
            ConfigVersion   = universeTemplate.ConfigVersion,
            Funds           = assembled,
            FrozenPositions = universeTemplate.FrozenPositions,
            CashAvailableKr = universeTemplate.CashAvailableKr,
            DataQuality     = universeTemplate.DataQuality,
        };
    }

    public MetricsCalculatorConfig LoadMetricsConfig()
    {
        var path = _paths.Config02MetricsJson;
        if (!File.Exists(path))
            return MetricsCalculatorConfig.Default;
        return JsonSerializer.Deserialize<MetricsCalculatorConfig>(File.ReadAllText(path), JsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize Step 2 config at {path}");
    }

    public SignalScorerConfig LoadSignalConfig()
    {
        var path = _paths.Config04SignalsJson;
        if (!File.Exists(path))
            return SignalScorerConfig.Default;
        return JsonSerializer.Deserialize<SignalScorerConfig>(File.ReadAllText(path), JsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize Step 4 config at {path}");
    }

    public void SaveStepOutput(StepId step, string isoWeek, PipelineRunId runId, DataLoaderOutput output)
    {
        // Validate first so universe-wide steps still throw even when the
        // disk gate is closed — callers passing the wrong step are buggy
        // regardless of the artifact flag.
        var path = step.Value switch
        {
            2 => _paths.MetricsCalculatorOutput(isoWeek, runId),
            4 => _paths.SignalScorerOutput(isoWeek, runId),
            5 => _paths.MacroAlignerOutput(isoWeek, runId),
            6 => _paths.CatalystTaggerOutput(isoWeek, runId),
            7 => _paths.ThesisValidatorOutput(isoWeek, runId),
            8 => _paths.RecommenderOutput(isoWeek, runId),
            _ => throw new ArgumentOutOfRangeException(nameof(step), step,
                "SaveStepOutput only supports per-ISIN steps (2, 4, 5, 6, 7, 8)."),
        };

        // Open Q #4 gate: disk JSON is dev-debugging scaffolding. When the
        // option flips to false (after the per-ISIN row inspector UI lands),
        // the IsinProgress columns become the only on-the-wire artifact.
        if (!_options.WriteDiskJsonArtifacts) return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(output, JsonOptions.Default));
    }

    public async Task ClaimIsinProgressAsync(DataLoaderOutput step1Output, PipelineRunId runId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(step1Output);
        ArgumentException.ThrowIfNullOrEmpty(runId.Value);

        var now = DateTimeOffset.UtcNow;

        var entities = step1Output.Funds
            .Select(fund => new IsinProgressEntity
            {
                PartitionKey = IsinProgressPartition,
                RowKey = fund.Isin.Value,
                Isin = fund.Isin.Value,
                State = IsinProgressState.Processing,
                RunId = runId,
                CurrentStep = 1,
                ProcessingStartedAt = now,
                Step01Json = SerializeFund(fund),
                // Clear every later column — see backend-nav-sync-plan.md
                // §"Run boundary". Explicit nulls overwrite whatever lingered
                // from the prior run.
                Step02Json = null,
                Step03Json = null,
                Step04Json = null,
                Step05Json = null,
                Step06Json = null,
                Step07Json = null,
                Step08Json = null,
                Step09Json = null,
            })
            .ToList();

        await UpsertInChunksAsync(entities, ct).ConfigureAwait(false);
    }

    public async Task WriteIsinProgressBlockAsync(PerIsinBlockResult block, PipelineRunId runId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentException.ThrowIfNullOrEmpty(runId.Value);

        var step2By = block.Step2Output.Funds.ToDictionary(f => f.Isin.Value, f => f);
        var step4By = block.Step4Output.Funds.ToDictionary(f => f.Isin.Value, f => f);
        var step5By = block.Step5Output.Funds.ToDictionary(f => f.Isin.Value, f => f);
        var step6By = block.Step6Output.Funds.ToDictionary(f => f.Isin.Value, f => f);
        var step7By = block.Step7Output.Funds.ToDictionary(f => f.Isin.Value, f => f);
        var step8By = block.Step8Output.Funds.ToDictionary(f => f.Isin.Value, f => f);

        // Use Step 2's fund list as the canonical iteration order — every
        // boundary snapshot shares the same input order from Step 1.
        var entities = new List<IsinProgressEntity>(block.Step2Output.Funds.Count);
        foreach (var fund in block.Step2Output.Funds)
        {
            var isin = fund.Isin.Value;
            var existing = await _isinProgress.GetAsync(IsinProgressPartition, isin, ct).ConfigureAwait(false)
                ?? new IsinProgressEntity
                {
                    PartitionKey = IsinProgressPartition,
                    RowKey = isin,
                    Isin = isin,
                    State = IsinProgressState.Processing,
                    RunId = runId,
                };

            entities.Add(new IsinProgressEntity
            {
                PartitionKey = existing.PartitionKey,
                RowKey = existing.RowKey,
                Isin = existing.Isin,
                State = IsinProgressState.Processing,
                RunId = runId,
                NavDate = existing.NavDate,
                CurrentStep = 8,
                LatestProcessedNavDate = existing.LatestProcessedNavDate,
                ProcessingStartedAt = existing.ProcessingStartedAt,
                LastError = existing.LastError,
                AttemptCount = existing.AttemptCount,
                Step01Json = existing.Step01Json,
                Step02Json = SerializeFund(step2By[isin]),
                Step03Json = existing.Step03Json,
                Step04Json = SerializeFund(step4By[isin]),
                Step05Json = SerializeFund(step5By[isin]),
                Step06Json = SerializeFund(step6By[isin]),
                Step07Json = SerializeFund(step7By[isin]),
                Step08Json = SerializeFund(step8By[isin]),
                Step09Json = existing.Step09Json,
            });
        }

        await UpsertInChunksAsync(entities, ct).ConfigureAwait(false);
    }

    public async Task WriteIsinProgressStep9Async(DataLoaderOutput step9Output, PipelineRunId runId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(step9Output);
        ArgumentException.ThrowIfNullOrEmpty(runId.Value);

        var entities = new List<IsinProgressEntity>(step9Output.Funds.Count);
        foreach (var fund in step9Output.Funds)
        {
            var isin = fund.Isin.Value;
            var existing = await _isinProgress.GetAsync(IsinProgressPartition, isin, ct).ConfigureAwait(false)
                ?? new IsinProgressEntity
                {
                    PartitionKey = IsinProgressPartition,
                    RowKey = isin,
                    Isin = isin,
                    State = IsinProgressState.Processing,
                    RunId = runId,
                };

            entities.Add(new IsinProgressEntity
            {
                PartitionKey = existing.PartitionKey,
                RowKey = existing.RowKey,
                Isin = existing.Isin,
                State = IsinProgressState.Processing,
                RunId = runId,
                NavDate = existing.NavDate,
                CurrentStep = 9,
                LatestProcessedNavDate = existing.LatestProcessedNavDate,
                ProcessingStartedAt = existing.ProcessingStartedAt,
                LastError = existing.LastError,
                AttemptCount = existing.AttemptCount,
                Step01Json = existing.Step01Json,
                Step02Json = existing.Step02Json,
                Step03Json = existing.Step03Json,
                Step04Json = existing.Step04Json,
                Step05Json = existing.Step05Json,
                Step06Json = existing.Step06Json,
                Step07Json = existing.Step07Json,
                Step08Json = existing.Step08Json,
                Step09Json = SerializeFund(fund),
            });
        }

        await UpsertInChunksAsync(entities, ct).ConfigureAwait(false);
    }

    public async Task ReleaseIsinProgressAsync(DataLoaderOutput step1Output, PipelineRunId runId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(step1Output);
        ArgumentException.ThrowIfNullOrEmpty(runId.Value);

        var entities = new List<IsinProgressEntity>(step1Output.Funds.Count);
        foreach (var fund in step1Output.Funds)
        {
            var isin = fund.Isin.Value;
            var existing = await _isinProgress.GetAsync(IsinProgressPartition, isin, ct).ConfigureAwait(false);
            if (existing is null) continue;

            entities.Add(new IsinProgressEntity
            {
                PartitionKey = existing.PartitionKey,
                RowKey = existing.RowKey,
                Isin = existing.Isin,
                State = IsinProgressState.Free,
                RunId = existing.RunId,
                NavDate = existing.NavDate,
                CurrentStep = existing.CurrentStep,
                LatestProcessedNavDate = existing.LatestProcessedNavDate,
                ProcessingStartedAt = null,
                LastError = existing.LastError,
                AttemptCount = existing.AttemptCount,
                Step01Json = existing.Step01Json,
                Step02Json = existing.Step02Json,
                Step03Json = existing.Step03Json,
                Step04Json = existing.Step04Json,
                Step05Json = existing.Step05Json,
                Step06Json = existing.Step06Json,
                Step07Json = existing.Step07Json,
                Step08Json = existing.Step08Json,
                Step09Json = existing.Step09Json,
            });
        }

        await UpsertInChunksAsync(entities, ct).ConfigureAwait(false);
    }

    public async Task MarkFundFailedAsync(string isin, PipelineRunId runId, string errorMessage, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(isin);
        ArgumentException.ThrowIfNullOrEmpty(runId.Value);
        ArgumentNullException.ThrowIfNull(errorMessage);

        var existing = await _isinProgress.GetAsync(IsinProgressPartition, isin, ct).ConfigureAwait(false);
        if (existing is null) return;

        var updated = new IsinProgressEntity
        {
            PartitionKey = existing.PartitionKey,
            RowKey = existing.RowKey,
            Isin = existing.Isin,
            State = existing.State,
            RunId = runId,
            NavDate = existing.NavDate,
            CurrentStep = existing.CurrentStep,
            LatestProcessedNavDate = existing.LatestProcessedNavDate,
            ProcessingStartedAt = existing.ProcessingStartedAt,
            LastError = errorMessage,
            AttemptCount = existing.AttemptCount + 1,
            Step01Json = existing.Step01Json,
            Step02Json = existing.Step02Json,
            Step03Json = existing.Step03Json,
            Step04Json = existing.Step04Json,
            Step05Json = existing.Step05Json,
            Step06Json = existing.Step06Json,
            Step07Json = existing.Step07Json,
            Step08Json = existing.Step08Json,
            Step09Json = existing.Step09Json,
        };

        await _isinProgress.UpsertAsync(updated, ct).ConfigureAwait(false);
    }

    private async Task UpsertInChunksAsync(IReadOnlyList<IsinProgressEntity> entities, CancellationToken ct)
    {
        if (entities.Count == 0) return;

        for (var offset = 0; offset < entities.Count; offset += IsinProgressBatchSize)
        {
            var chunk = entities
                .Skip(offset)
                .Take(IsinProgressBatchSize)
                .ToList();
            await _isinProgress.UpsertBatchAsync(IsinProgressPartition, chunk, ct).ConfigureAwait(false);
        }
    }

    private static string SerializeFund(FundRecord fund) =>
        JsonSerializer.Serialize(fund, JsonOptions.Default);
}
