using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Portfolio;
using FikaFinans.Infrastructure.Pipeline.Json;
using System.Text.Json;

namespace FikaFinans.Infrastructure.Pipeline.Agents;

public sealed class RecommenderAgent : IRecommenderAgent
{
    private readonly IPathsService _paths;

    public RecommenderAgent(IPathsService paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
    }

    public DataLoaderOutput Run(string isoWeek, string runId)
    {
        var inputPath = _paths.ThesisValidatorOutput(isoWeek, runId);

        var input = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(inputPath), JsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize step 07 output at {inputPath}");

        var output = RunInMemory(input);
        WriteJson(_paths.RecommenderOutput(isoWeek, runId), output);
        return output;
    }

    public DataLoaderOutput RunInMemory(DataLoaderOutput thesisValidated)
    {
        var warnings = new List<string>(thesisValidated.DataQuality.Warnings);
        var enrichedFunds = new List<FundRecord>(thesisValidated.Funds.Count);

        foreach (var fund in thesisValidated.Funds)
        {
            enrichedFunds.Add(EnrichFund(fund, warnings));
        }

        return new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = thesisValidated.IsoWeek,
            Family          = thesisValidated.Family,
            RunId           = thesisValidated.RunId,
            ConfigVersion   = thesisValidated.ConfigVersion,
            Funds           = enrichedFunds,
            FrozenPositions = thesisValidated.FrozenPositions,
            CashAvailableKr = thesisValidated.CashAvailableKr,
            DataQuality     = new DataQuality
            {
                MetadataRows  = thesisValidated.DataQuality.MetadataRows,
                SummaryRows   = thesisValidated.DataQuality.SummaryRows,
                SnapshotRows  = thesisValidated.DataQuality.SnapshotRows,
                PositionsRows = thesisValidated.DataQuality.PositionsRows,
                WriteoffCount = thesisValidated.DataQuality.WriteoffCount,
                CoreCount     = thesisValidated.DataQuality.CoreCount,
                Warnings      = warnings,
            },
        };
    }

    private static FundRecord EnrichFund(FundRecord fund, List<string> warnings)
    {
        // signal=null → Skip + warning. The Recommender only acts on the
        // four-tuple (signal, thesis, catalyst.exposure_type, currently_held);
        // a missing signal means upstream (DataLoader / SignalScorer) skipped
        // this fund, so there's nothing to recommend.
        if (fund.Signal is null)
        {
            warnings.Add($"fund {fund.Isin} has null signal — recommendation=Skip");
            return WithRecommendation(fund, Recommendation.Skip, "no_signal_no_action");
        }

        var (recommendation, reason) = Map(fund);
        return WithRecommendation(fund, recommendation, reason);
    }

    // The deterministic mapping table from 08-recommender.md. Every
    // (signal, thesis, exposure_type, currently_held) combination produces
    // exactly one (Recommendation, reason). Reasons are short structured
    // strings — not free-form prose — so audit trails stay byte-stable.
    internal static (Recommendation Recommendation, string Reason) Map(FundRecord fund)
    {
        var signal      = fund.Signal!.Value;
        var thesis      = fund.ThesisValidity;
        var exposure    = fund.Catalyst?.ExposureType;
        var hasDirect   = exposure == ExposureType.Direct;
        var held        = fund.CurrentlyHeld;

        var thesisLabel = thesis ?? ThesisValidity.NotApplicable;

        return signal switch
        {
            SignalLabel.Strength when hasDirect =>
                (Recommendation.CatalystEntry,
                 $"Strength + {thesisLabel} thesis with Direct catalyst — fundamental + technical entry"),
            SignalLabel.Strength =>
                (Recommendation.MomentumEntry,
                 exposure == ExposureType.Indirect
                    ? "Strength + Indirect catalyst exposure — counted as momentum, not catalyst"
                    : $"Strength + {thesisLabel} thesis, no catalyst — pure momentum entry"),

            SignalLabel.Weakness when thesis == ThesisValidity.Invalid =>
                (Recommendation.ThesisExit,
                 "Weakness + Invalid thesis (catalyst still active but momentum reversed)"),
            SignalLabel.Weakness =>
                (Recommendation.MomentumExit,
                 "Weakness + Partial thesis — technicals decaying, story not fully broken"),

            SignalLabel.Forming when held =>
                (Recommendation.Maintain, "Forming signal, held position — wait for confirmation"),
            SignalLabel.Forming =>
                (Recommendation.Skip, "Forming signal, fund not held — wait for confirmation"),

            SignalLabel.Neutral when held =>
                (Recommendation.Maintain, "Neutral signal, held position — no change"),
            SignalLabel.Neutral =>
                (Recommendation.Skip, "Neutral signal, fund not held — no action"),

            _ => held
                ? (Recommendation.Maintain, "No actionable signal — held position kept")
                : (Recommendation.Skip, "No actionable signal — no action"),
        };
    }

    private static FundRecord WithRecommendation(
        FundRecord fund,
        Recommendation recommendation,
        string reason) => new()
    {
        Isin                  = fund.Isin,
        Metadata              = fund.Metadata,
        NavBuckets            = fund.NavBuckets,
        Snapshot              = fund.Snapshot,
        CurrentlyHeld         = fund.CurrentlyHeld,
        CurrentValueKr        = fund.CurrentValueKr,
        CostBasisKr           = fund.CostBasisKr,
        Layer                 = fund.Layer,
        Metrics               = fund.Metrics,
        Signal                = fund.Signal,
        RuleFired             = fund.RuleFired,
        CriteriaEvaluation    = fund.CriteriaEvaluation,
        MacroAlignment        = fund.MacroAlignment,
        MatchedTheme          = fund.MatchedTheme,
        PromotedToForming     = fund.PromotedToForming,
        PromotionReason       = fund.PromotionReason,
        Catalyst              = fund.Catalyst,
        ThesisValidity        = fund.ThesisValidity,
        ThesisRationale       = fund.ThesisRationale,
        ThesisMethod          = fund.ThesisMethod,
        Recommendation        = recommendation,
        RecommendationReason  = reason,
    };

    private static void WriteJson(string path, DataLoaderOutput output)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(output, JsonOptions.Default));
    }
}
