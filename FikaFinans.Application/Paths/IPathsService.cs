using FikaFinans.Domain.Pipeline;

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

    string DataLoaderOutput(string isoWeek, PipelineRunId runId);
    string DataLoaderError(string isoWeek, PipelineRunId runId);
    string MetricsCalculatorOutput(string isoWeek, PipelineRunId runId);
    string MetricsCalculatorError(string isoWeek, PipelineRunId runId);
    string MacroAnalystOutput(string isoWeek, PipelineRunId runId);
    string MacroAnalystError(string isoWeek, PipelineRunId runId);
    string SignalScorerOutput(string isoWeek, PipelineRunId runId);
    string SignalScorerError(string isoWeek, PipelineRunId runId);
    string MacroAlignerOutput(string isoWeek, PipelineRunId runId);
    string MacroAlignerError(string isoWeek, PipelineRunId runId);
    string CatalystTaggerOutput(string isoWeek, PipelineRunId runId);
    string CatalystTaggerError(string isoWeek, PipelineRunId runId);
    string ThesisValidatorOutput(string isoWeek, PipelineRunId runId);
    string ThesisValidatorError(string isoWeek, PipelineRunId runId);
    string RecommenderOutput(string isoWeek, PipelineRunId runId);
    string RecommenderError(string isoWeek, PipelineRunId runId);
    string UniverseEnricherOutput(string isoWeek, PipelineRunId runId);
    string UniverseEnricherError(string isoWeek, PipelineRunId runId);
    string PortfolioConstructorOutput(string isoWeek, PipelineRunId runId);
    string PortfolioConstructorError(string isoWeek, PipelineRunId runId);

    string MacroAnalystPromptsDir { get; }
}
