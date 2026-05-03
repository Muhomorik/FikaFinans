using System.Text.Json;
using System.Text.Json.Serialization;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

namespace FikaFinans.InfrastructureV2.Tests.Models.PortfolioConstructor;

public sealed class PortfolioConstructorConfig
{
    public const string ExpectedConfigVersion = "1.0.0";

    [JsonPropertyName("_meta")]
    public ConfigMeta? Meta { get; init; }

    public CashPolicy CashPolicy { get; init; } = new();
    public PortfolioConstraints Constraints { get; init; } = new();
    public ConvictionGuards Guards { get; init; } = new();
    public SizingPolicy Sizing { get; init; } = new();
    public RotationPairHandling RotationPairHandling { get; init; } = new();
    public decimal DefaultBuyTargetPct { get; init; } = 0.05m;

    public static PortfolioConstructorConfig Default => new()
    {
        Meta = new ConfigMeta { ConfigVersion = ExpectedConfigVersion },
    };

    public static PortfolioConstructorConfig Load(string path) =>
        JsonSerializer.Deserialize<PortfolioConstructorConfig>(
            File.ReadAllText(path), JsonOptions.Default)
        ?? throw new InvalidDataException($"Failed to deserialize config at {path}");
}

public sealed class ConfigMeta
{
    public string? ConfigVersion { get; init; }
    public string? ConsumedByStep { get; init; }
    public string? MatchesPlan { get; init; }
    public string? Notes { get; init; }
}

public sealed class CashPolicy
{
    public decimal FloorPct { get; init; } = 0.05m;
    public bool MacroOverrideEnabled { get; init; } = false;
    public IReadOnlyDictionary<string, decimal> MacroOverrideTable { get; init; } =
        new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["Crisis"]      = 0.15m,
            ["Stagflation"] = 0.10m,
            ["Mixed"]       = 0.07m,
            ["Risk-on"]     = 0.05m,
        };
}

public sealed class PortfolioConstraints
{
    public decimal MaxPositionPctOfPortfolio { get; init; } = 0.10m;
    public decimal MaxSectorPctOfPortfolio { get; init; } = 0.30m;
    public decimal MinTradeKr { get; init; } = 5_000m;
}

public sealed class ConvictionGuards
{
    public decimal SkipSellBelowConviction { get; init; } = 0.40m;
    public bool SkipSellBelowConvictionUnlessThesisInvalid { get; init; } = true;
    public decimal SkipBuyBelowConviction { get; init; } = 0.30m;
}

public sealed class SizingPolicy
{
    public bool ScaleBuysByConvictionRank { get; init; } = true;
    public bool SmallestFirstWhenShrinking { get; init; } = false;
    public bool DropBuyIfFallsBelowMinTrade { get; init; } = true;
}

public sealed class RotationPairHandling
{
    public bool ExecuteAtomicallyIfPossible { get; init; } = true;

    [JsonPropertyName("if_one_leg_fails")]
    public string IfOneLegFails { get; init; } = "execute_other_leg_alone_and_flag_constraint_violation";
}
