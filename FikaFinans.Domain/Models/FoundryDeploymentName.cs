using System.Text.Json;
using System.Text.Json.Serialization;

namespace FikaFinans.Domain.Models;

/// <summary>
/// Azure Foundry deployment identifier (e.g. <c>gpt-5.4-1</c>). The exact string Azure
/// expects in <c>DeclarativeAgentDefinition</c>. Never shown in dropdowns; only typed
/// by the user once per deployment.
/// </summary>
[JsonConverter(typeof(FoundryDeploymentNameJsonConverter))]
public readonly record struct FoundryDeploymentName(string Value)
{
    public override string ToString() => Value;
}

internal sealed class FoundryDeploymentNameJsonConverter : JsonConverter<FoundryDeploymentName>
{
    public override FoundryDeploymentName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, FoundryDeploymentName value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
