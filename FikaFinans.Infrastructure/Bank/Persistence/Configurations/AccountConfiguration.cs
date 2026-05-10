using FikaFinans.Domain.Bank.Accounts;
using FikaFinans.Domain.Bank.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FikaFinans.Infrastructure.Bank.Persistence.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasConversion(id => id.Value, guid => new AccountId(guid));

        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Code).IsRequired().HasMaxLength(10);
        builder.Property(a => a.Currency).IsRequired().HasMaxLength(3);
        builder.Property(a => a.Type).HasConversion<string>();

        builder.HasIndex(a => a.Code).IsUnique();
    }
}
