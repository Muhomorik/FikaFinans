using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Bank.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FikaFinans.Infrastructure.Bank.Persistence.Configurations;

public class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, guid => new JournalEntryId(guid));

        builder.Property(e => e.TransactionId)
            .HasConversion(id => id.Value, guid => new TransactionId(guid));

        builder.Property(e => e.AccountId)
            .HasConversion(id => id.Value, guid => new AccountId(guid));

        builder.Property(e => e.Currency).IsRequired().HasMaxLength(3);
        builder.Property(e => e.DebitAmount).HasPrecision(18, 4);
        builder.Property(e => e.CreditAmount).HasPrecision(18, 4);

        builder.HasIndex(e => e.AccountId);
        // Stitching index — LedgerService joins entries to transactions in
        // memory by TransactionId now that the FK relationship is gone.
        builder.HasIndex(e => e.TransactionId);
    }
}
