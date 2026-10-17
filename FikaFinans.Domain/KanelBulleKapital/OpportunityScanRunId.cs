using System.Text.Json;
using System.Text.Json.Serialization;

namespace FikaFinans.Domain.KanelBulleKapital;

/// <summary>Strongly-typed identifier for an <see cref="Macro.OpportunityScanRun"/>.</summary>
/// <remarks>
/// A copy of KanelBulleKapital's <c>KanelBrief.Core.Models.OpportunityScanRunId</c>, which
/// mints it. The export file and <c>SourceRunIds.RotationTargetsRunId</c> call the same report
/// "rotation targets". The JSON converter is the one addition: serialized as a plain string,
/// as KanelBrief's <c>StronglyTypedIdJsonConverter</c> writes it.
/// </remarks>
[JsonConverter(typeof(OpportunityScanRunIdJsonConverter))]
public readonly record struct OpportunityScanRunId(string Value)
{
    public bool IsEmpty => string.IsNullOrEmpty(Value);
    public static OpportunityScanRunId NewId() => new(Guid.NewGuid().ToString());
    public static implicit operator string(OpportunityScanRunId id) => id.Value ?? string.Empty;
    public static implicit operator OpportunityScanRunId(string value) => new(value);
    public override string ToString() => Value ?? string.Empty;
}

internal sealed class OpportunityScanRunIdJsonConverter : JsonConverter<OpportunityScanRunId>
{
    public override OpportunityScanRunId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, OpportunityScanRunId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value ?? string.Empty);
}
