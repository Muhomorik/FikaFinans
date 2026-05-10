using System.IO;
using System.Text;
using System.Text.Json;
using DevExpress.Mvvm;
using FikaFinans.Application.Paths;
using NLog;

namespace FikaFinans.Wpf.ViewModels;

public sealed class FundDetailViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions IndentedOpts = new() { WriteIndented = true };

    private readonly ILogger? _logger;
    private readonly IPathsService? _paths;

    private string _fundIsin = string.Empty;
    private string _isoWeek = string.Empty;
    private string _runId = string.Empty;
    private string _fundName = string.Empty;
    private FundStepSection[] _sections = Enumerable.Range(1, 10)
        .Select(i => new FundStepSection(i, StepName(i), "(not loaded)"))
        .ToArray();

    public string FundIsin
    {
        get => _fundIsin;
        set => SetProperty(ref _fundIsin, value, nameof(FundIsin));
    }

    public string IsoWeek
    {
        get => _isoWeek;
        set => SetProperty(ref _isoWeek, value, nameof(IsoWeek));
    }

    public string RunId
    {
        get => _runId;
        set => SetProperty(ref _runId, value, nameof(RunId));
    }

    public string FundName
    {
        get => _fundName;
        set => SetProperty(ref _fundName, value, nameof(FundName));
    }

    public string WindowTitle => $"{FundIsin} — {FundName}";

    public FundStepSection[] Sections
    {
        get => _sections;
        private set => SetProperty(ref _sections, value, nameof(Sections));
    }

    public FundDetailViewModel(ILogger logger, IPathsService paths) : this()
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _paths  = paths  ?? throw new ArgumentNullException(nameof(paths));
    }

    public FundDetailViewModel() { }

    public void Load(string fundIsin, string isoWeek, string runId)
    {
        FundIsin = fundIsin;
        IsoWeek  = isoWeek;
        RunId    = runId;

        var sections = new FundStepSection[10];

        if (_paths is null)
        {
            for (var i = 0; i < 10; i++)
                sections[i] = new FundStepSection(i + 1, StepName(i + 1), "(paths not configured)");
            Sections = sections;
            RaisePropertyChanged(nameof(WindowTitle));
            return;
        }

        var stepPaths = new[]
        {
            _paths.DataLoaderOutput(isoWeek, runId),
            _paths.MetricsCalculatorOutput(isoWeek, runId),
            _paths.MacroAnalystOutput(isoWeek, runId),
            _paths.SignalScorerOutput(isoWeek, runId),
            _paths.MacroAlignerOutput(isoWeek, runId),
            _paths.CatalystTaggerOutput(isoWeek, runId),
            _paths.ThesisValidatorOutput(isoWeek, runId),
            _paths.RecommenderOutput(isoWeek, runId),
            _paths.UniverseEnricherOutput(isoWeek, runId),
            _paths.PortfolioConstructorOutput(isoWeek, runId),
        };

        for (var i = 0; i < 10; i++)
        {
            var step = i + 1;
            var path = stepPaths[i];
            try
            {
                var content = BuildSectionContent(step, fundIsin, path);
                sections[i] = new FundStepSection(step, StepName(step), content);

                // Extract fund name from step 1 while we're there
                if (step == 1 && string.IsNullOrEmpty(FundName) && File.Exists(path))
                    FundName = ExtractFundName(path, fundIsin) ?? fundIsin;
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, "Failed to load step {Step} for {Isin}", step, fundIsin);
                sections[i] = new FundStepSection(step, StepName(step), $"(error: {ex.Message})");
            }
        }

        Sections = sections;
        if (string.IsNullOrEmpty(FundName)) FundName = fundIsin;
        RaisePropertyChanged(nameof(WindowTitle));
    }

    private static string BuildSectionContent(int step, string isin, string jsonPath)
    {
        if (!File.Exists(jsonPath)) return "(step not run yet)";

        if (step == 10)
            return BuildStep10Content(isin, jsonPath);

        var fund = FindFundInOutput(jsonPath, isin);
        if (fund is null) return "(fund not found in output)";

        return step switch
        {
            1 => FormatFields(fund.Value,
                    "metadata.name", "metadata.category", "metadata.currency_code",
                    "nav_buckets_count", "currently_held", "current_value_kr", "snapshot"),
            2 => FormatFieldOrFallback(fund.Value, "metrics"),
            3 => "(global macro analysis — no per-fund data)",
            4 => FormatFields(fund.Value, "signal", "rule_fired", "criteria_evaluation"),
            5 => FormatFields(fund.Value, "macro_alignment", "matched_theme", "promoted_to_forming", "promotion_reason"),
            6 => FormatFieldOrFallback(fund.Value, "catalyst"),
            7 => FormatFields(fund.Value, "thesis_validity", "thesis_rationale", "thesis_method"),
            8 => FormatFields(fund.Value, "recommendation", "recommendation_reason"),
            9 => FormatFields(fund.Value, "conviction_score", "conviction_breakdown", "universe_rank", "alternatives"),
            _ => "(unknown step)",
        };
    }

    private static JsonElement? FindFundInOutput(string jsonPath, string isin)
    {
        var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        if (!doc.RootElement.TryGetProperty("funds", out var funds)) return null;

        foreach (var fund in funds.EnumerateArray())
        {
            if (fund.TryGetProperty("isin", out var isinEl) &&
                string.Equals(isinEl.GetString(), isin, StringComparison.OrdinalIgnoreCase))
                return fund;
        }
        return null;
    }

    private static string? ExtractFundName(string jsonPath, string isin)
    {
        var fund = FindFundInOutput(jsonPath, isin);
        if (fund is null) return null;
        if (!fund.Value.TryGetProperty("metadata", out var meta)) return null;
        if (!meta.TryGetProperty("name", out var name)) return null;
        return name.GetString();
    }

    private static string FormatFields(JsonElement fund, params string[] fields)
    {
        var sb = new StringBuilder();
        foreach (var field in fields)
        {
            // Support "metadata.name" dot-notation for nested objects
            var parts = field.Split('.');
            var el = fund;
            var found = true;
            foreach (var part in parts)
            {
                if (!el.TryGetProperty(part, out el)) { found = false; break; }
            }
            if (!found || el.ValueKind == JsonValueKind.Null) continue;

            var label = parts[^1];
            string value;
            if (field == "nav_buckets_count")
            {
                var count = fund.TryGetProperty("nav_buckets", out var nb) ? nb.GetArrayLength() : 0;
                value = $"{count} buckets";
            }
            else
            {
                value = FormatValue(el);
            }
            sb.AppendLine($"{label}: {value}");
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no data for this step)";
    }

    private static string FormatFieldOrFallback(JsonElement fund, string field)
    {
        if (!fund.TryGetProperty(field, out var el) || el.ValueKind == JsonValueKind.Null)
            return "(no data for this step)";
        return el.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Serialize(el, IndentedOpts)
            : FormatValue(el);
    }

    private static string FormatValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String  => el.GetString() ?? string.Empty,
        JsonValueKind.Number  => el.ToString(),
        JsonValueKind.True    => "true",
        JsonValueKind.False   => "false",
        JsonValueKind.Array   => $"[{el.GetArrayLength()} items]",
        JsonValueKind.Object  => JsonSerializer.Serialize(el, IndentedOpts),
        _                     => el.ToString(),
    };

    private static string BuildStep10Content(string isin, string jsonPath)
    {
        var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        if (!doc.RootElement.TryGetProperty("trades", out var trades))
            return "(no trades output)";

        var sb = new StringBuilder();
        foreach (var trade in trades.EnumerateArray())
        {
            if (!trade.TryGetProperty("isin", out var isinEl)) continue;
            if (!string.Equals(isinEl.GetString(), isin, StringComparison.OrdinalIgnoreCase)) continue;

            var tradeType = trade.TryGetProperty("trade", out var tt) ? tt.GetString() : "?";
            var amount    = trade.TryGetProperty("amount_kr", out var amt) ? $"{amt} kr" : "?";
            var reason    = trade.TryGetProperty("trade_reason", out var tr) ? tr.GetString() : "?";
            sb.AppendLine($"trade: {tradeType}");
            sb.AppendLine($"amount: {amount}");
            sb.AppendLine($"reason: {reason}");
            sb.AppendLine();
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no trades for this fund)";
    }

    private static string StepName(int step) => step switch
    {
        1  => "Data loader",
        2  => "Metrics calculator",
        3  => "Macro analyst",
        4  => "Signal scorer",
        5  => "Macro aligner",
        6  => "Catalyst tagger",
        7  => "Thesis validator",
        8  => "Recommender",
        9  => "Universe enricher",
        10 => "Portfolio constructor",
        _  => $"Step {step}",
    };
}

public sealed record FundStepSection(int StepNumber, string AgentName, string Content);
