using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

namespace FikaFinans.InfrastructureV2.Tests.Agents.DataLoader;

public sealed class MetadataCsvParser
{
    public IReadOnlyList<FundMetadata> Parse(TextReader reader)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            DetectColumnCountChanges = true,
        };
        using var csv = new CsvReader(reader, config);
        csv.Context.RegisterClassMap<MetadataMap>();
        return csv.GetRecords<FundMetadata>().ToList();
    }

    private sealed class MetadataMap : ClassMap<FundMetadata>
    {
        public MetadataMap()
        {
            Map(m => m.Isin).Name("isin");
            Map(m => m.Name).Name("name");
            Map(m => m.CompanyName).Name("company_name");
            Map(m => m.CurrencyCode).Name("currency_code");
            Map(m => m.Category).Name("category");
            Map(m => m.FundType).Name("fund_type");
            Map(m => m.IsIndexFund).Name("is_index_fund");
            Map(m => m.ManagedType).Name("managed_type");
            Map(m => m.TotalFee).Name("total_fee");
            Map(m => m.ManagementFee).Name("management_fee");
            Map(m => m.Risk).Name("risk");
            Map(m => m.Rating).Name("rating");
            Map(m => m.SharpeRatioStatic).Name("sharpe_ratio");
            Map(m => m.StandardDeviationStatic).Name("standard_deviation");
            Map(m => m.RecommendedHoldingPeriod).Name("recommended_holding_period");
            Map(m => m.Capital).Name("capital");
            Map(m => m.NumberOfOwners).Name("number_of_owners");
        }
    }
}
