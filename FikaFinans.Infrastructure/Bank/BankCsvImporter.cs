using FikaFinans.Application.Bank;
using FikaFinans.Domain.Bank.Common;
using FikaFinans.Domain.Bank.Funds;
using FikaFinans.Domain.Bank.Holdings;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Bank.Persistence;
using FikaFinans.Infrastructure.Pipeline.Csv;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace FikaFinans.Infrastructure.Bank;

public sealed class BankCsvImporter : IBankCsvImporter
{
    private const decimal ImportNavPerUnit = 100m;

    private readonly ILogger _logger;
    private readonly BankDbContext _db;
    private readonly BankSimulator _clock;

    public BankCsvImporter(ILogger logger, BankDbContext db, BankSimulator clock)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task ImportAsync(string csvPath, CancellationToken ct = default)
    {
        var hasHoldings = await _db.FundHoldings.AnyAsync(h => h.Units > 0, ct);
        if (hasHoldings)
        {
            _logger.Debug("BankCsvImporter: holdings already exist, skipping initial import");
            return;
        }
        await DoImportAsync(csvPath, ct);
    }

    public async Task ReimportAsync(string csvPath, CancellationToken ct = default)
    {
        _db.FundHoldings.RemoveRange(_db.FundHoldings);
        await _db.SaveChangesAsync(ct);
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

        foreach (var position in result.Holdings)
        {
            var isinStr = position.Isin.Value;

            var allFunds = await _db.Funds.Include(f => f.NavHistory).ToListAsync(ct);
            var fund = allFunds.FirstOrDefault(f => f.Isin.Value == isinStr);

            if (fund is null)
            {
                fund = Fund.Create(position.Name ?? isinStr, isinStr);
                fund.RecordNav(_clock.Now, ImportNavPerUnit);
                _db.Funds.Add(fund);
                await _db.SaveChangesAsync(ct);
            }

            var nav = fund.GetLatestNav();
            if (nav <= 0) nav = ImportNavPerUnit;

            var units = position.CurrentValueKr / nav;
            var holding = FundHolding.Create(fund.Id);
            _db.FundHoldings.Add(holding);
            await _db.SaveChangesAsync(ct);

            holding.AddUnits(units, Money.SEK(position.CostBasisKr));
            await _db.SaveChangesAsync(ct);
        }

        _logger.Info("BankCsvImporter: imported {Count} fund positions from {Path}",
            result.Holdings.Count, csvPath);
    }
}
