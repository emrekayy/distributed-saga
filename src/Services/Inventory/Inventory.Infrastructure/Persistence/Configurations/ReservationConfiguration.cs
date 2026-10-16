using Inventory.Domain.Reservations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Infrastructure.Persistence.Configurations;

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations");

        // İş anahtarı: sipariş başına tek rezervasyon (PK = OrderId). Eşzamanlı ikinci insert 23505 alır.
        builder.HasKey(r => r.OrderId);
        builder.Property(r => r.OrderId).ValueGeneratedNever();

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Rejection).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.Reason).HasMaxLength(1000);

        builder.OwnsMany(r => r.Items, items =>
        {
            items.ToTable("ReservationItems");
            items.WithOwner().HasForeignKey("OrderId");
            items.HasKey("OrderId", nameof(ReservationItem.ProductId));
        });
        builder.Navigation(r => r.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
