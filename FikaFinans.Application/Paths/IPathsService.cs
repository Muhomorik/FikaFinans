namespace FikaFinans.Application.Paths;

// Contract used by every pipeline agent for resolving the absolute paths of
// inputs, outputs, and configs. Production wiring (FikaFinans.Wpf) provides
// SettingsBackedPathsService; tests inject TestPathsService that delegates to
// the [CallerFilePath]-based Paths.cs fixture loader.
public interface IPathsService
{
    string MetadataCsv(string family, string isoWeek);
    string SummaryCsv(string family, string isoWeek);
    string SnapshotCsv(string family, string isoWeek);
    string PositionsCsv { get; }
    string PortfolioStructureMd { get; }

    string Config02MetricsJson { get; }
    string Config04SignalsJson { get; }
    string Config09ConvictionJson { get; }
    string Config10PortfolioJson { get; }

    string AnalyticsWeeklySummaryJson { get; }
    string AnalyticsSubstitutionChainJson { get; }
    string AnalyticsRotationTargetsJson { get; }

    string DataLoaderOutput(string isoWeek, string runId);
    string DataLoaderError(string isoWeek, string runId);
    string MetricsCalculatorOutput(string isoWeek, string runId);
    string MetricsCalculatorError(string isoWeek, string runId);
    string MacroAnalystOutput(string isoWeek, string runId);
    string MacroAnalystError(string isoWeek, string runId);
    string SignalScorerOutput(string isoWeek, string runId);
    string SignalScorerError(string isoWeek, string runId);
    string MacroAlignerOutput(string isoWeek, string runId);
    string MacroAlignerError(string isoWeek, string runId);
    string CatalystTaggerOutput(string isoWeek, string runId);
    string CatalystTaggerError(string isoWeek, string runId);
    string ThesisValidatorOutput(string isoWeek, string runId);
    string ThesisValidatorError(string isoWeek, string runId);
    string RecommenderOutput(string isoWeek, string runId);
    string RecommenderError(string isoWeek, string runId);
    string UniverseEnricherOutput(string isoWeek, string runId);
    string UniverseEnricherError(string isoWeek, string runId);
    string PortfolioConstructorOutput(string isoWeek, string runId);
    string PortfolioConstructorError(string isoWeek, string runId);

    string MacroAnalystPromptsDir { get; }
}
