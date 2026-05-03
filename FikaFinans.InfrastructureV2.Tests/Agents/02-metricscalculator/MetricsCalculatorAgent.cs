using System.Text.Json;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;
using FikaFinans.InfrastructureV2.Tests.Models.MetricsCalculator;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MetricsCalculator;

public sealed class MetricsCalculatorAgent
{
    public DataLoaderOutput Run(string isoWeek, string runId)
    {
        var inputPath  = Paths.DataLoaderOutput(isoWeek, runId);
        var configPath = Paths.Config02MetricsJsonAbs;
        var outputPath = Paths.MetricsCalculatorOutput(isoWeek, runId);

        var input = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(inputPath), JsonOptions.Default)
            ?? throw new InvalidDataException($"Failed to deserialize step 01 output at {inputPath}");

        var config = File.Exists(configPath)
            ? MetricsCalculatorConfig.Load(configPath)
            : MetricsCalculatorConfig.Default;

        var output = RunInMemory(input, config);

        WriteJson(outputPath, output);
        return output;
    }

    public DataLoaderOutput RunInMemory(DataLoaderOutput input, MetricsCalculatorConfig config)
    {
        var enrichedFunds = input.Funds.Select(f => EnrichWithMetrics(f, config)).ToList();

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

    private static FundRecord EnrichWithMetrics(FundRecord fund, MetricsCalculatorConfig config)
    {
        var metrics = ComputeMetrics(fund, config);
        return new FundRecord
        {
            Isin           = fund.Isin,
            Metadata       = fund.Metadata,
            NavBuckets     = fund.NavBuckets,
            Snapshot       = fund.Snapshot,
            CurrentlyHeld  = fund.CurrentlyHeld,
            CurrentValueKr = fund.CurrentValueKr,
            CostBasisKr    = fund.CostBasisKr,
            Layer          = fund.Layer,
            Metrics        = metrics,
        };
    }

    internal static Metrics ComputeMetrics(FundRecord fund, MetricsCalculatorConfig config)
    {
        var buckets   = fund.NavBuckets;
        var snapshot  = fund.Snapshot;
        var totalFee  = fund.Metadata.TotalFee;

        // Window aggregates over the last 3 buckets (fewer if not enough).
        var window = buckets.TakeLast(3).ToList();
        var windowsTotal = window.Count;
        var windowsPositive = window.Count(b => b.Return2wPct > 0);

        // Latest-bucket fields.
        var latest = buckets.Count > 0 ? buckets[^1] : null;
        var currentDrawdown   = latest?.CurrentDrawdownPct;
        var annVol2w          = latest?.AnnVolatility2wPct;
        var sharpe2w          = latest?.Sharpe2w;

        // Stale-snapshot detection: snapshot.AsOfDate vs latest bucket period_end.
        var snapshotMissing = snapshot is null;
        var snapshotStale = false;
        if (snapshot is not null && latest is not null)
        {
            var ageDays = latest.PeriodEnd.DayNumber - snapshot.AsOfDate.DayNumber;
            snapshotStale = ageDays > config.StaleSnapshotWarnDays;
        }

        // Net return after fee for the 12w horizon.
        decimal? netReturn12w = null;
        if (snapshot?.Return12wCompoundPct is not null)
        {
            var horizon = config.FeeDeductionHorizonWeeks;
            netReturn12w = snapshot.Return12wCompoundPct.Value
                         - totalFee * horizon / 52m;
        }

        var dataQuality = new MetricsDataQuality
        {
            BucketsUsed             = buckets.Count,
            SnapshotMissing         = snapshotMissing,
            SnapshotStaleVsSummary  = snapshotStale,
            Sharpe2wIsNan           = latest is not null && latest.Sharpe2w is null,
            Sharpe12wIsNan          = snapshot is not null && snapshot.Sharpe12w is null,
            Sharpe1yIsNan           = snapshot is not null && snapshot.Sharpe1y is null,
        };

        return new Metrics
        {
            WindowsPositiveCount    = windowsPositive,
            WindowsTotal            = windowsTotal,
            CurrentDrawdownPct      = currentDrawdown,
            AnnVolatility2wPct      = annVol2w,
            Sharpe2w                = sharpe2w,
            Sharpe12w               = snapshot?.Sharpe12w,
            Sharpe1y                = snapshot?.Sharpe1y,
            AnnVolatility12wPct     = snapshot?.AnnVolatility12wPct,
            AnnVolatility1yPct      = snapshot?.AnnVolatility1yPct,
            Return12wCompoundPct    = snapshot?.Return12wCompoundPct,
            Return1yCompoundPct     = snapshot?.Return1yCompoundPct,
            MaxDrawdown12wPct       = snapshot?.MaxDrawdown12wPct,
            MaxDrawdown1yPct        = snapshot?.MaxDrawdown1yPct,
            TotalFeePct             = totalFee,
            NetReturnAfterFee12wPct = netReturn12w,
            AsOfDate                = snapshot?.AsOfDate,
            DataQuality             = dataQuality,
        };
    }

    private static void WriteJson(string path, DataLoaderOutput output)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(output, JsonOptions.Default));
    }
}
