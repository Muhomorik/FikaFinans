using System.Text.Json;
using System.Text.Json.Serialization;

namespace FikaFinans.Domain.KanelBulleKapital;

/// <summary>Strongly-typed identifier for a <see cref="Macro.WeeklySummaryRun"/>.</summary>
/// <remarks>
/// A copy of KanelBulleKapital's <c>KanelBrief.Core.Models.WeeklySummaryRunId</c>, which mints
/// it. The JSON converter is the one addition: serialized as a plain string, as KanelBrief's
/// <c>StronglyTypedIdJsonConverter</c> writes it.
/// </remarks>
[JsonConverter(typeof(WeeklySummaryRunIdJsonConverter))]
public readonly record struct WeeklySummaryRunId(string Value)
{
    public bool IsEmpty => string.IsNullOrEmpty(Value);
    public static WeeklySummaryRunId NewId() => new(Guid.NewGuid().ToString());
    public static implicit operator string(WeeklySummaryRunId id) => id.Value ?? string.Empty;
    public static implicit operator WeeklySummaryRunId(string value) => new(value);
    public override string ToString() => Value ?? string.Empty;
}

internal sealed class WeeklySummaryRunIdJsonConverter : JsonConverter<WeeklySummaryRunId>
{
    public override WeeklySummaryRunId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, WeeklySummaryRunId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value ?? string.Empty);
}
