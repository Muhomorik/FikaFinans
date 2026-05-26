using FikaFinans.Domain.Bank.Funds;
using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FikaFinans.Infrastructure.Bank.Persistence.Configurations;

public class FundConfiguration : IEntityTypeConfiguration<Fund>
{
    public void Configure(EntityTypeBuilder<Fund> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id)
            .HasConversion(id => id.Value, guid => new FundId(guid));

        builder.Property(f => f.Name).IsRequired().HasMaxLength(200);
        builder.Property(f => f.Isin)
            .HasConversion(isin => isin.Value, value => new Isin(value))
            .IsRequired()
            .HasMaxLength(12);
        builder.Property(f => f.Currency).IsRequired().HasMaxLength(3);

        builder.HasIndex(f => f.Isin);

        // NavHistory is no longer EF-managed. The Funds repo populates the
        // backing field manually via Fund.Rehydrate(...) when needed.
        builder.Ignore(f => f.NavHistory);
    }
}
