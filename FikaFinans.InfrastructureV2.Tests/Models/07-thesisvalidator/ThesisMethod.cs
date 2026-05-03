using System.Text.Json.Serialization;

namespace FikaFinans.InfrastructureV2.Tests.Models.ThesisValidator;

[JsonConverter(typeof(JsonStringEnumConverter<ThesisMethod>))]
public enum ThesisMethod
{
    [JsonStringEnumMemberName("matrix")]         Matrix,
    [JsonStringEnumMemberName("llm_refinement")] LlmRefinement,
}
