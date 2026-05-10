using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
using FikaFinans.Infrastructure.Pipeline.Json;
using System.Text.Json;

namespace FikaFinans.Infrastructure.Pipeline.Agents;

public sealed class ThesisValidatorAgent : IThesisValidatorAgent
{
    private readonly IPathsService _paths;
    private readonly IThesisRefinementLlmClient _llm;

    public ThesisValidatorAgent(IPathsService paths, IThesisRefinementLlmClient llm)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(llm);
        _paths = paths;
        _llm = llm;
    }

    public async Task<DataLoaderOutput> RunAsync(string isoWeek, string runId, CancellationToken ct = default)
    {
        var taggedPath = _paths.CatalystTaggerOutput(isoWeek, runId);

        var tagged = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(taggedPath), JsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize step 06 output at {taggedPath}");

        var output = await RunInMemoryAsync(tagged, ct);
        WriteJson(_paths.ThesisValidatorOutput(isoWeek, runId), output);
        return output;
    }

    public async Task<DataLoaderOutput> RunInMemoryAsync(
        DataLoaderOutput tagged,
        CancellationToken ct = default)
    {
        var warnings = new List<string>(tagged.DataQuality.Warnings);
        var validatedFunds = new List<FundRecord>(tagged.Funds.Count);

        foreach (var fund in tagged.Funds)
        {
            var validated = await ValidateFundAsync(fund, warnings, ct);
            validatedFunds.Add(validated);
        }

        return new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = tagged.IsoWeek,
            Family          = tagged.Family,
            RunId           = tagged.RunId,
            ConfigVersion   = tagged.ConfigVersion,
            Funds           = validatedFunds,
            FrozenPositions = tagged.FrozenPositions,
            CashAvailableKr = tagged.CashAvailableKr,
            DataQuality     = new DataQuality
            {
                MetadataRows  = tagged.DataQuality.MetadataRows,
                SummaryRows   = tagged.DataQuality.SummaryRows,
                SnapshotRows  = tagged.DataQuality.SnapshotRows,
                PositionsRows = tagged.DataQuality.PositionsRows,
                WriteoffCount = tagged.DataQuality.WriteoffCount,
                CoreCount     = tagged.DataQuality.CoreCount,
                Warnings      = warnings,
            },
        };
    }

    private async Task<FundRecord> ValidateFundAsync(
        FundRecord fund,
        List<string> warnings,
        CancellationToken ct)
    {
        // signal=null (DataLoader/SignalScorer skipped) → NotApplicable, no LLM.
        if (fund.Signal is null)
        {
            warnings.Add($"fund {fund.Isin} has null signal — thesis_validity=NotApplicable");
            return WithThesis(fund, ThesisValidity.NotApplicable, ThesisMethod.Matrix,
                "No signal — no thesis to validate.");
        }

        // Catalyst.exposure_type missing means the catalyst object exists but is
        // malformed. ExposureType is a non-nullable enum on FundCatalyst, so this
        // can't actually happen in C#; the contract calls it out as a defensive
        // line, so we keep the warning shape for future schema relaxations.
        var (baseline, refine) = ComputeBaseline(fund);

        if (!refine)
        {
            return WithThesis(fund, baseline, ThesisMethod.Matrix, MatrixRationale(fund, baseline));
        }

        var verdict = await _llm.RefineAsync(fund, baseline, ct);
        if (StepsBetween(baseline, verdict.Validity) > 1)
        {
            warnings.Add(
                $"fund {fund.Isin} LLM jumped {baseline}→{verdict.Validity} (>1 step) — using matrix baseline");
            return WithThesis(fund, baseline, ThesisMethod.Matrix, MatrixRationale(fund, baseline));
        }

        var rationale = string.IsNullOrWhiteSpace(verdict.Rationale)
            ? MatrixRationale(fund, verdict.Validity)
            : verdict.Rationale;
        return WithThesis(fund, verdict.Validity, ThesisMethod.LlmRefinement, rationale);
    }

    // Decision matrix from 07-thesisvalidator.md. Returns the baseline label
    // plus a flag indicating whether the LLM should be consulted to refine the
    // rationale (and possibly adjust by one step).
    internal static (ThesisValidity Baseline, bool Refine) ComputeBaseline(FundRecord fund)
    {
        var signal = fund.Signal;
        if (signal is null or SignalLabel.Neutral)
            return (ThesisValidity.NotApplicable, false);

        var hasCatalyst = fund.Catalyst is not null;
        var macro       = fund.MacroAlignment ?? MacroAlignment.None;

        return (signal.Value, hasCatalyst, macro) switch
        {
            // Strength rows.
            (SignalLabel.Strength, true,  MacroAlignment.Strong)  => (ThesisValidity.Valid,   false),
            (SignalLabel.Strength, true,  MacroAlignment.Partial) => (ThesisValidity.Partial, true),
            (SignalLabel.Strength, true,  MacroAlignment.None)    => (ThesisValidity.Partial, true),
            (SignalLabel.Strength, false, MacroAlignment.Strong)  => (ThesisValidity.Valid,   false),
            (SignalLabel.Strength, false, MacroAlignment.Partial) => (ThesisValidity.Partial, false),
            (SignalLabel.Strength, false, MacroAlignment.None)    => (ThesisValidity.Partial, false),

            // Weakness rows.
            (SignalLabel.Weakness, true,  _)                      => (ThesisValidity.Invalid, true),
            (SignalLabel.Weakness, false, MacroAlignment.Strong)  => (ThesisValidity.Partial, true),
            (SignalLabel.Weakness, false, _)                      => (ThesisValidity.Partial, false),

            // Forming rows.
            (SignalLabel.Forming,  _,     MacroAlignment.Strong)  => (ThesisValidity.Partial, false),
            (SignalLabel.Forming,  _,     MacroAlignment.Partial) => (ThesisValidity.Partial, false),
            (SignalLabel.Forming,  _,     MacroAlignment.None)    => (ThesisValidity.NotApplicable, false),

            _ => (ThesisValidity.NotApplicable, false),
        };
    }

    // Linear distance on Invalid(0) → Partial(1) → Valid(2). Any move
    // touching NotApplicable from an actionable label (or vice versa) counts
    // as out-of-range — the LLM cannot drag a Strength signal into NA.
    internal static int StepsBetween(ThesisValidity a, ThesisValidity b)
    {
        if (a == b) return 0;
        if (a == ThesisValidity.NotApplicable || b == ThesisValidity.NotApplicable)
            return int.MaxValue;

        var rank = (ThesisValidity v) => v switch
        {
            ThesisValidity.Invalid => 0,
            ThesisValidity.Partial => 1,
            ThesisValidity.Valid   => 2,
            _                      => -1,
        };
        return Math.Abs(rank(a) - rank(b));
    }

    // Matrix-only rationales — short, names the input pattern. Used when no
    // LLM refinement happens (clean cases) or as a fallback when the LLM is
    // overridden.
    internal static string MatrixRationale(FundRecord fund, ThesisValidity validity)
    {
        var signal = fund.Signal;
        var hasCatalyst = fund.Catalyst is not null;
        var macro = fund.MacroAlignment ?? MacroAlignment.None;

        return validity switch
        {
            ThesisValidity.Valid when hasCatalyst =>
                "Strength momentum supported by an active catalyst and Strong macro alignment.",
            ThesisValidity.Valid =>
                "Strength momentum aligned with Strong macro tailwind.",
            ThesisValidity.Invalid =>
                "Weakness signal with active catalyst — price action contradicts the fundamental narrative; thesis broken.",
            ThesisValidity.Partial when signal == SignalLabel.Forming =>
                "Forming candidate with macro tailwind; awaiting catalyst confirmation.",
            ThesisValidity.Partial when signal == SignalLabel.Weakness =>
                "Weakness signal without supporting catalyst or macro tailwind.",
            ThesisValidity.Partial when hasCatalyst =>
                "Strength momentum with catalyst but no Strong macro tailwind.",
            ThesisValidity.Partial =>
                "Strength momentum without catalyst or Strong macro tailwind.",
            ThesisValidity.NotApplicable when signal == SignalLabel.Neutral =>
                "No directional signal — no thesis to validate.",
            ThesisValidity.NotApplicable =>
                "No actionable thesis under current macro context.",
            _ => "Matrix baseline applied.",
        };
    }

    private static FundRecord WithThesis(
        FundRecord fund,
        ThesisValidity validity,
        ThesisMethod method,
        string rationale) => new()
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
        Catalyst            = fund.Catalyst,
        ThesisValidity      = validity,
        ThesisRationale     = rationale,
        ThesisMethod        = method,
    };

    private static void WriteJson(string path, DataLoaderOutput output)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(output, JsonOptions.Default));
    }
}
