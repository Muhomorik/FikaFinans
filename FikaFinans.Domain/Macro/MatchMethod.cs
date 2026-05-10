using System.Text.Json.Serialization;

namespace FikaFinans.Domain.Macro;

public enum MatchMethod
{
    [JsonStringEnumMemberName("direct_category")] DirectCategory,
    [JsonStringEnumMemberName("llm_adjacency")]   LlmAdjacency,
    [JsonStringEnumMemberName("none")]            None,
}
