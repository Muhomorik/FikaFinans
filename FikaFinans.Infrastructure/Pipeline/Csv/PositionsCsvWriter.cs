using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace FikaFinans.Infrastructure.Pipeline.Csv;

/// <summary>
/// Diagnostic writer that produces a <c>positions.csv</c> file matching
/// <see cref="PositionsCsvParser"/>'s read shape — so the parser can
/// re-read its own export round-trip clean. One-way export: the runtime
/// read path is the <c>Positions</c> repo (chunk 5); this writer exists
/// only for the WPF "Export Positions CSV" diagnostic button.
/// </summary>
/// <remarks>
/// Columns mirror <c>PositionsCsvParser.RowMap</c>:
/// <c>isin,name,current_value_kr,cost_basis_kr</c>. The Tables-shaped
/// extras on <see cref="FikaFinans.Application.Storage.Bank.Entities.PositionEntity"/>
/// (<c>Units</c>, <c>AvgCostPerUnit</c>, <c>LastUpdatedAt</c>, <c>Source</c>)
/// are intentionally not written — the CSV is a value-only diagnostic
/// view, not a serialised dump of the repo.
/// </remarks>
public sealed class PositionsCsvWriter
{
    public void Write(TextWriter writer, IEnumerable<PositionsCsvRow> rows)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
        };
        using var csv = new CsvWriter(writer, config);
        csv.Context.RegisterClassMap<RowMap>();
        csv.WriteRecords(rows);
    }

    private sealed class RowMap : ClassMap<PositionsCsvRow>
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

/// <summary>
/// Row shape consumed by <see cref="PositionsCsvWriter"/>. Plain POCO so
/// callers can stitch from any source (repo, in-memory data, fixtures)
/// without taking a dep on the application-layer
/// <c>PositionEntity</c> type.
/// </summary>
public sealed class PositionsCsvRow
{
    public string Isin { get; init; } = string.Empty;
    public string? Name { get; init; }
    public decimal CurrentValueKr { get; init; }
    public decimal CostBasisKr { get; init; }
}
