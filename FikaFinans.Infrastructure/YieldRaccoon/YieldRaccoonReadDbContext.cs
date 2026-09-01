using Microsoft.EntityFrameworkCore;

namespace FikaFinans.Infrastructure.YieldRaccoon;

/// <summary>
/// Read-only EF Core context over YieldRacoon's SQLite database, covering all six
/// of its tables. A mirror of
/// <c>YieldRaccoon.Infrastructure.Data.Context.YieldRaccoonDbContext</c>;
/// FikaFinans never writes through it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This context owns no schema.</b> The producer creates and migrates those
/// tables; FikaFinans only opens the finished file with
/// <c>Mode=ReadOnly</c>. There are no migrations here and none may be added — the
/// mapping below exists purely to read the shape the producer already wrote, so
/// column names, SQLite column types, key columns and index names are copied
/// verbatim from YR's <c>Data/Configuration/*Configuration.cs</c>.
/// </para>
/// <para>
/// YR wraps its keys in strongly-typed ID structs behind value converters
/// (<c>IsinId</c>, <c>CountryId</c>, …). The mirror declares the converted
/// primitive instead — <see cref="string"/> for ISINs, <see cref="Guid"/> for the
/// GUID keys — which lands on the identical storage type without dragging the
/// producer's value objects across the repo boundary.
/// </para>
/// </remarks>
public sealed class YieldRaccoonReadDbContext : DbContext
{
    public YieldRaccoonReadDbContext(DbContextOptions<YieldRaccoonReadDbContext> options)
        : base(options)
    {
    }

    public DbSet<FundProfile> FundProfiles => Set<FundProfile>();

    public DbSet<FundHistoryRecord> FundHistoryRecords => Set<FundHistoryRecord>();

    public DbSet<Country> Countries => Set<Country>();

    public DbSet<Sector> Sectors => Set<Sector>();

    public DbSet<FundCountryAllocation> FundCountryAllocations => Set<FundCountryAllocation>();

    public DbSet<FundSectorAllocation> FundSectorAllocations => Set<FundSectorAllocation>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FundProfile>(entity =>
        {
            entity.ToTable("FundProfiles");

            // YR's key property is Id (an IsinId); the column it lands on is Isin.
            entity.HasKey(f => f.Isin);
            entity.Property(f => f.Isin).HasColumnName("Isin").HasMaxLength(12).IsFixedLength().IsRequired();
            entity.Property(f => f.Name).HasColumnName("Name").HasMaxLength(500).IsRequired();
            entity.Property(f => f.OrderbookId).HasColumnName("OrderbookId").HasMaxLength(50);
            entity.Property(f => f.Category).HasColumnName("Category").HasMaxLength(200);
            entity.Property(f => f.CompanyName).HasColumnName("CompanyName").HasMaxLength(200);
            entity.Property(f => f.FundType).HasColumnName("FundType").HasMaxLength(50);
            entity.Property(f => f.IsIndexFund).HasColumnName("IsIndexFund");
            entity.Property(f => f.CurrencyCode).HasColumnName("CurrencyCode").HasMaxLength(10);
            entity.Property(f => f.ManagedType).HasColumnName("ManagedType").HasMaxLength(20);
            entity.Property(f => f.StartDate).HasColumnName("StartDate").HasColumnType("TEXT").HasMaxLength(10);
            entity.Property(f => f.Buyable).HasColumnName("Buyable");
            entity.Property(f => f.HasCashDividends).HasColumnName("HasCashDividends");
            entity.Property(f => f.HasCurrencyExchangeFee).HasColumnName("HasCurrencyExchangeFee");
            entity.Property(f => f.RecommendedHoldingPeriod).HasColumnName("RecommendedHoldingPeriod").HasMaxLength(50);
            entity.Property(f => f.NumberOfOwners).HasColumnName("NumberOfOwners");
            entity.Property(f => f.Rating).HasColumnName("Rating");
            entity.Property(f => f.Risk).HasColumnName("Risk");
            entity.Property(f => f.SustainabilityLevel).HasColumnName("SustainabilityLevel").HasMaxLength(20);
            entity.Property(f => f.SustainabilityRating).HasColumnName("SustainabilityRating");
            entity.Property(f => f.LowCarbon).HasColumnName("LowCarbon");
            entity.Property(f => f.EuArticleType).HasColumnName("EuArticleType").HasMaxLength(50);
            entity.Property(f => f.FirstSeenAt).HasColumnName("FirstSeenAt");
            entity.Property(f => f.CrawlerLastUpdatedAt).HasColumnName("CrawlerLastUpdatedAt");
            entity.Property(f => f.AboutFundLastVisitedAt).HasColumnName("AboutFundLastVisitedAt");
            entity.Property(f => f.Description).HasColumnName("Description").HasMaxLength(4000);

            // YR declares every decimal as REAL; EF's SQLite provider would
            // otherwise default them to TEXT and fail to read the real file.
            entity.Property(f => f.ManagementFee).HasColumnName("ManagementFee").HasColumnType("REAL");
            entity.Property(f => f.TotalFee).HasColumnName("TotalFee").HasColumnType("REAL");
            entity.Property(f => f.TransactionFee).HasColumnName("TransactionFee").HasColumnType("REAL");
            entity.Property(f => f.OngoingFee).HasColumnName("OngoingFee").HasColumnType("REAL");
            entity.Property(f => f.MinimumBuy).HasColumnName("MinimumBuy").HasColumnType("REAL");
            entity.Property(f => f.Capital).HasColumnName("Capital").HasColumnType("REAL");
            entity.Property(f => f.SharpeRatio).HasColumnName("SharpeRatio").HasColumnType("REAL");
            entity.Property(f => f.StandardDeviation).HasColumnName("StandardDeviation").HasColumnType("REAL");
            entity.Property(f => f.EsgScore).HasColumnName("EsgScore").HasColumnType("REAL");
            entity.Property(f => f.EnvironmentalScore).HasColumnName("EnvironmentalScore").HasColumnType("REAL");
            entity.Property(f => f.SocialScore).HasColumnName("SocialScore").HasColumnType("REAL");
            entity.Property(f => f.GovernanceScore).HasColumnName("GovernanceScore").HasColumnType("REAL");

            entity.HasMany(f => f.HistoryRecords)
                .WithOne(h => h.FundProfile)
                .HasForeignKey(h => h.FundId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FundHistoryRecord>(entity =>
        {
            entity.ToTable("FundHistoryRecords");

            entity.HasKey(h => h.Id);
            entity.Property(h => h.Id).HasColumnName("Id").ValueGeneratedOnAdd();
            entity.Property(h => h.FundId).HasColumnName("FundId").HasMaxLength(12).IsFixedLength().IsRequired();
            entity.Property(h => h.NavDate).HasColumnName("NavDate").HasColumnType("TEXT").HasMaxLength(10);
            entity.Property(h => h.NumberOfOwners).HasColumnName("NumberOfOwners");
            entity.Property(h => h.Risk).HasColumnName("Risk");

            entity.Property(h => h.Nav).HasColumnName("Nav").HasColumnType("REAL");
            entity.Property(h => h.Capital).HasColumnName("Capital").HasColumnType("REAL");
            entity.Property(h => h.SharpeRatio).HasColumnName("SharpeRatio").HasColumnType("REAL");
            entity.Property(h => h.StandardDeviation).HasColumnName("StandardDeviation").HasColumnType("REAL");

            entity.HasIndex(h => new { h.FundId, h.NavDate })
                .HasDatabaseName("IX_FundHistoryRecords_FundId_NavDate")
                .IsDescending(false, true);

            entity.HasIndex(h => new { h.FundId, h.NavDate })
                .HasDatabaseName("UX_FundHistoryRecords_FundId_NavDate")
                .IsUnique();
        });

        modelBuilder.Entity<Country>(entity =>
        {
            entity.ToTable("Countries");

            entity.HasKey(c => c.CountryId);
            entity.Property(c => c.CountryId).HasColumnName("CountryId").IsRequired();
            entity.Property(c => c.DisplayName).HasColumnName("DisplayName").HasMaxLength(200).IsRequired();
            entity.Property(c => c.CountryCode).HasColumnName("CountryCode").HasMaxLength(2);

            entity.HasIndex(c => c.DisplayName)
                .HasDatabaseName("UX_Countries_DisplayName")
                .IsUnique();
        });

        modelBuilder.Entity<Sector>(entity =>
        {
            entity.ToTable("Sectors");

            entity.HasKey(s => s.SectorId);
            entity.Property(s => s.SectorId).HasColumnName("SectorId").IsRequired();
            entity.Property(s => s.DisplayName).HasColumnName("DisplayName").HasMaxLength(200).IsRequired();

            entity.HasIndex(s => s.DisplayName)
                .HasDatabaseName("UX_Sectors_DisplayName")
                .IsUnique();
        });

        modelBuilder.Entity<FundCountryAllocation>(entity =>
        {
            entity.ToTable("FundCountryAllocations");

            entity.HasKey(a => a.FundCountryAllocationId);
            entity.Property(a => a.FundCountryAllocationId).HasColumnName("FundCountryAllocationId").IsRequired();
            entity.Property(a => a.Isin).HasColumnName("Isin").HasMaxLength(12).IsFixedLength().IsRequired();
            entity.Property(a => a.CountryId).HasColumnName("CountryId").IsRequired();
            entity.Property(a => a.Percentage).HasColumnName("Percentage").HasColumnType("REAL").IsRequired();

            entity.HasIndex(a => new { a.Isin, a.CountryId })
                .HasDatabaseName("UX_FundCountryAllocations_Isin_CountryId")
                .IsUnique();

            // No navigation properties either side — the producer queries this
            // table directly, and so does FikaFinans.
            entity.HasOne<FundProfile>()
                .WithMany()
                .HasForeignKey(a => a.Isin)
                .HasPrincipalKey(p => p.Isin)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Country>()
                .WithMany()
                .HasForeignKey(a => a.CountryId)
                .HasPrincipalKey(c => c.CountryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FundSectorAllocation>(entity =>
        {
            entity.ToTable("FundSectorAllocations");

            entity.HasKey(a => a.FundSectorAllocationId);
            entity.Property(a => a.FundSectorAllocationId).HasColumnName("FundSectorAllocationId").IsRequired();
            entity.Property(a => a.Isin).HasColumnName("Isin").HasMaxLength(12).IsFixedLength().IsRequired();
            entity.Property(a => a.SectorId).HasColumnName("SectorId").IsRequired();
            entity.Property(a => a.Percentage).HasColumnName("Percentage").HasColumnType("REAL").IsRequired();

            entity.HasIndex(a => new { a.Isin, a.SectorId })
                .HasDatabaseName("UX_FundSectorAllocations_Isin_SectorId")
                .IsUnique();

            entity.HasOne<FundProfile>()
                .WithMany()
                .HasForeignKey(a => a.Isin)
                .HasPrincipalKey(p => p.Isin)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Sector>()
                .WithMany()
                .HasForeignKey(a => a.SectorId)
                .HasPrincipalKey(s => s.SectorId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
