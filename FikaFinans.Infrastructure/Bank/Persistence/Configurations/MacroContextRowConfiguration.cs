using FikaFinans.Infrastructure.Storage.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FikaFinans.Infrastructure.Bank.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="MacroContextRow"/>. Composite key
/// <c>(PartitionKey, RowKey)</c> with no FKs and no navigation properties — shaped for the
/// Azure Tables swap, where it becomes a partition of the backend's one table.
/// </summary>
/// <remarks>
/// <c>ContextJson</c> is left unbounded here. In Tables it must fit the 32K UTF-16 string
/// cap; a context is a regime and a handful of catalysts and themes, expected well under it.
/// </remarks>
public class MacroContextRowConfiguration : IEntityTypeConfiguration<MacroContextRow>
{
    public void Configure(EntityTypeBuilder<MacroContextRow> builder)
    {
        builder.ToTable("MacroContexts");

        builder.HasKey(p => new { p.PartitionKey, p.RowKey });

        builder.Property(p => p.PartitionKey).IsRequired().HasMaxLength(64);
        builder.Property(p => p.RowKey).IsRequired().HasMaxLength(64);
        builder.Property(p => p.IsoWeek).IsRequired().HasMaxLength(16);
        builder.Property(p => p.GeneratedAt).IsRequired().HasMaxLength(64);
        builder.Property(p => p.ContextJson).IsRequired();
    }
}
