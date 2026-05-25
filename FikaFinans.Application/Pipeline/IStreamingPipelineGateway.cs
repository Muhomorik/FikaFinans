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
}
