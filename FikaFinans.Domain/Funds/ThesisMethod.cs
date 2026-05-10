using System.Text.Json.Serialization;

namespace FikaFinans.Domain.Funds;

public enum ThesisMethod
{
    [JsonStringEnumMemberName("matrix")]         Matrix,
    [JsonStringEnumMemberName("llm_refinement")] LlmRefinement,
}
