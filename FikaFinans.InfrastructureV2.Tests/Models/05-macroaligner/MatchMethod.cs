using System.Text.Json.Serialization;

namespace FikaFinans.InfrastructureV2.Tests.Models.MacroAligner;

[JsonConverter(typeof(JsonStringEnumConverter<MatchMethod>))]
public enum MatchMethod
{
    [JsonStringEnumMemberName("direct_category")] DirectCategory,
    [JsonStringEnumMemberName("llm_adjacency")]   LlmAdjacency,
    [JsonStringEnumMemberName("none")]            None,
}
