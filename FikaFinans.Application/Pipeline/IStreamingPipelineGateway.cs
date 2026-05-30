using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// File-IO seam used by <see cref="PipelineRunner.RunAllStreamingAsync"/>.
/// Keeps the runner free of JSON / disk concerns: the Application layer
/// orchestrates the per-ISIN block in memory, and the Infrastructure
/// implementation persists boundary JSON files exactly as the universe-wide
/// path does. This is the same contract the per-tab "Run this step" buttons
/// rely on, so a streaming run produces identical on-disk artifacts.
/// </summary>
public interface IStreamingPipelineGateway
{
    /// <summary>Read Step 1 (DataLoader) output JSON from disk.</summary>
    DataLoaderOutput LoadStep1Output(string isoWeek, string runId);

    /// <summary>Read Step 3 (MacroAnalyst) output JSON from disk.</summary>
    MacroContext LoadStep3Output(string isoWeek, string runId);

    /// <summary>
    /// Assemble a universe-wide <see cref="DataLoaderOutput"/> from the per-ISIN
    /// SQLite columns. <paramref name="perFundSource"/> selects which
    /// <c>Step{N}Json</c> column to read (currently
    /// <see cref="StepId.Recommender"/> → <c>Step08Json</c> or
    /// <see cref="StepId.UniverseEnricher"/> → <c>Step09Json</c>); the
    /// universe-wide fields (<c>IsoWeek</c>, <c>Family</c>, <c>RunId</c>,
    /// <c>ConfigVersion</c>, <c>FrozenPositions</c>, <c>CashAvailableKr</c>,
    /// <c>DataQuality</c>) come from <paramref name="universeTemplate"/>.
    /// Phase 8 sub-step 8b: lets Step 9 + Step 10 read their inputs from
    /// SQLite instead of disk JSON.
    /// </summary>
    Task<DataLoaderOutput> LoadUniverseFromIsinProgressAsync(
        DataLoaderOutput universeTemplate,
        StepId perFundSource,
        CancellationToken ct = default);

    /// <summary>Read the Step 2 (MetricsCalculator) config; default if missing.</summary>
    MetricsCalculatorConfig LoadMetricsConfig();

    /// <summary>Read the Step 4 (SignalScorer) config; default if missing.</summary>
    SignalScorerConfig LoadSignalConfig();

    /// <summary>
    /// Write the universe output for a per-ISIN step (2, 4, 5, 6, 7, or 8) to
    /// disk. Calling with a universe-wide step throws — those are written by
    /// their own agents.
    /// </summary>
    void SaveStepOutput(StepId step, string isoWeek, string runId, DataLoaderOutput output);

    /// <summary>
    /// Claim per-ISIN progress rows at the start of a streaming run. For each
    /// fund in <paramref name="step1Output"/> upserts an
    /// <c>IsinProgressEntity</c> with state <c>Processing</c>, the new
    /// <paramref name="runId"/>, <c>CurrentStep = 1</c>,
    /// <c>ProcessingStartedAt = UtcNow</c>, <c>Step01Json</c> populated, and
    /// every later step column cleared (so columns from an earlier run never
    /// coexist with the in-flight run — see backend-nav-sync-plan.md §"Run
    /// boundary").
    /// </summary>
    Task ClaimIsinProgressAsync(DataLoaderOutput step1Output, string runId, CancellationToken ct = default);

    /// <summary>
    /// Write the per-ISIN step columns produced by the per-ISIN block (Steps
    /// 2 → 4 → 5 → 6 → 7 → 8). For each fund identity, upserts the row with
    /// <c>Step02Json</c> … <c>Step08Json</c> populated from the matching
    /// <see cref="PerIsinBlockResult"/> snapshot and <c>CurrentStep = 8</c>.
    /// <c>Step03Json</c> stays null because Step 3 is universe-wide.
    /// </summary>
    Task WriteIsinProgressBlockAsync(PerIsinBlockResult block, string runId, CancellationToken ct = default);

    /// <summary>
    /// Write <c>Step09Json</c> + <c>CurrentStep = 9</c> for every fund in
    /// <paramref name="step9Output"/> into its per-ISIN row. Called after the
    /// universe-wide Step 9 barrier completes; the caller threads Step 9's
    /// in-memory <see cref="DataLoaderOutput"/> through so the gateway no
    /// longer round-trips it via disk JSON (Phase 8 sub-step 8a).
    /// </summary>
    Task WriteIsinProgressStep9Async(DataLoaderOutput step9Output, string runId, CancellationToken ct = default);

    /// <summary>
    /// Stamp a per-fund failure into the IsinProgress row: sets
    /// <c>LastError</c> to <paramref name="errorMessage"/> and increments
    /// <c>AttemptCount</c>. Called from the streaming runner once for each
    /// fund whose per-ISIN chain threw mid-run (see
    /// <see cref="PerIsinBlockResult.FailedFunds"/>). State stays
    /// <c>Processing</c>; the row is flipped back to <c>Free</c> at the end
    /// of the run via <see cref="ReleaseIsinProgressAsync"/>. No-op if the
    /// row doesn't exist (e.g. standalone <see cref="PipelineRunner.RunPerIsinBlockAsync"/>
    /// callers that never claimed).
    /// </summary>
    Task MarkFundFailedAsync(string isin, string runId, string errorMessage, CancellationToken ct = default);

    /// <summary>
    /// Release per-ISIN progress rows at the end of a successful streaming
    /// run. For each fund in <paramref name="step1Output"/> upserts the row
    /// with state <c>Free</c> and clears <c>ProcessingStartedAt</c>; existing
    /// step columns + <c>RunId</c> are preserved as a record of the latest
    /// run.
    /// </summary>
    Task ReleaseIsinProgressAsync(DataLoaderOutput step1Output, string runId, CancellationToken ct = default);
}
