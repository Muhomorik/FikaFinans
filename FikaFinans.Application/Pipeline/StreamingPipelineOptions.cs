namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Tunables for <see cref="PipelineRunner.RunAllStreamingAsync"/>. Bound from
/// <see cref="FikaFinans.Application.Settings.AppSettings"/> at DI composition
/// time so per-environment overrides (local dev, CI, eventual cloud Function
/// host) all flow through one knob. Per Open Question #5 in
/// <c>Docs/pipeline-step-flow-plan.md</c>: the same value the Phase 2 Function
/// host's <c>maxConcurrentCalls</c> will use.
/// </summary>
public sealed record StreamingPipelineOptions
{
    /// <summary>
    /// Hardcoded prior to 2026-05-27. Kept as a public constant so the doc /
    /// audit references stay anchored to a single source of truth. Real value
    /// to be tuned once per-fund wall time is measured against a real
    /// universe.
    /// </summary>
    public const int DefaultMaxConcurrentFunds = 5;

    /// <summary>
    /// Concurrency cap passed to <c>Merge(maxConcurrent: N)</c> on the
    /// per-ISIN block. Must be at least 1. Higher values trade per-fund
    /// latency against parallel Foundry-call pressure (Steps 5/6/7 are
    /// LLM-bound).
    /// </summary>
    public int MaxConcurrentFunds { get; init; } = DefaultMaxConcurrentFunds;

    /// <summary>
    /// When <c>true</c> (default), <see cref="IStreamingPipelineGateway.SaveStepOutput"/>
    /// writes the six per-ISIN boundary JSON files to disk so the per-tab
    /// "Run this step" buttons and the WPF
    /// <c>StepViewModel.LoadOutputAsync</c> paths keep working after a
    /// streaming run. Per Open Question #4 in
    /// <c>Docs/pipeline-step-flow-plan.md</c> the disk JSON is dev-debugging
    /// scaffolding: once the per-ISIN row inspector UI replaces what disk
    /// JSON gave engineers, this flag flips to <c>false</c> by default and
    /// the canonical-SQLite migration retires the disk readers (separate
    /// future plan). IsinProgress column writes are unaffected — the
    /// gateway always populates Step01Json..Step09Json regardless of this
    /// flag.
    /// </summary>
    public bool WriteDiskJsonArtifacts { get; init; } = true;
}
