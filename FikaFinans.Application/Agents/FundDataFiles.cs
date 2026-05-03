namespace FikaFinans.Application.Agents;

/// <summary>
/// Hardcoded canonical filenames the analytics prompt expects in the data folder.
/// User is responsible for placing files with these exact names — no fuzzy matching.
/// </summary>
public static class FundDataFiles
{
    public const string Summary = "summary.csv";
    public const string Metadata = "metadata.csv";
    public const string Positions = "positions.csv";
    public const string Structure = "portfolio_structure.md";
    public const string AnalyticsRotationTargets = "analytics-rotation-targets.md";
    public const string AnalyticsSubstitutionChain = "analytics-substitution-chain.md";
    public const string AnalyticsWeeklySummary = "analytics-weekly-summary.md";

    public static IReadOnlyList<string> All { get; } =
        [Summary, Metadata, Positions, Structure,
         AnalyticsRotationTargets, AnalyticsSubstitutionChain, AnalyticsWeeklySummary];

    /// <summary>One-line UI hints describing what each canonical file contains.</summary>
    public static IReadOnlyDictionary<string, string> Descriptions { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Summary] = "Rolling NAV/return windows per fund",
            [Metadata] = "Static fund info (fees, category, ISIN)",
            [Positions] = "Current portfolio holdings",
            [Structure] = "Layer assignments (core/satellite)",
            [AnalyticsRotationTargets] = "Distilled actionable themes",
            [AnalyticsSubstitutionChain] = "Observed capital rotations",
            [AnalyticsWeeklySummary] = "Weekly macro recap",
        };
}
