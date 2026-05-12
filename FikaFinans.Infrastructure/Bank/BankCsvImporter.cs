using FikaFinans.Application.Bank;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Bank.Funds;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Bank.Persistence;
using FikaFinans.Infrastructure.Pipeline.Csv;
using Microsoft.EntityFrameworkCore;
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
/// <c>FundHolding</c> retired in chunk 5 — this importer no longer touches
/// it. <c>Fund</c> records still get created via direct EF because
/// <c>Fund</c> isn't on a repo yet (Phase 5 cleanup); the seeded NAV
/// (<c>ImportNavPerUnit</c>) is the bootstrap unit price used to derive
/// <c>Units</c> and <c>AvgCostPerUnit</c> from the value-only CSV.
/// </remarks>
public sealed class BankCsvImporter : IBankCsvImporter
{
    private const decimal ImportNavPerUnit = 100m;
    private const string PositionsPartition = "positions";
    private const string CashRowKey = "CASH";

    private readonly ILogger _logger;
    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly IPositionsRepository _positions;
    private readonly BankSimulator _clock;

    public BankCsvImporter(
        ILogger logger,
        IDbContextFactory<BankDbContext> dbFactory,
        IPositionsRepository positions,
        BankSimulator clock)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _positions = positions ?? throw new ArgumentNullException(nameof(positions));
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

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        foreach (var position in result.Holdings)
        {
            var isinStr = position.Isin.Value;

            var allFunds = await db.Funds.Include(f => f.NavHistory).ToListAsync(ct);
            var fund = allFunds.FirstOrDefault(f => f.Isin.Value == isinStr);

            if (fund is null)
            {
                fund = Fund.Create(position.Name ?? isinStr, isinStr);
                fund.RecordNav(now, ImportNavPerUnit);
                db.Funds.Add(fund);
                await db.SaveChangesAsync(ct);
            }

            var nav = fund.GetLatestNav();
            if (nav <= 0) nav = ImportNavPerUnit;

            var units = position.CurrentValueKr / nav;
            var avgCost = units > 0 ? position.CostBasisKr / units : 0m;

            rows.Add(new PositionEntity
            {
                PartitionKey = PositionsPartition,
                RowKey = isinStr,
                Isin = isinStr,
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
