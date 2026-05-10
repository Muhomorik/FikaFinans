using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Pipeline.Json;
using System.Text.Json;

namespace FikaFinans.Infrastructure.Pipeline.Agents;

public sealed class SignalScorerAgent : ISignalScorerAgent
{
    private readonly IPathsService _paths;

    public SignalScorerAgent(IPathsService paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
    }

    public DataLoaderOutput Run(string isoWeek, string runId)
    {
        var inputPath  = _paths.MetricsCalculatorOutput(isoWeek, runId);
        var configPath = _paths.Config04SignalsJson;
        var outputPath = _paths.SignalScorerOutput(isoWeek, runId);

        var input = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(inputPath), JsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize step 02 output at {inputPath}");

        var config = File.Exists(configPath)
            ? DeserializeConfig(configPath)
            : SignalScorerConfig.Default;

        var output = RunInMemory(input, config);

        WriteJson(outputPath, output);
        return output;
    }

    private static SignalScorerConfig DeserializeConfig(string path)
    {
        var raw = JsonSerializer.Deserialize<SignalScorerConfig>(
            File.ReadAllText(path), JsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize config at {path}");

        return new SignalScorerConfig
        {
            BuyStrength         = raw.BuyStrength,
            SellWeaknessAnyOf   = raw.SellWeaknessAnyOf is { Count: > 0 } ? raw.SellWeaknessAnyOf : SellTrigger.Defaults,
            DataQualityHandling = raw.DataQualityHandling,
        };
    }

    public DataLoaderOutput RunInMemory(DataLoaderOutput input, SignalScorerConfig config)
    {
        var enrichedFunds = input.Funds.Select(f => EnrichWithSignal(f, config)).ToList();

        return new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = input.IsoWeek,
            Family          = input.Family,
            RunId           = input.RunId,
            ConfigVersion   = input.ConfigVersion,
            Funds           = enrichedFunds,
            FrozenPositions = input.FrozenPositions,
            CashAvailableKr = input.CashAvailableKr,
            DataQuality     = input.DataQuality,
        };
    }

    private static FundRecord EnrichWithSignal(FundRecord fund, SignalScorerConfig config)
    {
        var (signal, ruleFired, evaluation) = ScoreFund(fund.Metrics, config);

        return new FundRecord
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
            Signal              = signal,
            RuleFired           = ruleFired,
            CriteriaEvaluation  = evaluation,
        };
    }

    internal static (SignalLabel Signal, string RuleFired, CriteriaEvaluation Evaluation)
        ScoreFund(Metrics? metrics, SignalScorerConfig config)
    {
        if (metrics is null)
        {
            return (
                SignalLabel.Neutral,
                RuleFired.NeutralNoData,
                new CriteriaEvaluation
                {
                    DataQualityWarnings = new[] { "metrics_missing" },
                });
        }

        var warnings = new List<string>();
        var sharpe2wForRules = ResolveSharpe2wForRules(metrics, config, warnings);

        var buy = config.BuyStrength;
        var buy3of3 = metrics.WindowsPositiveCount >= buy.MinPositiveWindows
                      && metrics.WindowsTotal >= buy.OfTotalWindows;
        var buyMaxDd = metrics.CurrentDrawdownPct is { } dd && dd >= buy.MaxDrawdownPct;
        var buyMinSharpe12w = metrics.Sharpe12w is { } s12 && s12 >= buy.MinSharpe12w;
        var allBuyPass = buy3of3 && buyMaxDd && buyMinSharpe12w;

        var sellSharpeNeg = sharpe2wForRules is { } s2 && s2 < 0m;
        var sellDdBreach = EvaluateSellTrigger(config, "current_drawdown_pct", metrics.CurrentDrawdownPct);
        var sellPosLe1 = EvaluateSellTrigger(config, "windows_positive_count", metrics.WindowsPositiveCount);
        var sellTriggerCount = (sellSharpeNeg ? 1 : 0) + (sellDdBreach ? 1 : 0) + (sellPosLe1 ? 1 : 0);

        var evaluation = new CriteriaEvaluation
        {
            Buy3Of3Passed                  = buy3of3,
            BuyMaxDdPassed                 = buyMaxDd,
            BuyMinSharpe12wPassed          = buyMinSharpe12w,
            SellSharpe2wLt0                = sellSharpeNeg,
            SellDdLtThreshold              = sellDdBreach,
            SellPosLe1                     = sellPosLe1,
            WatchPartialWithMacroAlignment = false,
            DataQualityWarnings            = warnings,
        };

        if (metrics.WindowsTotal < 3)
        {
            return (SignalLabel.Neutral, RuleFired.NeutralInsufficient, evaluation);
        }

        if (allBuyPass && sellTriggerCount > 0)
        {
            return (SignalLabel.Neutral, RuleFired.NeutralConflicting, evaluation);
        }

        if (allBuyPass)
        {
            return (SignalLabel.Strength, RuleFired.Buy3of3ZeroDd, evaluation);
        }

        if (sellTriggerCount > 0)
        {
            var ruleFired = sellTriggerCount switch
            {
                >= 2 => RuleFired.SellCombined,
                1 when sellSharpeNeg => RuleFired.SellSharpeNegative,
                1 when sellDdBreach  => RuleFired.SellDrawdownBreach,
                1 when sellPosLe1    => RuleFired.SellPosLe1,
                _ => RuleFired.SellCombined,
            };
            return (SignalLabel.Weakness, ruleFired, evaluation);
        }

        // No buy, no sell — Neutral default. Surface "missing one criterion" on
        // the evaluation so MacroAligner can decide on Forming downstream.
        var neutralEvaluation = evaluation with
        {
            MissingForUpgrade = MissingForUpgrade(buy3of3, buyMaxDd, buyMinSharpe12w),
        };
        return (SignalLabel.Neutral, RuleFired.NeutralDefault, neutralEvaluation);
    }

    private static decimal? ResolveSharpe2wForRules(Metrics metrics, SignalScorerConfig config, List<string> warnings)
    {
        if (metrics.Sharpe2w is not null)
            return metrics.Sharpe2w;

        if (metrics.DataQuality.Sharpe2wIsNan && config.DataQualityHandling.TreatNanSharpeAsZero)
        {
            warnings.Add("sharpe_2w_nan_treated_as_zero");
            return 0m;
        }

        return null;
    }

    private static bool EvaluateSellTrigger(SignalScorerConfig config, string metric, decimal? value)
    {
        if (value is null) return false;
        var trigger = config.SellWeaknessAnyOf.FirstOrDefault(t => t.Metric == metric);
        if (trigger is null) return false;

        return trigger.Comparator switch
        {
            "lt" => value <  trigger.Value,
            "le" => value <= trigger.Value,
            "gt" => value >  trigger.Value,
            "ge" => value >= trigger.Value,
            _    => false,
        };
    }

    private static bool EvaluateSellTrigger(SignalScorerConfig config, string metric, int value) =>
        EvaluateSellTrigger(config, metric, (decimal)value);

    private static string? MissingForUpgrade(bool buy3of3, bool buyMaxDd, bool buyMinSharpe12w)
    {
        var missing = new List<string>();
        if (!buy3of3)         missing.Add("3rd_positive_window");
        if (!buyMaxDd)        missing.Add("drawdown_above_threshold");
        if (!buyMinSharpe12w) missing.Add("sharpe_12w_above_threshold");

        // Only surface as a Forming candidate if exactly one criterion is missing.
        return missing.Count == 1 ? missing[0] : null;
    }

    private static void WriteJson(string path, DataLoaderOutput output)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(output, JsonOptions.Default));
    }
}
