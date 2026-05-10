using System.Runtime.CompilerServices;

namespace FikaFinans.InfrastructureV2.Tests;

/// <summary>
/// Provides utility methods and constants for managing file paths and templates
/// used in the FikaFinans.InfrastructureV2.Tests project.
/// </summary>
/// <remarks>
/// This class includes predefined file path templates, methods to resolve absolute paths.
/// </remarks>
public static class Paths
{
    public static string ProjectRoot { get; } = ResolveProjectRoot();

    public const string MetadataCsvTemplate = "docs/inputs/YieldRaccoon_metadata_{family}_{isoWeek}.csv";
    public const string SummaryCsvTemplate = "docs/inputs/YieldRaccoon_summary_{family}_{isoWeek}.csv";
    public const string SnapshotCsvTemplate = "docs/inputs/YieldRaccoon_snapshot_{family}_{isoWeek}.csv";
    public const string PositionsCsv = "docs/inputs/positions.csv";
    public const string PortfolioStructureMd = "docs/inputs/portfolio_structure.md";
    public const string Config02MetricsJson = "docs/inputs/config-02-metrics.json";
    public const string Config04SignalsJson = "docs/inputs/config-04-signals.json";
    public const string Config09ConvictionJson = "docs/inputs/config-09-conviction.json";
    public const string Config10PortfolioJson = "docs/inputs/config-10-portfolio.json";
    public const string AnalyticsWeeklySummaryJson = "docs/inputs/analytics-weekly-summary.json";
    public const string AnalyticsSubstitutionChainJson = "docs/inputs/analytics-substitution-chain.json";
    public const string AnalyticsRotationTargetsJson = "docs/inputs/analytics-rotation-targets.json";

    public const string DataLoaderOutputTemplate = "stepOutputs/01-dataloader-{isoWeek}-{runId}.json";
    public const string DataLoaderErrorTemplate = "stepOutputs/01-error-{isoWeek}-{runId}.json";
    public const string MetricsCalculatorOutputTemplate = "stepOutputs/02-metrics-{isoWeek}-{runId}.json";
    public const string MetricsCalculatorErrorTemplate = "stepOutputs/02-error-{isoWeek}-{runId}.json";
    public const string MacroAnalystOutputTemplate = "stepOutputs/03-macro-{isoWeek}-{runId}.json";
    public const string MacroAnalystErrorTemplate = "stepOutputs/03-error-{isoWeek}-{runId}.json";
    public const string SignalScorerOutputTemplate = "stepOutputs/04-signal-{isoWeek}-{runId}.json";
    public const string SignalScorerErrorTemplate = "stepOutputs/04-error-{isoWeek}-{runId}.json";
    public const string MacroAlignerOutputTemplate = "stepOutputs/05-macro-align-{isoWeek}-{runId}.json";
    public const string MacroAlignerErrorTemplate = "stepOutputs/05-error-{isoWeek}-{runId}.json";
    public const string CatalystTaggerOutputTemplate = "stepOutputs/06-catalyst-{isoWeek}-{runId}.json";
    public const string CatalystTaggerErrorTemplate = "stepOutputs/06-error-{isoWeek}-{runId}.json";
    public const string ThesisValidatorOutputTemplate = "stepOutputs/07-thesis-{isoWeek}-{runId}.json";
    public const string ThesisValidatorErrorTemplate = "stepOutputs/07-error-{isoWeek}-{runId}.json";
    public const string RecommenderOutputTemplate = "stepOutputs/08-recommendation-{isoWeek}-{runId}.json";
    public const string RecommenderErrorTemplate = "stepOutputs/08-error-{isoWeek}-{runId}.json";
    public const string UniverseEnricherOutputTemplate = "stepOutputs/09-enrichment-{isoWeek}-{runId}.json";
    public const string UniverseEnricherErrorTemplate = "stepOutputs/09-error-{isoWeek}-{runId}.json";
    public const string PortfolioConstructorOutputTemplate = "stepOutputs/10-trades-{isoWeek}-{runId}.json";
    public const string PortfolioConstructorErrorTemplate = "stepOutputs/10-error-{isoWeek}-{runId}.json";

    public const string MacroAnalystPromptsRelative = "Agents/03-macroanalyst/Prompts";
    public static string MacroAnalystPromptsAbs => Path.Combine(ProjectRoot, MacroAnalystPromptsRelative);

    public static string MetadataCsv(string family, string isoWeek) =>
        Path.Combine(ProjectRoot, MetadataCsvTemplate.Replace("{family}", family).Replace("{isoWeek}", isoWeek));

    public static string SummaryCsv(string family, string isoWeek) =>
        Path.Combine(ProjectRoot, SummaryCsvTemplate.Replace("{family}", family).Replace("{isoWeek}", isoWeek));

    public static string SnapshotCsv(string family, string isoWeek) =>
        Path.Combine(ProjectRoot, SnapshotCsvTemplate.Replace("{family}", family).Replace("{isoWeek}", isoWeek));

    public static string PositionsCsvAbs => Path.Combine(ProjectRoot, PositionsCsv);
    public static string PortfolioStructureMdAbs => Path.Combine(ProjectRoot, PortfolioStructureMd);
    public static string Config02MetricsJsonAbs => Path.Combine(ProjectRoot, Config02MetricsJson);
    public static string Config04SignalsJsonAbs => Path.Combine(ProjectRoot, Config04SignalsJson);
    public static string Config09ConvictionJsonAbs => Path.Combine(ProjectRoot, Config09ConvictionJson);
    public static string Config10PortfolioJsonAbs => Path.Combine(ProjectRoot, Config10PortfolioJson);
    public static string AnalyticsWeeklySummaryJsonAbs => Path.Combine(ProjectRoot, AnalyticsWeeklySummaryJson);
    public static string AnalyticsSubstitutionChainJsonAbs => Path.Combine(ProjectRoot, AnalyticsSubstitutionChainJson);
    public static string AnalyticsRotationTargetsJsonAbs => Path.Combine(ProjectRoot, AnalyticsRotationTargetsJson);

    public static string DataLoaderOutput(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            DataLoaderOutputTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string DataLoaderError(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            DataLoaderErrorTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string MetricsCalculatorOutput(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            MetricsCalculatorOutputTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string MetricsCalculatorError(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            MetricsCalculatorErrorTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string MacroAnalystOutput(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            MacroAnalystOutputTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string MacroAnalystError(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            MacroAnalystErrorTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string SignalScorerOutput(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            SignalScorerOutputTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string SignalScorerError(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            SignalScorerErrorTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string MacroAlignerOutput(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            MacroAlignerOutputTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string MacroAlignerError(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            MacroAlignerErrorTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string CatalystTaggerOutput(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            CatalystTaggerOutputTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string CatalystTaggerError(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            CatalystTaggerErrorTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string ThesisValidatorOutput(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            ThesisValidatorOutputTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string ThesisValidatorError(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            ThesisValidatorErrorTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string RecommenderOutput(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            RecommenderOutputTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string RecommenderError(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            RecommenderErrorTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string UniverseEnricherOutput(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            UniverseEnricherOutputTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string UniverseEnricherError(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            UniverseEnricherErrorTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string PortfolioConstructorOutput(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            PortfolioConstructorOutputTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    public static string PortfolioConstructorError(string isoWeek, string runId) =>
        Path.Combine(ProjectRoot,
            PortfolioConstructorErrorTemplate.Replace("{isoWeek}", isoWeek).Replace("{runId}", runId));

    private static string ResolveProjectRoot([CallerFilePath] string thisFile = "") =>
        Path.GetDirectoryName(thisFile)!;
}
