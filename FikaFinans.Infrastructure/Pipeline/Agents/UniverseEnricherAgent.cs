using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Portfolio;
using FikaFinans.Infrastructure.Pipeline.Json;
using System.Diagnostics;
using System.Text.Json;

using FikaFinans.Domain.Pipeline;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Agents;

public sealed class UniverseEnricherAgent : IUniverseEnricherAgent
{
    private static readonly Recommendation[] BuyRecommendations =
    [
        Recommendation.CatalystEntry,
        Recommendation.MomentumEntry,
    ];

    private static readonly Recommendation[] SellRecommendations =
    [
        Recommendation.ThesisExit,
        Recommendation.MomentumExit,
    ];

    private static readonly Recommendation[] RankableRecommendations =
    [
        Recommendation.CatalystEntry,
        Recommendation.MomentumEntry,
        Recommendation.ThesisExit,
        Recommendation.MomentumExit,
    ];

    private readonly ILogger _logger;
    private readonly IPathsService _paths;
    private readonly IDifferentiatorLlmClient _llm;
    private readonly UniverseEnricherConfig _config;

    public UniverseEnricherAgent(ILogger logger, IPathsService paths, IDifferentiatorLlmClient llm)
        : this(logger, paths, llm, UniverseEnricherConfig.Default)
    {
    }

    public UniverseEnricherAgent(
        ILogger logger, IPathsService paths, IDifferentiatorLlmClient llm, UniverseEnricherConfig config)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(config);
        _logger = logger;
        _paths = paths;
        _llm = llm;
        _config = config;
    }

    public async Task<DataLoaderOutput> RunAsync(string isoWeek, PipelineRunId runId, CancellationToken ct = default)
    {
        var inputPath = _paths.RecommenderOutput(isoWeek, runId);
        var input = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(inputPath), JsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize step 08 output at {inputPath}");

        var output = await RunInMemoryAsync(input, ct);
        WriteJson(_paths.UniverseEnricherOutput(isoWeek, runId), output);
        return output;
    }

    public async Task<DataLoaderOutput> RunFromInputAsync(
        DataLoaderOutput step8Input,
        string isoWeek,
        PipelineRunId runId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(step8Input);

        var output = await RunInMemoryAsync(step8Input, ct);
        WriteJson(_paths.UniverseEnricherOutput(isoWeek, runId), output);
        return output;
    }

    /// <summary>Not on the interface — kept for <see cref="RunAsync"/> and the runner, on the ctor's config.</summary>
    public async Task<DataLoaderOutput> RunInMemoryAsync(
        DataLoaderOutput recommended,
        CancellationToken ct = default)
    {
        var warnings = new List<string>(recommended.DataQuality.Warnings);
        var enrichedFunds = await EnrichAsync(recommended.Funds, recommended.IsoWeek, _config, warnings, ct);

        return new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = recommended.IsoWeek,
            Family          = recommended.Family,
            RunId           = recommended.RunId,
            ConfigVersion   = recommended.ConfigVersion,
            Funds           = enrichedFunds,
            FrozenPositions = recommended.FrozenPositions,
            CashAvailableKr = recommended.CashAvailableKr,
            DataQuality     = new DataQuality
            {
                MetadataRows  = recommended.DataQuality.MetadataRows,
                SummaryRows   = recommended.DataQuality.SummaryRows,
                SnapshotRows  = recommended.DataQuality.SnapshotRows,
                PositionsRows = recommended.DataQuality.PositionsRows,
                WriteoffCount = recommended.DataQuality.WriteoffCount,
                CoreCount     = recommended.DataQuality.CoreCount,
                Warnings      = warnings,
            },
        };
    }

    /// <inheritdoc />
    public async Task<DataLoaderOutput> RunInMemoryAsync(
        Company family, IsoWeek isoWeek, PipelineRunId runId,
        IReadOnlyList<FundRecord> funds,
        UniverseEnricherConfig config,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(funds);
        ArgumentNullException.ThrowIfNull(config);

        var warnings = new List<string>();
        var enrichedFunds = await EnrichAsync(funds, isoWeek.Value, config, warnings, ct);

        return new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = isoWeek.Value,
            Family          = family.Value,
            RunId           = runId,
            ConfigVersion   = "1.0.0",
            Funds           = enrichedFunds,
            FrozenPositions = Array.Empty<FrozenPosition>(),
            CashAvailableKr = 0m,
            DataQuality     = new DataQuality { Warnings = warnings },
        };
    }

    /// <summary>The four passes, whichever entry point is asking.</summary>
    private async Task<List<FundRecord>> EnrichAsync(
        IReadOnlyList<FundRecord> funds,
        string isoWeek,
        UniverseEnricherConfig config,
        List<string> warnings,
        CancellationToken ct)
    {
        ValidateWeights(config);

        var stopwatch = Stopwatch.StartNew();
        var warningsBefore = warnings.Count;
        var w = config.Weights;
        _logger.Debug(
            "UniverseEnricher compute — week {0}, {1} fund(s), weights signal {2}, metrics {3}, macro {4}, thesis {5}, universe {6}, up to {7} alternative(s) per Buy",
            isoWeek, funds.Count, w.SignalStrength, w.MetricsQuality, w.MacroAlignment, w.ThesisValidity,
            w.UniverseContext, config.Alternatives.MaxPerFund);

        // Pass 1: rotation pairing — assign rot_{iso_week}_{letter} ids by
        // grouping ThesisExit/MomentumExit + CatalystEntry/MomentumEntry on
        // their shared matched_theme.id. We need this before conviction so
        // universe_context can take credit for it.
        var rotationPairs = ComputeRotationPairs(funds, isoWeek);
        LogRotationPairs(funds, rotationPairs);

        // Pass 2: peer index per category (Buys only) — used both for
        // alternatives and to score the universe_context component.
        var alternativesByIsin = await BuildAlternativesAsync(funds, config, warnings, ct);

        // Pass 3: per-fund conviction breakdown.
        var enrichedFunds = new List<FundRecord>(funds.Count);
        foreach (var fund in funds)
        {
            var rotationPairId = rotationPairs.GetValueOrDefault(fund.Isin);
            // Only Buys carry alternatives; Sells/Maintain/Skip stay null per
            // the contract ("the alternative for a Sell is exit to cash").
            // We still need the list locally so universe_context can credit
            // peers existing in the universe.
            var alternatives = alternativesByIsin.GetValueOrDefault(fund.Isin)
                ?? Array.Empty<Alternative>();
            var breakdown = ScoreFund(fund, rotationPairId, alternatives, config);
            var score = WeightedSum(breakdown, config);

            var emittedAlternatives = BuyRecommendations.Contains(fund.Recommendation ?? Recommendation.Skip)
                ? alternatives
                : null;

            enrichedFunds.Add(WithEnrichment(
                fund,
                convictionScore:     Round(score),
                breakdown:           breakdown,
                universeRank:        null,
                alternatives:        emittedAlternatives,
                rotationPairId:      rotationPairId));
        }

        // Pass 4: universe_rank per recommendation type (after conviction is
        // settled across the whole universe).
        var ranked = AssignRanks(enrichedFunds);

        stopwatch.Stop();
        var newWarnings = warnings.Skip(warningsBefore).ToList();
        _logger.Debug(
            "UniverseEnricher computed — week {0}, {1} fund(s), {2} rotation pair(s), {3} Buy(s) with alternatives, {4} warning(s), {5} ms",
            isoWeek, ranked.Count, rotationPairs.Values.Distinct().Count(),
            alternativesByIsin.Count(kv => kv.Value.Count > 0), newWarnings.Count, stopwatch.ElapsedMilliseconds);

        foreach (var warning in newWarnings)
            _logger.Debug("UniverseEnricher warning — {0}", warning);

        return ranked;
    }

    /// <summary>
    /// One line per pair: which theme it is and which funds leave and enter on it — the
    /// grouping step 10 trades on, otherwise only visible as an opaque id per record.
    /// </summary>
    private void LogRotationPairs(IReadOnlyList<FundRecord> funds, Dictionary<Isin, string> rotationPairs)
    {
        if (rotationPairs.Count == 0)
        {
            _logger.Debug("UniverseEnricher pairs — none: no theme holds both an Exit and an Entry");
            return;
        }

        foreach (var pair in funds.Where(f => rotationPairs.ContainsKey(f.Isin)).GroupBy(f => rotationPairs[f.Isin]))
        {
            var exits = pair.Where(f => SellRecommendations.Contains(f.Recommendation!.Value)).Select(f => f.Isin.Value);
            var entries = pair.Where(f => BuyRecommendations.Contains(f.Recommendation!.Value)).Select(f => f.Isin.Value);
            _logger.Debug("UniverseEnricher pair {0} — theme {1}: exits [{2}], entries [{3}]",
                pair.Key, pair.First().MatchedTheme!.Id, string.Join(", ", exits), string.Join(", ", entries));
        }
    }

    private static void ValidateWeights(UniverseEnricherConfig config)
    {
        var sum = config.Weights.Sum();
        var diff = Math.Abs(sum - config.WeightValidation.MustSumTo);
        if (diff > config.WeightValidation.Tolerance)
        {
            throw new InvalidDataException(
                $"Conviction weights sum to {sum} but must equal {config.WeightValidation.MustSumTo} " +
                $"within ±{config.WeightValidation.Tolerance}.");
        }
    }

    // Group funds by their matched_theme.id where one bucket has at least one
    // Exit and one Entry; assign rotation pair ids in descending order of
    // bucket size, ties broken by alphabetical theme id.
    internal static Dictionary<Isin, string> ComputeRotationPairs(
        IReadOnlyList<FundRecord> funds, string isoWeek)
    {
        var byTheme = funds
            .Where(f => !string.IsNullOrEmpty(f.MatchedTheme?.Id))
            .Where(f => SellRecommendations.Contains(f.Recommendation ?? Recommendation.Skip)
                     || BuyRecommendations.Contains(f.Recommendation ?? Recommendation.Skip))
            .GroupBy(f => f.MatchedTheme!.Id!)
            .Where(g =>
                g.Any(f => SellRecommendations.Contains(f.Recommendation!.Value)) &&
                g.Any(f => BuyRecommendations.Contains(f.Recommendation!.Value)))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var assignments = new Dictionary<Isin, string>();
        var letter = 'a';
        foreach (var theme in byTheme)
        {
            var pairId = $"rot_{isoWeek}_{letter}";
            foreach (var fund in theme)
            {
                assignments[fund.Isin] = pairId;
            }
            letter++;
        }
        return assignments;
    }

    // For each Buy candidate (CatalystEntry or MomentumEntry), find peers in
    // the same category, sort by Sharpe descending (with fee tie-break), keep
    // up to max_per_fund, then ask the LLM to write differentiators in a
    // single batched call. LLM failures degrade gracefully — empty
    // differentiator strings keep the structural alternatives intact.
    private async Task<Dictionary<Isin, IReadOnlyList<Alternative>>> BuildAlternativesAsync(
        IReadOnlyList<FundRecord> funds,
        UniverseEnricherConfig config,
        List<string> warnings,
        CancellationToken ct)
    {
        var result = new Dictionary<Isin, IReadOnlyList<Alternative>>();

        var buys = funds
            .Where(f => BuyRecommendations.Contains(f.Recommendation ?? Recommendation.Skip))
            .ToList();

        foreach (var buy in buys)
        {
            var category = buy.Metadata.Category;
            if (string.IsNullOrWhiteSpace(category))
            {
                _logger.Debug("UniverseEnricher alternatives — fund {0}: no category, no peers looked for", buy.Isin);
                result[buy.Isin] = Array.Empty<Alternative>();
                continue;
            }

            var peers = funds
                .Where(f => f.Isin != buy.Isin)
                .Where(f => string.Equals(f.Metadata.Category, category, StringComparison.Ordinal))
                .OrderByDescending(f => f.Metrics?.Sharpe12w ?? decimal.MinValue)
                .ThenBy(f => f.Metadata.TotalFee)
                .ThenBy(f => f.Isin.Value, StringComparer.Ordinal)
                .Take(config.Alternatives.MaxPerFund)
                .ToList();

            _logger.Debug("UniverseEnricher alternatives — fund {0} in '{1}': {2} peer(s) [{3}]",
                buy.Isin, category, peers.Count, string.Join(", ", peers.Select(p => p.Isin.Value)));

            if (peers.Count == 0)
            {
                result[buy.Isin] = Array.Empty<Alternative>();
                continue;
            }

            var differentiators = await SafeWriteDifferentiatorsAsync(buy, peers, warnings, ct);

            var alternatives = peers
                .Select(peer => new Alternative
                {
                    Isin           = peer.Isin,
                    Name           = peer.Metadata.Name,
                    Differentiator = differentiators.GetValueOrDefault(peer.Isin, string.Empty),
                })
                .ToArray();
            result[buy.Isin] = alternatives;
        }

        return result;
    }

    private async Task<Dictionary<Isin, string>> SafeWriteDifferentiatorsAsync(
        FundRecord primary,
        IReadOnlyList<FundRecord> peers,
        List<string> warnings,
        CancellationToken ct)
    {
        try
        {
            var lines = await _llm.WriteDifferentiatorsAsync(
                new DifferentiatorRequest { Primary = primary, Alternatives = peers }, ct);
            return lines.ToDictionary(l => (Isin)l.Isin, l => l.Differentiator ?? string.Empty);
        }
        catch (Exception ex)
        {
            warnings.Add($"fund {primary.Isin} differentiator LLM failed: {ex.GetType().Name} — emitting empty differentiators");
            return new Dictionary<Isin, string>();
        }
    }

    // Five components, each returning a [0, 1] score. Component math is
    // grouped on a single method per dimension so the doc and the test
    // suite can target each independently.
    internal ConvictionBreakdown ScoreFund(
        FundRecord fund,
        string? rotationPairId,
        IReadOnlyList<Alternative> alternatives,
        UniverseEnricherConfig config)
    {
        return new ConvictionBreakdown
        {
            SignalStrength  = ScoreSignalStrength(fund),
            MetricsQuality  = ScoreMetricsQuality(fund, config),
            MacroAlignment  = ScoreMacroAlignment(fund),
            ThesisValidity  = ScoreThesisValidity(fund),
            UniverseContext = ScoreUniverseContext(rotationPairId, alternatives),
        };
    }

    internal static decimal ScoreSignalStrength(FundRecord fund) => fund.Signal switch
    {
        SignalLabel.Strength => SignalStrengthBuyScore(fund),
        SignalLabel.Weakness => SignalStrengthSellScore(fund),
        SignalLabel.Forming  => 0.4m,
        SignalLabel.Neutral  => 0.0m,
        _                    => 0.0m,
    };

    private static decimal SignalStrengthBuyScore(FundRecord fund)
    {
        var metrics = fund.Metrics;
        if (metrics is null) return 0.6m;

        var marginCount = 0;
        if (metrics.WindowsPositiveCount >= metrics.WindowsTotal) marginCount++;
        if ((metrics.Sharpe12w ?? 0m) >= 1.0m) marginCount++;
        if ((metrics.CurrentDrawdownPct ?? 0m) >= 0m) marginCount++;
        return marginCount >= 2 ? 1.0m : 0.6m;
    }

    private static decimal SignalStrengthSellScore(FundRecord fund)
    {
        var metrics = fund.Metrics;
        if (metrics is null) return 0.6m;

        var triggers = 0;
        if ((metrics.Sharpe2w ?? 0m) < 0m) triggers++;
        if ((metrics.CurrentDrawdownPct ?? 0m) < -1.5m) triggers++;
        if (metrics.WindowsPositiveCount <= 1) triggers++;
        return triggers >= 2 ? 1.0m : 0.6m;
    }

    internal static decimal ScoreMetricsQuality(FundRecord fund, UniverseEnricherConfig config)
    {
        var metrics = fund.Metrics;
        if (metrics is null) return 0.0m;

        var max = config.MetricsQualityComponents.Sharpe12wNormalizationMax;
        var sharpe = Math.Clamp(metrics.Sharpe12w ?? 0m, 0m, max);
        var score = max > 0m ? sharpe / max : 0m;

        if ((metrics.CurrentDrawdownPct ?? 0m) < config.MetricsQualityComponents.DrawdownPenaltyThresholdPct)
            score -= 0.20m;

        if ((metrics.AnnVolatility12wPct ?? 0m) > config.MetricsQualityComponents.VolPenaltyMinPct)
            score -= 0.15m;

        if (metrics.DataQuality.Sharpe2wIsNan) score -= 0.10m;
        if (metrics.DataQuality.Sharpe12wIsNan) score -= 0.10m;
        if (metrics.DataQuality.Sharpe1yIsNan) score -= 0.10m;

        return Math.Max(0.0m, score);
    }

    internal static decimal ScoreMacroAlignment(FundRecord fund) => fund.MacroAlignment switch
    {
        MacroAlignment.Strong  => 1.0m,
        MacroAlignment.Partial => 0.5m,
        MacroAlignment.None    => 0.0m,
        _                      => 0.0m,
    };

    internal static decimal ScoreThesisValidity(FundRecord fund)
    {
        var rec = fund.Recommendation;
        var thesis = fund.ThesisValidity ?? ThesisValidity.NotApplicable;

        if (rec is Recommendation.CatalystEntry or Recommendation.MomentumEntry)
        {
            return thesis switch
            {
                ThesisValidity.Valid         => 1.0m,
                ThesisValidity.Partial       => 0.5m,
                ThesisValidity.NotApplicable => 0.3m,
                ThesisValidity.Invalid       => 0.0m,
                _                                                   => 0.0m,
            };
        }

        if (rec is Recommendation.ThesisExit or Recommendation.MomentumExit)
        {
            return thesis switch
            {
                ThesisValidity.Invalid       => 1.0m,
                ThesisValidity.Partial       => 0.5m,
                ThesisValidity.Valid         => 0.0m,
                ThesisValidity.NotApplicable => 0.5m,
                _                                                   => 0.5m,
            };
        }

        // Maintain / Skip / null — context not directional.
        return 0.5m;
    }

    internal static decimal ScoreUniverseContext(
        string? rotationPairId,
        IReadOnlyList<Alternative> alternatives)
    {
        if (!string.IsNullOrEmpty(rotationPairId)) return 1.0m;
        if (alternatives.Count > 0) return 0.5m;
        return 0.0m;
    }

    private static decimal WeightedSum(ConvictionBreakdown b, UniverseEnricherConfig config)
    {
        var w = config.Weights;
        return
            w.SignalStrength  * b.SignalStrength +
            w.MetricsQuality  * b.MetricsQuality +
            w.MacroAlignment  * b.MacroAlignment +
            w.ThesisValidity  * b.ThesisValidity +
            w.UniverseContext * b.UniverseContext;
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private List<FundRecord> AssignRanks(List<FundRecord> funds)
    {
        var ranks = new Dictionary<Isin, UniverseRank>();

        foreach (var rec in RankableRecommendations)
        {
            var bucket = funds
                .Where(f => f.Recommendation == rec)
                .OrderByDescending(f => f.ConvictionScore ?? 0m)
                .ThenBy(f => f.Metadata.TotalFee)
                .ThenBy(f => f.Isin.Value, StringComparer.Ordinal)
                .ToList();

            for (var i = 0; i < bucket.Count; i++)
            {
                ranks[bucket[i].Isin] = new UniverseRank
                {
                    WithinRecommendation     = i + 1,
                    OfTotalInRecommendation  = bucket.Count,
                };
            }
        }

        return funds.Select(f => ranks.TryGetValue(f.Isin, out var rank)
            ? WithRank(f, rank)
            : f).ToList();
    }

    private static FundRecord WithRank(FundRecord fund, UniverseRank rank) => new()
    {
        Isin                 = fund.Isin,
        Metadata             = fund.Metadata,
        NavBuckets           = fund.NavBuckets,
        Snapshot             = fund.Snapshot,
        CurrentlyHeld        = fund.CurrentlyHeld,
        CurrentValueKr       = fund.CurrentValueKr,
        CostBasisKr          = fund.CostBasisKr,
        Layer                = fund.Layer,
        Metrics              = fund.Metrics,
        Signal               = fund.Signal,
        RuleFired            = fund.RuleFired,
        CriteriaEvaluation   = fund.CriteriaEvaluation,
        MacroAlignment       = fund.MacroAlignment,
        MatchedTheme         = fund.MatchedTheme,
        PromotedToForming    = fund.PromotedToForming,
        PromotionReason      = fund.PromotionReason,
        Catalyst             = fund.Catalyst,
        ThesisValidity       = fund.ThesisValidity,
        ThesisRationale      = fund.ThesisRationale,
        ThesisMethod         = fund.ThesisMethod,
        Recommendation       = fund.Recommendation,
        RecommendationReason = fund.RecommendationReason,
        ConvictionScore      = fund.ConvictionScore,
        ConvictionBreakdown  = fund.ConvictionBreakdown,
        UniverseRank         = rank,
        Alternatives         = fund.Alternatives,
        RotationPairId       = fund.RotationPairId,
    };

    private static FundRecord WithEnrichment(
        FundRecord fund,
        decimal convictionScore,
        ConvictionBreakdown breakdown,
        UniverseRank? universeRank,
        IReadOnlyList<Alternative>? alternatives,
        string? rotationPairId) => new()
    {
        Isin                 = fund.Isin,
        Metadata             = fund.Metadata,
        NavBuckets           = fund.NavBuckets,
        Snapshot              = fund.Snapshot,
        CurrentlyHeld        = fund.CurrentlyHeld,
        CurrentValueKr       = fund.CurrentValueKr,
        CostBasisKr          = fund.CostBasisKr,
        Layer                = fund.Layer,
        Metrics              = fund.Metrics,
        Signal               = fund.Signal,
        RuleFired            = fund.RuleFired,
        CriteriaEvaluation   = fund.CriteriaEvaluation,
        MacroAlignment       = fund.MacroAlignment,
        MatchedTheme         = fund.MatchedTheme,
        PromotedToForming    = fund.PromotedToForming,
        PromotionReason      = fund.PromotionReason,
        Catalyst             = fund.Catalyst,
        ThesisValidity       = fund.ThesisValidity,
        ThesisRationale      = fund.ThesisRationale,
        ThesisMethod         = fund.ThesisMethod,
        Recommendation       = fund.Recommendation,
        RecommendationReason = fund.RecommendationReason,
        ConvictionScore      = convictionScore,
        ConvictionBreakdown  = breakdown,
        UniverseRank         = universeRank,
        Alternatives         = alternatives,
        RotationPairId       = rotationPairId,
    };

    private static void WriteJson(string path, DataLoaderOutput output)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(output, JsonOptions.Default));
    }
}
