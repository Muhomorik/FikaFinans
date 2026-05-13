using FikaFinans.Application.Bank;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Infrastructure.Pipeline.Csv;
using NLog;

namespace FikaFinans.Infrastructure.Bank;

/// <summary>
/// One-shot positions seed. Reads <c>positions.csv</c> on first run (when
/// the <c>Positions</c> partition is empty) and writes a row per holding
/// + a <c>"CASH"</c> pseudo-row. Subsequent <see cref="ImportAsync"/>
/// short-circuits; <see cref="ReimportAsync"/> wipes the partition and
/// re-seeds.
/// </summary>
/// <remarks>
/// Phase 5 migration: <see cref="Domain.Bank.Funds.Fund"/> records now
/// flow through <see cref="IFundsRepository"/>. The seeded NAV
/// (<c>ImportNavPerUnit</c>) is the bootstrap unit price used to derive
/// <c>Units</c> and <c>AvgCostPerUnit</c> from the value-only CSV.
/// </remarks>
public sealed class BankCsvImporter : IBankCsvImporter
{
    private const decimal ImportNavPerUnit = 100m;
    private const string PositionsPartition = "positions";
    private const string CashRowKey = "CASH";

    private readonly ILogger _logger;
    private readonly IPositionsRepository _positions;
    private readonly IFundsRepository _funds;
    private readonly BankSimulator _clock;

    public BankCsvImporter(
        ILogger logger,
        IPositionsRepository positions,
        IFundsRepository funds,
        BankSimulator clock)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _positions = positions ?? throw new ArgumentNullException(nameof(positions));
        _funds = funds ?? throw new ArgumentNullException(nameof(funds));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task ImportAsync(string csvPath, CancellationToken ct = default)
    {
        var existing = await _positions.QueryPartitionAsync(PositionsPartition, ct);
        if (existing.Any(p => p.Units > 0 || p.RowKey == CashRowKey))
        {
            _logger.Debug("BankCsvImporter: positions already exist, skipping initial import");
            return;
        }
        await DoImportAsync(csvPath, ct);
    }

    public async Task ReimportAsync(string csvPath, CancellationToken ct = default)
    {
        var existing = await _positions.QueryPartitionAsync(PositionsPartition, ct);
        foreach (var row in existing)
            await _positions.DeleteAsync(row.PartitionKey, row.RowKey, ct);
        await DoImportAsync(csvPath, ct);
    }

    private async Task DoImportAsync(string csvPath, CancellationToken ct)
    {
        if (!File.Exists(csvPath))
        {
            _logger.Warn("BankCsvImporter: {CsvPath} not found, skipping import", csvPath);
            return;
        }

        PositionsParseResult result;
        using (var reader = new StreamReader(csvPath))
            result = new PositionsCsvParser().Parse(reader);

        var now = _clock.Now;
        var rows = new List<PositionEntity>(result.Holdings.Count + 1);

        foreach (var position in result.Holdings)
        {
            var isin = position.Isin;
            var fund = await _funds.GetByIsinAsync(isin, ct);
            if (fund is null)
            {
                var newFund = new FundEntity
                {
                    PartitionKey = "funds",
                    RowKey = isin.Value,
                    FundId = Guid.NewGuid(),
                    Name = position.Name ?? isin.Value,
                    Isin = isin.Value,
                    Currency = "SEK"
                };
                await _funds.UpsertAsync(newFund, ct);
                await _funds.UpsertNavAsync(new NavSnapshotEntity
                {
                    PartitionKey = "nav/" + isin.Value,
                    RowKey = now.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                    NavSnapshotId = Guid.NewGuid(),
                    FundId = newFund.FundId,
                    Isin = isin.Value,
                    Date = now,
                    NavPerUnit = ImportNavPerUnit
                }, ct);
            }

            var nav = await _funds.GetLatestNavAsync(isin, ct) ?? ImportNavPerUnit;
            if (nav <= 0) nav = ImportNavPerUnit;

            var units = position.CurrentValueKr / nav;
            var avgCost = units > 0 ? position.CostBasisKr / units : 0m;

            rows.Add(new PositionEntity
            {
                PartitionKey = PositionsPartition,
                RowKey = isin.Value,
                Isin = isin.Value,
                Name = position.Name,
                CurrentValueKr = position.CurrentValueKr,
                CostBasisKr = position.CostBasisKr,
                Units = units,
                AvgCostPerUnit = avgCost,
                LastUpdatedAt = now,
                Source = "csvSeed"
            });
        }

        rows.Add(new PositionEntity
        {
            PartitionKey = PositionsPartition,
            RowKey = CashRowKey,
            Isin = CashRowKey,
            Name = "Cash",
            CurrentValueKr = result.CashAvailableKr,
            CostBasisKr = result.CashAvailableKr,
            Units = 0m,
            AvgCostPerUnit = 0m,
            LastUpdatedAt = now,
            Source = "csvSeed"
        });

        if (rows.Count > 0)
            await _positions.UpsertBatchAsync(PositionsPartition, rows, ct);

        _logger.Info("BankCsvImporter: imported {Count} fund positions + cash row from {Path}",
            result.Holdings.Count, csvPath);
    }
}
