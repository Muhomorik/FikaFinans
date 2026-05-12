using FikaFinans.Infrastructure.Storage.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FikaFinans.Infrastructure.Bank.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="PositionRow"/>. Composite key
/// <c>(PartitionKey, RowKey)</c> with no FKs and no navigation properties —
/// shaped for an eventual Azure Tables swap.
/// </summary>
public class PositionRowConfiguration : IEntityTypeConfiguration<PositionRow>
{
    public void Configure(EntityTypeBuilder<PositionRow> builder)
    {
        builder.ToTable("Positions");

        builder.HasKey(p => new { p.PartitionKey, p.RowKey });

        builder.Property(p => p.PartitionKey).IsRequired().HasMaxLength(64);
        builder.Property(p => p.RowKey).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Isin).IsRequired().HasMaxLength(12);
        builder.Property(p => p.Name).HasMaxLength(200);
        builder.Property(p => p.Source).IsRequired().HasMaxLength(32);

        builder.Property(p => p.CurrentValueKr).HasPrecision(18, 4);
        builder.Property(p => p.CostBasisKr).HasPrecision(18, 4);
        builder.Property(p => p.Units).HasPrecision(18, 6);
        builder.Property(p => p.AvgCostPerUnit).HasPrecision(18, 6);
    }
}
