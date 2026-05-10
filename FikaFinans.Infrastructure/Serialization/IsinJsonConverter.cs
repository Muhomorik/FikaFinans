using System.Text.Json;
using System.Text.Json.Serialization;
using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Infrastructure.Serialization;

public sealed class IsinJsonConverter : JsonConverter<Isin>
{
    public override Isin Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, Isin value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
