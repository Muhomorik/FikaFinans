using System.Text.Json;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
using FikaFinans.Infrastructure.Pipeline.Json;

namespace FikaFinans.Infrastructure.Pipeline;

/// <summary>
/// JSON / disk implementation of <see cref="IStreamingPipelineGateway"/>.
/// Reads and writes the same files the universe-wide agents read and write,
/// so a streaming run leaves identical on-disk artifacts behind.
/// </summary>
public sealed class StreamingPipelineGateway : IStreamingPipelineGateway
{
    private readonly IPathsService _paths;

    public StreamingPipelineGateway(IPathsService paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
    }

    public DataLoaderOutput LoadStep1Output(string isoWeek, string runId)
    {
        var path = _paths.DataLoaderOutput(isoWeek, runId);
        return JsonSerializer.Deserialize<DataLoaderOutput>(File.ReadAllText(path), JsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize Step 1 output at {path}");
    }

    public MacroContext LoadStep3Output(string isoWeek, string runId)
    {
        var path = _paths.MacroAnalystOutput(isoWeek, runId);
        return JsonSerializer.Deserialize<MacroContext>(File.ReadAllText(path), JsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize Step 3 output at {path}");
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

    public void SaveStepOutput(StepId step, string isoWeek, string runId, DataLoaderOutput output)
    {
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

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(output, JsonOptions.Default));
    }
}
