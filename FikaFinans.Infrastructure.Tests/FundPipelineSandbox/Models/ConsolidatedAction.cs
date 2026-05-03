using System.Text.Json.Serialization;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

/// <summary>One row in the Step 6 output. <see cref="Step"/> is 1-based.</summary>
public sealed record ConsolidatedAction(
    [property: JsonPropertyName("step")] int Step,
    [property: JsonPropertyName("fund_name")] string FundName,
    [property: JsonPropertyName("isin")] string Isin,
    [property: JsonPropertyName("action")] ActionType Action,
    [property: JsonPropertyName("thesis_validity")] ThesisValidity ThesisValidity,
    [property: JsonPropertyName("amount_kr")] decimal AmountKr,
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("alternative")] ActionAlternative? Alternative);
