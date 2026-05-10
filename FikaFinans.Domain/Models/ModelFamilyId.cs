using System.Text.Json;
using System.Text.Json.Serialization;

namespace FikaFinans.Domain.Models;

/// <summary>
/// Friendly model family name shown in settings (e.g. <c>gpt-5.4</c>).
/// Selected by the user; resolved to a <see cref="FoundryDeploymentName"/> at runtime.
/// </summary>
[JsonConverter(typeof(ModelFamilyIdJsonConverter))]
public readonly record struct ModelFamilyId(string Value)
{
    public override string ToString() => Value;
}

internal sealed class ModelFamilyIdJsonConverter : JsonConverter<ModelFamilyId>
{
    public override ModelFamilyId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, ModelFamilyId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
