using FikaFinans.Domain.Bank.Funds;
using FikaFinans.Domain.Bank.Identifiers;
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
        builder.Property(f => f.Isin).IsRequired().HasMaxLength(12);
        builder.Property(f => f.Currency).IsRequired().HasMaxLength(3);

        builder.HasMany(f => f.NavHistory)
            .WithOne()
            .HasForeignKey(n => n.FundId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(f => f.NavHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
