using System.Text;
using System.Text.Json;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;
using FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;
using DataLoaderJsonOptions = FikaFinans.InfrastructureV2.Tests.Models.DataLoader.JsonOptions;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAnalyst;

public sealed class MacroAnalystAgent
{
    private const string ConfigVersion = "1.0.0";

    private readonly IMacroLlmClient _llm;
    private readonly string _systemPromptTemplate;
    private readonly string _userPromptTemplate;
    private readonly string _retryPromptTemplate;

    public MacroAnalystAgent(
        IMacroLlmClient llm,
        string? systemPromptTemplate = null,
        string? userPromptTemplate = null,
        string? retryPromptTemplate = null)
    {
        _llm = llm;
        var promptDir = ResolvePromptDir();
        _systemPromptTemplate = systemPromptTemplate ?? File.ReadAllText(Path.Combine(promptDir, "system.md"));
        _userPromptTemplate   = userPromptTemplate   ?? File.ReadAllText(Path.Combine(promptDir, "user-template.md"));
        _retryPromptTemplate  = retryPromptTemplate  ?? File.ReadAllText(Path.Combine(promptDir, "retry.md"));
    }

    public async Task<MacroContext> RunAsync(string isoWeek, string runId, CancellationToken ct = default)
    {
        var dataLoaderPath = Paths.DataLoaderOutput(isoWeek, runId);
        var dataLoader = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(dataLoaderPath), DataLoaderJsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize step 01 output at {dataLoaderPath}");

        var summary = JsonSerializer.Deserialize<WeeklySummaryRun>(
            File.ReadAllText(Paths.AnalyticsWeeklySummaryJsonAbs), AnalyticsJsonOptions.Default)
            ?? throw new InvalidDataException("Failed to deserialize analytics-weekly-summary.json");
        var chain = JsonSerializer.Deserialize<SubstitutionChainRun>(
            File.ReadAllText(Paths.AnalyticsSubstitutionChainJsonAbs), AnalyticsJsonOptions.Default)
            ?? throw new InvalidDataException("Failed to deserialize analytics-substitution-chain.json");
        var targets = JsonSerializer.Deserialize<OpportunityScanRun>(
            File.ReadAllText(Paths.AnalyticsRotationTargetsJsonAbs), AnalyticsJsonOptions.Default)
            ?? throw new InvalidDataException("Failed to deserialize analytics-rotation-targets.json");

        try
        {
            var ctx = await RunInMemoryAsync(summary, chain, targets, dataLoader, isoWeek, ct);
            WriteJson(Paths.MacroAnalystOutput(isoWeek, runId), ctx);
            return ctx;
        }
        catch (MacroAnalystValidationException ex)
        {
            WriteError(Paths.MacroAnalystError(isoWeek, runId), isoWeek, runId, ex.Message);
            throw;
        }
    }

    public async Task<MacroContext> RunInMemoryAsync(
        WeeklySummaryRun summary,
        SubstitutionChainRun chain,
        OpportunityScanRun targets,
        DataLoaderOutput dataLoader,
        string isoWeek,
        CancellationToken ct = default)
    {
        var warnings = new List<string>();
        ValidateInputs(summary, chain, targets, dataLoader, warnings);

        var categoryUniverse = ExtractCategories(dataLoader);

        var systemPrompt = _systemPromptTemplate;
        var userPrompt = BuildUserPrompt(summary, chain, targets, categoryUniverse);

        var raw = await CallWithRetryAsync(systemPrompt, userPrompt, ct);

        var ctx = BuildContext(raw, summary, chain, targets, categoryUniverse, isoWeek, warnings);
        return ctx;
    }

    internal static void ValidateInputs(
        WeeklySummaryRun summary,
        SubstitutionChainRun chain,
        OpportunityScanRun targets,
        DataLoaderOutput dataLoader,
        List<string> warnings)
    {
        if (summary.Status == RunStatus.Failed
         || chain.Status == RunStatus.Failed
         || targets.Status == RunStatus.Failed)
        {
            throw new MacroAnalystValidationException(
                "one or more upstream analytics inputs has status=Failed — content unreliable");
        }

        if (summary.Status == RunStatus.Partial)
            warnings.Add("analytics-weekly-summary status=Partial — proceeding with degraded data");
        if (chain.Status == RunStatus.Partial)
            warnings.Add("analytics-substitution-chain status=Partial — proceeding with degraded data");
        if (targets.Status == RunStatus.Partial)
            warnings.Add("analytics-rotation-targets status=Partial — proceeding with degraded data");

        if (!string.Equals(summary.PeriodIsoWeek, chain.PeriodIsoWeek, StringComparison.Ordinal)
         || !string.Equals(summary.PeriodIsoWeek, targets.PeriodIsoWeek, StringComparison.Ordinal))
        {
            throw new MacroAnalystValidationException(
                $"analytics inputs have mismatched periodIsoWeek: " +
                $"summary={summary.PeriodIsoWeek}, chain={chain.PeriodIsoWeek}, targets={targets.PeriodIsoWeek}");
        }

        if (!string.Equals(chain.WeeklySummaryRunId, summary.RunId, StringComparison.Ordinal))
        {
            throw new MacroAnalystValidationException(
                $"FK chain broken: substitution-chain.weeklySummaryRunId={chain.WeeklySummaryRunId} " +
                $"!= weekly-summary.runId={summary.RunId}");
        }

        if (!string.Equals(targets.SubstitutionChainRunId, chain.RunId, StringComparison.Ordinal))
        {
            throw new MacroAnalystValidationException(
                $"FK chain broken: rotation-targets.substitutionChainRunId={targets.SubstitutionChainRunId} " +
                $"!= substitution-chain.runId={chain.RunId}");
        }

        if (!string.Equals(summary.PeriodIsoWeek, dataLoader.IsoWeek, StringComparison.Ordinal))
        {
            throw new MacroAnalystValidationException(
                $"bundle drift: analytics periodIsoWeek={summary.PeriodIsoWeek} != dataLoader.iso_week={dataLoader.IsoWeek}");
        }
    }

    internal static IReadOnlyList<string> ExtractCategories(DataLoaderOutput dataLoader) =>
        dataLoader.Funds
            .Select(f => f.Metadata.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

    private string BuildUserPrompt(
        WeeklySummaryRun summary,
        SubstitutionChainRun chain,
        OpportunityScanRun targets,
        IReadOnlyList<string> categoryUniverse)
    {
        var categoryList = string.Join("\n", categoryUniverse.Select(c => $"- {c}"));
        var summaryJson  = JsonSerializer.Serialize(summary, AnalyticsJsonOptions.Default);
        var chainJson    = JsonSerializer.Serialize(chain,   AnalyticsJsonOptions.Default);
        var targetsJson  = JsonSerializer.Serialize(targets, AnalyticsJsonOptions.Default);

        return _userPromptTemplate
            .Replace("{iso_week}",                  summary.PeriodIsoWeek)
            .Replace("{weekly_summary_run_id}",     summary.RunId)
            .Replace("{substitution_chain_run_id}", chain.RunId)
            .Replace("{rotation_targets_run_id}",   targets.RunId)
            .Replace("{category_list}",             categoryList)
            .Replace("{weekly_summary_json}",       summaryJson)
            .Replace("{substitution_chain_json}",   chainJson)
            .Replace("{rotation_targets_json}",     targetsJson);
    }

    private async Task<RawMacroResponse> CallWithRetryAsync(
        string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var firstRaw = await _llm.CompleteAsync(systemPrompt, userPrompt, ct);
        if (TryParse(firstRaw, out var firstParsed, out var firstError))
            return firstParsed!;

        var retryPrompt = userPrompt
            + "\n\n---\n\n"
            + _retryPromptTemplate.Replace("{error_message}", firstError ?? "unknown");

        var secondRaw = await _llm.CompleteAsync(systemPrompt, retryPrompt, ct);
        if (TryParse(secondRaw, out var secondParsed, out var secondError))
            return secondParsed!;

        throw new MacroAnalystValidationException(
            $"LLM returned invalid response after retry. First error: {firstError}. Second error: {secondError}");
    }

    private static bool TryParse(string raw, out RawMacroResponse? parsed, out string? error)
    {
        parsed = null;
        error = null;
        try
        {
            var json = JsonExtraction.ExtractFirstJsonObject(raw);
            parsed = JsonSerializer.Deserialize<RawMacroResponse>(json, DataLoaderJsonOptions.Default);
            if (parsed is null)
            {
                error = "deserialization returned null";
                return false;
            }
            if (parsed.RegimeConfidence < 0 || parsed.RegimeConfidence > 1)
            {
                error = $"regime_confidence must be in [0,1], got {parsed.RegimeConfidence}";
                return false;
            }
            return true;
        }
        catch (JsonException ex)
        {
            error = $"JSON parse error: {ex.Message}";
            return false;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static MacroContext BuildContext(
        RawMacroResponse raw,
        WeeklySummaryRun summary,
        SubstitutionChainRun chain,
        OpportunityScanRun targets,
        IReadOnlyList<string> categoryUniverse,
        string isoWeek,
        List<string> warnings)
    {
        var universeSet = new HashSet<string>(categoryUniverse, StringComparer.Ordinal);

        var catalysts = new List<Catalyst>();
        foreach (var rc in raw.Catalysts)
        {
            var filtered = rc.AffectedCategories.Where(universeSet.Contains).Distinct(StringComparer.Ordinal).ToList();
            if (filtered.Count == 0)
            {
                warnings.Add($"dropping catalyst '{rc.Name}' — no affected_categories survive universe filter");
                continue;
            }
            catalysts.Add(new Catalyst
            {
                Name               = rc.Name,
                Intensity          = rc.Intensity,
                WeeksActive        = rc.WeeksActive,
                AffectedCategories = filtered,
                Rationale          = rc.Rationale,
            });
        }

        var themes = new List<RotationTheme>();
        foreach (var rt in raw.RotationThemes)
        {
            var filtered = rt.AffectedCategories.Where(universeSet.Contains).Distinct(StringComparer.Ordinal).ToList();
            if (filtered.Count == 0)
            {
                warnings.Add($"dropping rotation theme '{rt.Label}' — no affected_categories survive universe filter");
                continue;
            }
            themes.Add(new RotationTheme
            {
                Id                 = $"rot_theme_{Slugify(rt.Label)}_{isoWeek}",
                Label              = rt.Label,
                SignalStrength     = rt.SignalStrength,
                AffectedCategories = filtered,
                Rationale          = rt.Rationale,
                SourceChain        = rt.SourceChain,
            });
        }

        if (catalysts.Count == 0 && themes.Count == 0)
            warnings.Add("all catalysts and rotation themes dropped — universe coupling is weak this week");

        return new MacroContext
        {
            GeneratedAt          = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek              = isoWeek,
            ConfigVersion        = ConfigVersion,
            SourceRunIds         = new SourceRunIds
            {
                WeeklySummaryRunId     = summary.RunId,
                SubstitutionChainRunId = chain.RunId,
                RotationTargetsRunId   = targets.RunId,
            },
            MacroRegime          = raw.MacroRegime,
            MacroRegimeSecondary = raw.MacroRegimeSecondary,
            RegimeConfidence     = Math.Clamp(raw.RegimeConfidence, 0m, 1m),
            NetMoodInput         = summary.NetMood,
            Catalysts            = catalysts,
            RotationThemes       = themes,
            Warnings             = warnings.Count > 0 ? warnings : null,
        };
    }

    internal static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "untitled";
        var sb = new StringBuilder(input.Length);
        var lastWasSeparator = false;
        foreach (var ch in input.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && sb.Length > 0)
            {
                sb.Append('_');
                lastWasSeparator = true;
            }
        }
        if (sb.Length > 0 && sb[^1] == '_') sb.Length--;
        return sb.Length == 0 ? "untitled" : sb.ToString();
    }

    private static void WriteJson(string path, MacroContext output)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(output, DataLoaderJsonOptions.Default));
    }

    private static void WriteError(string path, string isoWeek, string runId, string message)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var payload = new
        {
            generated_at = DateTimeOffset.UtcNow.ToString("o"),
            iso_week = isoWeek,
            run_id = runId,
            agent = "03-macroanalyst",
            error = message,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, DataLoaderJsonOptions.Default));
    }

    private static string ResolvePromptDir()
    {
        var testDir = AppContext.BaseDirectory;
        return Path.Combine(testDir, "Agents", "03-macroanalyst", "Prompts");
    }
}
