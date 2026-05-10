using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Bank.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FikaFinans.Infrastructure.Bank.Persistence.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasConversion(id => id.Value, guid => new TransactionId(guid));

        builder.Property(t => t.Description).IsRequired().HasMaxLength(500);
        builder.Property(t => t.Status).HasConversion<string>();
        builder.Property(t => t.RelatedOrderId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                guid => guid.HasValue ? new TradingOrderId(guid.Value) : null);

        builder.HasMany(t => t.Entries)
            .WithOne()
            .HasForeignKey(e => e.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
