using FikaFinans.Domain.Macro;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IMacroAnalystAgent
{
    Task<MacroContext> RunAsync(string isoWeek, string runId, CancellationToken ct = default);
}
