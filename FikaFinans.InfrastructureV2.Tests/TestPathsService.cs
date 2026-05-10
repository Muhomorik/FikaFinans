using FikaFinans.Application.Paths;

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

    public string DataLoaderOutput(string isoWeek, string runId)         => Paths.DataLoaderOutput(isoWeek, runId);
    public string DataLoaderError(string isoWeek, string runId)          => Paths.DataLoaderError(isoWeek, runId);
    public string MetricsCalculatorOutput(string isoWeek, string runId)  => Paths.MetricsCalculatorOutput(isoWeek, runId);
    public string MetricsCalculatorError(string isoWeek, string runId)   => Paths.MetricsCalculatorError(isoWeek, runId);
    public string MacroAnalystOutput(string isoWeek, string runId)       => Paths.MacroAnalystOutput(isoWeek, runId);
    public string MacroAnalystError(string isoWeek, string runId)        => Paths.MacroAnalystError(isoWeek, runId);
    public string SignalScorerOutput(string isoWeek, string runId)       => Paths.SignalScorerOutput(isoWeek, runId);
    public string SignalScorerError(string isoWeek, string runId)        => Paths.SignalScorerError(isoWeek, runId);
    public string MacroAlignerOutput(string isoWeek, string runId)       => Paths.MacroAlignerOutput(isoWeek, runId);
    public string MacroAlignerError(string isoWeek, string runId)        => Paths.MacroAlignerError(isoWeek, runId);
    public string CatalystTaggerOutput(string isoWeek, string runId)     => Paths.CatalystTaggerOutput(isoWeek, runId);
    public string CatalystTaggerError(string isoWeek, string runId)      => Paths.CatalystTaggerError(isoWeek, runId);
    public string ThesisValidatorOutput(string isoWeek, string runId)    => Paths.ThesisValidatorOutput(isoWeek, runId);
    public string ThesisValidatorError(string isoWeek, string runId)     => Paths.ThesisValidatorError(isoWeek, runId);
    public string RecommenderOutput(string isoWeek, string runId)        => Paths.RecommenderOutput(isoWeek, runId);
    public string RecommenderError(string isoWeek, string runId)         => Paths.RecommenderError(isoWeek, runId);
    public string UniverseEnricherOutput(string isoWeek, string runId)   => Paths.UniverseEnricherOutput(isoWeek, runId);
    public string UniverseEnricherError(string isoWeek, string runId)    => Paths.UniverseEnricherError(isoWeek, runId);
    public string PortfolioConstructorOutput(string isoWeek, string runId) => Paths.PortfolioConstructorOutput(isoWeek, runId);
    public string PortfolioConstructorError(string isoWeek, string runId)  => Paths.PortfolioConstructorError(isoWeek, runId);

    public string MacroAnalystPromptsDir => Paths.MacroAnalystPromptsAbs;
}
