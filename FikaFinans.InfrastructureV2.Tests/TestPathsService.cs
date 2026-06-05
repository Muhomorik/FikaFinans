using FikaFinans.Application.Paths;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.InfrastructureV2.Tests;

// Test-only IPathsService that delegates to the static Paths fixture loader
// (resolves repo root via [CallerFilePath]). Injected into agents via
// AutoFixture: _fixture.Inject<IPathsService>(new TestPathsService()).
public sealed class TestPathsService : IPathsService
{
    public string MetadataCsv(string family, string isoWeek) => Paths.MetadataCsv(family, isoWeek);
    public string SummaryCsv(string family, string isoWeek)  => Paths.SummaryCsv(family, isoWeek);
    public string SnapshotCsv(string family, string isoWeek) => Paths.SnapshotCsv(family, isoWeek);

    public string PositionsCsv               => Paths.PositionsCsvAbs;
    public string PortfolioStructureMd       => Paths.PortfolioStructureMdAbs;
    public string Config02MetricsJson        => Paths.Config02MetricsJsonAbs;
    public string Config04SignalsJson        => Paths.Config04SignalsJsonAbs;
    public string Config09ConvictionJson     => Paths.Config09ConvictionJsonAbs;
    public string Config10PortfolioJson      => Paths.Config10PortfolioJsonAbs;
    public string AnalyticsWeeklySummaryJson => Paths.AnalyticsWeeklySummaryJsonAbs;
    public string AnalyticsSubstitutionChainJson => Paths.AnalyticsSubstitutionChainJsonAbs;
    public string AnalyticsRotationTargetsJson   => Paths.AnalyticsRotationTargetsJsonAbs;

    public string DataLoaderOutput(string isoWeek, PipelineRunId runId)         => Paths.DataLoaderOutput(isoWeek, runId.Value);
    public string DataLoaderError(string isoWeek, PipelineRunId runId)          => Paths.DataLoaderError(isoWeek, runId.Value);
    public string MetricsCalculatorOutput(string isoWeek, PipelineRunId runId)  => Paths.MetricsCalculatorOutput(isoWeek, runId.Value);
    public string MetricsCalculatorError(string isoWeek, PipelineRunId runId)   => Paths.MetricsCalculatorError(isoWeek, runId.Value);
    public string MacroAnalystOutput(string isoWeek, PipelineRunId runId)       => Paths.MacroAnalystOutput(isoWeek, runId.Value);
    public string MacroAnalystError(string isoWeek, PipelineRunId runId)        => Paths.MacroAnalystError(isoWeek, runId.Value);
    public string SignalScorerOutput(string isoWeek, PipelineRunId runId)       => Paths.SignalScorerOutput(isoWeek, runId.Value);
    public string SignalScorerError(string isoWeek, PipelineRunId runId)        => Paths.SignalScorerError(isoWeek, runId.Value);
    public string MacroAlignerOutput(string isoWeek, PipelineRunId runId)       => Paths.MacroAlignerOutput(isoWeek, runId.Value);
    public string MacroAlignerError(string isoWeek, PipelineRunId runId)        => Paths.MacroAlignerError(isoWeek, runId.Value);
    public string CatalystTaggerOutput(string isoWeek, PipelineRunId runId)     => Paths.CatalystTaggerOutput(isoWeek, runId.Value);
    public string CatalystTaggerError(string isoWeek, PipelineRunId runId)      => Paths.CatalystTaggerError(isoWeek, runId.Value);
    public string ThesisValidatorOutput(string isoWeek, PipelineRunId runId)    => Paths.ThesisValidatorOutput(isoWeek, runId.Value);
    public string ThesisValidatorError(string isoWeek, PipelineRunId runId)     => Paths.ThesisValidatorError(isoWeek, runId.Value);
    public string RecommenderOutput(string isoWeek, PipelineRunId runId)        => Paths.RecommenderOutput(isoWeek, runId.Value);
    public string RecommenderError(string isoWeek, PipelineRunId runId)         => Paths.RecommenderError(isoWeek, runId.Value);
    public string UniverseEnricherOutput(string isoWeek, PipelineRunId runId)   => Paths.UniverseEnricherOutput(isoWeek, runId.Value);
    public string UniverseEnricherError(string isoWeek, PipelineRunId runId)    => Paths.UniverseEnricherError(isoWeek, runId.Value);
    public string PortfolioConstructorOutput(string isoWeek, PipelineRunId runId) => Paths.PortfolioConstructorOutput(isoWeek, runId.Value);
    public string PortfolioConstructorError(string isoWeek, PipelineRunId runId)  => Paths.PortfolioConstructorError(isoWeek, runId.Value);

    public string MacroAnalystPromptsDir => Paths.MacroAnalystPromptsAbs;
}
