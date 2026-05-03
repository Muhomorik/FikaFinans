using System.Text.Json.Serialization;

namespace FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

[JsonConverter(typeof(JsonStringEnumConverter<FundLayer>))]
public enum FundLayer
{
    [JsonStringEnumMemberName("core")]   Core,
    [JsonStringEnumMemberName("active")] Active,
}

public enum PinnedLayer
{
    Core,
    Writeoff,
}
