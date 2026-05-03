using System.Text.Json;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

namespace FikaFinans.InfrastructureV2.Tests.Models.MetricsCalculator;

public sealed class MetricsCalculatorConfig
{
    public int PrimarySharpeHorizonWeeks { get; init; } = 12;
    public int MinBucketDays { get; init; } = 7;
    public bool DropPartialBuckets { get; init; } = true;
    public int StaleSnapshotWarnDays { get; init; } = 14;
    public bool TreatNanSharpeAsZeroForRules { get; init; } = true;
    public int WarnOnBucketsTotalLt { get; init; } = 3;
    public int FeeDeductionHorizonWeeks { get; init; } = 12;
    public List<string> DataQualityFlagsEnabled { get; init; } = new();

    public static MetricsCalculatorConfig Default => new();

    public static MetricsCalculatorConfig Load(string path) =>
        JsonSerializer.Deserialize<MetricsCalculatorConfig>(
            File.ReadAllText(path), JsonOptions.Default)
        ?? throw new InvalidDataException($"Failed to deserialize config at {path}");
}
