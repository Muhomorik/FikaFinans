using FikaFinans.Infrastructure.Storage.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FikaFinans.Infrastructure.Bank.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="IsinProgressRow"/>. Composite key
/// <c>(PartitionKey, RowKey)</c> with no FKs and no navigation properties —
/// shaped for an eventual Azure Tables swap. Step JSON columns are sized
/// to fit the Tables 32K UTF-16 string cap (per-fund-per-step payloads
/// measured at ~17–19 KiB in
/// <c>FikaFinans.InfrastructureV2.Tests/stepOutputs</c>).
/// </summary>
public class IsinProgressRowConfiguration : IEntityTypeConfiguration<IsinProgressRow>
{
    public void Configure(EntityTypeBuilder<IsinProgressRow> builder)
    {
        builder.ToTable("IsinProgress");

        builder.HasKey(p => new { p.PartitionKey, p.RowKey });

        builder.Property(p => p.PartitionKey).IsRequired().HasMaxLength(64);
        builder.Property(p => p.RowKey).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Isin).IsRequired().HasMaxLength(12);
        builder.Property(p => p.State).IsRequired().HasMaxLength(16);
        builder.Property(p => p.RunId).HasMaxLength(64);
        builder.Property(p => p.LastError).HasMaxLength(2000);
    }
}
