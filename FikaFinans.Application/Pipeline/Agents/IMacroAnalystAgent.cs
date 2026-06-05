using FikaFinans.Domain.Macro;

using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IMacroAnalystAgent
{
    Task<MacroContext> RunAsync(string isoWeek, PipelineRunId runId, CancellationToken ct = default);
}
