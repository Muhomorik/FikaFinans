namespace FikaFinans.Domain.Macro;

public sealed class WeeklySummaryRun
{
    public required string ReportType { get; init; }
    public required string RunId { get; init; }
    public required RunStatus Status { get; init; }
    public required string PeriodIsoWeek { get; init; }
    public required MarketSentiment NetMood { get; init; }
    public required string MoodSummary { get; init; }
    public required IReadOnlyList<WeeklySummaryTheme> Themes { get; init; }
}

public sealed class WeeklySummaryTheme
{
    public required string Category { get; init; }
    public required string Summary { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
    public required MarketSentiment Sentiment { get; init; }
}
