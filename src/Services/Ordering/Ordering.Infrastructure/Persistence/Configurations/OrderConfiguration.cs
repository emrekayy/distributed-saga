using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Ordering.Domain.Orders;

namespace Ordering.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public const string IdempotencyKeyIndex = "IX_Orders_IdempotencyKey";

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.ReasonCode).HasMaxLength(64);
        builder.Property(o => o.Reason).HasMaxLength(1000);
        builder.Property(o => o.IdempotencyKey).HasMaxLength(Order.MaxIdempotencyKeyLength);
        builder.Property(o => o.RequestHash).HasMaxLength(64).IsFixedLength();

        builder.HasIndex(o => o.IdempotencyKey).IsUnique().HasDatabaseName(IdempotencyKeyIndex);

        // Optimistic concurrency: uint row version → Postgres xmin sistem kolonu (fiziksel kolon eklenmez).
        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.OwnsMany(o => o.Items, items =>
        {
            items.ToTable("OrderItems");
            items.WithOwner().HasForeignKey("OrderId");
            items.HasKey("OrderId", nameof(OrderItem.ProductId));
            items.Ignore(i => i.LineTotal);
        });
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
