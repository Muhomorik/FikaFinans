using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Six per-step universe outputs captured during a single
/// <see cref="PipelineRunner.RunPerIsinBlockAsync"/> fan-out. Each output is a
/// full <see cref="DataLoaderOutput"/> with field population matching the
/// canonical universe-wide step output (e.g. <see cref="Step2Output"/> has
/// metrics populated but signals null, <see cref="Step8Output"/> has the
/// full per-ISIN chain populated). Callers that need to write per-step
/// boundary JSON files (so the per-tab "Run this step" buttons stay
/// functional after a streaming run) save each output through
/// <see cref="IStreamingPipelineGateway.SaveStepOutput"/>.
/// </summary>
/// <param name="FailedFunds">
/// ISIN → error-message map for funds whose per-ISIN chain threw mid-run.
/// Failed funds are excluded from every <c>StepNOutput.Funds</c> list so
/// downstream consumers see a clean universe; callers stamp each entry into
/// <see cref="IStreamingPipelineGateway.MarkFundFailedAsync"/> to record the
/// failure on the per-ISIN row.
/// </param>
public sealed record PerIsinBlockResult(
    DataLoaderOutput Step2Output,
    DataLoaderOutput Step4Output,
    DataLoaderOutput Step5Output,
    DataLoaderOutput Step6Output,
    DataLoaderOutput Step7Output,
    DataLoaderOutput Step8Output,
    IReadOnlyDictionary<string, string> FailedFunds);
