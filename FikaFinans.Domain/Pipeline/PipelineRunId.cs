using System.Text.Json;
using System.Text.Json.Serialization;

namespace FikaFinans.Domain.Pipeline;

/// <summary>
/// Identifies a single pipeline execution (the per-ISIN run shared by
/// <c>DataLoaderOutput</c> and <c>IsinProgressEntity</c>), threaded through the
/// runner, gateway and every agent. Minted when a fund's progress row is claimed
/// — claim-succeeded and run-started are the same event, so a signal that loses
/// the contention race mints nothing.
/// </summary>
/// <remarks>
/// <para>
/// Shape is <c>{navDate:yyyyMMdd}-{isin}-{8 hex}</c>, for example
/// <c>20260913-SE0001234567-3f9a2b17</c>. The date sorts runs chronologically,
/// the ISIN makes one fund's run greppable across log lines and queue hops, and
/// the random suffix keeps retries of the same fund on the same trading date
/// distinct without any coordination between hosts.
/// </para>
/// <para>
/// The manual WPF path still mints the older <c>yyyyMMdd-HHmm</c> wall-clock
/// stamp from the ViewModel. That shape collides as soon as a queue drives the
/// work — two funds claimed in the same minute collapse onto one id — and goes
/// away when run context leaves the UI.
/// </para>
/// <para>
/// Distinct from the macro run ids (<c>WeeklySummaryRun.RunId</c>,
/// <c>SubstitutionChainRun.RunId</c>, <c>OpportunityScanRun.RunId</c>), which
/// remain plain strings — those identify separate upstream analyses, not a
/// pipeline run.
/// </para>
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
