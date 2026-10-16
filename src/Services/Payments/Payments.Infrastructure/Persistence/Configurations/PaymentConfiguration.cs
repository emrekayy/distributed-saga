using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payments.Domain.Transactions;

namespace Payments.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        // İş anahtarı: sipariş başına tek tahsilat. Eşzamanlı ikinci insert 23505 alır.
        builder.HasIndex(p => p.OrderId).IsUnique().HasDatabaseName("IX_Payments_OrderId");

        builder.Property(p => p.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.Failure).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Reason).HasMaxLength(1000);
    }
}
