using FikaFinans.Domain.Bank.Identifiers;
using FikaFinans.Domain.Bank.Trading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FikaFinans.Infrastructure.Bank.Persistence.Configurations;

public class TradingOrderConfiguration : IEntityTypeConfiguration<TradingOrder>
{
    public void Configure(EntityTypeBuilder<TradingOrder> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, guid => new TradingOrderId(guid));

        builder.Property(o => o.FundId)
            .HasConversion(id => id.Value, guid => new FundId(guid));

        builder.Property(o => o.Side).HasConversion<string>();
        builder.Property(o => o.Status).HasConversion<string>();
        builder.Property(o => o.AmountValue).HasPrecision(18, 4);
        builder.Property(o => o.Currency).IsRequired().HasMaxLength(3);
        builder.Property(o => o.Units).HasPrecision(18, 6);
        builder.Property(o => o.SettlementNavPerUnit).HasPrecision(18, 4);
        builder.Property(o => o.SettledUnits).HasPrecision(18, 6);

        builder.Ignore(o => o.Amount);

        builder.HasIndex(o => o.Status);
    }
}
