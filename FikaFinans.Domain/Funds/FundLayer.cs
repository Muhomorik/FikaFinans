using System.Text.Json.Serialization;

namespace FikaFinans.Domain.Funds;

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
