using FikaFinans.Domain.Macro;

using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Agents;

/// <summary>
/// Step 3 of the pipeline (<see cref="StepId.MacroAnalyst"/>). Reads the week's three
/// analytics reports — weekly summary, substitution chain, rotation targets — and produces
/// one <see cref="MacroContext"/>: the macro regime, active catalysts and rotation themes.
/// LLM step, one call per week rather than one per fund.
/// </summary>
/// <remarks>
/// The output is shared by every fund in the week — steps 5 and 6 match each fund against
/// its themes and catalysts. Catalysts and themes are restricted to the fund categories
/// that exist in step 1's output, so the LLM cannot invent one no fund has.
/// </remarks>
public interface IMacroAnalystAgent
{
    /// <summary>
    /// Universe-wide entry point used by the per-tab "Run this step" button and the current
    /// sequential <c>PipelineRunner</c>. Reads step 1's output and the three analytics JSON
    /// files from disk, calls the LLM, writes the output file, and returns the in-memory
    /// result. On a validation failure it writes the step's error file and rethrows.
    /// </summary>
    /// <param name="isoWeek">Must match the analytics reports' <c>periodIsoWeek</c>.</param>
    /// <param name="runId">Selects step 1's output file and names this step's output.</param>
    /// <param name="ct">Cancels the LLM call.</param>
    Task<MacroContext> RunAsync(string isoWeek, PipelineRunId runId, CancellationToken ct = default);
}
