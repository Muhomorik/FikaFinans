using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

namespace FikaFinans.InfrastructureV2.Tests.Agents.DataLoader;

public sealed class PositionsCsvParser
{
    private const string CashRowName = "Cash";

    public PositionsParseResult Parse(TextReader reader)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            DetectColumnCountChanges = true,
        };
        using var csv = new CsvReader(reader, config);
        csv.Context.RegisterClassMap<RowMap>();
        var rows = csv.GetRecords<Row>().ToList();

        var cashRows = rows.Where(IsCashRow).ToList();
        if (cashRows.Count > 1)
            throw new DataLoaderHaltException(
                "multiple_cash_rows",
                $"positions.csv contains {cashRows.Count} Cash rows; expected at most 1.");

        var warnings = new List<string>();
        decimal cashAvailable = 0m;
        if (cashRows.Count == 1)
            cashAvailable = cashRows[0].CurrentValueKr;
        else
            warnings.Add("positions.csv has no Cash row; cash_available_kr defaults to 0.");

        var holdings = rows
            .Where(r => !IsCashRow(r))
            .Select(r => new Position
            {
                Isin = r.Isin,
                Name = string.IsNullOrWhiteSpace(r.Name) ? null : r.Name,
                CurrentValueKr = r.CurrentValueKr,
                CostBasisKr = r.CostBasisKr,
            })
            .ToList();

        return new PositionsParseResult
        {
            Holdings = holdings,
            CashAvailableKr = cashAvailable,
            Warnings = warnings,
            TotalRowCount = rows.Count,
        };
    }

    private static bool IsCashRow(Row r) =>
        string.IsNullOrWhiteSpace(r.Isin)
        && string.Equals(r.Name?.Trim(), CashRowName, StringComparison.Ordinal);

    private sealed class Row
    {
        public string Isin { get; set; } = "";
        public string? Name { get; set; }
        public decimal CurrentValueKr { get; set; }
        public decimal CostBasisKr { get; set; }
    }

    private sealed class RowMap : ClassMap<Row>
    {
        public RowMap()
        {
            Map(m => m.Isin).Name("isin");
            Map(m => m.Name).Name("name");
            Map(m => m.CurrentValueKr).Name("current_value_kr");
            Map(m => m.CostBasisKr).Name("cost_basis_kr");
        }
    }
}
