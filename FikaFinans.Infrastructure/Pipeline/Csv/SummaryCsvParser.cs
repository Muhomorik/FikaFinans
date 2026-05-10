using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Infrastructure.Pipeline.Csv;

public sealed class SummaryCsvParser
{
    public IReadOnlyDictionary<Isin, IReadOnlyList<NavBucket>> Parse(TextReader reader)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            DetectColumnCountChanges = true,
        };
        using var csv = new CsvReader(reader, config);
        csv.Context.RegisterClassMap<FlatRowMap>();
        var rows = csv.GetRecords<FlatRow>().ToList();

        return rows
            .GroupBy(r => new Isin(r.Isin))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<NavBucket>)g
                    .OrderBy(r => r.PeriodEnd)
                    .Select(ToBucket)
                    .ToList());
    }

    private static NavBucket ToBucket(FlatRow r) => new()
    {
        PeriodStart        = r.PeriodStart,
        PeriodEnd          = r.PeriodEnd,
        FirstNav           = r.FirstNav,
        LastNav            = r.LastNav,
        NavHigh            = r.NavHigh,
        NavLow             = r.NavLow,
        Return2wPct        = r.Return2wPct,
        AnnVolatility2wPct = r.AnnVolatility2wPct,
        MaxDrawdown2wPct   = r.MaxDrawdown2wPct,
        CurrentDrawdownPct = r.CurrentDrawdownPct,
        Sharpe2w           = r.Sharpe2w,
        BestDayPct         = r.BestDayPct,
        WorstDayPct        = r.WorstDayPct,
        PctPositiveDays    = r.PctPositiveDays,
        Skewness           = r.Skewness,
    };

    private sealed class FlatRow
    {
        public string Isin { get; set; } = "";
        public DateOnly PeriodStart { get; set; }
        public DateOnly PeriodEnd { get; set; }
        public decimal FirstNav { get; set; }
        public decimal LastNav { get; set; }
        public decimal NavHigh { get; set; }
        public decimal NavLow { get; set; }
        public decimal Return2wPct { get; set; }
        public decimal AnnVolatility2wPct { get; set; }
        public decimal MaxDrawdown2wPct { get; set; }
        public decimal CurrentDrawdownPct { get; set; }
        public decimal? Sharpe2w { get; set; }
        public decimal BestDayPct { get; set; }
        public decimal WorstDayPct { get; set; }
        public decimal PctPositiveDays { get; set; }
        public decimal Skewness { get; set; }
    }

    private sealed class FlatRowMap : ClassMap<FlatRow>
    {
        public FlatRowMap()
        {
            Map(m => m.Isin).Name("isin");
            Map(m => m.PeriodStart).Name("period_start").TypeConverterOption.Format("yyyy-MM-dd");
            Map(m => m.PeriodEnd).Name("period_end").TypeConverterOption.Format("yyyy-MM-dd");
            Map(m => m.FirstNav).Name("first_nav");
            Map(m => m.LastNav).Name("last_nav");
            Map(m => m.NavHigh).Name("nav_high");
            Map(m => m.NavLow).Name("nav_low");
            Map(m => m.Return2wPct).Name("return_2w_pct");
            Map(m => m.AnnVolatility2wPct).Name("ann_volatility_2w_pct");
            Map(m => m.MaxDrawdown2wPct).Name("max_drawdown_2w_pct");
            Map(m => m.CurrentDrawdownPct).Name("current_drawdown_pct");
            Map(m => m.Sharpe2w).Name("sharpe_2w").TypeConverterOption.NullValues("NaN");
            Map(m => m.BestDayPct).Name("best_day_pct");
            Map(m => m.WorstDayPct).Name("worst_day_pct");
            Map(m => m.PctPositiveDays).Name("pct_positive_days");
            Map(m => m.Skewness).Name("skewness");
        }
    }
}
