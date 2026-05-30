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
}
