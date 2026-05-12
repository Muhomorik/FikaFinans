using FikaFinans.Domain.Bank.Accounts;
using FikaFinans.Domain.Bank.Funds;
using FikaFinans.Domain.Bank.Ledger;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace FikaFinans.Infrastructure.Bank.Persistence;

public class DataSeeder
{
    private readonly ILogger _logger;
    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly BankSimulator _clock;

    public DataSeeder(ILogger logger, IDbContextFactory<BankDbContext> dbFactory, BankSimulator clock)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
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
        SeedFunds(db);
        await db.SaveChangesAsync();
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

    private void SeedFunds(BankDbContext db)
    {
        var baseDate = _clock.Now.AddDays(-30);

        var globalIndex = Fund.Create("Avanza Global Index Fund", "SE0012345678");
        globalIndex.RecordNav(baseDate, 185.50m);
        globalIndex.RecordNav(baseDate.AddDays(7), 187.20m);
        globalIndex.RecordNav(baseDate.AddDays(14), 184.90m);
        globalIndex.RecordNav(baseDate.AddDays(21), 189.75m);
        globalIndex.RecordNav(baseDate.AddDays(28), 100.00m);

        var techFund = Fund.Create("Handelsbanken Tech Theme", "SE0098765432");
        techFund.RecordNav(baseDate, 342.10m);
        techFund.RecordNav(baseDate.AddDays(7), 348.50m);
        techFund.RecordNav(baseDate.AddDays(14), 339.80m);
        techFund.RecordNav(baseDate.AddDays(21), 355.25m);
        techFund.RecordNav(baseDate.AddDays(28), 360.00m);

        var bondFund = Fund.Create("SPP Obligationsfond", "SE0011223344");
        bondFund.RecordNav(baseDate, 108.20m);
        bondFund.RecordNav(baseDate.AddDays(7), 108.45m);
        bondFund.RecordNav(baseDate.AddDays(14), 108.30m);
        bondFund.RecordNav(baseDate.AddDays(21), 108.60m);
        bondFund.RecordNav(baseDate.AddDays(28), 108.75m);

        db.Funds.AddRange(globalIndex, techFund, bondFund);
        _logger.Info("Seeded 3 funds with NAV history");
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
