using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Infrastructure.Pipeline.Csv;

public sealed class SnapshotCsvParser
{
    public IReadOnlyDictionary<Isin, FundSnapshot> Parse(TextReader reader)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            DetectColumnCountChanges = true,
        };
        using var csv = new CsvReader(reader, config);
        csv.Context.RegisterClassMap<FlatRowMap>();
        var rows = csv.GetRecords<FlatRow>().ToList();

        return rows.ToDictionary(r => new Isin(r.Isin), ToSnapshot);
    }

    private static FundSnapshot ToSnapshot(FlatRow r) => new()
    {
        AsOfDate            = r.AsOfDate,
        Return12wCompoundPct = r.Return12wCompoundPct,
        AnnVolatility12wPct  = r.AnnVolatility12wPct,
        Sharpe12w            = r.Sharpe12w,
        MaxDrawdown12wPct    = r.MaxDrawdown12wPct,
        Return1yCompoundPct  = r.Return1yCompoundPct,
        AnnVolatility1yPct   = r.AnnVolatility1yPct,
        Sharpe1y             = r.Sharpe1y,
        MaxDrawdown1yPct     = r.MaxDrawdown1yPct,
    };

    private sealed class FlatRow
    {
        public string Isin { get; set; } = "";
        public DateOnly AsOfDate { get; set; }
        public decimal? Return12wCompoundPct { get; set; }
        public decimal? AnnVolatility12wPct { get; set; }
        public decimal? Sharpe12w { get; set; }
        public decimal? MaxDrawdown12wPct { get; set; }
        public decimal? Return1yCompoundPct { get; set; }
        public decimal? AnnVolatility1yPct { get; set; }
        public decimal? Sharpe1y { get; set; }
        public decimal? MaxDrawdown1yPct { get; set; }
    }

    private sealed class FlatRowMap : ClassMap<FlatRow>
    {
        public FlatRowMap()
        {
            Map(m => m.Isin).Name("isin");
            Map(m => m.AsOfDate).Name("as_of_date").TypeConverterOption.Format("yyyy-MM-dd");
            Map(m => m.Return12wCompoundPct).Name("return_12w_compound_pct").TypeConverterOption.NullValues("NaN", "");
            Map(m => m.AnnVolatility12wPct).Name("ann_volatility_12w_pct").TypeConverterOption.NullValues("NaN", "");
            Map(m => m.Sharpe12w).Name("sharpe_12w").TypeConverterOption.NullValues("NaN", "");
            Map(m => m.MaxDrawdown12wPct).Name("max_drawdown_12w_pct").TypeConverterOption.NullValues("NaN", "");
            Map(m => m.Return1yCompoundPct).Name("return_1y_compound_pct").TypeConverterOption.NullValues("NaN", "");
            Map(m => m.AnnVolatility1yPct).Name("ann_volatility_1y_pct").TypeConverterOption.NullValues("NaN", "");
            Map(m => m.Sharpe1y).Name("sharpe_1y").TypeConverterOption.NullValues("NaN", "");
            Map(m => m.MaxDrawdown1yPct).Name("max_drawdown_1y_pct").TypeConverterOption.NullValues("NaN", "");
        }
    }
}
