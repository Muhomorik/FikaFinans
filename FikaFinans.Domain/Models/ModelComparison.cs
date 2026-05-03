namespace FikaFinans.Domain.Models;

/// <summary>One question, multiple model answers — the unit of side-by-side comparison.</summary>
public sealed record ModelComparison(string Question, IReadOnlyList<FundAnalyticsRun> Runs);
