using System.Text.Json;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

namespace FikaFinans.InfrastructureV2.Tests.Models.SignalScorer;

public sealed class SignalScorerConfig
{
    public BuyStrengthRules BuyStrength { get; init; } = new();
    public IReadOnlyList<SellTrigger> SellWeaknessAnyOf { get; init; } = SellTrigger.Defaults;
    public DataQualityHandling DataQualityHandling { get; init; } = new();

    public static SignalScorerConfig Default => new();

    public static SignalScorerConfig Load(string path)
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
}

public sealed class BuyStrengthRules
{
    public int MinPositiveWindows { get; init; } = 3;
    public int OfTotalWindows { get; init; } = 3;
    public decimal MaxDrawdownPct { get; init; } = -1.0m;
    public decimal MinSharpe12w { get; init; } = 0.5m;
}

public sealed class SellTrigger
{
    public string Metric { get; init; } = string.Empty;
    public string Comparator { get; init; } = "lt";
    public decimal Value { get; init; }

    public static IReadOnlyList<SellTrigger> Defaults { get; } = new[]
    {
        new SellTrigger { Metric = "sharpe_2w",              Comparator = "lt", Value =  0m   },
        new SellTrigger { Metric = "current_drawdown_pct",   Comparator = "lt", Value = -1.5m },
        new SellTrigger { Metric = "windows_positive_count", Comparator = "le", Value =  1m   },
    };
}

public sealed class DataQualityHandling
{
    public bool TreatNanSharpeAsZero { get; init; } = true;
}
