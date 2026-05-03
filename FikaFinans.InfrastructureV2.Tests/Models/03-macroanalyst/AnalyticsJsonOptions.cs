using System.Text.Encodings.Web;
using System.Text.Json;

namespace FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;

// Inputs to MacroAnalyst (the three analytics JSONs) are camelCase per the
// upstream KanelBrief schema, with PascalCase string enums. This is distinct
// from the snake_case scheme used by the fund-data chain (steps 01/02).
public static class AnalyticsJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy  = JsonNamingPolicy.CamelCase,
        ReadCommentHandling  = JsonCommentHandling.Skip,
        Encoder              = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
