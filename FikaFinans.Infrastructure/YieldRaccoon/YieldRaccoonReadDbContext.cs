using Microsoft.EntityFrameworkCore;

namespace FikaFinans.Infrastructure.YieldRaccoon;

/// <summary>
/// Read-only EF Core context over YieldRacoon's SQLite database — only the two
/// fund-side tables FikaFinans reads (<c>FundProfiles</c> + <c>FundHistoryRecords</c>).
/// A trimmed mirror of <c>YieldRaccoon.Infrastructure.Data.Context.YieldRaccoonDbContext</c>;
/// FikaFinans never writes through it. Unmapped columns on the real (richer)
/// tables are simply ignored.
/// </summary>
public sealed class YieldRaccoonReadDbContext : DbContext
{
    public YieldRaccoonReadDbContext(DbContextOptions<YieldRaccoonReadDbContext> options)
        : base(options)
    {
    }

    public DbSet<FundProfile> FundProfiles => Set<FundProfile>();

    public DbSet<FundHistoryRecord> FundHistoryRecords => Set<FundHistoryRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FundProfile>(entity =>
        {
            entity.ToTable("FundProfiles");
            entity.HasKey(f => f.Isin);
            entity.Property(f => f.Isin).HasColumnName("Isin");
            entity.Property(f => f.Name).HasColumnName("Name");
            entity.Property(f => f.CompanyName).HasColumnName("CompanyName");
            entity.Property(f => f.CurrencyCode).HasColumnName("CurrencyCode");
            entity.Property(f => f.Category).HasColumnName("Category");
            entity.Property(f => f.FundType).HasColumnName("FundType");
            entity.Property(f => f.IsIndexFund).HasColumnName("IsIndexFund");
            entity.Property(f => f.ManagedType).HasColumnName("ManagedType");
            entity.Property(f => f.RecommendedHoldingPeriod).HasColumnName("RecommendedHoldingPeriod");
            entity.Property(f => f.Risk).HasColumnName("Risk");
            entity.Property(f => f.Rating).HasColumnName("Rating");
            entity.Property(f => f.NumberOfOwners).HasColumnName("NumberOfOwners");
            entity.Property(f => f.Buyable).HasColumnName("Buyable");

            // YR declares every decimal as REAL; EF's SQLite provider would
            // otherwise default them to TEXT and fail to read the real file.
            entity.Property(f => f.TotalFee).HasColumnName("TotalFee").HasColumnType("REAL");
            entity.Property(f => f.ManagementFee).HasColumnName("ManagementFee").HasColumnType("REAL");
            entity.Property(f => f.SharpeRatio).HasColumnName("SharpeRatio").HasColumnType("REAL");
            entity.Property(f => f.StandardDeviation).HasColumnName("StandardDeviation").HasColumnType("REAL");
            entity.Property(f => f.Capital).HasColumnName("Capital").HasColumnType("REAL");

            entity.HasMany(f => f.HistoryRecords)
                .WithOne(h => h.FundProfile)
                .HasForeignKey(h => h.FundId);
        });

        modelBuilder.Entity<FundHistoryRecord>(entity =>
        {
            entity.ToTable("FundHistoryRecords");
            entity.HasKey(h => h.Id);
            entity.Property(h => h.Id).HasColumnName("Id");
            entity.Property(h => h.FundId).HasColumnName("FundId");
            entity.Property(h => h.NavDate).HasColumnName("NavDate");
        });
    }
}
