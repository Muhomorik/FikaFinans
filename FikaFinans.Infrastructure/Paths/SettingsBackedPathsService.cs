using FikaFinans.Application.Paths;
using FikaFinans.Application.Settings;

namespace FikaFinans.Infrastructure.Paths;

/// <summary>
/// Production <see cref="IPathsService"/> that resolves every path from
/// <see cref="AppSettings.Folders"/> stored in <see cref="IAppSettingsStore"/>.
/// When a folder setting is empty, falls back to <c>Documents\{AppName}\...</c>.
/// Tests use the [CallerFilePath]-based <c>TestPathsService</c> instead.
/// </summary>
public sealed class SettingsBackedPathsService : IPathsService
{
    // Derived from the assembly namespace — no literal string hardcoded.
    private static readonly string AppName =
        typeof(SettingsBackedPathsService).Namespace!.Split('.')[0];

    private static string DefaultBase =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppName);

    private readonly IAppSettingsStore _store;

    public SettingsBackedPathsService(IAppSettingsStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    // Reloads settings each time so live changes (Settings dialog Save) take effect
    // without restarting. Load() returns the cached instance when unchanged.
    private FolderSettings Folders => _store.Load().Folders;

    private string InputBase
    {
        get
        {
            var v = Folders.YieldRaccoonInputs;
            return string.IsNullOrWhiteSpace(v) ? Path.Combine(DefaultBase, "inputs") : v;
        }
    }

    private string AnalyticsBase
    {
        get
        {
            var f = Folders;
            return string.IsNullOrWhiteSpace(f.AnalyticsJson) ? InputBase : f.AnalyticsJson;
        }
    }

    private string OutputBase
    {
        get
        {
            var v = Folders.StepOutputs;
            return string.IsNullOrWhiteSpace(v) ? Path.Combine(DefaultBase, "stepOutputs") : v;
        }
    }

    // ── CSV inputs ─────────────────────────────────────────────────────────
    public string MetadataCsv(string family, string isoWeek) =>
        Path.Combine(InputBase, $"YieldRaccoon_metadata_{family}_{isoWeek}.csv");

    public string SummaryCsv(string family, string isoWeek) =>
        Path.Combine(InputBase, $"YieldRaccoon_summary_{family}_{isoWeek}.csv");

    public string SnapshotCsv(string family, string isoWeek) =>
        Path.Combine(InputBase, $"YieldRaccoon_snapshot_{family}_{isoWeek}.csv");

    // ── Static inputs ──────────────────────────────────────────────────────
    public string PositionsCsv         => Path.Combine(InputBase, "positions.csv");
    public string PortfolioStructureMd => Path.Combine(InputBase, "portfolio_structure.md");

    // ── Configs ────────────────────────────────────────────────────────────
    public string Config02MetricsJson    => Path.Combine(InputBase, "config-02-metrics.json");
    public string Config04SignalsJson    => Path.Combine(InputBase, "config-04-signals.json");
    public string Config09ConvictionJson => Path.Combine(InputBase, "config-09-conviction.json");
    public string Config10PortfolioJson  => Path.Combine(InputBase, "config-10-portfolio.json");

    // ── Analytics JSONs ────────────────────────────────────────────────────
    public string AnalyticsWeeklySummaryJson      => Path.Combine(AnalyticsBase, "analytics-weekly-summary.json");
    public string AnalyticsSubstitutionChainJson  => Path.Combine(AnalyticsBase, "analytics-substitution-chain.json");
    public string AnalyticsRotationTargetsJson    => Path.Combine(AnalyticsBase, "analytics-rotation-targets.json");

    // ── Agent assets ──────────────────────────────────────────────────────
    // Always rooted under Documents\{AppName}\Agents — not user-configurable yet.
    public string MacroAnalystPromptsDir =>
        Path.Combine(DefaultBase, "Agents", "03-macroanalyst", "Prompts");

    // ── Step outputs ───────────────────────────────────────────────────────
    public string DataLoaderOutput(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"01-dataloader-{isoWeek}-{runId}.json");

    public string DataLoaderError(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"01-error-{isoWeek}-{runId}.json");

    public string MetricsCalculatorOutput(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"02-metrics-{isoWeek}-{runId}.json");

    public string MetricsCalculatorError(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"02-error-{isoWeek}-{runId}.json");

    public string MacroAnalystOutput(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"03-macro-{isoWeek}-{runId}.json");

    public string MacroAnalystError(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"03-error-{isoWeek}-{runId}.json");

    public string SignalScorerOutput(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"04-signal-{isoWeek}-{runId}.json");

    public string SignalScorerError(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"04-error-{isoWeek}-{runId}.json");

    public string MacroAlignerOutput(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"05-macro-align-{isoWeek}-{runId}.json");

    public string MacroAlignerError(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"05-error-{isoWeek}-{runId}.json");

    public string CatalystTaggerOutput(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"06-catalyst-{isoWeek}-{runId}.json");

    public string CatalystTaggerError(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"06-error-{isoWeek}-{runId}.json");

    public string ThesisValidatorOutput(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"07-thesis-{isoWeek}-{runId}.json");

    public string ThesisValidatorError(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"07-error-{isoWeek}-{runId}.json");

    public string RecommenderOutput(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"08-recommendation-{isoWeek}-{runId}.json");

    public string RecommenderError(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"08-error-{isoWeek}-{runId}.json");

    public string UniverseEnricherOutput(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"09-enrichment-{isoWeek}-{runId}.json");

    public string UniverseEnricherError(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"09-error-{isoWeek}-{runId}.json");

    public string PortfolioConstructorOutput(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"10-trades-{isoWeek}-{runId}.json");

    public string PortfolioConstructorError(string isoWeek, string runId) =>
        Path.Combine(OutputBase, $"10-error-{isoWeek}-{runId}.json");
}
