using System.Text.Json;
using FikaFinans.InfrastructureV2.Tests.Models.CatalystTagger;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;
using FikaFinans.InfrastructureV2.Tests.Models.MacroAnalyst;

namespace FikaFinans.InfrastructureV2.Tests.Agents.CatalystTagger;

public sealed class CatalystTaggerAgent
{
    private readonly IFundCatalystLlmClient _llm;

    public CatalystTaggerAgent(IFundCatalystLlmClient llm)
    {
        ArgumentNullException.ThrowIfNull(llm);
        _llm = llm;
    }

    public async Task<DataLoaderOutput> RunAsync(string isoWeek, string runId, CancellationToken ct = default)
    {
        var alignedPath = Paths.MacroAlignerOutput(isoWeek, runId);
        var macroPath   = Paths.MacroAnalystOutput(isoWeek, runId);

        var aligned = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(alignedPath), JsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize step 05 output at {alignedPath}");

        var macro = JsonSerializer.Deserialize<MacroContext>(
            File.ReadAllText(macroPath), JsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize step 03 output at {macroPath}");

        var output = await RunInMemoryAsync(aligned, macro, ct);
        WriteJson(Paths.CatalystTaggerOutput(isoWeek, runId), output);
        return output;
    }

    public async Task<DataLoaderOutput> RunInMemoryAsync(
        DataLoaderOutput aligned,
        MacroContext macro,
        CancellationToken ct = default)
    {
        var warnings = new List<string>(aligned.DataQuality.Warnings);
        var activeCatalysts = (macro.Catalysts ?? Array.Empty<Catalyst>())
            .Where(c => c.AffectedCategories.Count > 0)
            .ToArray();

        var taggedFunds = new List<FundRecord>(aligned.Funds.Count);
        foreach (var fund in aligned.Funds)
        {
            var tagged = await TagFundAsync(fund, activeCatalysts, warnings, ct);
            taggedFunds.Add(tagged);
        }

        return new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = aligned.IsoWeek,
            Family          = aligned.Family,
            RunId           = aligned.RunId,
            ConfigVersion   = aligned.ConfigVersion,
            Funds           = taggedFunds,
            FrozenPositions = aligned.FrozenPositions,
            CashAvailableKr = aligned.CashAvailableKr,
            DataQuality     = new DataQuality
            {
                MetadataRows  = aligned.DataQuality.MetadataRows,
                SummaryRows   = aligned.DataQuality.SummaryRows,
                SnapshotRows  = aligned.DataQuality.SnapshotRows,
                PositionsRows = aligned.DataQuality.PositionsRows,
                WriteoffCount = aligned.DataQuality.WriteoffCount,
                CoreCount     = aligned.DataQuality.CoreCount,
                Warnings      = warnings,
            },
        };
    }

    private async Task<FundRecord> TagFundAsync(
        FundRecord fund,
        IReadOnlyList<Catalyst> activeCatalysts,
        List<string> warnings,
        CancellationToken ct)
    {
        // Empty catalysts → null, no LLM call (per contract failure mode).
        if (activeCatalysts.Count == 0)
            return WithCatalyst(fund, null);

        // Null/empty category → null immediately, no LLM call.
        if (string.IsNullOrWhiteSpace(fund.Metadata.Category))
        {
            warnings.Add($"fund {fund.Isin} has null/empty category — catalyst=null");
            return WithCatalyst(fund, null);
        }

        var classifications = await _llm.ClassifyAsync(
            fund.Metadata.Name,
            fund.Metadata.Category,
            activeCatalysts,
            ct);

        var picked = SelectBest(classifications, activeCatalysts);
        if (picked is null)
            return WithCatalyst(fund, null);

        var (classification, source) = picked.Value;
        var catalyst = new FundCatalyst
        {
            Name         = source.Name,
            Intensity    = source.Intensity,
            WeeksActive  = source.WeeksActive,
            ExposureType = classification.Exposure == ExposureKind.Direct
                ? ExposureType.Direct
                : ExposureType.Indirect,
            Rationale    = classification.Rationale ?? string.Empty,
        };
        return WithCatalyst(fund, catalyst);
    }

    // Pick the strongest classification: Direct beats Indirect; ties broken by
    // higher catalyst intensity, then longer weeks_active. None entries are
    // discarded.
    internal static (CatalystExposureClassification Classification, Catalyst Source)? SelectBest(
        IReadOnlyList<CatalystExposureClassification> classifications,
        IReadOnlyList<Catalyst> activeCatalysts)
    {
        var best = classifications
            .Where(c => c.Exposure != ExposureKind.None)
            .Select(c => (Classification: c, Source: activeCatalysts.FirstOrDefault(a => a.Name == c.CatalystName)))
            .Where(p => p.Source is not null)
            .OrderByDescending(p => ExposureRank(p.Classification.Exposure))
            .ThenByDescending(p => IntensityRank(p.Source!.Intensity))
            .ThenByDescending(p => p.Source!.WeeksActive)
            .FirstOrDefault();

        if (best.Classification is null || best.Source is null)
            return null;

        return (best.Classification, best.Source);
    }

    private static int ExposureRank(ExposureKind kind) => kind switch
    {
        ExposureKind.Direct   => 2,
        ExposureKind.Indirect => 1,
        _                     => 0,
    };

    private static int IntensityRank(Intensity intensity) => intensity switch
    {
        Intensity.High   => 3,
        Intensity.Medium => 2,
        Intensity.Low    => 1,
        _                => 0,
    };

    private static FundRecord WithCatalyst(FundRecord fund, FundCatalyst? catalyst) => new()
    {
        Isin                = fund.Isin,
        Metadata            = fund.Metadata,
        NavBuckets          = fund.NavBuckets,
        Snapshot            = fund.Snapshot,
        CurrentlyHeld       = fund.CurrentlyHeld,
        CurrentValueKr      = fund.CurrentValueKr,
        CostBasisKr         = fund.CostBasisKr,
        Layer               = fund.Layer,
        Metrics             = fund.Metrics,
        Signal              = fund.Signal,
        RuleFired           = fund.RuleFired,
        CriteriaEvaluation  = fund.CriteriaEvaluation,
        MacroAlignment      = fund.MacroAlignment,
        MatchedTheme        = fund.MatchedTheme,
        PromotedToForming   = fund.PromotedToForming,
        PromotionReason     = fund.PromotionReason,
        Catalyst            = catalyst,
    };

    private static void WriteJson(string path, DataLoaderOutput output)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(output, JsonOptions.Default));
    }
}
