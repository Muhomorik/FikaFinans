namespace FikaFinans.Domain.Models;

/// <summary>
/// One model's response to a fund-analytics question. Free-form Markdown response (no
/// structured-JSON contract) plus token usage and elapsed time for side-by-side comparison.
/// </summary>
public sealed record FundAnalyticsRun(
    string ModelId,
    string Question,
    string ResponseText,
    int InputTokens,
    int OutputTokens,
    long ElapsedMs,
    DateTimeOffset RanAtUtc);
