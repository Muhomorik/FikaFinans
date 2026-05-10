using FikaFinans.Domain.Bank.Holdings;
using FikaFinans.Domain.Bank.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FikaFinans.Infrastructure.Bank.Persistence.Configurations;

public class FundHoldingConfiguration : IEntityTypeConfiguration<FundHolding>
{
    public void Configure(EntityTypeBuilder<FundHolding> builder)
    {
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id)
            .HasConversion(id => id.Value, guid => new FundHoldingId(guid));

        builder.Property(h => h.FundId)
            .HasConversion(id => id.Value, guid => new FundId(guid));

        builder.Property(h => h.Units).HasPrecision(18, 6);
        builder.Property(h => h.AverageCostPerUnit).HasPrecision(18, 4);
        builder.Property(h => h.TotalCostBasis).HasPrecision(18, 4);
        builder.Property(h => h.Currency).IsRequired().HasMaxLength(3);

        builder.HasIndex(h => h.FundId).IsUnique();
    }
}
