using System.Text.Json;
using System.Text.Json.Serialization;

namespace FikaFinans.Domain.Pipeline;

/// <summary>
/// Identifies a single pipeline execution (the per-ISIN run shared by
/// <c>DataLoaderOutput</c> and <c>IsinProgressEntity</c>). Generated at the UI
/// as a <c>yyyyMMdd-HHmm</c> stamp and threaded through the runner, gateway and
/// every agent.
/// </summary>
/// <remarks>
/// Distinct from the macro run ids (<c>WeeklySummaryRun.RunId</c>,
/// <c>SubstitutionChainRun.RunId</c>, <c>OpportunityScanRun.RunId</c>), which
/// remain plain strings — those identify separate upstream analyses, not a
/// pipeline run.
/// </remarks>
[JsonConverter(typeof(PipelineRunIdJsonConverter))]
public readonly record struct PipelineRunId(string Value)
{
    public static PipelineRunId From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new PipelineRunId(value);
    }

    public override string ToString() => Value;
}

internal sealed class PipelineRunIdJsonConverter : JsonConverter<PipelineRunId>
{
    public override PipelineRunId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, PipelineRunId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
