using System.Text.Json;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;
using FikaFinans.InfrastructureV2.Tests.Models.MacroAligner;
using FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;
using FikaFinans.InfrastructureV2.Tests.Models.SignalScorer;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAligner;

public sealed class MacroAlignerAgent
{
    private readonly IThemeAdjacencyLlmClient _llm;

    public MacroAlignerAgent(IThemeAdjacencyLlmClient llm)
    {
        ArgumentNullException.ThrowIfNull(llm);
        _llm = llm;
    }

    public async Task<DataLoaderOutput> RunAsync(string isoWeek, string runId, CancellationToken ct = default)
    {
        var signalsPath = Paths.SignalScorerOutput(isoWeek, runId);
        var macroPath   = Paths.MacroAnalystOutput(isoWeek, runId);

        var signals = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(signalsPath), JsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize step 04 output at {signalsPath}");

        var macro = JsonSerializer.Deserialize<MacroContext>(
            File.ReadAllText(macroPath), JsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize step 03 output at {macroPath}");

        var output = await RunInMemoryAsync(signals, macro, ct);
        WriteJson(Paths.MacroAlignerOutput(isoWeek, runId), output);
        return output;
    }

    public async Task<DataLoaderOutput> RunInMemoryAsync(
        DataLoaderOutput signals,
        MacroContext macro,
        CancellationToken ct = default)
    {
        var warnings = new List<string>(signals.DataQuality.Warnings);
        var alignedFunds = new List<FundRecord>(signals.Funds.Count);

        foreach (var fund in signals.Funds)
        {
            var aligned = await AlignFundAsync(fund, macro.RotationThemes, warnings, ct);
            alignedFunds.Add(aligned);
        }

        return new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = signals.IsoWeek,
            Family          = signals.Family,
            RunId           = signals.RunId,
            ConfigVersion   = signals.ConfigVersion,
            Funds           = alignedFunds,
            FrozenPositions = signals.FrozenPositions,
            CashAvailableKr = signals.CashAvailableKr,
            DataQuality     = new DataQuality
            {
                MetadataRows  = signals.DataQuality.MetadataRows,
                SummaryRows   = signals.DataQuality.SummaryRows,
                SnapshotRows  = signals.DataQuality.SnapshotRows,
                PositionsRows = signals.DataQuality.PositionsRows,
                WriteoffCount = signals.DataQuality.WriteoffCount,
                CoreCount     = signals.DataQuality.CoreCount,
                Warnings      = warnings,
            },
        };
    }

    private async Task<FundRecord> AlignFundAsync(
        FundRecord fund,
        IReadOnlyList<RotationTheme> activeThemes,
        List<string> warnings,
        CancellationToken ct)
    {
        var category = fund.Metadata.Category;

        // Empty universe → all funds get None, no LLM calls (per contract failure mode).
        if (activeThemes.Count == 0)
            return WithAlignment(fund, MacroAlignment.None, MatchedTheme.None, promoted: false, reason: null);

        if (string.IsNullOrWhiteSpace(category))
        {
            warnings.Add($"fund {fund.Isin} has null/empty category — macro_alignment=None");
            return WithAlignment(fund, MacroAlignment.None, MatchedTheme.None, promoted: false, reason: null);
        }

        // Step 1: direct category match.
        var directMatch = SelectBestDirectMatch(category, activeThemes);
        if (directMatch is not null)
        {
            var alignment = directMatch.SignalStrength == SignalStrength.Strong
                ? MacroAlignment.Strong
                : MacroAlignment.Partial;
            var matched = new MatchedTheme
            {
                Id          = directMatch.Id,
                Label       = directMatch.Label,
                MatchMethod = MatchMethod.DirectCategory,
            };
            return MaybePromote(fund, alignment, matched);
        }

        // Step 2: LLM adjacency (lazy — only if no direct match).
        var verdict = await _llm.ClassifyAsync(category, activeThemes, ct);
        if (verdict.Alignment == MacroAlignment.Partial && !string.IsNullOrEmpty(verdict.ThemeId))
        {
            var theme = activeThemes.FirstOrDefault(t => t.Id == verdict.ThemeId);
            if (theme is not null)
            {
                var matched = new MatchedTheme
                {
                    Id          = theme.Id,
                    Label       = theme.Label,
                    MatchMethod = MatchMethod.LlmAdjacency,
                };
                // LLM-adjacency is Partial by definition — never promotes.
                return WithAlignment(fund, MacroAlignment.Partial, matched, promoted: false, reason: null);
            }

            warnings.Add($"fund {fund.Isin} LLM returned unknown theme_id '{verdict.ThemeId}' — defaulting to None");
        }

        return WithAlignment(fund, MacroAlignment.None, MatchedTheme.None, promoted: false, reason: null);
    }

    // Pick the theme with highest signal_strength; tie-break by longest
    // affected_categories list (more specific theme wins).
    internal static RotationTheme? SelectBestDirectMatch(
        string category,
        IReadOnlyList<RotationTheme> themes)
    {
        return themes
            .Where(t => t.AffectedCategories.Any(c =>
                string.Equals(c, category, StringComparison.Ordinal)))
            .OrderByDescending(t => StrengthRank(t.SignalStrength))
            .ThenByDescending(t => t.AffectedCategories.Count)
            .FirstOrDefault();
    }

    private static int StrengthRank(SignalStrength strength) => strength switch
    {
        SignalStrength.Strong   => 3,
        SignalStrength.Moderate => 2,
        SignalStrength.Weak     => 1,
        _                       => 0,
    };

    private static FundRecord MaybePromote(FundRecord fund, MacroAlignment alignment, MatchedTheme matched)
    {
        var canPromote = alignment == MacroAlignment.Strong
                         && fund.Signal == SignalLabel.Neutral
                         && !string.IsNullOrEmpty(fund.CriteriaEvaluation?.MissingForUpgrade);

        if (!canPromote)
            return WithAlignment(fund, alignment, matched, promoted: false, reason: null);

        return WithAlignment(
            fund,
            alignment,
            matched,
            promoted: true,
            reason: "Strong macro alignment + 1 missing buy criterion",
            promotedSignal: SignalLabel.Forming);
    }

    private static FundRecord WithAlignment(
        FundRecord fund,
        MacroAlignment alignment,
        MatchedTheme matched,
        bool promoted,
        string? reason,
        SignalLabel? promotedSignal = null)
    {
        return new FundRecord
        {
            Isin                = fund.Isin,
            Metadata            = fund.Metadata,
            NavBuckets          = fund.NavBuckets,
            Snapshot             = fund.Snapshot,
            CurrentlyHeld       = fund.CurrentlyHeld,
            CurrentValueKr      = fund.CurrentValueKr,
            CostBasisKr         = fund.CostBasisKr,
            Layer               = fund.Layer,
            Metrics             = fund.Metrics,
            Signal              = promotedSignal ?? fund.Signal,
            RuleFired           = fund.RuleFired,
            CriteriaEvaluation  = fund.CriteriaEvaluation,
            MacroAlignment      = alignment,
            MatchedTheme        = matched,
            PromotedToForming   = promoted,
            PromotionReason     = reason,
        };
    }

    private static void WriteJson(string path, DataLoaderOutput output)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(output, JsonOptions.Default));
    }
}
