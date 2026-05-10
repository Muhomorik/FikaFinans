using FikaFinans.Domain.Bank.Accounts;
using FikaFinans.Domain.Bank.Funds;
using FikaFinans.Domain.Bank.Holdings;
using FikaFinans.Domain.Bank.Ledger;
using FikaFinans.Domain.Bank.Trading;
using Microsoft.EntityFrameworkCore;

namespace FikaFinans.Infrastructure.Bank.Persistence;

public class BankDbContext : DbContext
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<Fund> Funds => Set<Fund>();
    public DbSet<NavSnapshot> NavSnapshots => Set<NavSnapshot>();
    public DbSet<FundHolding> FundHoldings => Set<FundHolding>();
    public DbSet<TradingOrder> TradingOrders => Set<TradingOrder>();

    public BankDbContext(DbContextOptions<BankDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BankDbContext).Assembly,
            t => t.Namespace?.StartsWith("FikaFinans.Infrastructure.Bank") == true);
    }
}
