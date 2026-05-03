namespace FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

public sealed class FundMetadata
{
    public required string Isin { get; init; }
    public required string Name { get; init; }
    public required string CompanyName { get; init; }
    public required string CurrencyCode { get; init; }
    public required string Category { get; init; }
    public required string FundType { get; init; }
    public bool? IsIndexFund { get; init; }
    public required string ManagedType { get; init; }
    public decimal TotalFee { get; init; }
    public decimal ManagementFee { get; init; }
    public int? Risk { get; init; }
    public int? Rating { get; init; }
    public decimal? SharpeRatioStatic { get; init; }
    public decimal? StandardDeviationStatic { get; init; }
    public required string RecommendedHoldingPeriod { get; init; }
    public decimal Capital { get; init; }
    public int NumberOfOwners { get; init; }
}
