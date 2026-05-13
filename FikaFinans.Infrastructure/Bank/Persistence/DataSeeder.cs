using System.Globalization;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Bank.Accounts;
using FikaFinans.Domain.Bank.Ledger;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace FikaFinans.Infrastructure.Bank.Persistence;

public class DataSeeder
{
    private readonly ILogger _logger;
    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly IFundsRepository _funds;
    private readonly BankSimulator _clock;

    public DataSeeder(ILogger logger, IDbContextFactory<BankDbContext> dbFactory, IFundsRepository funds, BankSimulator clock)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _funds = funds ?? throw new ArgumentNullException(nameof(funds));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task SeedAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Ensure the SQLite schema exists before any read. No-op for InMemory.
        await db.Database.EnsureCreatedAsync();

        if (db.Accounts.Any())
        {
            _logger.Debug("DataSeeder: already seeded, skipping");
            return;
        }

        _logger.Info("Seeding database...");
        SeedChartOfAccounts(db);
        await db.SaveChangesAsync();
        await SeedFundsAsync();
        await SeedInitialDepositAsync(db);
        _logger.Info("Database seeding complete");
    }

    private void SeedChartOfAccounts(BankDbContext db)
    {
        var accounts = new[]
        {
            Account.Create("Available Cash", "1000", AccountType.Asset),
            Account.Create("Pending Settlement (Buy)", "1100", AccountType.Asset),
            Account.Create("Pending Settlement (Sell)", "2000", AccountType.Liability),
            Account.Create("Owner's Equity", "3000", AccountType.Equity),
            Account.Create("Realized Gains", "4000", AccountType.Revenue),
            Account.Create("Realized Losses", "5000", AccountType.Expense),
        };
        db.Accounts.AddRange(accounts);
        _logger.Info("Seeded {0} chart of accounts entries", accounts.Length);
    }

    private async Task SeedFundsAsync()
    {
        var baseDate = _clock.Now.AddDays(-30);

        await SeedFundWithNavsAsync("Avanza Global Index Fund", "SE0012345678", baseDate,
            new[] { 185.50m, 187.20m, 184.90m, 189.75m, 100.00m });

        await SeedFundWithNavsAsync("Handelsbanken Tech Theme", "SE0098765432", baseDate,
            new[] { 342.10m, 348.50m, 339.80m, 355.25m, 360.00m });

        await SeedFundWithNavsAsync("SPP Obligationsfond", "SE0011223344", baseDate,
            new[] { 108.20m, 108.45m, 108.30m, 108.60m, 108.75m });

        _logger.Info("Seeded 3 funds with NAV history");
    }

    private async Task SeedFundWithNavsAsync(string name, string isin, DateTimeOffset baseDate, decimal[] navs)
    {
        var fundId = Guid.NewGuid();
        await _funds.UpsertAsync(new FundEntity
        {
            PartitionKey = "funds",
            RowKey = isin,
            FundId = fundId,
            Name = name,
            Isin = isin,
            Currency = "SEK"
        });

        for (var i = 0; i < navs.Length; i++)
        {
            var date = baseDate.AddDays(i * 7);
            await _funds.UpsertNavAsync(new NavSnapshotEntity
            {
                PartitionKey = "nav/" + isin,
                RowKey = date.ToString("o", CultureInfo.InvariantCulture),
                NavSnapshotId = Guid.NewGuid(),
                FundId = fundId,
                Isin = isin,
                Date = date,
                NavPerUnit = navs[i]
            });
        }
    }

    private async Task SeedInitialDepositAsync(BankDbContext db)
    {
        var cashAccount = db.Accounts.Local.First(a => a.Code == "1000");
        var equityAccount = db.Accounts.Local.First(a => a.Code == "3000");

        const decimal depositAmount = 100_000m;
        var result = Transaction.Create(
            _clock.Now,
            "Initial deposit - opening balance",
            new[]
            {
                (cashAccount.Id, depositAmount, 0m, "SEK"),
                (equityAccount.Id, 0m, depositAmount, "SEK")
            });

        if (result.IsSuccess)
        {
            db.Transactions.Add(result.Value);
            await db.SaveChangesAsync();
            _logger.Info("Seeded initial deposit of {0:N0} SEK", depositAmount);
        }
    }
}
