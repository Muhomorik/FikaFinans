using System.Text.Json;
using System.Text.Json.Serialization;

namespace FikaFinans.Domain.KanelBulleKapital;

/// <summary>Strongly-typed identifier for a <see cref="Macro.SubstitutionChainRun"/>.</summary>
/// <remarks>
/// A copy of KanelBulleKapital's <c>KanelBrief.Core.Models.SubstitutionChainRunId</c>, which
/// mints it. The JSON converter is the one addition: serialized as a plain string, as
/// KanelBrief's <c>StronglyTypedIdJsonConverter</c> writes it.
/// </remarks>
[JsonConverter(typeof(SubstitutionChainRunIdJsonConverter))]
public readonly record struct SubstitutionChainRunId(string Value)
{
    public bool IsEmpty => string.IsNullOrEmpty(Value);
    public static SubstitutionChainRunId NewId() => new(Guid.NewGuid().ToString());
    public static implicit operator string(SubstitutionChainRunId id) => id.Value ?? string.Empty;
    public static implicit operator SubstitutionChainRunId(string value) => new(value);
    public override string ToString() => Value ?? string.Empty;
}

internal sealed class SubstitutionChainRunIdJsonConverter : JsonConverter<SubstitutionChainRunId>
{
    public override SubstitutionChainRunId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, SubstitutionChainRunId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value ?? string.Empty);
}
