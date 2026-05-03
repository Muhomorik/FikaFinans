using System.Text.Json.Serialization;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

/// <summary>
/// One-line alternative-fund hint surfaced under a Buy action. Null when no
/// meaningfully better same-category peer exists.
/// </summary>
public sealed record ActionAlternative(
    [property: JsonPropertyName("fund_name")] string FundName,
    [property: JsonPropertyName("differentiator")] string Differentiator);
