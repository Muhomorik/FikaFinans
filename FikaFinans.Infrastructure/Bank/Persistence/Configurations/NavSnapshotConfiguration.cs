using FikaFinans.Domain.Bank.Funds;
using FikaFinans.Domain.Bank.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FikaFinans.Infrastructure.Bank.Persistence.Configurations;

public class NavSnapshotConfiguration : IEntityTypeConfiguration<NavSnapshot>
{
    public void Configure(EntityTypeBuilder<NavSnapshot> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id)
            .HasConversion(id => id.Value, guid => new NavSnapshotId(guid));

        builder.Property(n => n.FundId)
            .HasConversion(id => id.Value, guid => new FundId(guid));

        builder.Property(n => n.NavPerUnit).HasPrecision(18, 4);
    }
}
