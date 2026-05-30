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
    /// When <c>true</c>, <see cref="IStreamingPipelineGateway.SaveStepOutput"/>
    /// writes the six per-ISIN boundary JSON files to disk for
    /// developer-debugging only. Default is <c>false</c> as of 2026-05-30
    /// (Phase 8 sub-step 8d) — the canonical source of step outputs is the
    /// SQLite <c>IsinProgress</c> Step01Json..Step09Json columns; WPF
    /// per-step VMs read from there (8c). Flip to <c>true</c> in
    /// <c>appsettings.json</c> when you need on-disk JSON to diff against
    /// a known-good prior run, or for ad-hoc CLI tooling. IsinProgress
    /// column writes are unaffected by this flag — they always happen.
    /// </summary>
    public bool WriteDiskJsonArtifacts { get; init; } = false;
}
